using System.Reflection;
using OpenConnectApp.Services;

namespace OpenConnectApp.Tests;

public class KeychainCredentialStoreTests
{
    [Fact]
    public void ExtractValue_ParsesSecurityOutputValue()
    {
        var method = typeof(KeychainCredentialStore).GetMethod(
            "ExtractValue",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var value = method!.Invoke(null, new object[] { "\"acct\"<blob>=\"alice\"" });

        Assert.Equal("alice", value);
    }
}
