namespace Dfo.Server.Abstractions.World;

/// <summary>The gameplay an ordinary-engine run was admitted into.</summary>
/// <remarks>
/// Only the ordinary dungeon engine has kinds. The two towers keep their own runs and are told
/// apart by <see cref="DungeonParticipationMode"/>; folding them in here would force every
/// switch below to carry a "not this engine" arm.
/// <para>
/// Every decision that differs by kind switches over this enum and names each member, with no
/// discard arm. The build keeps CS8509 and drops only CS8524 (unnamed values), so a new kind
/// fails to compile at every decision that has not answered for it.
/// </para>
/// </remarks>
public enum DungeonRunKind : byte
{
    Ordinary,
    Dimension,
    Hell,
    Mirror,
    Sao,
    Despair,
    Tournament,
    Altar,
    Guardian,
    GiantDragon,
    SouthernDale,
    TauKingdom,
    SealedTreasure,
    Heras,
    GoldSilverHorn,
    Couple,
    LostLand,
}

/// <summary>
/// One run's kind and the plan its kind froze at entry. Exactly one of these exists per run,
/// which is what the former set of mutually exclusive nullable plans on the layout could not say.
/// </summary>
/// <remarks>
/// Closed: the constructor is private, so the nested records are the whole set. Kinds that carry
/// no entry-time plan share <see cref="Plain"/>.
/// </remarks>
public abstract record DungeonRunMode
{
    private DungeonRunMode() { }

    public abstract DungeonRunKind Kind { get; }

    public DungeonRunCapabilities Capabilities => DungeonRunCapabilities.Of(Kind);

    public static Plain Ordinary { get; } = new(DungeonRunKind.Ordinary);

    /// <summary>A kind whose behaviour is fixed by its kind alone.</summary>
    public sealed record Plain : DungeonRunMode
    {
        public Plain(DungeonRunKind kind)
        {
            Kind = kind switch
            {
                DungeonRunKind.Ordinary or DungeonRunKind.Dimension or DungeonRunKind.SouthernDale
                    or DungeonRunKind.TauKingdom or DungeonRunKind.SealedTreasure or DungeonRunKind.Heras
                    or DungeonRunKind.GoldSilverHorn or DungeonRunKind.Couple => kind,
                DungeonRunKind.Hell or DungeonRunKind.Mirror or DungeonRunKind.Sao or DungeonRunKind.Despair
                    or DungeonRunKind.Tournament or DungeonRunKind.Altar or DungeonRunKind.Guardian
                    or DungeonRunKind.GiantDragon or DungeonRunKind.LostLand => throw new ArgumentException(
                        $"{kind} runs carry an entry plan.", nameof(kind)),
            };
        }

        public override DungeonRunKind Kind { get; }
    }

    public sealed record LostLand(LostLandEntryPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.LostLand;
    }

    public sealed record Hell(HellEntryPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Hell;
    }

    public sealed record Mirror(MirrorDungeonRunIdentity Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Mirror;
    }

    public sealed record Sao(SaoTowerEntryPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Sao;
    }

    public sealed record Despair(DespairTowerEntryPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Despair;
    }

    public sealed record Tournament(TournamentPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Tournament;
    }

    public sealed record Altar(AltarEntryPlan Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Altar;
    }

    public sealed record Guardian(GuardianAltarAdmission Plan) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.Guardian;
    }

    /// <summary>A giant dragon run; the time reward is absent when the archive declares none.</summary>
    public sealed record GiantDragon(GiantDragonTimeRewardPlan? TimeReward) : DungeonRunMode
    {
        public override DungeonRunKind Kind => DungeonRunKind.GiantDragon;
    }
}
