namespace OpenConnectApp.Models;

public class VpnConnection
{
    public string DisplayName { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string UserGroup { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string ServerCert { get; set; } = string.Empty;
}
