using Microsoft.EntityFrameworkCore;
using QatFarm.Web.Data;
using QatFarm.Web.Models;

namespace QatFarm.Web.Services;

public sealed class FarmService(IDbContextFactory<ApplicationDbContext> factory, CurrentUserService currentUser)
{
    public async Task<List<Farm>> GetAllAsync(string? search = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var query = db.Farms.AsNoTracking().OrderByDescending(x => x.IsActive).ThenBy(x => x.Name).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search) || (x.OwnerName ?? "").Contains(search));
        return await query.ToListAsync();
    }

    public async Task SaveAsync(Farm model)
    {
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        if (model.Id == 0)
        {
            model.Name = model.Name.Trim();
            model.CreatedByUserId = actor.UserId;
            db.Farms.Add(model);
            await db.SaveChangesAsync();
            db.AuditLogs.Add(new AuditLog { UserId = actor.UserId, IpAddress = actor.IpAddress, Action = "Create", EntityName = nameof(Farm), EntityId = model.Id.ToString(), NewValues = model.Name });
        }
        else
        {
            var entity = await db.Farms.FirstAsync(x => x.Id == model.Id);
            if (model.RowVersion.Length > 0)
                db.Entry(entity).Property(x => x.RowVersion).OriginalValue = model.RowVersion;
            var old = entity.Name;
            entity.Name = model.Name.Trim();
            entity.OwnerName = model.OwnerName;
            entity.Location = model.Location;
            entity.Phone = model.Phone;
            entity.Notes = model.Notes;
            entity.IsActive = model.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;
            entity.UpdatedByUserId = actor.UserId;
            db.AuditLogs.Add(new AuditLog { UserId = actor.UserId, IpAddress = actor.IpAddress, Action = "Update", EntityName = nameof(Farm), EntityId = entity.Id.ToString(), OldValues = old, NewValues = entity.Name });
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("تم تعديل المزرعة من مستخدم آخر. حدّث الصفحة ثم أعد المحاولة.");
        }
    }

    public async Task DeleteAsync(long id)
    {
        await currentUser.EnsureAdministratorAsync();
        var actor = await currentUser.GetAsync();
        await using var db = await factory.CreateDbContextAsync();
        var entity = await db.Farms.FirstAsync(x => x.Id == id);

        // لا نحذف المزرعة منطقيًا حتى لا تختفي فواتيرها وتقاريرها التاريخية.
        // الإجراء الآمن هو إيقافها ومنع استخدامها في العمليات الجديدة.
        entity.IsActive = false;
        entity.UpdatedAt = DateTime.UtcNow;
        entity.UpdatedByUserId = actor.UserId;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actor.UserId,
            IpAddress = actor.IpAddress,
            Action = "Deactivate",
            EntityName = nameof(Farm),
            EntityId = id.ToString(),
            OldValues = entity.Name,
            NewValues = "IsActive=false"
        });
        await db.SaveChangesAsync();
    }
}
