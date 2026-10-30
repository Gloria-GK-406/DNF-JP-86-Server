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
    /// <summary>False only for domains that have already validated their own independent difficulty progression.</summary>
    public bool EnforceOrdinaryDifficulty { get; init; } = true;
    public bool? ConsumesFatigue { get; init; }
    public IReadOnlyList<int> RequiredQuestIds { get; init; } = [];
    public IReadOnlyList<HellTicketAlternative>? HellTickets { get; init; }
    public MirrorDungeonCatalog? MirrorCatalog { get; init; }
    public IReadOnlyList<DungeonRequiredItem>? RequiredItemsOverride { get; init; }
    public DungeonAdmissionQuota? Quota { get; init; }
}
/// <summary>The daily entry counter an admission charges. Only dungeon entry families have one.</summary>
public sealed record DungeonAdmissionQuota(UsageQuotaFamily Family, DateOnly Period, int Maximum, int? DungeonId = null)
{
    /// <summary>Whether this family counts dungeon entries; the others are not admission quotas.</summary>
    public static bool CountsEntries(UsageQuotaFamily family) => family switch
    {
        UsageQuotaFamily.MirrorDungeon or UsageQuotaFamily.DimensionDungeon or UsageQuotaFamily.DespairTower
            or UsageQuotaFamily.AltarDungeon or UsageQuotaFamily.TournamentDungeon or UsageQuotaFamily.HerasDungeon
            or UsageQuotaFamily.GoldSilverHorn or UsageQuotaFamily.LostLand => true,
        UsageQuotaFamily.Consumable or UsageQuotaFamily.Premium or UsageQuotaFamily.ReviveCoin
            or UsageQuotaFamily.GiantDragonCrystalExchange => false,
    };

    /// <summary>The stored counter an entry of this family charges.</summary>
    public UsageQuotaKey Key => Family switch
    {
        UsageQuotaFamily.MirrorDungeon => UsageQuotaKey.Mirror("entries", Period),
        UsageQuotaFamily.DimensionDungeon => UsageQuotaKey.Dimension(Period),
        UsageQuotaFamily.DespairTower => UsageQuotaKey.Despair("entries", Period),
        UsageQuotaFamily.AltarDungeon => UsageQuotaKey.Altar(DungeonId ?? throw new InvalidDataException("Altar quota requires dungeon id."), Period),
        UsageQuotaFamily.TournamentDungeon => UsageQuotaKey.Tournament(DungeonId ?? throw new InvalidDataException("Tournament quota requires dungeon id."), Period),
        UsageQuotaFamily.HerasDungeon => UsageQuotaKey.Heras("entries", Period),
        UsageQuotaFamily.GoldSilverHorn => UsageQuotaKey.GoldSilverHorn(Period),
        UsageQuotaFamily.LostLand => UsageQuotaKey.LostLand(Period),
        UsageQuotaFamily.Consumable or UsageQuotaFamily.Premium or UsageQuotaFamily.ReviveCoin
            or UsageQuotaFamily.GiantDragonCrystalExchange => throw new InvalidOperationException($"{Family} is not an entry quota."),
    };

    /// <summary>
    /// Whether <paramref name="used"/> entries exhaust the quota. Dimension passes what remains, so zero refuses;
    /// every other family passes its daily allowance, where zero means the content sets no limit.
    /// </summary>
    public bool IsExhaustedBy(int used) => Family == UsageQuotaFamily.DimensionDungeon ? used >= Maximum : Maximum > 0 && used >= Maximum;
}

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
