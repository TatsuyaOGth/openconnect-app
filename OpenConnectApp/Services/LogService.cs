namespace OpenConnectApp.Services;

public class LogService
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public LogService(string logPath)
    {
        _logPath = logPath;
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_lock)
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
    }

    public void Log(string message, Exception ex)
    {
        Log($"{message}: {ex.Message}");
    }
}
