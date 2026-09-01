using System.Diagnostics;

namespace QatFarm.Web.Infrastructure;

public static class BrowserLauncher
{
    public static void Open(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // فشل فتح المتصفح لا يوقف خادم النظام؛ يمكن فتح الرابط يدويًا.
        }
    }
}
