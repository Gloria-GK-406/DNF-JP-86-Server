using Dfo.Server.Abstractions.ExpertJobs;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.ExpertJobs;

namespace Dfo.Server.Application.ExpertJobs;

internal sealed class ExpertJobQueryDomainService(IExpertJobStateStore states, IExpertJobRuleService rules)
    : IExpertJobQueryDomainService
{
    public ValueTask<ExpertJobState> ReadAsync(CharacterId character, CancellationToken ct) => states.ReadAsync(character, ct);

    public async ValueTask<ExpertJobProgress?> DescribeAsync(ExpertJobState state, CancellationToken ct)
    {
        var definition = state.Type == ExpertJobType.None ? null : await rules.FindAsync(state.Type, ct).ConfigureAwait(false);
        return definition is null ? null
            : new(definition.LevelAt(state.Experience), definition.CardQualificationsAt(state.Experience), definition.Skills);
    }
}
