using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using QatFarm.Mobile.Data;

namespace QatFarm.Mobile.Services;

public sealed class BackupService
{
    private readonly MobileDb _db;
    private readonly AppSession _session;
    public BackupService(MobileDb db, AppSession session) { _db = db; _session = session; }

    private void EnsureAdmin()
    {
        if (!_session.IsAdmin) throw new InvalidOperationException("النسخ الاحتياطي والاستعادة متاحان للمدير فقط.");
    }

    public async Task<string> CreateBackupAsync()
    {
        EnsureAdmin();
        await _db.CheckpointAsync();
        var path = Path.Combine(FileSystem.CacheDirectory, $"QatFarmBackup-{DateTime.Now:yyyyMMdd-HHmmss}.db3");
        File.Copy(_db.DatabasePath, path, true);
        return path;
    }

    public async Task ShareBackupAsync()
    {
        var path = await CreateBackupAsync();
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "نسخة احتياطية لنظام زراعي عواد سوفت",
            File = new ShareFile(path)
        });
    }

    public async Task RestoreBackupAsync()
    {
        EnsureAdmin();
        var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "اختر ملف النسخة الاحتياطية" });
        if (result is null) return;
        await using var stream = await result.OpenReadAsync();
        await _db.ReplaceDatabaseAsync(stream);
    }
}
