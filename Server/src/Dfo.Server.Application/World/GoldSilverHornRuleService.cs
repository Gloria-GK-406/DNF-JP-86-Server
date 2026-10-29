using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Events;

namespace Dfo.Server.Application.World;

internal sealed class GoldSilverHornRuleService(IDungeonDefinitionSource content,
    IGoldSilverHornContentSource settings) : IGoldSilverHornRuleService
{
    public const int Dungeon = 4123;
    public int DungeonId => Dungeon;
    internal const int ActivityId = 2332;
    public bool Supports(int dungeonId) => dungeonId == Dungeon;
    public int EventId => ActivityId;
    public int MaximumEntries => settings.Load() is { DungeonId: Dungeon, DailyEntries: > 0 and <= ushort.MaxValue } value
        ? value.DailyEntries : throw new InvalidDataException("Gold/silver horn configuration does not describe the current dungeon.");
    public EventConfiguration InitialActivity => new(0, "permanent", true, "新·金角银角", "新·金角银角",
        "单人挑战金角银角；免费入场，次数按本服日切刷新。", "", "", DateTimeOffset.UnixEpoch,
        new DateTimeOffset(2037, 12, 31, 23, 59, 59, TimeSpan.FromHours(8)));

    public async ValueTask<DungeonDefinition> DefinitionAsync(CancellationToken ct)
    {
        _ = MaximumEntries;
        var definition = await content.GetAsync(Dungeon, ct).ConfigureAwait(false);
        if (definition is null || definition.ConsumesFatigue || !definition.NoGiveupPenalty
            || definition.PartyLimit != 1 || !definition.ProhibitsPractice || definition.ExperienceWeight != 0
            || definition.SupportedDifficulties is not { Count: 1 } tiers || tiers[0] != 2
            || definition.RequiredItems.Count != 0 || definition.EventRequiredItems.Count != 0)
            throw new InvalidDataException("Gold/silver horn dungeon content is incomplete or incompatible.");
        return definition;
    }
}
