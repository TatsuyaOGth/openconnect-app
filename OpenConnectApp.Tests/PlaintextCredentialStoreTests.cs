using OpenConnectApp.Services;

namespace OpenConnectApp.Tests;

public class PlaintextCredentialStoreTests
{
    [Fact]
    public void SaveLoadClear_RoundTripsCredentials()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ocgui-{Guid.NewGuid():N}.json");
        var store = new PlaintextCredentialStore(path, "alice");

        try
        {
            store.Save("alice", "secret");

            Assert.True(File.Exists(path));
            var creds = store.Load();
            Assert.NotNull(creds);
            Assert.Equal("alice", creds!.Value.Username);
            Assert.Equal("secret", creds.Value.Password);

            store.Clear();
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
