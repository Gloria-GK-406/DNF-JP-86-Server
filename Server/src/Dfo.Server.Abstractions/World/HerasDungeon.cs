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
public sealed record HerasQuotaSnapshot(int DungeonId, DateOnly Period, int Maximum, UsageQuotaValue? Entries, UsageQuotaValue? Bonus)
{
    public int Used => Entries?.UsedCount ?? 0;
    public int Granted => Bonus?.UsedCount ?? 0;
    public int Remaining => Math.Max(0, checked(Maximum + Granted - Used));
}
public interface IHerasContentSource { ValueTask<HerasDefinition> ReadAsync(CancellationToken ct); }
public interface IHerasRuleService
{
    int DungeonId { get; }
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
}
/// <summary>A character's daily Heras entries and the allowances coupons add to them.</summary>
/// <remarks>
/// Apart from entering the dungeon because the allowance is an effect, and entering reaches dungeon admission, which
/// reaches the towers and their rewards, which apply effects; an allowance that reached entering would close that loop.
/// </remarks>
public interface IHerasQuotaDomainService
{
    ValueTask<HerasQuotaSnapshot> ReadQuotaAsync(CharacterId character, CancellationToken ct);
    ValueTask<HerasQuotaSnapshot> ReadQuotaAtAsync(CharacterId character, DateTimeOffset at, CancellationToken ct);
    ValueTask ValidateAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct);
    ValueTask ApplyAllowanceAsync(EffectExecutionContext context, AddDungeonEntryAllowanceEffect effect, CancellationToken ct);
}
public interface IEntryAllowanceItemDomainService
{
    bool Supports(int itemId);
    ValueTask<ItemUseApplication?> TryUseAsync(SelectedSessionIdentity session, BusinessSlot source, ItemReference item, IEffectFootprintRecorder footprint, CancellationToken ct);
}
public interface IEntryAllowanceItemRuleService
{
    bool Supports(int itemId);
    ValueTask<StackableActionDefinition?> DefinitionAsync(int itemId, CancellationToken ct);
}
