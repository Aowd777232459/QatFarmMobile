using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using QatFarm.Mobile.Data;

namespace QatFarm.Mobile.Services;

public sealed class BackupService
{
    private readonly MobileDb _db;
    private readonly AppSession _session;
    private readonly AwadStorageService _storage;

    public BackupService(MobileDb db, AppSession session, AwadStorageService storage)
    {
        _db = db;
        _session = session;
        _storage = storage;
    }

    private void EnsureAdmin()
    {
        if (!_session.IsAdmin)
            throw new InvalidOperationException("النسخ الاحتياطي والاستعادة متاحان للمدير فقط.");
    }

    public async Task<string> CreateBackupAsync()
    {
        EnsureAdmin();
        var automatic = await _storage.CreateAutomaticBackupAsync(_db, "manual");
        if (!string.IsNullOrWhiteSpace(automatic)) return automatic;

        await _db.CheckpointAsync();
        var path = Path.Combine(FileSystem.CacheDirectory, $"QatFarmBackup-{DateTime.Now:yyyyMMdd-HHmmss}.db3");
        File.Copy(_db.DatabasePath, path, true);
        return path;
    }

    public async Task ShareBackupAsync()
    {
        var path = await CreateBackupAsync();
#if WINDOWS
        // على Windows تبقى النسخة محفوظة داخل Documents\عواد سوفت حتى لو لم يفتح المستخدم نافذة مشاركة.
        return;
#else
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "نسخة احتياطية لنظام زراعي عواد سوفت",
            File = new ShareFile(path)
        });
#endif
    }

    public async Task RestoreBackupAsync()
    {
        EnsureAdmin();
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "اختر ملف النسخة الاحتياطية"
        });
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        await _db.ReplaceDatabaseAsync(stream);
        await _storage.CreateAutomaticBackupAsync(_db, "after-restore");
    }
}
