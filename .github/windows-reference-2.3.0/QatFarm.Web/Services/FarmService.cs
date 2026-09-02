using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class FarmService(
    IDbContextFactory<ApplicationDbContext> factory,
    CurrentUserService currentUser,
    UserManager<ApplicationUser> userManager)
{
    public async Task<List<Farm>> GetAllAsync(string? search = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Farms.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Name.Contains(search));
        return await query.ToListAsync();
    }

    private async Task<string> PrimaryAdministratorNameAsync()
    {
        var admins = await userManager.GetUsersInRoleAsync("Administrator");
        var primary = admins.Where(x => x.IsActive).OrderBy(x => x.CreatedAt).FirstOrDefault();
        return primary?.FullName ?? "مدير النظام";
    }

    public async Task SaveAsync(Farm model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) throw new InvalidOperationException("اسم المزرعة مطلوب.");
        var actor = await currentUser.GetAsync();
        var primaryOwner = await PrimaryAdministratorNameAsync();
        await using var db = await factory.CreateDbContextAsync();
        if (model.Id == 0)
        {
            model.Name = model.Name.Trim();
            model.OwnerName = primaryOwner;
            model.Location = null;
            model.CreatedByUserId = actor.UserId;
            db.Farms.Add(model);
            await db.SaveChangesAsync();
            db.AuditLogs.Add(new AuditLog { UserId=actor.UserId,IpAddress=actor.IpAddress,Action="Create",EntityName=nameof(Farm),EntityId=model.Id.ToString(),NewValues=model.Name });
        }
        else
        {
            var entity = await db.Farms.FirstAsync(x => x.Id == model.Id);
            if (model.RowVersion.Length > 0) db.Entry(entity).Property(x => x.RowVersion).OriginalValue = model.RowVersion;
            entity.Name = model.Name.Trim();
            entity.OwnerName = primaryOwner;
            entity.Location = null;
            entity.Phone = model.Phone;
            entity.Notes = model.Notes;
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = actor.UserId;
            db.AuditLogs.Add(new AuditLog { UserId=actor.UserId,IpAddress=actor.IpAddress,Action="Update",EntityName=nameof(Farm),EntityId=entity.Id.ToString(),NewValues=entity.Name });
        }
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateConcurrencyException){ throw new InvalidOperationException("تم تعديل المزرعة من مستخدم آخر. حدّث الصفحة ثم أعد المحاولة."); }
    }

    public async Task DeleteAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor=await currentUser.GetAsync();
        await using var db=await factory.CreateDbContextAsync();
        var entity=await db.Farms.FirstAsync(x=>x.Id==id);
        entity.IsActive=false;entity.UpdatedAt=DateTime.UtcNow;entity.UpdatedByUserId=actor.UserId;
        db.AuditLogs.Add(new AuditLog{UserId=actor.UserId,IpAddress=actor.IpAddress,Action="Deactivate",EntityName=nameof(Farm),EntityId=id.ToString(),OldValues=entity.Name,NewValues="IsActive=false"});
        await db.SaveChangesAsync();
    }
}
