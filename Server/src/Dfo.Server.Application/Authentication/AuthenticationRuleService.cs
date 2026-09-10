using Microsoft.Extensions.Configuration;
using Dfo.Server.Data.Accounts;
using System.Collections.Frozen;
using System.Globalization;

namespace Dfo.Server.Application.Authentication;

internal sealed class AuthenticationRuleService(FrozenSet<int> allowedAccountIds)
{
    public static AuthenticationRuleService From(IConfiguration configuration)
    {
        var section = configuration.GetSection("LauncherAuth:LegacyMd5Migration");
        var list = section.GetSection("AllowedAccountIds");
        if (section.Value is not null || list.Value is not null
            || section.GetChildren().Any(child => !child.Key.Equals("AllowedAccountIds", StringComparison.OrdinalIgnoreCase)))
            throw InvalidConfiguration();
        var ids = new HashSet<int>();
        foreach (var entry in list.GetChildren())
        {
            if (entry.GetChildren().Any() || !int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _)
                || !int.TryParse(entry.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                || id <= 0 || !ids.Add(id))
                throw InvalidConfiguration();
        }
        return new(ids.ToFrozenSet());
    }

    public bool MayMigrate(AccountId accountId) => allowedAccountIds.Contains(accountId.Value);
    public IReadOnlyDictionary<string, string> Validate(string loginId, string password, bool registration) =>
        AuthenticationInputRules.Validate(loginId, password, registration);

    private static InvalidOperationException InvalidConfiguration() =>
        new("Legacy MD5 migration requires a list of distinct positive AccountIds.");
}
