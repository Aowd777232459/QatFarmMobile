using QatFarm.Mobile.Data;
using QatFarm.Mobile.Services;

namespace QatFarm.Mobile;

public partial class App : Application
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AwadStorageService _storage;
    private readonly MobileDb _db;
    private readonly DesktopSyncServer _desktopSync;

    public App(AwadStorageService storage, MobileDb db, DesktopSyncServer desktopSync)
    {
        InitializeComponent();
        _storage = storage;
        _db = db;
        _desktopSync = desktopSync;
        _ = InitializeBackgroundServicesAsync();
    }

    private async Task InitializeBackgroundServicesAsync()
    {
        try
        {
            _storage.EnsureStructure();
            await _db.InitializeAsync();
#if WINDOWS
            await _storage.CreateAutomaticBackupAsync(_db, "startup");
            await _desktopSync.StartAsync();
            _ = _storage.RunAutomaticBackupLoopAsync(_db, _lifetime.Token);
#endif
        }
        catch
        {
            // لا نمنع فتح النظام بسبب تعذر خدمة مساعدة؛ تظهر حالة المزامنة داخل الإعدادات.
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "نظام زراعي عواد سوفت" };
#if WINDOWS
        window.Width = 1280;
        window.Height = 860;
        window.MinimumWidth = 900;
        window.MinimumHeight = 650;
#endif
        return window;
    }
}
