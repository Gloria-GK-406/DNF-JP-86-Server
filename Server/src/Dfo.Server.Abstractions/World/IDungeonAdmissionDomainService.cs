using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Data.Characters;

namespace Dfo.Server.Abstractions.World;

public abstract record DungeonAdmissionCost;
public sealed record DungeonGoldAdmissionCost(int Amount) : DungeonAdmissionCost;
public sealed record DungeonMaterialAdmissionCost(int ItemId, int Count, bool AllowSharedMaterial) : DungeonAdmissionCost;
public sealed record DungeonQuotaAdmissionCost(UsageQuotaKey Key, UsageQuotaValue? Expected, int Used) : DungeonAdmissionCost;

public sealed record DungeonAdmissionOptions
{
    public bool Mirror { get; init; }
    public IReadOnlyDictionary<CharacterId, int> GoldCosts { get; init; } = new Dictionary<CharacterId, int>();
    public bool RequireMinimumLevel { get; init; }
    public bool? ConsumesFatigue { get; init; }
    public IReadOnlyList<int> RequiredQuestIds { get; init; } = [];
    public IReadOnlyList<HellTicketAlternative>? HellTickets { get; init; }
    public MirrorDungeonCatalog? MirrorCatalog { get; init; }
    public IReadOnlyList<DungeonRequiredItem>? RequiredItemsOverride { get; init; }
    public DungeonAdmissionQuota? Quota { get; init; }
}
public sealed record DungeonAdmissionQuota(UsageQuotaFamily Family, DateOnly Period, int Maximum, int? DungeonId = null);

/// <summary>Issued by the admission service for one participant in one entry transaction.</summary>
public interface IDungeonAdmissionPermit
{
    DungeonRunParticipant Participant { get; }
    ImmutableArray<DungeonAdmissionCost> Costs { get; }
}
public interface IDungeonAdmissionBatch
{
    ImmutableArray<IDungeonAdmissionPermit> Permits { get; }
}
public interface IDungeonAdmissionReceipt
{
    IReadOnlyList<DungeonEntryInventoryChange> InventoryChanges { get; }
}
public sealed record DungeonAdmissionDecision(IDungeonAdmissionBatch? Batch, DungeonEntryAttempt? Refusal = null);
public sealed record DungeonAdmissionConsumption(IDungeonAdmissionReceipt? Receipt, DungeonEntryAttempt? Refusal = null);

public interface IDungeonAdmissionDomainService
{
    ValueTask<DungeonAdmissionDecision> PrepareAsync(DungeonEntryContext context, DungeonAdmissionOptions options, CancellationToken ct);
    ValueTask<DungeonAdmissionConsumption> ConsumeAsync(IDungeonAdmissionBatch batch, CancellationToken ct);
    DungeonEntryAttempt? BeginEntry(IDungeonAdmissionReceipt receipt, IReadOnlyList<DungeonRunParticipant> participants, int dungeonId);
    ValueTask<DungeonEntryAttempt> ExecuteAsync(DungeonEntryContext context, DungeonAdmissionOptions options,
        Func<IDungeonAdmissionReceipt, CancellationToken, ValueTask<DungeonEntryAttempt>> enter, CancellationToken ct);
}
