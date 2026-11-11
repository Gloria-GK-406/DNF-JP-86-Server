using Dfo.Server.Testing;
using System.Security.Cryptography;
using System.Text;

namespace Dfo.Server.HttpApi.Tests;

public sealed class GateOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t ")]
    public void MissingOrBlankTokenLeavesTheGateUnconfigured(string? raw)
    {
        var options = GateTestOptions.Create(raw);

        Assert.False(options.TokenConfigured);
        Assert.False(options.Matches(""));
        Assert.False(options.Matches(GateTestToken.New()));
    }

    [Fact]
    public void ConfiguredTokenMatchesOnlyItsExactValue()
    {
        var token = GateTestToken.New();
        var options = GateTestOptions.Create(token);

        Assert.True(options.TokenConfigured);
        Assert.True(options.Matches(token));
        Assert.False(options.Matches(token.AsSpan(0, token.Length - 1)));
        Assert.False(options.Matches(token + "x"));
        Assert.False(options.Matches(token.ToUpperInvariant() == token ? token.ToLowerInvariant() : token.ToUpperInvariant()));
        Assert.False(options.Matches(""));
    }

    [Fact]
    public void SurroundingWhitespaceIsTrimmedFromTheConfiguredTokenButNotFromThePresentedOne()
    {
        var token = GateTestToken.New();
        var options = GateTestOptions.Create("  " + token + " \t");

        Assert.True(options.Matches(token));
        Assert.False(options.Matches(" " + token));
        Assert.False(options.Matches(token + " "));
    }

    [Fact]
    public void ThirtyTwoPrintableCharactersAreTheShortestAcceptedToken()
    {
        var token = GateTestToken.New()[..32];

        Assert.True(GateTestOptions.Create(token).Matches(token));
    }

    public static TheoryData<string, string> InvalidTokens()
    {
        var token = GateTestToken.New();
        return new TheoryData<string, string>
        {
            { token[..31], "too short" },
            { token[..16] + " " + token[16..], "unsupported characters" },
            { token[..16] + "é" + token[16..], "unsupported characters" },
            { token[..16] + "中" + token[16..], "unsupported characters" },
            { token[..16] + "\u007f" + token[16..], "unsupported characters" },
            { token[..16] + "\t" + token[16..], "unsupported characters" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public void InvalidTokenFailsStartupNamingTheKeyButNotTheValue(string token, string reason)
    {
        var error = Assert.Throws<InvalidOperationException>(() => GateTestOptions.Create(token));

        Assert.Contains("LauncherAuth:GateToken", error.Message, StringComparison.Ordinal);
        Assert.Contains(reason, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token.Trim(), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(token[..8], error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultsApplyWhenOnlyTheTokenIsConfigured()
    {
        var options = GateTestOptions.Create(GateTestToken.New());

        Assert.Equal(10, options.AuthFailureLimit);
        Assert.Equal(TimeSpan.FromSeconds(900), options.AuthFailureWindow);
        Assert.Equal("a21", options.Launch);
        Assert.Null(options.AdminUrl);
    }

    [Fact]
    public void ConfiguredLimitsLaunchAndAdminUrlAreRead()
    {
        var options = GateTestOptions.Create(GateTestToken.New(), new Dictionary<string, string?>
        {
            [GateOptions.AuthFailureLimitKey] = " 3 ",
            [GateOptions.AuthFailureWindowKey] = "60",
            [GateOptions.LaunchKey] = "  b1  ",
            [GateOptions.AdminUrlKey] = " https://admin.example.test/gm/ ",
        });

        Assert.Equal(3, options.AuthFailureLimit);
        Assert.Equal(TimeSpan.FromSeconds(60), options.AuthFailureWindow);
        Assert.Equal("b1", options.Launch);
        Assert.Equal("https://admin.example.test/gm/", options.AdminUrl);
    }

    [Theory]
    [InlineData(GateOptions.LaunchKey, "   ")]
    [InlineData(GateOptions.AdminUrlKey, "   ")]
    public void BlankLaunchAndAdminUrlFallBack(string key, string value)
    {
        var options = GateTestOptions.Create(null, new Dictionary<string, string?> { [key] = value });

        Assert.Equal("a21", options.Launch);
        Assert.Null(options.AdminUrl);
    }

    [Theory]
    [InlineData(GateOptions.AuthFailureLimitKey, "0")]
    [InlineData(GateOptions.AuthFailureLimitKey, "-1")]
    [InlineData(GateOptions.AuthFailureLimitKey, "ten")]
    [InlineData(GateOptions.AuthFailureLimitKey, "1.5")]
    [InlineData(GateOptions.AuthFailureWindowKey, "0")]
    [InlineData(GateOptions.AuthFailureWindowKey, "-900")]
    [InlineData(GateOptions.AuthFailureWindowKey, "15m")]
    [InlineData(GateOptions.AdminUrlKey, "not a url")]
    [InlineData(GateOptions.AdminUrlKey, "/gm/")]
    [InlineData(GateOptions.AdminUrlKey, "ftp://admin.example.test/gm/")]
    public void InvalidLimiterOrAdminValuesFailStartupNamingTheKey(string key, string value)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            GateTestOptions.Create(null, new Dictionary<string, string?> { [key] = value }));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringCarriesNeitherTheTokenNorItsDigest()
    {
        var token = GateTestToken.New();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        var text = GateTestOptions.Create(token).ToString();

        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(digest), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(digest), text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Byte[]", text, StringComparison.Ordinal);
        Assert.Contains("TokenConfigured", text, StringComparison.Ordinal);
    }
}
