using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class AuditLogService(
    IDbContextFactory<ApplicationDbContext> factory,
    UserManager<ApplicationUser> userManager,
    CurrentUserService currentUser)
{
    public async Task<List<AuditLogRow>> GetAsync(string? search = null, int take = 500)
    {
        await currentUser.EnsureAdministratorAsync();
        take = Math.Clamp(take, 50, 2000);

        await using var db = await factory.CreateDbContextAsync();
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Action.Contains(term) ||
                x.EntityName.Contains(term) ||
                x.EntityId.Contains(term) ||
                (x.IpAddress != null && x.IpAddress.Contains(term)));
        }

        var logs = await query
            .OrderByDescending(x => x.ActionDate)
            .ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync();

        var userIds = logs
            .Where(x => !string.IsNullOrWhiteSpace(x.UserId))
            .Select(x => x.UserId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var users = await userManager.Users
            .AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.Email })
            .ToDictionaryAsync(x => x.Id, x => x.FullName + (x.Email == null ? string.Empty : $" ({x.Email})"));

        return logs.Select(log => new AuditLogRow(
            log.Id,
            log.ActionDate,
            log.UserId is not null && users.TryGetValue(log.UserId, out var name) ? name : "النظام / مستخدم غير معروف",
            log.Action,
            log.EntityName,
            log.EntityId,
            log.OldValues,
            log.NewValues,
            log.IpAddress)).ToList();
    }
}
