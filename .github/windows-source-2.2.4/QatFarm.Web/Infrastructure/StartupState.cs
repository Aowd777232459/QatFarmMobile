namespace QatFarm.Web.Infrastructure;

public sealed class StartupState
{
    private readonly object _sync = new();
    private Exception? _error;

    public bool IsReady { get; private set; }
    public DateTimeOffset LastCheckAt { get; private set; } = DateTimeOffset.Now;

    public Exception? Error
    {
        get
        {
            lock (_sync) return _error;
        }
    }

    public void MarkReady()
    {
        lock (_sync)
        {
            IsReady = true;
            _error = null;
            LastCheckAt = DateTimeOffset.Now;
        }
    }

    public void MarkFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            IsReady = false;
            _error = exception;
            LastCheckAt = DateTimeOffset.Now;
        }
    }
}
