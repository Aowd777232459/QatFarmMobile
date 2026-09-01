using QatFarm.Mobile.Data;
using QatFarm.Mobile.Models;

namespace QatFarm.Mobile.Services;

public sealed class AppSession
{
    private readonly MobileDb _db;
    public AppSession(MobileDb db) => _db = db;

    public AppUser? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsAdmin => CurrentUser?.Role == UserRole.Administrator;
    public bool CanRecordPayments => CurrentUser?.Role is UserRole.Administrator or UserRole.Accountant;
    public bool CanEditInvoices => IsAdmin || CurrentUser?.CanEditInvoices == true;
    public bool CanDeleteInvoices => IsAdmin || CurrentUser?.CanDeleteInvoices == true;
    public event Action? Changed;

    public async Task<bool> HasUsersAsync()
    {
        var db = await _db.GetAsync();
        return await db.Table<AppUser>().Where(x => !x.IsDeleted).CountAsync() > 0;
    }

    public async Task<bool> NeedsAccessCodeSetupAsync()
    {
        var db = await _db.GetAsync();
        var users = await db.Table<AppUser>().Where(x => !x.IsDeleted && x.IsActive).ToListAsync();
        return users.Count > 0 && users.All(x => string.IsNullOrWhiteSpace(x.AccessCodeHash));
    }

    public async Task<(bool Success, string Message)> CreateFirstAdministratorAsync(FirstRunAdminModel model)
    {
        var db = await _db.GetAsync();
        if (await db.Table<AppUser>().Where(x => !x.IsDeleted).CountAsync() > 0)
            return (false, "تم إنشاء حساب الإدارة مسبقاً.");
        var code = NormalizeAccessCode(model.AccessCode);
        if (code is null) return (false, "رمز الدخول يجب أن يتكون من 6 أحرف بالضبط.");

        var (hash, salt) = PasswordHasher.HashPassword(code);
        var user = new AppUser
        {
            FullName = "مدير النظام",
            Email = $"admin-{Guid.NewGuid():N}@local.awad",
            PasswordHash = hash,
            PasswordSalt = salt,
            AccessCodeHash = hash,
            AccessCodeSalt = salt,
            Role = UserRole.Administrator,
            CanEditInvoices = true,
            CanDeleteInvoices = true,
            IsActive = true,
            LastLoginAt = DateTime.Now
        };
        await db.InsertAsync(user);
        CurrentUser = user;
        Changed?.Invoke();
        return (true, string.Empty);
    }

    public async Task<(bool Success, string Message)> SetupExistingAdministratorAccessCodeAsync(string accessCode)
    {
        var code = NormalizeAccessCode(accessCode);
        if (code is null) return (false, "رمز الدخول يجب أن يتكون من 6 أحرف بالضبط.");
        var db = await _db.GetAsync();
        var admin = (await db.Table<AppUser>().Where(x => !x.IsDeleted && x.IsActive && x.Role == UserRole.Administrator).ToListAsync())
            .OrderBy(x => x.Id).FirstOrDefault();
        if (admin is null) return (false, "لا يوجد حساب مدير نشط.");
        var (hash, salt) = PasswordHasher.HashPassword(code);
        admin.AccessCodeHash = hash;
        admin.AccessCodeSalt = salt;
        admin.PasswordHash = hash;
        admin.PasswordSalt = salt;
        admin.CanEditInvoices = true;
        admin.CanDeleteInvoices = true;
        admin.LastLoginAt = DateTime.Now;
        admin.UpdatedAt = DateTime.Now;
        await db.UpdateAsync(admin);
        CurrentUser = admin;
        Changed?.Invoke();
        return (true, string.Empty);
    }

    public async Task<(bool Success, string Message)> LoginAsync(string accessCode)
    {
        var code = NormalizeAccessCode(accessCode);
        if (code is null) return (false, "أدخل رمز الدخول المكون من 6 أحرف.");
        var db = await _db.GetAsync();
        var users = await db.Table<AppUser>().Where(x => !x.IsDeleted && x.IsActive).ToListAsync();
        var user = users.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.AccessCodeHash) &&
            PasswordHasher.Verify(code, x.AccessCodeHash, x.AccessCodeSalt));
        if (user is null) return (false, "رمز الدخول غير صحيح أو الحساب موقوف.");
        user.LastLoginAt = DateTime.Now;
        await db.UpdateAsync(user);
        CurrentUser = user;
        Changed?.Invoke();
        return (true, string.Empty);
    }

    public static string? NormalizeAccessCode(string? value)
    {
        var code = value?.Trim() ?? string.Empty;
        return code.Length == 6 && code.All(c => !char.IsWhiteSpace(c)) ? code : null;
    }

    public void Logout() { CurrentUser = null; Changed?.Invoke(); }
}
