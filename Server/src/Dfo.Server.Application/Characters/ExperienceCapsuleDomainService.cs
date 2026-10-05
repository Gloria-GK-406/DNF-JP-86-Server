using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;

namespace Dfo.Server.Application.Characters;

internal sealed class ExperienceCapsuleDomainService(IAccountTable accounts, IExperienceRuleService experienceRules,
    IDungeonEffectRuleService rules, IDomainTransactionAccessor transactions) : IExperienceCapsuleDomainService
{
    public async ValueTask<DungeonHonourOutcome> AddAsync(AccountId account, uint experience, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Capsule mutations require a domain transaction.");
        var current = await accounts.FindExperienceAsync(account, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Experience capsule account is unavailable.");
        var result = rules.AccumulateHonour(experience, checked((ulong)Math.Max(0, current.HonourExperience)),
            checked((uint)Math.Max(0, current.GrowthCapsuleExperience)), experienceRules.Progression());
        if (experience > 0 && await accounts.SetExperienceAsync(account, checked((long)result.TotalHonor), result.TotalCapsule, ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Experience capsule account refused the write.");
        return result;
    }
}
