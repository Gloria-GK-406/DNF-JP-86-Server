using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Marriage;
internal sealed class MarriageGuestbookRuleService : IMarriageGuestbookRuleService
{ public int MaximumBytes => 20; public int MaximumComments => 100; }
internal sealed class MarriageGuestbookDomainService(IMarriageGuestbookDomainStore store, IMarriageProposalDomainStore proposals,
    IMarriageWeddingDomainStore weddings, IMarriageChurchDomainStore rooms, ISelectedSessionDomainService sessions,
    ITownAreaDomainService areas, ICharacterDomainService characters, IMarriageGuestbookRuleService rules, TimeProvider clock) : IMarriageGuestbookDomainService
{
    private async ValueTask<(Guid Relation, bool Owner, bool CanPost)?> AccessAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct)
    {
        if (sessions.Find(actor.AccountId) != actor || key == 0 || key > int.MaxValue) return null;
        var relation = await proposals.FindActiveAsync(new CharacterId((int)key), ct).ConfigureAwait(false);
        if (relation?.State != MarriageProposalState.Engaged) return null;
        var owner = actor.CharacterId == relation.Proposer.CharacterId || actor.CharacterId == relation.Recipient.CharacterId;
        if (owner)
        {
            var peer = actor.CharacterId == relation.Proposer.CharacterId ? relation.Recipient : relation.Proposer;
            return peer.CharacterId.Value == key ? (relation.Id, true, false) : null;
        }
        foreach (var wedding in await weddings.PendingAsync(ct).ConfigureAwait(false))
        {
            if (wedding.Ceremony.RelationId != relation.Id || wedding.Phase == MarriageWeddingPhase.Cancelled) continue;
            var invitation = (await weddings.InvitationsAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false)).SingleOrDefault(x => x.Guest == actor.CharacterId && x.Key == key);
            if (invitation is null || await rooms.ReadAsync(wedding.Ceremony.Id, ct).ConfigureAwait(false) is not { } room) continue;
            var member = areas.Find(actor.CharacterId);
            if (member is null || !actor.Owns(member) || member.Area.ChannelId != room.ChannelId) continue;
            return (relation.Id, false, member.Area == new TownAreaKey(room.ChannelId, room.TownId, room.AreaId)
                && await rooms.IsAdmittedAsync(wedding.Ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false));
        }
        return null;
    }
    public async ValueTask<MarriageGuestbookResult> ReadAsync(SelectedSessionIdentity actor, uint key, CancellationToken ct) =>
        await AccessAsync(actor, key, ct).ConfigureAwait(false) is { } access ? new(0, await store.ReadAsync(access.Relation, ct).ConfigureAwait(false)) : new(-1, []);
    public async ValueTask<int> PostAsync(SelectedSessionIdentity actor, uint key, byte[] text, CancellationToken ct)
    {
        if (text.Length == 0 || text.Length > rules.MaximumBytes || text.Any(x => x < 32 || x == 127)
            || await AccessAsync(actor, key, ct).ConfigureAwait(false) is not { CanPost: true } access) return -1;
        if ((await store.ReadAsync(access.Relation, ct).ConfigureAwait(false)).Count >= rules.MaximumComments) return -1;
        if (await characters.Get(actor.CharacterId).IdentityAsync(ct).ConfigureAwait(false) is not { } identity) return -1;
        await store.AddAsync(access.Relation, actor.CharacterId, identity.Name, text, checked((uint)clock.GetUtcNow().ToUnixTimeSeconds()), ct).ConfigureAwait(false);
        return 0;
    }
    public async ValueTask<int> DeleteAsync(SelectedSessionIdentity actor, uint key, uint id, CancellationToken ct)
    {
        if (await AccessAsync(actor, key, ct).ConfigureAwait(false) is not { Owner: true } access) return -1;
        return await store.DeleteAsync(access.Relation, id, ct).ConfigureAwait(false) ? 0 : -1;
    }
}
