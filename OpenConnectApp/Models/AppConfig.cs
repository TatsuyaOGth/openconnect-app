namespace OpenConnectApp.Models;

public class AppConfig
{
    public string CommonUsername { get; set; } = string.Empty;

    /// <summary>"keychain" または "plaintext"</summary>
    public string CredentialStoreType { get; set; } = "keychain";

    public string? OpenConnectPath { get; set; }
    public string? VpncScriptPath { get; set; }

    /// <summary>最後に接続した接続先のDisplayName。状態復元用。</summary>
    public string? LastConnectedName { get; set; }

    /// <summary>
    /// 自動ピン留め（TOFU）した接続先ホスト→サーバ証明書 pin の対応表。
    /// 初回接続時にサーバ証明書から計算した `pin-sha256:...` を保存し、以後の検証に使う。
    /// </summary>
    public Dictionary<string, string> ServerCertPins { get; set; } = new();
}
