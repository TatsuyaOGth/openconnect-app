using System.Diagnostics;
using OpenConnectApp.Interfaces;
using OpenConnectApp.Models;

namespace OpenConnectApp.Services;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Disconnecting,
}

public class ConnectionStateChangedEventArgs : EventArgs
{
    public ConnectionStatus Status { get; init; }
    public string? ConnectedName { get; init; }
    public string? ErrorMessage { get; init; }
}

public class ConnectionManager : IDisposable
{
    private readonly IPrivilegedExecutor _executor;
    private readonly AppConfigService _configService;
    private readonly LogService _logger;
    private readonly System.Timers.Timer _pollingTimer;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string? ConnectedName { get; private set; }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionManager(
        IPrivilegedExecutor executor,
        AppConfigService configService,
        LogService logger)
    {
        _executor = executor;
        _configService = configService;
        _logger = logger;

        _pollingTimer = new System.Timers.Timer(7_000); // 7秒間隔
        _pollingTimer.Elapsed += (_, _) => CheckConnectionStatus();
        _pollingTimer.AutoReset = true;
    }

    /// <summary>起動時の接続状態復元。</summary>
    public void RestoreState()
    {
        var pidPath = _configService.PidFilePath;
        if (!File.Exists(pidPath))
        {
            SetStatus(ConnectionStatus.Disconnected, null);
            return;
        }

        var pidText = File.ReadAllText(pidPath).Trim();
        if (int.TryParse(pidText, out int pid) && IsProcessAlive(pid))
        {
            var config = _configService.Load();
            ConnectedName = config.LastConnectedName ?? "不明";
            SetStatus(ConnectionStatus.Connected, ConnectedName);
            _pollingTimer.Start();
        }
        else
        {
            TryDeletePidFile();
            SetStatus(ConnectionStatus.Disconnected, null);
        }
    }

    public async Task ConnectAsync(
        VpnConnection connection,
        ICredentialStore credentialStore,
        AppConfig config)
    {
        // 既存接続があれば先に切断
        if (Status == ConnectionStatus.Connected)
            await DisconnectAsync();

        SetStatus(ConnectionStatus.Connecting, connection.DisplayName);
        _logger.Log($"接続試行開始: {connection.DisplayName} ({connection.Host})");

        string? tmpFile = null;
        try
        {
            var creds = credentialStore.Load()
                ?? throw new InvalidOperationException("認証情報が保存されていません。設定タブでユーザー名とパスワードを入力してください。");

            // パスワードを一時ファイルに書き込む（権限600）
            tmpFile = Path.Combine(Path.GetTempPath(), $"ocgui_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tmpFile, creds.Password);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                File.SetUnixFileMode(tmpFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var command = BuildConnectCommand(connection, config, creds.Username, tmpFile);
            await _executor.RunAsync(command, ConnectTimeout);

            ConnectedName = connection.DisplayName;

            // 最後に接続した接続先を記録
            config.LastConnectedName = connection.DisplayName;
            _configService.Save(config);

            SetStatus(ConnectionStatus.Connected, connection.DisplayName);
            _pollingTimer.Start();
            _logger.Log($"接続成功: {connection.DisplayName}");
        }
        catch (TimeoutException ex)
        {
            _logger.Log($"接続タイムアウト: {connection.DisplayName}", ex);
            SetStatus(ConnectionStatus.Disconnected, null, ex.Message);
            await KillByHostAsync(connection.Host);
        }
        catch (Exception ex)
        {
            _logger.Log($"接続失敗: {connection.DisplayName}", ex);
            SetStatus(ConnectionStatus.Disconnected, null, ex.Message);
        }
        finally
        {
            if (tmpFile != null)
            {
                try { File.Delete(tmpFile); } catch { }
            }
        }
    }

    public async Task DisconnectAsync()
    {
        var pidPath = _configService.PidFilePath;
        SetStatus(ConnectionStatus.Disconnecting, ConnectedName);
        _pollingTimer.Stop();
        _logger.Log($"切断実行: {ConnectedName}");

        try
        {
            if (!File.Exists(pidPath))
                throw new InvalidOperationException("PIDファイルが存在しません。");

            var pidText = File.ReadAllText(pidPath).Trim();
            var command = $"kill -INT {OsascriptPrivilegedExecutor.ShellQuote(pidText)}";
            await _executor.RunAsync(command, TimeSpan.FromSeconds(10));

            TryDeletePidFile();
            ConnectedName = null;
            SetStatus(ConnectionStatus.Disconnected, null);
            _logger.Log("切断完了");
        }
        catch (Exception ex)
        {
            _logger.Log("切断エラー", ex);
            SetStatus(ConnectionStatus.Disconnected, null, ex.Message);
            TryDeletePidFile();
        }
    }

    private void CheckConnectionStatus()
    {
        var pidPath = _configService.PidFilePath;
        if (!File.Exists(pidPath))
        {
            _pollingTimer.Stop();
            ConnectedName = null;
            SetStatus(ConnectionStatus.Disconnected, null);
            return;
        }

        var pidText = File.ReadAllText(pidPath).Trim();
        if (!int.TryParse(pidText, out int pid) || !IsProcessAlive(pid))
        {
            _pollingTimer.Stop();
            TryDeletePidFile();
            ConnectedName = null;
            SetStatus(ConnectionStatus.Disconnected, null);
            _logger.Log("プロセスが終了しました。未接続に切り替えます。");
        }
    }

    private static string BuildConnectCommand(
        VpnConnection conn,
        AppConfig config,
        string username,
        string tmpFile)
    {
        var q = OsascriptPrivilegedExecutor.ShellQuote;
        var pidFile = q(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "OpenConnectApp", "openconnect.pid"));

        var sb = new System.Text.StringBuilder();
        sb.Append($"cat {q(tmpFile)} | {q(config.OpenConnectPath ?? "/opt/homebrew/bin/openconnect")}");
        sb.Append($" --background --pid-file={pidFile}");
        sb.Append($" -u {q(username)}");
        sb.Append(" --passwd-on-stdin");
        sb.Append($" -s {q(config.VpncScriptPath ?? "/opt/homebrew/etc/vpnc-script")}");

        if (!string.IsNullOrEmpty(conn.UserGroup))
            sb.Append($" --usergroup={q(conn.UserGroup)}");
        if (!string.IsNullOrEmpty(conn.Protocol))
            sb.Append($" --protocol={q(conn.Protocol)}");
        if (!string.IsNullOrEmpty(conn.ServerCert))
            sb.Append($" --servercert={q(conn.ServerCert)}");

        sb.Append($" {q(conn.Host)}");
        sb.Append($" ; rm -f {q(tmpFile)}");

        return sb.ToString();
    }

    private async Task KillByHostAsync(string host)
    {
        try
        {
            var pattern = OsascriptPrivilegedExecutor.ShellQuote($"openconnect.*{host}");
            var cmd = $"pkill -TERM -f {pattern}";
            _logger.Log($"異常系後始末: {cmd}");
            await _executor.RunAsync(cmd, TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.Log("pkill 実行エラー", ex);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("ps", $"-p {pid}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            proc.Start();
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void TryDeletePidFile()
    {
        try { File.Delete(_configService.PidFilePath); } catch { }
    }

    private void SetStatus(ConnectionStatus status, string? name, string? errorMessage = null)
    {
        Status = status;
        if (status == ConnectionStatus.Connected)
            ConnectedName = name;

        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs
        {
            Status = status,
            ConnectedName = name,
            ErrorMessage = errorMessage,
        });
    }

    public void Dispose()
    {
        _pollingTimer.Dispose();
    }
}
