using System.Text;
using OpenConnectApp.Models;
using OpenConnectApp.Services;

namespace OpenConnectApp.Tests;

public class CsvServiceTests
{
    [Fact]
    public void LoadOrCreate_CreatesTemplateWhenMissing()
    {
        var service = new CsvService();
        var path = Path.Combine(Path.GetTempPath(), $"ocgui-{Guid.NewGuid():N}.csv");

        try
        {
            var (connections, wasCreated) = service.LoadOrCreate(path);

            Assert.True(wasCreated);
            Assert.True(File.Exists(path));
            Assert.Single(connections);
            Assert.Equal("Example", connections[0].DisplayName);
            Assert.Equal("vpn.example.com", connections[0].Host);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void ValidateAndParse_ParsesQuotedFields()
    {
        var service = new CsvService();
        var path = Path.Combine(Path.GetTempPath(), $"ocgui-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            path,
            """
            DisplayName,Host,UserGroup,Protocol,ServerCert
            "Office, VPN",vpn.example.com,"group,1",anyconnect,pin-sha256:abc
            """,
            Encoding.UTF8);

        try
        {
            var connections = service.ValidateAndParse(path);

            Assert.Single(connections);
            Assert.Equal("Office, VPN", connections[0].DisplayName);
            Assert.Equal("vpn.example.com", connections[0].Host);
            Assert.Equal("group,1", connections[0].UserGroup);
            Assert.Equal("anyconnect", connections[0].Protocol);
            Assert.Equal("pin-sha256:abc", connections[0].ServerCert);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateAndParse_RejectsBlankDisplayNameOrHost()
    {
        var service = new CsvService();
        var path = Path.Combine(Path.GetTempPath(), $"ocgui-{Guid.NewGuid():N}.csv");
        File.WriteAllText(
            path,
            """
            DisplayName,Host,UserGroup,Protocol,ServerCert
            ,vpn.example.com,,,
            """,
            Encoding.UTF8);

        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => service.ValidateAndParse(path));
            Assert.Contains("行番号: 2", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ValidateAndParse_SupportsShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var service = new CsvService();
        var path = Path.Combine(Path.GetTempPath(), $"ocgui-{Guid.NewGuid():N}.csv");
        var csv = """
                  DisplayName,Host,UserGroup,Protocol,ServerCert
                  社内VPN,vpn.example.com,,,
                  """;
        File.WriteAllBytes(path, Encoding.GetEncoding(932).GetBytes(csv));

        try
        {
            var connections = service.ValidateAndParse(path);

            Assert.Single(connections);
            Assert.Equal("社内VPN", connections[0].DisplayName);
            Assert.Equal("vpn.example.com", connections[0].Host);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
