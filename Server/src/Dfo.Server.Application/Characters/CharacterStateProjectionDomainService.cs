using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Data.Game;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterStateProjectionDomainService(
    ICharacterDomainService characters, ICharacterSkillStateDomainService skills,
    ICharacterSkillPointDomainService points) : ICharacterStateProjectionDomainService
{
    public async ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken cancellationToken)
    {
        var character = characters.Get(result.Character.Identity.CharacterId);
        var identity = await character.IdentityAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character identity is unavailable.");
        var progress = await character.ProgressAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character progression is unavailable.");
        var stats = await character.BaseStatsAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character base stats are unavailable.");
        var facts = await skills.ReadAsync(character.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character skills are unavailable.");
        var ledger = await points.LedgerAsync(character.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Character skill ledger is unavailable.");
        return result with
        {
            Character = result.Character with { Identity = identity, Progression = progress },
            CharacterEntry = result.CharacterEntry with
            {
                Public = result.CharacterEntry.Public with
                {
                    DerivedStats = CharacterBaseStatProjection.Apply(result.CharacterEntry.Public.DerivedStats, stats),
                    Extras = result.CharacterEntry.Public.Extras with { CharacterExperience = progress.Experience },
                },
                Skills = result.CharacterEntry.Skills with
                {
                    Pages = facts.Pages,
                    SkillPointBasis = facts.PointBasis,
                    ProtocolMirror = ledger.ToProtocolMirror(),
                },
            },
        };
    }
}
