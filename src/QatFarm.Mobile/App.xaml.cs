namespace QatFarm.Mobile;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "نظام زراعي عواد سوفت" };
#if WINDOWS
        window.Width = 1450;
        window.Height = 900;
        window.MinimumWidth = 980;
        window.MinimumHeight = 650;
#endif
        return window;
    }
}
