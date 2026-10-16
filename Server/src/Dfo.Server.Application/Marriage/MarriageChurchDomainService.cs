using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Marriage;

internal sealed class MarriageChurchDomainService(IMarriageProposalDomainStore relations,
    IMarriageCeremonyDomainStore ceremonies, IMarriageChurchRuleService rules,
    ISelectedSessionDomainService sessions, ITownAreaDomainService areas, ICharacterDomainService characters,
    TimeProvider clock, ICharacterPositionDomainService positions, ISelectedSessionPositionPersistence persistence,
    IMarriageChurchDomainStore? store = null) : IMarriageChurchDomainService
{
    private async ValueTask<(MarriageProposal, MarriageCeremony)?> ReservedAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (store is null || sessions.Find(actor.AccountId) != actor) return null;
        var relation = await relations.FindActiveAsync(actor.CharacterId, ct).ConfigureAwait(false);
        if (relation is not { State: MarriageProposalState.Engaged }) return null;
        var own = relation.Proposer.CharacterId == actor.CharacterId ? relation.Proposer : relation.Recipient;
        if (own.AccountId != actor.AccountId || (relation.Proposer.CharacterId != actor.CharacterId
                && relation.Recipient.CharacterId != actor.CharacterId)) return null;
        var ceremony = await ceremonies.FindActiveByRelationAsync(relation.Id, ct).ConfigureAwait(false);
        if (ceremony is not { State: MarriageCeremonyState.Reserved, Tier: 0 }) return null;
        static bool SameCharacter(SelectedSessionIdentity first, SelectedSessionIdentity second) =>
            first.AccountId == second.AccountId && first.CharacterId == second.CharacterId;
        var samePair = SameCharacter(ceremony.Applicant, relation.Proposer) && SameCharacter(ceremony.Partner, relation.Recipient)
            || SameCharacter(ceremony.Applicant, relation.Recipient) && SameCharacter(ceremony.Partner, relation.Proposer);
        return samePair ? (relation, ceremony) : null;
    }

    public async ValueTask<MarriageChurchSnapshot?> PrepareAsync(SelectedSessionIdentity actor, CancellationToken ct)
    {
        if (await ReservedAsync(actor, ct).ConfigureAwait(false) is not { } reserved
            || areas.Find(actor.CharacterId) is not { } member || !actor.Owns(member)) return null;
        var ceremony = reserved.Item2;
        var room = await store!.ReadAsync(ceremony.Id, ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (room is null)
        {
            foreach (var location in await rules.LocationsAsync(member.Area.TownId, ct).ConfigureAwait(false))
            {
                room = await store.TryCreateAsync(ceremony.Id, member.Area.ChannelId, location,
                    now, now + rules.PreparationTime(ceremony.WaitIndex), ct).ConfigureAwait(false);
                if (room is not null) break;
            }
        }
        if (room is null || room.ChannelId != member.Area.ChannelId) return null;
        // Preparation can be reopened while no ceremony engine has begun it. The room and
        // reservation stay stable; this is not completion or a new fee/reward settlement.
        if (room.ReadyUntil <= now)
        {
            var until = now + rules.PreparationTime(ceremony.WaitIndex);
            await store!.RenewAsync(room, now, until, ct).ConfigureAwait(false);
            room = room with { PreparedAt = now, ReadyUntil = until };
        }
        return await SnapshotAsync(actor, ceremony, room, ct).ConfigureAwait(false);
    }

    private async ValueTask<MarriageChurchSnapshot?> SnapshotAsync(SelectedSessionIdentity actor,
        MarriageCeremony ceremony, MarriageChurchRoom room, CancellationToken ct)
    {
        var location = (await rules.LocationsAsync(room.TownId, ct).ConfigureAwait(false)).SingleOrDefault(x => x.AreaId == room.AreaId);
        var first = await characters.Get(ceremony.Applicant.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        var second = await characters.Get(ceremony.Partner.CharacterId).IdentityAsync(ct).ConfigureAwait(false);
        if (location is null || first is null || second is null) return null;
        var peer = actor.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
        return new(room, peer, first.Name, second.Name, location);
    }

    public async ValueTask<MarriageChurchEntryResult> EnterAsync(SelectedSessionIdentity actor,
        uint peer, uint churchArea, byte flag, CancellationToken ct)
    {
        if (flag != 0 || await ReservedAsync(actor, ct).ConfigureAwait(false) is not { } reserved
            || areas.Find(actor.CharacterId) is not { } member || !actor.Owns(member)) return new(-10);
        var ceremony = reserved.Item2;
        var expectedPeer = actor.CharacterId == ceremony.Applicant.CharacterId ? ceremony.Partner.CharacterId : ceremony.Applicant.CharacterId;
        var room = await store!.ReadAsync(ceremony.Id, ct).ConfigureAwait(false);
        if (room is null || peer != expectedPeer.Value || churchArea != room.AreaId || room.ChannelId != member.Area.ChannelId) return new(-10);
        if (room.ReadyUntil <= clock.GetUtcNow()) return new(-7);
        var snapshot = await SnapshotAsync(actor, ceremony, room, ct).ConfigureAwait(false);
        if (snapshot is null || positions.Find(actor.CharacterId) is not { } standing) return new(-10);
        await persistence.PersistAsync(actor.AccountId, actor.CharacterId, standing with
        {
            TownId = room.TownId, AreaId = room.AreaId, X = snapshot.Location.X, Y = snapshot.Location.Y,
        }, ct).ConfigureAwait(false);
        await store.AdmitAsync(ceremony.Id, actor.CharacterId, ct).ConfigureAwait(false);
        return new(0, snapshot);
    }

    public async ValueTask<bool> CanCrossAsync(SelectedSessionIdentity actor, int channelId,
        byte townId, byte areaId, CancellationToken ct)
    {
        if (!await rules.IsChurchAsync(townId, areaId, ct).ConfigureAwait(false)) return true;
        if (await ReservedAsync(actor, ct).ConfigureAwait(false) is not { } reserved) return false;
        var room = await store!.ReadAsync(reserved.Item2.Id, ct).ConfigureAwait(false);
        if (room is null || room.ChannelId != channelId || room.TownId != townId || room.AreaId != areaId) return false;
        var standing = areas.Find(actor.CharacterId);
        var alreadyInside = standing is not null && actor.Owns(standing)
            && standing.Area == new TownAreaKey(channelId, townId, areaId);
        return (room.ReadyUntil > clock.GetUtcNow() || alreadyInside)
            && await store.IsAdmittedAsync(room.CeremonyId, actor.CharacterId, ct).ConfigureAwait(false);
    }
}
