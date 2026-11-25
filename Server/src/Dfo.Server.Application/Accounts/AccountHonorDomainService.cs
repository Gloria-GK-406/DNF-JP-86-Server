using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Application.Accounts;

internal sealed class AccountHonorDomainService(IAccountTable accounts, IExperienceRuleService experience)
    : IAccountHonorDomainService
{
    public async ValueTask<AccountHonorLevel> ReadAsync(AccountId accountId, CancellationToken cancellationToken)
    {
        var row = await accounts.FindExperienceAsync(accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Honor account {accountId.Value} is unavailable.");
        return row.HonourExperience <= 0
            ? AccountHonorLevel.Initial
            : experience.Progression().Honor.ProgressFor(checked((ulong)row.HonourExperience));
    }
}
