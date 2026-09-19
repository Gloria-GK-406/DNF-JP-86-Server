using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Tests.Game.Party;

internal sealed class VirtualOnlyPartyService : IPartyService
{
    public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
    public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }

    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [];

    public PartySnapshot FindParty(PartyCharacterSnapshot character) =>
        PartySnapshot.Virtual(character, new PartySettings(new byte[PartySettings.WireLength]));

    public PartyOperationResult CreateOrUpdateSettings(
        PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) =>
        throw new NotSupportedException();

    public PartyOperationResult RequestInvite(
        PartyCharacterSnapshot inviter, PartyCharacterSnapshot target,
        PartyInviteDisplayState display) => throw new NotSupportedException();

    public PartyOperationResult AcceptInvite(
        PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) =>
        throw new NotSupportedException();

    public PartyOperationResult TransferLeadership(
        CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) =>
        throw new NotSupportedException();

    public PartyOperationResult RemoveMember(
        CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) =>
        throw new NotSupportedException();

    public PartyOperationResult LeaveParty(CharacterId characterId) =>
        throw new NotSupportedException();

    public PartyOperationResult CleanupCharacter(CharacterId characterId) =>
        throw new NotSupportedException();
}
