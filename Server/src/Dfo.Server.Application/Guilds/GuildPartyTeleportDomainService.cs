using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildPartyTeleportDomainService(
    IPartyService parties,
    ISelectedSessionDomainService sessions,
    IOnlineCharacterRuntimeDomainService online,
    ICharacterDomainService characters,
    ICharacterQuestLogDomainService quests,
    IChannelEntryDomainService channels,
    GuildTravelRuleService travelRules,
    ITownTransitDomainService transit,
    IMarriageQueryDomainService marriage) : IGuildPartyTeleportDomainService
{
    public async ValueTask<GuildPartyTeleportPreparation> PrepareAsync(
        SelectedSessionIdentity leader, GuildPartyTeleportTarget target, CancellationToken ct)
    {
        var party = parties.FindParty(new(leader.AccountId, leader.CharacterId, leader.WireUserId, null));
        if (party.IsVirtual || party.IsLeased || party.LeaderWireUserId != leader.WireUserId
            || party.Members.Length < 2)
            return new(null, GuildPartyTeleportRefusal.PartyLeaderRequired);

        if (online.StandingOf(leader) is not { SharesTownPresence: true } standing)
            return new(null, GuildPartyTeleportRefusal.PartyNotTogether);

        var members = new List<TownAreaMember>(party.Members.Length);
        foreach (var member in party.Members)
        {
            if (!sessions.TryGetInWorld(member.CharacterId, out var session) || session.WireUserId != member.WireUserId
                || session.AccountId != member.Character.AccountId || session.ListenerPort != leader.ListenerPort
                || online.StandingOf(session) is not { SharesTownPresence: true } location || location.Area != standing.Area)
                return new(null, GuildPartyTeleportRefusal.PartyNotTogether);
            members.Add(location);
        }

        var channel = await channels.ResolveAsync(leader.ListenerPort, ct).ConfigureAwait(false);
        if (await travelRules.TeleportDestinationAsync(target, channel, standing.Area.ChannelId, ct).ConfigureAwait(false) is not { } landing)
            return new(null, GuildPartyTeleportRefusal.DestinationUnavailable);
        var (area, x, y) = landing;

        foreach (var member in members)
        {
            var session = member.Session;
            var progress = await characters.Get(member.CharacterId).ProgressAsync(ct).ConfigureAwait(false);
            var eligibility = GuildTravelRuleService.EvaluateTeleportEligibility(area, progress?.Level);
            if (eligibility == GuildTeleportEligibility.NeedsQuestLog)
            {
                var cleared = await quests.LogOf(session.AccountId, session.CharacterId).ClearedAsync(ct).ConfigureAwait(false);
                eligibility = GuildTravelRuleService.EvaluateTeleportEligibility(area, progress?.Level, cleared);
            }
            if (eligibility != GuildTeleportEligibility.Allowed)
                return new(null, GuildPartyTeleportRefusal.DestinationLocked);
            if (await transit.RefuseCrossingAsync(session, member.Position, target.TownId, target.AreaId, ct).ConfigureAwait(false) is not null
                || !await marriage.CanCrossAsync(session, channel.ChannelId, target.TownId, target.AreaId, ct).ConfigureAwait(false)
                || !await marriage.CanLeaveAsync(session, target.TownId, target.AreaId, ct).ConfigureAwait(false))
                return new(null, GuildPartyTeleportRefusal.DestinationLocked);
        }

        var destination = new TownAreaKey(channel.ChannelId, target.TownId, target.AreaId);
        var position = new CharacterPositionState(target.TownId, target.AreaId, x, y, target.Direction);
        if (members.All(member => member.Area == destination && member.Position == position))
            return new(null, GuildPartyTeleportRefusal.AlreadyThere);
        return new(new GuildPartyTeleportPlan(destination, position, members));
    }
}
