using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser)
{
    private static readonly string[] AllowedRoles = ["Administrator", "Accountant", "Employee"];

    public IReadOnlyList<LookupItem> GetRoles() =>
    [
        new LookupItem(1, "Administrator"),
        new LookupItem(2, "Accountant"),
        new LookupItem(3, "Employee")
    ];

    public async Task<List<UserListRow>> GetAsync()
    {
        await currentUser.EnsureAdministratorAsync();
        var users = await userManager.Users
            .AsNoTracking()
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.FullName)
            .ToListAsync();

        List<UserListRow> result = [];
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            result.Add(new UserListRow(
                user.Id,
                user.FullName,
                user.Email ?? user.UserName ?? "—",
                roles.FirstOrDefault() ?? "Employee",
                user.IsActive,
                user.MustChangePassword,
                user.LastLoginAt,
                user.CreatedAt,
                user.ConcurrencyStamp));
        }

        return result;
    }

    public async Task SaveAsync(UserEditorModel model)
    {
        await currentUser.EnsureAdministratorAsync();
        ValidateModel(model);
        await EnsureRoleExistsAsync(model.Role);
        var actor = await currentUser.GetAsync();

        if (string.IsNullOrWhiteSpace(model.Id))
        {
            var newPassword = model.NewPassword;
            if (string.IsNullOrWhiteSpace(newPassword))
                throw new InvalidOperationException("أدخل كلمة مرور مؤقتة للمستخدم الجديد.");

            var user = new ApplicationUser
            {
                FullName = model.FullName.Trim(),
                Email = model.Email.Trim(),
                UserName = model.Email.Trim(),
                EmailConfirmed = true,
                IsActive = model.IsActive,
                MustChangePassword = true,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, newPassword);
            EnsureSucceeded(createResult);

            var roleResult = await userManager.AddToRoleAsync(user, model.Role);
            if (!roleResult.Succeeded)
            {
                await userManager.DeleteAsync(user);
                EnsureSucceeded(roleResult);
            }

            await AddAuditAsync(actor, "CreateUser", user.Id, null, new
            {
                user.FullName,
                user.Email,
                Role = model.Role,
                user.IsActive
            });
            return;
        }

        var existing = await userManager.FindByIdAsync(model.Id)
            ?? throw new InvalidOperationException("المستخدم غير موجود.");

        if (!string.Equals(existing.ConcurrencyStamp, model.ConcurrencyStamp, StringComparison.Ordinal))
            throw new DbUpdateConcurrencyException("تم تعديل المستخدم من جلسة أخرى. أعد تحميل الصفحة.");

        var oldRoles = await userManager.GetRolesAsync(existing);
        var oldRole = oldRoles.FirstOrDefault() ?? "Employee";
        var actorIsTarget = string.Equals(actor.UserId, existing.Id, StringComparison.Ordinal);

        if (actorIsTarget && !model.IsActive)
            throw new InvalidOperationException("لا يمكنك إيقاف حسابك الحالي.");
        if (actorIsTarget && !string.Equals(model.Role, "Administrator", StringComparison.Ordinal))
            throw new InvalidOperationException("لا يمكنك إزالة صلاحية المدير من حسابك الحالي.");

        if (string.Equals(oldRole, "Administrator", StringComparison.Ordinal) &&
            (!model.IsActive || !string.Equals(model.Role, "Administrator", StringComparison.Ordinal)))
        {
            await EnsureAnotherAdministratorExistsAsync(existing.Id);
        }

        var before = new
        {
            existing.FullName,
            existing.Email,
            Role = oldRole,
            existing.IsActive,
            existing.MustChangePassword
        };

        existing.FullName = model.FullName.Trim();
        existing.Email = model.Email.Trim();
        existing.UserName = model.Email.Trim();
        existing.IsActive = model.IsActive;
        existing.EmailConfirmed = true;

        var updateResult = await userManager.UpdateAsync(existing);
        EnsureSucceeded(updateResult);

        if (!string.Equals(oldRole, model.Role, StringComparison.Ordinal))
        {
            if (oldRoles.Count > 0)
                EnsureSucceeded(await userManager.RemoveFromRolesAsync(existing, oldRoles));

            var addRoleResult = await userManager.AddToRoleAsync(existing, model.Role);
            if (!addRoleResult.Succeeded)
            {
                if (oldRoles.Count > 0)
                    await userManager.AddToRolesAsync(existing, oldRoles);
                EnsureSucceeded(addRoleResult);
            }
        }

        var replacementPassword = model.NewPassword;
        if (!string.IsNullOrWhiteSpace(replacementPassword))
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(existing);
            EnsureSucceeded(await userManager.ResetPasswordAsync(existing, token, replacementPassword));
            existing.MustChangePassword = true;
            EnsureSucceeded(await userManager.UpdateAsync(existing));
        }

        await AddAuditAsync(actor, "UpdateUser", existing.Id, before, new
        {
            existing.FullName,
            existing.Email,
            Role = model.Role,
            existing.IsActive,
            existing.MustChangePassword,
            PasswordReset = !string.IsNullOrWhiteSpace(replacementPassword)
        });
    }

    private static void ValidateModel(UserEditorModel model)
    {
        if (string.IsNullOrWhiteSpace(model.FullName))
            throw new InvalidOperationException("اسم المستخدم مطلوب.");
        if (string.IsNullOrWhiteSpace(model.Email))
            throw new InvalidOperationException("البريد الإلكتروني مطلوب.");
        if (!AllowedRoles.Contains(model.Role, StringComparer.Ordinal))
            throw new InvalidOperationException("الصلاحية المحددة غير صحيحة.");
    }

    private async Task EnsureRoleExistsAsync(string role)
    {
        if (!await roleManager.RoleExistsAsync(role))
            EnsureSucceeded(await roleManager.CreateAsync(new IdentityRole(role)));
    }

    private async Task EnsureAnotherAdministratorExistsAsync(string excludedUserId)
    {
        var administrators = await userManager.GetUsersInRoleAsync("Administrator");
        var hasAnother = administrators.Any(x =>
            x.IsActive && !string.Equals(x.Id, excludedUserId, StringComparison.Ordinal));
        if (!hasAnother)
            throw new InvalidOperationException("لا يمكن إيقاف أو تخفيض صلاحية آخر مدير نشط في النظام.");
    }

    private async Task AddAuditAsync(CurrentUserInfo actor, string action, string entityId, object? oldValues, object newValues)
    {
        await using var db = await factory.CreateDbContextAsync();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            Action = action,
            EntityName = nameof(ApplicationUser),
            EntityId = entityId,
            OldValues = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = JsonSerializer.Serialize(newValues),
            IpAddress = actor.IpAddress,
            ActionDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (result.Succeeded) return;
        throw new InvalidOperationException(string.Join(" | ", result.Errors.Select(x => x.Description)));
    }
}
