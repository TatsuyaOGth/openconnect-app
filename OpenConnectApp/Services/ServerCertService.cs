using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OpenConnectApp.Services;

/// <summary>
/// サーバ証明書の公開鍵ピン（openconnect の <c>--servercert pin-sha256:...</c> と同形式）を
/// 取得するサービス。外部依存なしに .NET の TLS で接続し、提示された証明書から計算する。
///
/// pin-sha256 は証明書の SubjectPublicKeyInfo(SPKI/DER) の SHA-256 を base64 化した値
/// （RFC 7469 / HPKP と同じ）で、openconnect の pin-sha256 と一致する。
/// </summary>
public class ServerCertService
{
    /// <summary>
    /// host（"host" または "host:port"、既定ポート 443）へ TLS 接続し、提示された
    /// サーバ証明書から <c>pin-sha256:...</c> を計算して返す。証明書の検証は行わない
    /// （TOFU: 提示された証明書をそのままピン化する）。
    /// </summary>
    public async Task<string> GetPinSha256Async(string host, TimeSpan timeout)
    {
        var (hostName, port) = ParseHostPort(host);

        using var cts = new CancellationTokenSource(timeout);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(hostName, port, cts.Token);

        X509Certificate2? captured = null;
        using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
            {
                // 検証はせず証明書だけ捕捉する。
                if (cert != null)
                    captured = new X509Certificate2(cert);
                return true;
            });

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = hostName, // SNI
        };
        await ssl.AuthenticateAsClientAsync(options, cts.Token);

        if (captured == null)
            throw new InvalidOperationException("サーバ証明書を取得できませんでした。");

        using (captured)
            return ComputePin(captured);
    }

    /// <summary>X509 証明書から <c>pin-sha256:...</c> を計算する。</summary>
    public static string ComputePin(X509Certificate2 cert)
    {
        // SubjectPublicKeyInfo(DER) の SHA-256 → base64。
        var spki = cert.PublicKey.ExportSubjectPublicKeyInfo();
        var hash = SHA256.HashData(spki);
        return "pin-sha256:" + Convert.ToBase64String(hash);
    }

    private static (string host, int port) ParseHostPort(string host)
    {
        var trimmed = host.Trim();

        // IPv6 リテラル: [::1]:443 / [::1]
        if (trimmed.StartsWith('['))
        {
            var end = trimmed.IndexOf(']');
            if (end > 0)
            {
                var addr = trimmed.Substring(1, end - 1);
                var rest = trimmed[(end + 1)..];
                if (rest.StartsWith(':') && int.TryParse(rest[1..], out var p6))
                    return (addr, p6);
                return (addr, 443);
            }
        }

        // host:port（":" がちょうど1個のときだけポート指定とみなす。生の IPv6 は除外）
        var idx = trimmed.LastIndexOf(':');
        if (idx > 0 && trimmed.IndexOf(':') == idx
            && int.TryParse(trimmed[(idx + 1)..], out var port))
        {
            return (trimmed[..idx], port);
        }

        return (trimmed, 443);
    }
}
