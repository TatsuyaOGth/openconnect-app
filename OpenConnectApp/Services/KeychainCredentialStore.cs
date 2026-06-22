using System.Diagnostics;
using OpenConnectApp.Interfaces;

namespace OpenConnectApp.Services;

/// <summary>
/// macOS実装: `security` コマンド経由でKeychainを操作する。
/// </summary>
public class KeychainCredentialStore : ICredentialStore
{
    private const string ServiceName = "OpenConnectApp";

    public void Save(string username, string password)
    {
        Run($"security add-generic-password -U -s {ShellQuote(ServiceName)} -a {ShellQuote(username)} -w {ShellQuote(password)}");
    }

    public (string Username, string Password)? Load()
    {
        // config.jsonからユーザー名を別途取得する必要があるため、
        // ここではユーザー名をKeychainから探す。
        // security find-generic-password はアカウント名なしでも検索できる。
        try
        {
            var output = Run($"security find-generic-password -s {ShellQuote(ServiceName)} -g 2>&1");
            string? username = null;
            string? password = null;

            foreach (var line in output.Split('\n'))
            {
                if (line.TrimStart().StartsWith("\"acct\"<blob>="))
                    username = ExtractValue(line);
                else if (line.TrimStart().StartsWith("password:"))
                    password = line.Substring(line.IndexOf('"') + 1).TrimEnd('"');
            }

            if (username != null && password != null)
                return (username, password);
            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        try
        {
            Run($"security delete-generic-password -s {ShellQuote(ServiceName)}");
        }
        catch
        {
            // 存在しない場合は無視
        }
    }

    private static string ExtractValue(string line)
    {
        var idx = line.IndexOf('"');
        if (idx < 0) return string.Empty;
        var val = line.Substring(idx + 1).TrimEnd('"');
        return val;
    }

    private static string Run(string args)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo("bash", $"-c \"{args.Replace("\"", "\\\"")}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        proc.Start();
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"security コマンドが失敗しました: {stderr}");

        return stdout + stderr;
    }

    internal static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\\''") + "'";
}
