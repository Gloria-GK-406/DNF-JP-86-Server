using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterCareerDomainService(
    ICharacterTable characters, ICharacterStatRuleService stats,
    ICharacterSkillStateDomainService skills, IDomainTransactionAccessor transactions) : ICharacterCareerDomainService
{
    public async ValueTask AdvanceAsync(AccountId accountId, CharacterId characterId, byte growType, CancellationToken cancellationToken)
    {
        if (transactions.Current is null)
            throw new InvalidOperationException("Career advancement requires a domain transaction.");
        if (await characters.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false) != accountId
            || await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } standing)
            throw new InvalidOperationException("Career advancement character is unavailable.");
        var before = CharacterCareer.FromPacked(checked((byte)standing.Job), standing.GrowType);
        if (growType == 0 || before.GrowType != 0 || before.Awakening != 0)
            throw new InvalidOperationException("Only an unadvanced character can receive a first advancement.");
        var after = new CharacterCareer(before.Job, growType);
        await stats.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _ = stats.GetStats(after, standing.Level);
        if (await characters.SetGrowTypeAsync(characterId, before.PackedGrowType, after.PackedGrowType,
                cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("Career changed during advancement.");
        await skills.ResetAllAsync(characterId, cancellationToken).ConfigureAwait(false);
    }
}
