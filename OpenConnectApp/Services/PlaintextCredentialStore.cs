using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using OpenConnectApp.Interfaces;

namespace OpenConnectApp.Services;

/// <summary>
/// 平文JSONファイルにパスワードを保存する実装。
/// ファイル権限は 600 相当に設定する。
/// </summary>
public class PlaintextCredentialStore : ICredentialStore
{
    private readonly string _filePath;
    private readonly string _username;

    public PlaintextCredentialStore(string filePath, string username)
    {
        _filePath = filePath;
        _username = username;
    }

    public void Save(string username, string password)
    {
        var obj = new { password };
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            SetFilePermission600(_filePath);
    }

    public (string Username, string Password)? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("password", out var pw))
                return (_username, pw.GetString() ?? string.Empty);
            return null;
        }
        catch
        {
            return null;
        }
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    [System.Runtime.Versioning.SupportedOSPlatform("freebsd")]
    private static void SetFilePermission600(string path)
    {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
