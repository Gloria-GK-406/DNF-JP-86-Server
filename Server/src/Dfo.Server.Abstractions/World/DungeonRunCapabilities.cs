using Dfo.Server.Abstractions.UsageQuotas;

namespace Dfo.Server.Abstractions.World;

/// <summary>Who settles a party wipe in an ordinary-engine run.</summary>
public enum DungeonPartyWipePolicy : byte
{
    /// <summary>The run's wipe deadline ends it and returns the party to town.</summary>
    OrdinaryTimer,

    /// <summary>The altar records a failed challenge and plays its own transition.</summary>
    AltarFailure,

    /// <summary>The tournament eliminates the entrant and shows its completion screen.</summary>
    TournamentElimination,

    /// <summary>The guardian altar owns its result screen and its own return deadline.</summary>
    Guardian,
}

/// <summary>Whether a dead participant may be revived with a coin, and who else must agree.</summary>
public enum DungeonRevivalPolicy : byte
{
    Ordinary,
    Forbidden,
    TournamentLife,
    AltarLife,
}

/// <summary>
/// The answers a run kind gives to decisions several kinds share. Behaviour belonging to one kind
/// alone stays with that kind's plan; this table only holds what callers used to work out by
/// testing several plans at once.
/// </summary>
/// <param name="EndsOnBossClear">
/// Whether killing the boss room opens the ordinary clear and pays the boss-clear reward. The
/// tournament and the altar settle through their own rounds instead.
/// </param>
/// <param name="AppliesFailureWeakness">
/// Whether failing the run applies weakness, before the archive's give-up exemption is applied.
/// </param>
/// <param name="DrawsClearCards">Whether the clear presents and pays the reward cards.</param>
/// <param name="UnlocksDifficulty">Whether a clear raises the character's recorded difficulty.</param>
/// <param name="AllowsOrdinaryRechallenge">
/// Whether leaving after a clear may restore the ordinary maze for an immediate retry.
/// </param>
/// <param name="QuotaFamily">The usage quota refreshed when a room finishes loading.</param>
public sealed record DungeonRunCapabilities(
    bool EndsOnBossClear,
    bool AppliesFailureWeakness,
    bool DrawsClearCards,
    bool UnlocksDifficulty,
    bool AllowsOrdinaryRechallenge,
    DungeonRevivalPolicy Revival,
    DungeonPartyWipePolicy PartyWipe,
    UsageQuotaFamily? QuotaFamily)
{
    private static readonly DungeonRunCapabilities OrdinaryRun = new(
        EndsOnBossClear: true, AppliesFailureWeakness: true, DrawsClearCards: true, UnlocksDifficulty: true,
        AllowsOrdinaryRechallenge: true, DungeonRevivalPolicy.Ordinary, DungeonPartyWipePolicy.OrdinaryTimer,
        QuotaFamily: null);

    public static DungeonRunCapabilities Of(DungeonRunKind kind) => kind switch
    {
        DungeonRunKind.Ordinary or DungeonRunKind.Dimension or DungeonRunKind.GiantDragon
            or DungeonRunKind.SouthernDale or DungeonRunKind.TauKingdom or DungeonRunKind.SealedTreasure
            or DungeonRunKind.GoldSilverHorn or DungeonRunKind.Couple => OrdinaryRun,
        DungeonRunKind.LostLand => OrdinaryRun with
        {
            EndsOnBossClear = false, AppliesFailureWeakness = false, DrawsClearCards = false,
            UnlocksDifficulty = false, AllowsOrdinaryRechallenge = false, QuotaFamily = UsageQuotaFamily.LostLand,
        },
        DungeonRunKind.Heras => OrdinaryRun with { QuotaFamily = UsageQuotaFamily.HerasDungeon },
        DungeonRunKind.Hell => OrdinaryRun with { AllowsOrdinaryRechallenge = false },
        DungeonRunKind.Sao => OrdinaryRun with { AllowsOrdinaryRechallenge = false },
        DungeonRunKind.Mirror => OrdinaryRun with
        {
            UnlocksDifficulty = false, AllowsOrdinaryRechallenge = false, QuotaFamily = UsageQuotaFamily.MirrorDungeon,
        },
        DungeonRunKind.Despair => OrdinaryRun with
        {
            DrawsClearCards = false, UnlocksDifficulty = false, AllowsOrdinaryRechallenge = false,
        },
        DungeonRunKind.Tournament => OrdinaryRun with
        {
            EndsOnBossClear = false, AllowsOrdinaryRechallenge = false, Revival = DungeonRevivalPolicy.TournamentLife,
            PartyWipe = DungeonPartyWipePolicy.TournamentElimination, QuotaFamily = UsageQuotaFamily.TournamentDungeon,
        },
        DungeonRunKind.Altar => OrdinaryRun with
        {
            EndsOnBossClear = false, AppliesFailureWeakness = false, AllowsOrdinaryRechallenge = false,
            Revival = DungeonRevivalPolicy.AltarLife, PartyWipe = DungeonPartyWipePolicy.AltarFailure,
            QuotaFamily = UsageQuotaFamily.AltarDungeon,
        },
        DungeonRunKind.Guardian => OrdinaryRun with
        {
            AllowsOrdinaryRechallenge = false, Revival = DungeonRevivalPolicy.Forbidden,
            PartyWipe = DungeonPartyWipePolicy.Guardian,
        },
    };
}
