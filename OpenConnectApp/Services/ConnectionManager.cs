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
            var creds = await Task.Run(() => credentialStore.Load())
                ?? throw new InvalidOperationException("認証情報が保存されていません。設定タブでユーザー名とパスワードを入力してください。");

            // パスワードを一時ファイルに書き込む（権限600）
            tmpFile = Path.Combine(Path.GetTempPath(), $"ocgui_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tmpFile, creds.Password);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                File.SetUnixFileMode(tmpFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var command = BuildConnectCommand(connection, config, creds.Username, tmpFile);
            await _executor.RunAsync(command, ConnectTimeout);

            // openconnect は接続確立後に PID ファイルを書いてからバックグラウンド化する。
            // コマンドの成功＝接続成功とは限らないため、実際にプロセスが生存しているか確認する。
            // --background は接続確立後すぐ PID を書くが、認証・トンネル確立に時間がかかる
            // サーバもあるため猶予を長めに取り、稼働中トンネルの誤検知（→誤kill）を防ぐ。
            if (!await WaitForAliveProcessAsync(TimeSpan.FromSeconds(10)))
            {
                _logger.Log($"接続確立を確認できませんでした: {connection.DisplayName}");
                SetStatus(ConnectionStatus.Disconnected, null, BuildErrorWithLog(
                    "接続が確立されませんでした。openconnect が終了したか、PIDファイルが生成されませんでした。"));
                await CleanupAsync(connection.Host, allowFallback: true);
                return;
            }

            ConnectedName = connection.DisplayName;

            // 最後に接続した接続先を記録
            config.LastConnectedName = connection.DisplayName;
            _configService.Save(config);

            SetStatus(ConnectionStatus.Connected, connection.DisplayName);
            _pollingTimer.Start();
            _logger.Log($"接続成功: {connection.DisplayName}");
            LogOpenConnectOutput(10);
        }
        catch (TimeoutException ex)
        {
            // openconnect が接続中のまま固まっている可能性が高い。ネットワークを半設定のまま
            // 放置すると OS 全体が固まるため、SIGINT でクリーン切断して復旧させる。
            _logger.Log($"接続タイムアウト: {connection.DisplayName}", ex);
            SetStatus(ConnectionStatus.Disconnected, null, BuildErrorWithLog(ex.Message));
            await CleanupAsync(connection.Host, allowFallback: true);
        }
        catch (Exception ex)
        {
            // openconnect 自身が終了済みでも、PID が残っていれば念のためクリーン切断する。
            _logger.Log($"接続失敗: {connection.DisplayName}", ex);
            SetStatus(ConnectionStatus.Disconnected, null, BuildErrorWithLog(ex.Message));
            await CleanupAsync(connection.Host, allowFallback: false);
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

    private string BuildConnectCommand(
        VpnConnection conn,
        AppConfig config,
        string username,
        string tmpFile)
    {
        var q = OsascriptPrivilegedExecutor.ShellQuote;
        var pidFile = q(_configService.PidFilePath);
        var logFile = q(_configService.OpenConnectLogPath);

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
        // openconnectの出力をログへ取得し、終了コードを保持したまま一時ファイルを削除する。
        // `;` で繋ぐと rm の終了コードに上書きされ openconnect の失敗を握りつぶすため rc を退避する。
        sb.Append($" > {logFile} 2>&1");
        sb.Append($"; rc=$?; rm -f {q(tmpFile)}; exit $rc");

        return sb.ToString();
    }

    /// <summary>
    /// 接続失敗・中断時のクリーンアップ。openconnect を SIGINT でクリーンに終了させ、
    /// vpnc-script による DNS/ルート復旧（reason=disconnect）を確実に走らせる。
    /// いきなり SIGTERM/SIGKILL で殺すと設定途中のネットワークが復旧されず、
    /// DNS/ルートが壊れたまま残って OS 全体が固まるため、必ず SIGINT を優先する。
    /// </summary>
    private async Task CleanupAsync(string host, bool allowFallback)
    {
        var pidPath = _configService.PidFilePath;
        int? pid = null;
        if (File.Exists(pidPath))
        {
            var pidText = File.ReadAllText(pidPath).Trim();
            if (int.TryParse(pidText, out int p))
                pid = p;
        }

        // 1. 該当 PID が生存していれば SIGINT でクリーン切断し、終了を待つ。
        if (pid is int target && IsProcessAlive(target))
        {
            try
            {
                _logger.Log($"クリーンアップ: kill -INT {target}（クリーン切断でネットワーク復旧）");
                await _executor.RunAsync(
                    $"kill -INT {OsascriptPrivilegedExecutor.ShellQuote(target.ToString())}",
                    TimeSpan.FromSeconds(10));

                if (await WaitForProcessExitAsync(target, TimeSpan.FromSeconds(10)))
                {
                    _logger.Log("クリーンアップ: openconnect は正常に終了しました。");
                    TryDeletePidFile();
                    return;
                }
                _logger.Log("クリーンアップ: SIGINT 後もプロセスが残存しています。");
            }
            catch (Exception ex)
            {
                _logger.Log("クリーンアップ(kill -INT)エラー", ex);
            }
        }

        // 2. PID で対処できない/残存する場合のみ、ホスト名パターンでフォールバック。
        //    こちらも INT を先に送り、それでも残る場合の最終手段として TERM を送る。
        if (allowFallback)
            await FallbackKillAsync(host);

        TryDeletePidFile();
    }

    private async Task FallbackKillAsync(string host)
    {
        var pattern = OsascriptPrivilegedExecutor.ShellQuote($"openconnect.*{host}");
        // 単一の特権実行にまとめて認証ダイアログの多重表示を避ける。
        var cmd = $"pkill -INT -f {pattern}; sleep 3; pkill -TERM -f {pattern}; true";
        try
        {
            _logger.Log($"クリーンアップ(フォールバック): {cmd}");
            await _executor.RunAsync(cmd, TimeSpan.FromSeconds(20));
        }
        catch (Exception ex)
        {
            _logger.Log("pkill 実行エラー", ex);
        }
    }

    /// <summary>指定 PID のプロセスが終了するまで最大 timeout 待つ。</summary>
    private static async Task<bool> WaitForProcessExitAsync(int pid, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsProcessAlive(pid))
                return true;
            await Task.Delay(300);
        }
        return !IsProcessAlive(pid);
    }

    /// <summary>PIDファイルが生成され、プロセスが生存するまで最大 timeout 待つ。</summary>
    private async Task<bool> WaitForAliveProcessAsync(TimeSpan timeout)
    {
        var pidPath = _configService.PidFilePath;
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (File.Exists(pidPath))
            {
                var pidText = File.ReadAllText(pidPath).Trim();
                if (int.TryParse(pidText, out int pid) && IsProcessAlive(pid))
                    return true;
            }
            await Task.Delay(300);
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    /// <summary>openconnect.log の末尾を読む（接続失敗時の原因表示・GUIログ用）。</summary>
    private string ReadOpenConnectLogTail(int maxLines = 20)
    {
        try
        {
            var path = _configService.OpenConnectLogPath;
            if (!File.Exists(path))
                return string.Empty;

            var lines = File.ReadAllLines(path);
            var tail = lines.Length <= maxLines ? lines : lines[^maxLines..];
            return string.Join(Environment.NewLine, tail).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>openconnect の出力をアプリログ（=GUIログタブ）へ書き出す。</summary>
    private void LogOpenConnectOutput(int maxLines = 20)
    {
        var tail = ReadOpenConnectLogTail(maxLines);
        if (!string.IsNullOrWhiteSpace(tail))
            _logger.Log("openconnect 出力:\n" + tail);
    }

    /// <summary>エラーメッセージに openconnect の出力を付加し、GUIログにも記録する。</summary>
    private string BuildErrorWithLog(string baseMessage)
    {
        var tail = ReadOpenConnectLogTail();
        if (string.IsNullOrWhiteSpace(tail))
            return baseMessage;

        _logger.Log("openconnect 出力:\n" + tail);
        return $"{baseMessage}\n\nopenconnect 出力:\n{tail}";
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
