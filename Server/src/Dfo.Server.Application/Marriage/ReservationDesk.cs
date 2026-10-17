using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Ceremony reservations: the requested tier and wait, who asked whom, and the answer.</summary>
internal sealed class ReservationDesk(IMarriageCeremonyDomainStore store, IMarriageCeremonyRuleService rules, TimeProvider clock)
{
    public int? RequiredGold(int tier, int waitIndex) => rules.RequiredGold(tier, waitIndex);

    /// <summary>The relation's live reservation as stored; a lapsed pending one is still returned.</summary>
    public ValueTask<MarriageCeremony?> ActiveAsync(Guid relation, CancellationToken ct) => store.FindActiveByRelationAsync(relation, ct);

    public ValueTask<MarriageCeremony?> ReceivedAsync(CharacterId character, CancellationToken ct) => store.FindLatestReceivedAsync(character, ct);

    public async ValueTask<MarriageCeremony> CreateAsync(Guid relation, SelectedSessionIdentity applicant, SelectedSessionIdentity partner,
        int tier, int waitIndex, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var ceremony = new MarriageCeremony(Guid.NewGuid(), relation, applicant, partner,
            tier, waitIndex, MarriageCeremonyState.Pending, 1, now, now + rules.Lifetime);
        await store.CreateAsync(ceremony, ct).ConfigureAwait(false);
        return ceremony;
    }

    public ValueTask<bool> TransitionAsync(MarriageCeremony expected, MarriageCeremonyState state, byte response, CancellationToken ct) =>
        store.TransitionAsync(expected, state, response, ct);

    public static bool Matches(MarriageCeremony ceremony, SelectedSessionIdentity applicant,
        SelectedSessionIdentity partner, int tier, int waitIndex) =>
        ceremony.Applicant == applicant
        && ceremony.Partner == partner
        && ceremony.Tier == tier && ceremony.WaitIndex == waitIndex;
}
