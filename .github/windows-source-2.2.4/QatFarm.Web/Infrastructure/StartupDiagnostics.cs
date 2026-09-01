namespace QatFarm.Web.Infrastructure;

public static class StartupDiagnostics
{
    public static void WriteFatal(Exception exception)
    {
        try
        {
            var content = $"""
                          وقت الخطأ: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}
                          إصدار النظام: {typeof(StartupDiagnostics).Assembly.GetName().Version}
                          مسار التشغيل: {AppContext.BaseDirectory}
                          نظام التشغيل: {Environment.OSVersion}

                          {exception}
                          """;
            File.WriteAllText(RuntimePaths.StartupErrorFile, content);
        }
        catch
        {
            // آخر وسيلة تسجيل؛ لا نعيد رمي خطأ التسجيل نفسه.
        }
    }
}
