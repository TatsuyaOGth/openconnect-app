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
}
