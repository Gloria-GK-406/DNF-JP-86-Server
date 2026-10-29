using System.Collections.Immutable;
using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Infrastructure.Content;

internal sealed class PvfLostLandConsumableDefinitionSource(PvfArchiveProvider provider)
    : ILostLandConsumableDefinitionSource
{
    private readonly Lazy<IReadOnlyDictionary<int, string>> paths = new(() =>
        PvfListFile.Read(provider.Archive, "stackable/stackable.lst").ToDictionary(row => row.Id, row => row.RelativePath));
    public ValueTask<LostLandConsumableDefinition?> GetAsync(int itemId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var archive = provider.Archive;
        if (!paths.Value.TryGetValue(itemId, out var path)) return ValueTask.FromResult<LostLandConsumableDefinition?>(null);
        var document = PvfDocument.Open(archive, "stackable/" + path).AtRoot();
        if (!document.TryRead(PvfField.Int32List("[action usable dungeon]", optional: true), out var dungeons)
            || dungeons.Count != 1 || dungeons[0] != 4000
            || !document.TryRead(PvfField.Tokens("[action type]", optional: true), out var action)
            || action.Count == 0 || !action[0].IsString)
            return ValueTask.FromResult<LostLandConsumableDefinition?>(null);
        var expected = archive.ResolveString(action[0].Value);
        if (expected is not ("[riding]" or "[move map]"))
            return ValueTask.FromResult<LostLandConsumableDefinition?>(null);
        var transformation = expected == "[riding]";
        if (action.Count != (transformation ? 3 : 6)
            || action.Skip(1).Any(value => value.Kind != PvfTokenKind.Int32))
            throw new InvalidDataException("Invalid LostLand consumable declaration.");
        if (transformation && (action[1].Value <= 0 || action[2].Value <= 0))
            throw new InvalidDataException("Invalid LostLand transformation declaration.");
        MazeCell? target = null;
        if (!transformation)
        {
            if (action[1].Value is < 0 or > 9 || action[2].Value is < 0 or > 9
                || action[3].Value < 0 || action[4].Value < 0 || action[5].Value != 0)
                throw new InvalidDataException("Invalid LostLand capsule destination.");
            target = new(checked((byte)action[1].Value), checked((byte)action[2].Value));
        }
        var disabled = document.TryRead(PvfField.Int32List("[action disable map]", optional: true), out var maps)
            ? maps.ToImmutableArray() : [];
        return ValueTask.FromResult<LostLandConsumableDefinition?>(new(target is null
            ? LostLandConsumableKind.Transformation : LostLandConsumableKind.Teleport, target, disabled));
    }
}
