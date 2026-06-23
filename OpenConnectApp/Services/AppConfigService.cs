using System.Text.Json;
using System.Text.Json.Serialization;
using OpenConnectApp.Models;

namespace OpenConnectApp.Services;

public class AppConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string AppDataDir { get; }

    public AppConfigService()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AppDataDir = Path.Combine(home, "Library", "Application Support", "OpenConnectApp");
        Directory.CreateDirectory(AppDataDir);
    }

    public string ConfigPath => Path.Combine(AppDataDir, "config.json");
    public string CsvPath => Path.Combine(AppDataDir, "connections.csv");
    public string PidFilePath => Path.Combine(AppDataDir, "openconnect.pid");
    public string LogPath => Path.Combine(AppDataDir, "app.log");
    public string PlainCredentialPath => Path.Combine(AppDataDir, "credentials.plain.json");

    public AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
            return new AppConfig();

        try
        {
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(ConfigPath, json);
    }
}
