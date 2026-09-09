using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Characters;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterSkillStateDomainService(
    ICharacterTable characters, ICharacterSkillTable skills, ICharacterEntryTable entries)
    : ICharacterSkillStateDomainService
{
    public async ValueTask<CharacterSkillFacts?> ReadAsync(CharacterId characterId, CancellationToken cancellationToken)
    {
        if (await characters.FindStandingAsync(characterId, cancellationToken).ConfigureAwait(false) is not { } standing)
            return null;
        var pages = CharacterSkillPages.Pages(CharacterSkillPages.Rows(
            await skills.ReadEntryAsync(characterId, cancellationToken).ConfigureAwait(false)));
        var bonus = await entries.FindBonusPointsAsync(characterId, cancellationToken).ConfigureAwait(false);
        return new CharacterSkillFacts(
            CharacterCareer.FromPacked(checked((byte)standing.Job), checked((byte)standing.GrowType)),
            standing.Level, pages, new CharacterSkillPointBasisState(bonus?.BonusSp ?? 0, bonus?.BonusTp ?? 0));
    }
}
