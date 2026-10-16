using Dfo.Server.Abstractions.Marriage;

namespace Dfo.Server.Application.Marriage;

public sealed record MarriageProposalOptions(TimeSpan Lifetime);

internal sealed class MarriageProposalRuleService(IMarriageProposalContentSource content, MarriageProposalOptions options)
    : IMarriageProposalRuleService
{
    // A21 RVA008916B0 / table008916E0, also checked against the same-sex prompt 72807.
    private static ReadOnlySpan<byte> GenderByJob => [0, 1, 0, 1, 0, 1, 1, 0, 0, 0, 1, 1, 1, 0];
    public int MinimumLevel => content.Load().MinimumLevel;
    public TimeSpan Lifetime => options.Lifetime;
    public int TrialQuestId => content.Load().TrialQuestId;
    public int MaterialQuestId => content.Load().MaterialQuestId;
    public bool CanPair(byte firstJob, byte secondJob) => firstJob < GenderByJob.Length
        && secondJob < GenderByJob.Length && GenderByJob[firstJob] != GenderByJob[secondJob];
}
