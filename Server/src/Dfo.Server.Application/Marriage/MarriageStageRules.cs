using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Application.Marriage;

internal enum MarriageTrigger
{
    Propose,
    AcceptProposal,
    RefuseProposal,
    ProposalLapsed,
    RequestCeremony,
    AcceptCeremony,
    CeremonyWithdrawn,
    WeddingCompleted,
    WeddingCancelled,
}

internal static class MarriageStageRules
{
    /// <returns>The stage the trigger leads to, or null when the transition table has no such row.</returns>
    public static MarriageStage? Next(MarriageStage? from, MarriageTrigger trigger) => (from, trigger) switch
    {
        (null, MarriageTrigger.Propose) => MarriageStage.Proposed,
        (MarriageStage.Proposed, MarriageTrigger.AcceptProposal) => MarriageStage.Engaged,
        (MarriageStage.Proposed, MarriageTrigger.RefuseProposal) => MarriageStage.Closed,
        (MarriageStage.Proposed, MarriageTrigger.ProposalLapsed) => MarriageStage.Closed,
        (MarriageStage.Engaged, MarriageTrigger.RequestCeremony) => MarriageStage.Requested,
        (MarriageStage.Requested, MarriageTrigger.AcceptCeremony) => MarriageStage.Preparing,
        (MarriageStage.Requested, MarriageTrigger.CeremonyWithdrawn) => MarriageStage.Engaged,
        (MarriageStage.Preparing, MarriageTrigger.WeddingCompleted) => MarriageStage.Married,
        (MarriageStage.Preparing, MarriageTrigger.WeddingCancelled) => MarriageStage.Engaged,
        _ => null,
    };

    /// <summary>The pair is still together: accepted and not closed.</summary>
    public static bool IsCouple(MarriageStage stage) => stage is not (MarriageStage.Proposed or MarriageStage.Closed);
}
