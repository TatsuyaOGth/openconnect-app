using OpenConnectApp.Services;

namespace OpenConnectApp.Tests;

public class AppConfigServiceTests
{
    [Fact]
    public void CsvPath_UsesConnectionsCsv()
    {
        var service = new AppConfigService();

        Assert.EndsWith(Path.Combine("OpenConnectApp", "connections.csv"), service.CsvPath);
    }
}
