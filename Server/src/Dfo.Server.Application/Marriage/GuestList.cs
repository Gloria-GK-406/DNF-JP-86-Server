using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

/// <summary>Wedding guests: invitations and their quota, gifts, and the accounts a wedding involves.</summary>
internal sealed class GuestList(IMarriageWeddingGuestDomainStore store, IMarriageChurchRuleService rules,
    ISelectedSessionSnapshotQuery online, ITownAreaDomainService areas, ICharacterDomainService characters, ICharacterTable owners)
{
    public ValueTask<IReadOnlyList<MarriageWeddingInvitation>> InvitationsAsync(Guid ceremony, CancellationToken ct) =>
        store.InvitationsAsync(ceremony, ct);

    public async ValueTask<MarriageWeddingInvitation?> InvitationOfAsync(Guid ceremony, CharacterId guest, CancellationToken ct) =>
        (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Guest == guest);

    public async ValueTask<MarriageWeddingInvitation?> InvitationAsync(Guid ceremony, CharacterId guest, uint key, CancellationToken ct) =>
        (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Guest == guest && x.Key == key);

    public async ValueTask<bool> IsInvitedAsync(Guid ceremony, CharacterId guest, CancellationToken ct) =>
        (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).Any(x => x.Guest == guest);

    public async ValueTask<IReadOnlyList<uint>> InvitedAsync(Guid ceremony, CancellationToken ct) =>
        (await store.InvitationsAsync(ceremony, ct).ConfigureAwait(false)).Select(x => checked((uint)x.Guest.Value)).ToArray();

    /// <summary>Invitations the inviter may still send for a wedding that is still preparing.</summary>
    public async ValueTask<ushort> RemainingAsync(MarriageCeremony ceremony, CharacterId inviter, CancellationToken ct)
    {
        var used = (await store.InvitationsAsync(ceremony.Id, ct).ConfigureAwait(false)).Count(x => x.Inviter == inviter);
        return checked((ushort)Math.Max(0, rules.WeddingRules(ceremony.Tier).InvitationLimit - used));
    }

    /// <summary>Online characters on the room's channel who may receive a letter: anyone uninvited in random mode, else the target.</summary>
    public SelectedSessionIdentity[] Candidates(MarriageCeremony ceremony, IReadOnlySet<CharacterId> invited, int channel,
        byte mode, uint target, int limit) =>
        online.Snapshot().Where(x => x.CharacterId != ceremony.Applicant.CharacterId && x.CharacterId != ceremony.Partner.CharacterId
            && !invited.Contains(x.CharacterId) && areas.Find(x.CharacterId)?.Area.ChannelId == channel
            && (mode == 1 || x.CharacterId.Value == target)).Take(mode == 1 ? limit : 1).ToArray();

    public ValueTask InviteAsync(MarriageWeddingInvitation invitation, CancellationToken ct) => store.InviteAsync(invitation, ct);

    public async ValueTask<int> PayAsync(SelectedSessionIdentity actor, MarriageWedding wedding, MarriageWeddingInvitation invitation,
        int gold, CancellationToken ct)
    {
        var rule = rules.WeddingRules(wedding.Ceremony.Tier);
        if (gold < rule.GiftMinimum || gold > rule.GiftMaximum) return -1;
        if (invitation.Gold != 0) return invitation.Gold == gold ? 0 : -1;
        if (await characters.Get(actor.CharacterId).TrySpendGoldAsync(gold, ct).ConfigureAwait(false) is null) return -1;
        if (!await store.PayAsync(wedding.Ceremony.Id, actor.CharacterId, gold, ct).ConfigureAwait(false)) throw new MarriageWeddingUnavailableException();
        return 0;
    }

    /// <summary>The couple's and every invited guest's accounts, in id order: the leases a wedding advance takes.</summary>
    public async ValueTask<IReadOnlyList<AccountId>> AccountsAsync(MarriageWedding wedding, CancellationToken ct)
    {
        var accounts = new HashSet<AccountId> { wedding.Ceremony.Applicant.AccountId, wedding.Ceremony.Partner.AccountId };
        foreach (var invitation in await store.InvitationsAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false))
            if (await owners.FindOwnerAsync(invitation.Guest, ct).ConfigureAwait(false) is { } account) accounts.Add(account);
        return accounts.OrderBy(x => x.Value).ToArray();
    }
}
