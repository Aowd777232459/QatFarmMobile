using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QatFarm.Web.Infrastructure;

public sealed class RollingFileLoggerProvider(string logsDirectory, LogLevel minimumLevel = LogLevel.Information)
    : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, RollingFileLogger> _loggers = new(StringComparer.OrdinalIgnoreCase);

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new RollingFileLogger(name, logsDirectory, minimumLevel));

    public void Dispose() => _loggers.Clear();
}

internal sealed class RollingFileLogger(
    string categoryName,
    string logsDirectory,
    LogLevel minimumLevel) : ILogger
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None && logLevel >= minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null) return;

        var now = DateTimeOffset.Now;
        var file = Path.Combine(logsDirectory, $"qatfarm-{now:yyyy-MM-dd}.log");
        var entry = $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {categoryName} ({eventId.Id}) {message}" +
                    (exception is null ? string.Empty : Environment.NewLine + exception) +
                    Environment.NewLine;

        var lockTaken = false;
        try
        {
            WriteLock.Wait();
            lockTaken = true;
            Directory.CreateDirectory(logsDirectory);
            File.AppendAllText(file, entry);
        }
        catch
        {
            // لا ينبغي أن يؤدي تعذر كتابة السجل إلى إيقاف النظام.
        }
        finally
        {
            if (lockTaken)
                WriteLock.Release();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();
        public void Dispose() { }
    }
}
