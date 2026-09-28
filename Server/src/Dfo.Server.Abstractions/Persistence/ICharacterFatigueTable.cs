using Dfo.Server.Data.Characters;
namespace Dfo.Server.Abstractions.Persistence;

public sealed record CharacterFatigueRow(int Used, int ConsumedToday, DateTimeOffset UpdatedAt);
public interface ICharacterFatigueTable
{
    ValueTask<CharacterFatigueRow?> ReadAsync(CharacterId id, CancellationToken token);
    ValueTask<int> TryWriteAsync(CharacterId id, CharacterFatigueRow? expected, CharacterFatigueRow value, CancellationToken token);
}
