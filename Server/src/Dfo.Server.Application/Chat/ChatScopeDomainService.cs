using Dfo.Server.Abstractions.Chat;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Chat;

/// <summary>Asks each group's own owner who is in it, and keeps only those in the world.</summary>
/// <remarks>
/// Every answer is read at the moment of asking and nothing is kept, so there is no chat-side
/// member list to drift from the sessions, areas and parties it would copy. A scope that reaches
/// no one is an empty answer, not a failure: the speaker is told nothing either way.
/// </remarks>
internal sealed class ChatScopeDomainService(
    ISelectedSessionSnapshotQuery online,
    ISelectedSessionDomainService sessions,
    ITownAreaDomainService townAreas,
    IPartyService parties) : IChatScopeDomainService
{
    public IReadOnlyList<CharacterId> MembersOf(ChatScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope switch
        {
            ChatScope.AreaOf area => AreaOf(area.Anchor),
            ChatScope.PartyOf party => PartyOf(party.Anchor),
            ChatScope.ChannelOf channel => ChannelOf(channel.Anchor),
            ChatScope.GuildOf => [],
            ChatScope.Server => InWorld(_ => true),
            ChatScope.Character character => sessions.TryGetInWorld(character.Target, out _) ? [character.Target] : [],
            _ => throw new NotSupportedException($"Chat scope '{scope.GetType().Name}' has no member rule."),
        };
    }

    private CharacterId[] AreaOf(CharacterId anchor)
    {
        // The area record is per session; one left behind by an earlier session of the anchor is not where it stands.
        if (townAreas.Find(anchor) is not { } standing || !IsCurrent(standing)
            || townAreas.Find(standing.Area) is not { } area)
            return [];
        return [.. area.Members.Where(IsCurrent).Select(member => member.CharacterId)];
    }

    private CharacterId[] PartyOf(CharacterId anchor)
    {
        var key = parties.KeyOf(anchor);
        if (key.Kind != PartyKeyKind.Real || parties.FindByKey(key) is not { } party)
            return [];
        return [.. party.Members.OrderBy(member => member.Slot)
            .Where(member => sessions.TryGetInWorld(member.CharacterId, out _))
            .Select(member => member.CharacterId)];
    }

    // One game listener is one channel, the same identity the client was told at connection.
    private CharacterId[] ChannelOf(CharacterId anchor) =>
        sessions.TryGetInWorld(anchor, out var session)
            ? InWorld(other => other.ListenerPort == session.ListenerPort)
            : [];

    private CharacterId[] InWorld(Func<Dfo.Server.Data.World.SelectedSessionIdentity, bool> belongs) =>
        [.. online.Snapshot().Where(session => belongs(session) && sessions.IsInWorld(session)).Select(session => session.CharacterId)];

    private bool IsCurrent(TownAreaMember member) =>
        sessions.TryGetInWorld(member.CharacterId, out var session) && member.Session == session;
}
