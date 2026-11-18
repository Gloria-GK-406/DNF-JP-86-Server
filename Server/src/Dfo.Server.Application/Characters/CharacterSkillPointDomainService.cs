using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;
namespace Dfo.Server.Application.Characters;

// Ordinary PVE pages only. Free-duel unlimited points remain a separate entry policy.
internal sealed class CharacterSkillPointDomainService(
    CharacterSkillDomainService skills,
    ILogger<CharacterSkillPointDomainService> logger) : ICharacterSkillPointDomainService
{
    public async ValueTask<CharacterSkillPointLedger?> LedgerAsync(CharacterId characterId, CancellationToken cancellationToken)
    {
        if (await skills.ReadOrdinaryLedgerAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } ledger)
            return null;
        if (!ledger.IsComplete)
            logger.LogWarning("Character {CharacterId} has unknown learned skill rules {MissingSkillIds}; ledger is incomplete",
                characterId.Value, string.Join(",", ledger.MissingSkillIds));
        return ledger;
    }

    public async ValueTask<CharacterSkillProtocolMirrorState?> RemainingAsync(
        CharacterId characterId, CancellationToken cancellationToken) =>
        (await LedgerAsync(characterId, cancellationToken).ConfigureAwait(false))?.ToProtocolMirror();
}
