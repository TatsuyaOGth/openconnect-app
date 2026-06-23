using OpenConnectApp.Services;

namespace OpenConnectApp.Tests;

public class ShellQuoteTests
{
    [Theory]
    [InlineData("simple", "'simple'")]
    [InlineData("a'b", "'a'\\''b'")]
    [InlineData("", "''")]
    public void ShellQuote_EscapesSingleQuotes(string input, string expected)
    {
        Assert.Equal(expected, OsascriptPrivilegedExecutor.ShellQuote(input));
    }
}
