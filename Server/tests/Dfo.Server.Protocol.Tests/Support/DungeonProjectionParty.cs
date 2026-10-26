using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Tests.Support;

// Fixed party membership for projection components. Run membership and exact-session admission
// are still checked independently by the production DungeonPartyRecipients algorithm.
internal sealed class DungeonProjectionParty(DungeonRunState run) : IPartyService
{
    private readonly PartyMembershipSnapshot membership = new(run.PartyKey,
        run.Participants.First().CharacterId, new PartySettings(new byte[PartySettings.WireLength]),
        PartyTitle.Empty, [.. run.Participants.Select(value => new PartyMembershipMember(value.CharacterId, value.Slot))]);
    public PartyMembershipSnapshot? FindByKey(PartyKey key) => key == membership.Key ? membership : null;
    public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => throw new NotSupportedException();
    public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
    public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
    public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
    public PartyOperationResult TransferLeadership(CharacterId leader, CharacterId next) => throw new NotSupportedException();
    public PartyOperationResult RemoveMember(CharacterId leader, CharacterId target) => throw new NotSupportedException();
    public PartyOperationResult LeaveParty(CharacterId character) => throw new NotSupportedException();
    public PartyOperationResult CleanupCharacter(CharacterId character) => throw new NotSupportedException();
    public PartySnapshot FindParty(PartyCharacterSnapshot character) => throw new NotSupportedException();
    public void UpdateDirectoryPresence(CharacterId character, PartyDirectoryScope? scope) => throw new NotSupportedException();
}
