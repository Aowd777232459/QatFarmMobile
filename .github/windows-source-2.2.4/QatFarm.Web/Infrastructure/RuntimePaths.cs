namespace QatFarm.Web.Infrastructure;

public static class RuntimePaths
{
    private const string ProductDirectoryName = "QatFarmSystem";

    public static string DataDirectory { get; } = CreateDirectory(GetDataDirectory());
    public static string LogsDirectory { get; } = CreateDirectory(Path.Combine(DataDirectory, "Logs"));
    public static string BackupsDirectory { get; } = CreateDirectory(Path.Combine(DataDirectory, "Backups"));
    public static string LocalConfigurationFile => Path.Combine(DataDirectory, "appsettings.Local.json");
    public static string StartupErrorFile => Path.Combine(LogsDirectory, "STARTUP_ERROR.txt");

    private static string GetDataDirectory()
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        return Path.Combine(root, ProductDirectoryName);
    }

    private static string CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
