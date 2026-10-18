using Dfo.Server.Abstractions.Marriage;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageCeremonyRuleService(
    IMarriageCeremonyContentSource content,
    IMarriageProposalRuleService proposals) : IMarriageCeremonyRuleService
{
    public TimeSpan Lifetime => proposals.Lifetime;
    public int TrialQuestId => proposals.TrialQuestId;
    public int MaterialQuestId => proposals.MaterialQuestId;

    public int? RequiredGold(int tier, int waitIndex)
    {
        var rules = content.Load();
        if (tier != 0 || waitIndex < 0 || waitIndex >= rules.WaitDurations.Count) return null;
        return rules.Tiers.SingleOrDefault(x => x.Tier == tier)?.Gold;
    }
}
