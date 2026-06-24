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
    private readonly ServerCertService _serverCertService;
    private readonly System.Timers.Timer _pollingTimer;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CertProbeTimeout = TimeSpan.FromSeconds(15);

    // openconnect が証明書検証失敗時に出力する pin の抽出用。
    private static readonly System.Text.RegularExpressions.Regex PinRegex =
        new(@"pin-sha256:[^\s'""]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;
    public string? ConnectedName { get; private set; }

    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    public ConnectionManager(
        IPrivilegedExecutor executor,
        AppConfigService configService,
        LogService logger,
        ServerCertService serverCertService)
    {
        _executor = executor;
        _configService = configService;
        _logger = logger;
        _serverCertService = serverCertService;

        _pollingTimer = new System.Timers.Timer(7_000); // 7秒間隔
        _pollingTimer.Elapsed += (_, _) => CheckConnectionStatus();
        _pollingTimer.AutoReset = true;
    }

    /// <summary>起動時の接続状態復元。PIDファイルがない場合も孤立プロセスを検出する。</summary>
    public void RestoreState()
    {
        var pidPath = _configService.PidFilePath;

        if (File.Exists(pidPath))
        {
            var pidText = File.ReadAllText(pidPath).Trim();
            if (int.TryParse(pidText, out int pid) && IsProcessAlive(pid))
            {
                var config = _configService.Load();
                ConnectedName = config.LastConnectedName ?? "不明";
                SetStatus(ConnectionStatus.Connected, ConnectedName);
                _pollingTimer.Start();
                return;
            }
            TryDeletePidFile();
        }

        // PIDファイルがない/無効な場合でも openconnect プロセスが残存していれば接続状態として復元する。
        var orphanedPid = FindOrphanedOpenConnectPid();
        if (orphanedPid.HasValue)
        {
            _logger.Log($"警告: PIDファイルなしで openconnect プロセス (PID:{orphanedPid}) を検出しました。");
            File.WriteAllText(pidPath, orphanedPid.Value.ToString());
            var config = _configService.Load();
            ConnectedName = config.LastConnectedName ?? "不明（孤立プロセス）";
            SetStatus(ConnectionStatus.Connected, ConnectedName);
            _pollingTimer.Start();
            return;
        }

        SetStatus(ConnectionStatus.Disconnected, null);
    }

    /// <summary>pgrep で実行中の openconnect プロセス PID を探す（孤立プロセス検出用）。</summary>
    private static int? FindOrphanedOpenConnectPid()
    {
        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo("pgrep", "openconnect")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit();
            if (proc.ExitCode == 0)
            {
                var firstLine = output.Split('\n')[0].Trim();
                if (int.TryParse(firstLine, out int pid))
                    return pid;
            }
        }
        catch { }
        return null;
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

        try
        {
            var creds = await Task.Run(() => credentialStore.Load())
                ?? throw new InvalidOperationException("認証情報が保存されていません。設定タブでユーザー名とパスワードを入力してください。");

            // サーバ証明書を自動ピン留め（TOFU）し、--servercert で検証することで対話プロンプトを回避する。
            var pin = await ResolveServerCertPinAsync(connection, config);

            // 1回目の試行。
            if (await AttemptConnectAsync(connection, config, creds, pin))
            {
                MarkConnected(connection, config);
                return;
            }

            // 証明書エラーで失敗した場合、openconnect が出力した本物の pin を拾って自動再ピン留めし、
            // 1回だけ再接続する（サーバ証明書のローテーション等への自己修復）。
            var harvested = TryHarvestPinFromLog();
            if (harvested != null && harvested != pin)
            {
                _logger.Log($"警告: サーバ証明書が変わりました。自動で再ピン留めします: {connection.Host}");
                config.ServerCertPins[connection.Host] = harvested;
                _configService.Save(config);

                if (await AttemptConnectAsync(connection, config, creds, harvested))
                {
                    MarkConnected(connection, config);
                    return;
                }
            }

            // 確立できなかった。ネットワークを半設定のまま残さないようクリーンアップする。
            _logger.Log($"接続確立を確認できませんでした: {connection.DisplayName}");
            SetStatus(ConnectionStatus.Disconnected, null, BuildErrorWithLog(
                "接続が確立されませんでした。openconnect が終了したか、PIDファイルが生成されませんでした。"));
            await CleanupAsync(connection.Host, allowFallback: true);
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
    }

    /// <summary>
    /// 接続に使うサーバ証明書 pin を解決する。
    /// 優先順: CSV の手動指定(ServerCert) → 保存済み(TOFU) → 未知ならTLSで取得して保存。
    /// </summary>
    private async Task<string?> ResolveServerCertPinAsync(VpnConnection connection, AppConfig config)
    {
        if (!string.IsNullOrEmpty(connection.ServerCert))
            return connection.ServerCert;

        if (config.ServerCertPins.TryGetValue(connection.Host, out var stored)
            && !string.IsNullOrEmpty(stored))
            return stored;

        _logger.Log($"サーバ証明書を取得して自動ピン留めします: {connection.Host}");
        var pin = await _serverCertService.GetPinSha256Async(connection.Host, CertProbeTimeout);
        config.ServerCertPins[connection.Host] = pin;
        _configService.Save(config);
        _logger.Log($"ピン留め完了: {connection.Host} = {pin}");
        return pin;
    }

    /// <summary>
    /// 1 回分の接続試行。パスワードを一時ファイルに書き、openconnect を実行し、
    /// プロセスの生存を確認して接続確立とみなせれば true を返す。
    /// </summary>
    private async Task<bool> AttemptConnectAsync(
        VpnConnection connection, AppConfig config, (string Username, string Password) creds, string? pin)
    {
        string? tmpFile = null;
        try
        {
            // パスワードを一時ファイルに書き込む（権限600）。--passwd-on-stdin が1行目を読む。
            tmpFile = Path.Combine(Path.GetTempPath(), $"ocgui_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tmpFile, creds.Password);
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                File.SetUnixFileMode(tmpFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var command = BuildConnectCommand(connection, config, creds.Username, tmpFile, pin);
            await _executor.RunAsync(command, ConnectTimeout);

            // openconnect は接続確立後に PID ファイルを書いてからバックグラウンド化する。
            // コマンドの成功＝接続成功とは限らないため、実際にプロセスが生存しているか確認する。
            // 認証・トンネル確立に時間がかかるサーバもあるため猶予を長めに取り、誤検知（→誤kill）を防ぐ。
            return await WaitForAliveProcessAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            // openconnect 実行が失敗（証明書不一致・認証失敗・タイムアウト等）。失敗として扱い、
            // 呼び出し側が openconnect.log から原因を判定し、必要なら自己修復する。
            _logger.Log("接続試行が失敗しました", ex);
            return false;
        }
        finally
        {
            if (tmpFile != null)
            {
                try { File.Delete(tmpFile); } catch { }
            }
        }
    }

    /// <summary>接続成功時の確定処理（状態・記録・監視開始）。</summary>
    private void MarkConnected(VpnConnection connection, AppConfig config)
    {
        ConnectedName = connection.DisplayName;
        config.LastConnectedName = connection.DisplayName;
        _configService.Save(config);

        SetStatus(ConnectionStatus.Connected, connection.DisplayName);
        _pollingTimer.Start();
        _logger.Log($"接続成功: {connection.DisplayName}");
        LogOpenConnectOutput(10);
    }

    /// <summary>openconnect.log から本物の pin-sha256 を抽出する（無ければ null）。</summary>
    private string? TryHarvestPinFromLog()
    {
        var tail = ReadOpenConnectLogTail(50);
        var m = PinRegex.Match(tail);
        return m.Success ? m.Value : null;
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

            // SIGINT 送出後、実際にプロセスが終了するまで待つ。終了を確認できてから
            // PIDファイルを消す（残存している間は消さず、後から kill できるようにする）。
            if (int.TryParse(pidText, out int pid))
                await WaitForProcessExitAsync(pid, TimeSpan.FromSeconds(10));

            ConnectedName = null;
            if (TryDeletePidFileIfDead())
            {
                SetStatus(ConnectionStatus.Disconnected, null);
                _logger.Log("切断完了");
            }
            else
            {
                SetStatus(ConnectionStatus.Disconnected, null,
                    "切断信号を送りましたが openconnect プロセスがまだ残存しています。"
                    + "PIDファイルは保持しています。ログを確認してください。");
            }
        }
        catch (Exception ex)
        {
            _logger.Log("切断エラー", ex);
            SetStatus(ConnectionStatus.Disconnected, null, ex.Message);
            // kill がキャンセル・失敗した場合、プロセスが生きたまま PIDファイルを消すと
            // 孤立化するため、生存確認できる間は PIDファイルを残す。
            TryDeletePidFileIfDead();
        }
    }

    /// <summary>
    /// PIDファイルに依存せず pkill で openconnect を強制終了する。
    /// 通常の切断が効かない場合や孤立プロセスが残存している場合の緊急用。
    /// ターミナルから手動で実行する場合は: sudo pkill -INT openconnect
    /// </summary>
    public async Task ForceDisconnectAsync()
    {
        _pollingTimer.Stop();
        SetStatus(ConnectionStatus.Disconnecting, ConnectedName);
        _logger.Log("強制切断を実行します (pkill -INT openconnect)");

        try
        {
            var cmd = "pkill -INT openconnect; sleep 2; pkill -TERM openconnect; true";
            await _executor.RunAsync(cmd, TimeSpan.FromSeconds(15));
            _logger.Log("強制切断完了");
        }
        catch (Exception ex)
        {
            _logger.Log("強制切断エラー", ex);
        }
        finally
        {
            // 強制切断後もプロセスが残存している場合は、PID を見失わないよう PIDファイルを残す。
            TryDeletePidFileIfDead();
            ConnectedName = null;
            SetStatus(ConnectionStatus.Disconnected, null);
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
        string tmpFile,
        string? serverCertPin)
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
        // 解決済みのサーバ証明書 pin（手動指定 or 自動ピン留め）。これにより証明書プロンプトを回避する。
        if (!string.IsNullOrEmpty(serverCertPin))
            sb.Append($" --servercert={q(serverCertPin)}");

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

        // フォールバック後も残存している可能性があるため、終了を確認できる場合のみ削除する。
        TryDeletePidFileIfDead();
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

    /// <summary>
    /// PIDファイルを削除する。ただし参照先 openconnect プロセスがまだ生存している場合は削除しない。
    /// openconnect は osascript 経由で root 権限のデーモンとして起動するため、PID を見失うと
    /// 後からそのプロセスを特定・kill できず（アクティビティモニターにも出ず）孤立化する。
    /// これを防ぐため「プロセスの終了を確認できるまで PIDファイルは残す」を保証する。
    /// </summary>
    /// <returns>削除できた（=プロセスは生存していない）場合 true、生存中で保持した場合 false。</returns>
    private bool TryDeletePidFileIfDead()
    {
        var pidPath = _configService.PidFilePath;
        try
        {
            if (!File.Exists(pidPath))
                return true;

            var pidText = File.ReadAllText(pidPath).Trim();
            // PID が壊れていて手がかりにならない場合は削除して構わない。
            if (!int.TryParse(pidText, out int pid))
            {
                File.Delete(pidPath);
                return true;
            }

            // まだ生存している場合は削除せず、後から kill できるよう PID を保持する。
            if (IsProcessAlive(pid))
            {
                _logger.Log(
                    $"警告: openconnect (PID:{pid}) がまだ生存しているため PIDファイルを保持します。" +
                    $"手動で切断する場合: sudo kill -INT {pid}");
                return false;
            }

            File.Delete(pidPath);
            return true;
        }
        catch
        {
            return false;
        }
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
