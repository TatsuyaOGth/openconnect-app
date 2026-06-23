namespace OpenConnectApp.Services;

public class LogService
{
    private readonly string _logPath;
    private readonly object _lock = new();

    /// <summary>ログが1行追記されたときに発火する（GUIのリアルタイム表示用）。</summary>
    public event Action<string>? LineAppended;

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
        LineAppended?.Invoke(line);
    }

    public void Log(string message, Exception ex)
    {
        Log($"{message}: {ex.Message}");
    }

    /// <summary>ログファイル末尾の最大 maxLines 行を返す（GUI初期表示用）。</summary>
    public IReadOnlyList<string> ReadRecent(int maxLines)
    {
        lock (_lock)
        {
            if (!File.Exists(_logPath))
                return Array.Empty<string>();

            try
            {
                var lines = File.ReadAllLines(_logPath);
                if (lines.Length <= maxLines)
                    return lines;
                return lines[^maxLines..];
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
