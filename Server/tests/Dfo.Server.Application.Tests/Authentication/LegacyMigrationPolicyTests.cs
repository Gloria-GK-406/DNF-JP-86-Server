using Dfo.Server.Application.Authentication;
using Dfo.Server.Data.Accounts;
using Microsoft.Extensions.Configuration;

namespace Dfo.Server.Application.Tests.Authentication;

public sealed class LegacyMigrationPolicyTests
{
    [Fact]
    public void OnlyExplicitPositiveAccountIdsAreAuthorizedAndMissingMeansNobody()
    {
        Assert.False(AuthenticationRuleService.From(new ConfigurationBuilder().Build()).MayMigrate(new AccountId(1)));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "41" }).Build();
        var rules = AuthenticationRuleService.From(config);
        Assert.True(rules.MayMigrate(new AccountId(41)));
        Assert.False(rules.MayMigrate(new AccountId(42)));
    }

    [Theory]
    [InlineData("AllowedAccountIds:0", "*")]
    [InlineData("AllowedAccountIds:0", "1-5")]
    [InlineData("AllowedAccountIds:0", "0")]
    [InlineData("AllowedAccountIds:0", "-1")]
    [InlineData("AllowedAccountIds:0", "2147483648")]
    [InlineData("AllowedAccountIds:0", "name")]
    [InlineData("AllowedAccountIds:0", " 41 ")]
    [InlineData("AllowedAccountIds:0", "")]
    [InlineData("AllowedAccountIds", "*")]
    [InlineData("Enabled", "true")]
    [InlineData("All", "false")]
    public void InvalidOrBroadMigrationConfigurationFailsClosed(string key, string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { [$"LauncherAuth:LegacyMd5Migration:{key}"] = value }).Build();
        var exception = Assert.Throws<InvalidOperationException>(() => AuthenticationRuleService.From(config));
        Assert.Equal("Legacy MD5 migration requires a list of distinct positive AccountIds.", exception.Message);
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:0"] = "41",
            ["LauncherAuth:LegacyMd5Migration:AllowedAccountIds:1"] = "41",
        }).Build();
        Assert.Throws<InvalidOperationException>(() => AuthenticationRuleService.From(config));
    }
}
