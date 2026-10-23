using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Outbox;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.BossTower;

/// <summary>XO-3 (spec appendix D): every member's tower entry batch is delivered before the requester's.</summary>
public sealed partial class BossTowerProjectionConcurrencyTests
{
    [Fact]
    public async Task A_slow_member_is_delivered_the_tower_entry_before_the_requester()
    {
        var requester = Session(1);
        var member = Session(2);
        await using var outbound = new XoDelivery();
        outbound.Connect(requester, member);
        outbound.Transport.Slow(member.ConnectionId, TimeSpan.FromMilliseconds(300));

        await outbound.RunAsync(async () =>
        {
            var towers = new TowerDomain();
            var sessions = new Sessions(requester, member);
            var views = new EntryViews(requester, member);
            var roster = EntryRoster(requester, member);
            var delivery = new BossTowerProjectionDelivery(
                towers, new EntryCommands(towers, requester, member), sessions, new EntryParties(roster), views,
                new SameState(), null!, null!, new Intents(), outbound.Game,
                TownAreaProjectionTestFactory.Create(sessions, views, outbound.Game), TimeProvider.System, outbound.Closer,
                NullLogger<BossTowerProjectionDelivery>.Instance, ordering: outbound.Ordering);

            Assert.True(await delivery.EnterAsync(
                requester.ConnectionId, requester, roster, DungeonEntryIntent.Interactive, () => { }, CancellationToken.None));
        });
        await outbound.SettledAsync();

        var memberEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == member.ConnectionId);
        var requesterEntry = outbound.Recorder.Entries.Single(entry => entry.Connection.ConnectionId == requester.ConnectionId);
        Assert.True(memberEntry.Order!.Value.Sequence < requesterEntry.Order!.Value.Sequence);
        Assert.Equal(OutboundDelivery.BestEffort, requesterEntry.Delivery);
        Assert.True(outbound.Delivered(requester.ConnectionId) > outbound.Delivered(member.ConnectionId),
            "the requester entered before the member");
    }

    private static PartySnapshot EntryRoster(params SelectedSessionIdentity[] members) => new(
        77, 1, 1, members[0].WireUserId,
        new PartySettings(new byte[PartySettings.WireLength]), PartyTitle.Empty,
        [.. members.Select((session, slot) => new PartyMemberSnapshot(PartyCharacter(session), checked((byte)slot), null, 100))]);

    private static SelectCharacterResult EntryOf(SelectedSessionIdentity session)
    {
        var entry = SelectCharacterPayloadTestData.CreateComplete();
        return entry with
        {
            Character = entry.Character with
            {
                Identity = entry.Character.Identity with
                {
                    CharacterId = session.CharacterId,
                    Name = [(byte)(session.WireUserId & byte.MaxValue)],
                },
            },
        };
    }

    private sealed class EntryViews(params SelectedSessionIdentity[] sessions) : ISelectedCharacterCurrentViewDomainService
    {
        public ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SelectCharacterResult?>(sessions.Contains(session) ? EntryOf(session) : null);
    }

    private sealed class SameState : ICharacterStateProjectionDomainService
    {
        public ValueTask<SelectCharacterResult> RefreshAsync(SelectCharacterResult result, CancellationToken cancellationToken) =>
            ValueTask.FromResult(result);
    }

    private sealed class EntryParties(PartySnapshot snapshot) : IPartyService
    {
        public PartyMembershipSnapshot? FindByKey(PartyKey key) => null;
        public PartySnapshot FindParty(PartyCharacterSnapshot character) => snapshot;
        public void UpdateDirectoryPresence(CharacterId characterId, PartyDirectoryScope? scope) { }
        public IReadOnlyList<PartySnapshot> Query(PartyDirectoryQuery query) => [snapshot];
        public PartyOperationResult CreateOrUpdateSettings(PartyCharacterSnapshot character, PartySettings settings, PartyTitle title) => throw new NotSupportedException();
        public PartyOperationResult RequestInvite(PartyCharacterSnapshot inviter, PartyCharacterSnapshot target, PartyInviteDisplayState display) => throw new NotSupportedException();
        public PartyOperationResult AcceptInvite(PartyCharacterSnapshot invitee, PartyCharacterSnapshot inviter) => throw new NotSupportedException();
        public PartyOperationResult TransferLeadership(CharacterId currentLeaderCharacterId, CharacterId nextLeaderCharacterId) => throw new NotSupportedException();
        public PartyOperationResult RemoveMember(CharacterId currentLeaderCharacterId, CharacterId targetCharacterId) => throw new NotSupportedException();
        public PartyOperationResult LeaveParty(CharacterId characterId) => throw new NotSupportedException();
        public PartyOperationResult CleanupCharacter(CharacterId characterId) => throw new NotSupportedException();
    }

    private sealed class EntryCommands(TowerDomain towers, params SelectedSessionIdentity[] members) : ICommandBus
    {
        public ValueTask<TResult> SendAsync<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            if (command is not EnterBossTowerCommand)
                throw new NotSupportedException(command.GetType().Name);
            var run = PartyRun(Guid.NewGuid(), members);
            towers.Put(run);
            return ValueTask.FromResult((TResult)(object)new BossTowerEntryResult(null, run, []));
        }
    }
}
