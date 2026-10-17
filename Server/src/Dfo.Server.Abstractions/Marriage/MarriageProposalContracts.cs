using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Marriage;

public interface IMarriageProposalDomainStore
{
    ValueTask<MarriageProposal?> FindActiveAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageProposal?> FindLatestReceivedAsync(CharacterId character, CancellationToken ct);
    ValueTask CreateAsync(MarriageProposal proposal, CancellationToken ct);
    ValueTask<bool> TransitionAsync(MarriageProposal expected, MarriageProposalState state, byte? response, CancellationToken ct);
}

public interface IMarriageProposalDomainService
{
    ValueTask<MarriageProposal?> FindReceivedAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageProposalResult> ProposeAsync(SelectedSessionIdentity proposer, SelectedSessionIdentity recipient, CancellationToken ct);
    ValueTask<MarriageProposalResult> RespondAsync(SelectedSessionIdentity recipient, SelectedSessionIdentity proposer, byte response, CancellationToken ct);
}

public interface IMarriageProposalContentSource { MarriageProposalContent Load(); }
public interface IMarriageProposalRuleService
{
    int MinimumLevel { get; }
    TimeSpan Lifetime { get; }
    int TrialQuestId { get; }
    int MaterialQuestId { get; }
    bool CanPair(byte firstJob, byte secondJob);
    bool IsFemale(byte job);
}

public interface IMarriageCeremonyDomainStore
{
    ValueTask<MarriageCeremony?> FindActiveByRelationAsync(Guid relationId, CancellationToken ct);
    ValueTask<MarriageCeremony?> FindLatestReceivedAsync(CharacterId character, CancellationToken ct);
    ValueTask CreateAsync(MarriageCeremony ceremony, CancellationToken ct);
    ValueTask<bool> TransitionAsync(MarriageCeremony expected, MarriageCeremonyState state,
        byte response, CancellationToken ct);
    ValueTask<bool> IsReservedAsync(Guid relationId, CancellationToken ct);
}

public interface IMarriageCeremonyDomainService
{
    ValueTask<MarriageCeremony?> FindReceivedAsync(CharacterId character, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RequestAsync(SelectedSessionIdentity applicant,
        SelectedSessionIdentity partner, int tier, int waitIndex, CancellationToken ct);
    ValueTask<MarriageCeremonyResult> RespondAsync(SelectedSessionIdentity partner,
        SelectedSessionIdentity applicant, byte response, CancellationToken ct);
}

public interface IMarriageCeremonyContentSource { MarriageCeremonyContent Load(); }

public interface IMarriageCeremonyRuleService
{
    TimeSpan Lifetime { get; }
    int TrialQuestId { get; }
    int MaterialQuestId { get; }
    int? RequiredGold(int tier, int waitIndex);
}

public interface IMarriageEngagementDomainService
{
    ValueTask<MarriageEngagementSnapshot> ReadAsync(AccountId account, CharacterId character, CancellationToken ct);
}

public interface IMarriageQuestDomainService
{
    ValueTask<IReadOnlyCollection<int>> EligibleQuestIdsAsync(AccountId account, CharacterId character, CancellationToken ct);
    ValueTask<bool> CanAcceptAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
    ValueTask BindAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
    ValueTask<bool> CanCompleteAsync(AccountId account, CharacterId character, int quest, CancellationToken ct);
}

public interface IMarriageQuestDomainStore
{
    ValueTask<MarriageQuestBinding?> ReadAsync(CharacterId character, int quest, CancellationToken ct);
    ValueTask BindAsync(CharacterId character, int quest, Guid relation, DateTimeOffset at, CancellationToken ct);
    ValueTask<bool> RecordClearAsync(CharacterId character, int quest, Guid relation, DungeonRunId run, CancellationToken ct);
    ValueTask<int> CountClearsAsync(CharacterId character, int quest, Guid relation, CancellationToken ct);
}

public interface IMarriageDungeonContentSource
{
    ValueTask<bool> IsOrdinaryMapAsync(int dungeon, int map, CancellationToken ct);
}
