using System.Diagnostics;
using OpenConnectApp.Interfaces;

namespace OpenConnectApp.Services;

/// <summary>
/// macOS実装: osascript "do shell script ... with administrator privileges" で特権実行する。
/// </summary>
public class OsascriptPrivilegedExecutor : IPrivilegedExecutor
{
    public async Task<string> RunAsync(string shellCommand, TimeSpan timeout)
    {
        // osascript に渡すAppleScriptを組み立てる
        // shellCommandはすでにShellQuote済みの値を含んでいる想定
        var escapedCommand = shellCommand.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var appleScript = $"do shell script \"{escapedCommand}\" with administrator privileges";

        using var cts = new CancellationTokenSource(timeout);
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo("osascript", $"-e \"{appleScript.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        proc.Start();

        try
        {
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
                throw new InvalidOperationException(
                    $"コマンドが失敗しました (終了コード {proc.ExitCode}): {stderr}");

            return stdout;
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"コマンドがタイムアウトしました ({timeout.TotalSeconds}秒)");
        }
    }

    /// <summary>
    /// シングルクオートで囲み、値中のシングルクオートをエスケープする。
    /// root権限で実行されるコマンドへの値埋め込みに必ず使用すること。
    /// </summary>
    public static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\\''") + "'";
}
