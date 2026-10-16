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
