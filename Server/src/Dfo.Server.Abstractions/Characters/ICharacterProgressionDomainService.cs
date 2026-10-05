using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterProgressionDomainService
{
    /// <summary>Set a level and its starting cumulative experience; reconcile skills in the same transaction.</summary>
    ValueTask<CharacterProgressionState> SetLevelAsync(CharacterId character, byte level, CancellationToken ct);
    /// <summary>Apply a decided progression pair, refusing a stale or missing character.</summary>
    ValueTask<bool> TryApplyAsync(CharacterId character, CharacterProgressionState expected,
        CharacterProgressionState next, CancellationToken ct);
}
