using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Events;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.World;

public sealed record HerasDefinition(DungeonDefinition Dungeon, int DailyEntries);
public sealed record HerasQuotaSnapshot(DateOnly Period, int Maximum, UsageQuotaValue? Entries, UsageQuotaValue? Bonus)
{
    public int Used => Entries?.UsedCount ?? 0;
    public int Granted => Bonus?.UsedCount ?? 0;
    public int Remaining => Math.Max(0, checked(Maximum + Granted - Used));
}
public interface IHerasContentSource { ValueTask<HerasDefinition> ReadAsync(CancellationToken ct); }
public interface IHerasRuleService
{
    bool Supports(int dungeonId);
    int EventId { get; }
    int DailyEntries { get; }
    EventConfiguration InitialActivity { get; }
    ValueTask<HerasDefinition> DefinitionAsync(CancellationToken ct);
}
public interface IHerasDungeonDomainService
{
    bool Supports(int dungeonId);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct);
    ValueTask<HerasQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct);
    ValueTask<HerasQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken ct);
    ValueTask ValidateAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct);
    ValueTask ApplyAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct);
}
public interface IEntryAllowanceItemDomainService
{
    ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source, ItemReference item, IEffectFootprintRecorder footprint, CancellationToken ct);
}
public interface IEntryAllowanceItemRuleService
{
    bool Supports(int itemId);
    ValueTask<StackableActionDefinition?> DefinitionAsync(int itemId, CancellationToken ct);
}
