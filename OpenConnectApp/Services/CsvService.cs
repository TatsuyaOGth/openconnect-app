using System.Text;
using OpenConnectApp.Models;

namespace OpenConnectApp.Services;

public class CsvService
{
    private static readonly string CsvTemplate =
        "DisplayName,Host,UserGroup,Protocol,ServerCert\nExample,vpn.example.com,,,\n";

    /// <summary>
    /// CSVを読み込む。ファイルが存在しない場合はテンプレートを作成して例外をスロー。
    /// </summary>
    public (List<VpnConnection> Connections, bool WasCreated) LoadOrCreate(string csvPath)
    {
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, CsvTemplate, Encoding.UTF8);
            return (ParseCsv(CsvTemplate), true);
        }

        var content = ReadWithFallback(csvPath);
        return (ParseCsv(content), false);
    }

    /// <summary>
    /// CSVを検証し、問題なければ接続先リストを返す。
    /// 検証エラーがある場合は例外をスロー（メッセージに問題行番号を含む）。
    /// </summary>
    public List<VpnConnection> ValidateAndParse(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"CSVファイルが見つかりません: {csvPath}");

        var content = ReadWithFallback(csvPath);
        return ParseCsv(content);
    }

    private static string ReadWithFallback(string path)
    {
        // 1. BOM付きUTF-8
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // 2. BOMなしUTF-8として試行
        try
        {
            var utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return utf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // 3. Shift-JIS (CP932)
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding(932);
            return sjis.GetString(bytes);
        }
    }

    private static List<VpnConnection> ParseCsv(string content)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.TrimEnd('\r'))
                           .ToArray();

        if (lines.Length < 2)
            return [];

        var errorLines = new List<int>();
        var connections = new List<VpnConnection>();

        // 0行目はヘッダー、1行目以降がデータ
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cols = SplitCsvLine(line);
            var displayName = cols.ElementAtOrDefault(0)?.Trim() ?? string.Empty;
            var host = cols.ElementAtOrDefault(1)?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(host))
            {
                errorLines.Add(i + 1); // 1-indexed（ヘッダーを含む行番号）
                continue;
            }

            connections.Add(new VpnConnection
            {
                DisplayName = displayName,
                Host = host,
                UserGroup = cols.ElementAtOrDefault(2)?.Trim() ?? string.Empty,
                Protocol = cols.ElementAtOrDefault(3)?.Trim() ?? string.Empty,
                ServerCert = cols.ElementAtOrDefault(4)?.Trim() ?? string.Empty,
            });
        }

        if (errorLines.Count > 0)
            throw new InvalidDataException(
                $"DisplayName または Host が空の行があります（行番号: {string.Join(", ", errorLines)}）。インポートを中止します。");

        return connections;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString());
        return result.ToArray();
    }
}
