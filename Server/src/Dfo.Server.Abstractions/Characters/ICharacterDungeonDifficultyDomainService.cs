using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Characters;

public interface ICharacterDungeonDifficultyDomainService
{
    ValueTask<DungeonDifficulty> GetMaximumAsync(CharacterId characterId, int dungeonId, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<CharacterDungeonDifficulty>> GetRecordedAsync(CharacterId characterId, CancellationToken cancellationToken);
    ValueTask<DungeonDifficulty> UnlockAsync(CharacterId characterId, int dungeonId, DungeonDifficulty difficulty, CancellationToken cancellationToken);
}

public sealed record CharacterDungeonDifficulty(int DungeonId, DungeonDifficulty Maximum);
