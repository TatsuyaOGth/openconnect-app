using OpenConnectApp.Models;

namespace OpenConnectApp.Services;

/// <summary>
/// openconnect / vpnc-script の実行パスを自動検出し、config.json にキャッシュする。
/// </summary>
public class PathDetectionService
{
    private static readonly string[] OpenConnectCandidates =
    [
        "/opt/homebrew/bin/openconnect",
        "/usr/local/bin/openconnect",
    ];

    private static readonly string[] VpncScriptCandidates =
    [
        "/opt/homebrew/etc/vpnc-script",
        "/usr/local/etc/vpnc-script",
        "/etc/vpnc/vpnc-script",
    ];

    private readonly AppConfigService _configService;
    private readonly LogService _logger;

    public PathDetectionService(AppConfigService configService, LogService logger)
    {
        _configService = configService;
        _logger = logger;
    }

    /// <summary>
    /// 設定にパスが未設定または存在しない場合のみ自動検出し、config.json を更新する。
    /// </summary>
    public AppConfig DetectAndCache(AppConfig config)
    {
        bool changed = false;

        if (string.IsNullOrEmpty(config.OpenConnectPath) || !File.Exists(config.OpenConnectPath))
        {
            var found = OpenConnectCandidates.FirstOrDefault(File.Exists);
            config.OpenConnectPath = found;
            _logger.Log($"openconnect 自動検出: {found ?? "未検出"}");
            changed = true;
        }

        if (string.IsNullOrEmpty(config.VpncScriptPath) || !File.Exists(config.VpncScriptPath))
        {
            var found = VpncScriptCandidates.FirstOrDefault(File.Exists);
            config.VpncScriptPath = found;
            _logger.Log($"vpnc-script 自動検出: {found ?? "未検出"}");
            changed = true;
        }

        if (changed)
            _configService.Save(config);

        return config;
    }
}
