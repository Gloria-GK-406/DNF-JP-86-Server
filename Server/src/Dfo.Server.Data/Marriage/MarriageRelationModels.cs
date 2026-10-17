using Dfo.Server.Data.World;

namespace Dfo.Server.Data.Marriage;

// Persisted integers: append only, never renumber. 6..15 are reserved for divorce.
public enum MarriageStage : byte { Proposed = 0, Engaged = 1, Requested = 2, Preparing = 3, Married = 4, Closed = 5 }

public sealed record MarriageRelation(Guid Id, MarriageStage Stage, long Revision,
    SelectedSessionIdentity Proposer, SelectedSessionIdentity Recipient, Guid? CeremonyId, DateTimeOffset StageSince)
{
    public DateTimeOffset? MarriedAt { get; init; }
    public int LovePoints { get; init; }
}

/// <summary>A relation as the read path sees it; <see cref="Stage"/> is the effective stage, not necessarily the stored one.</summary>
public sealed record MarriageView(Guid RelationId, MarriageStage Stage, long Revision,
    SelectedSessionIdentity Self, SelectedSessionIdentity Partner, Guid? CeremonyId)
{
    public DateTimeOffset? MarriedAt { get; init; }
    public int LovePoints { get; init; }
    /// <summary>Both partners have cleared the trial and material quests bound to this relation.</summary>
    public bool TrialsCleared { get; init; }
}
