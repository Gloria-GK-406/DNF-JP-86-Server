using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

public sealed class TownPresenceNotificationTests
{
    [Fact]
    public void Newcomer_receives_existing_identity_visibility_position_then_full_roster()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var newcomer = Participant(source, new CharacterId(60), x: 100);
        var existing = Participant(source, new CharacterId(61), x: 300);
        var state = new TownPresenceSynchronizationState(1, 0, newcomer, [existing]);

        var messages = TownPresenceMessages.ForNewcomer(state);

        Assert.Collection(
            messages,
            message => Assert.IsType<CurrentUserStateMessage>(message),
            message => Assert.IsType<UserVisibilityMessage>(message),
            message => Assert.IsType<UserAreaNotification>(message),
            message =>
            {
                var roster = Assert.IsType<AreaUsersNotification>(message);
                Assert.Equal([60, 61], roster.Users.Select(value => (int)value.WireUserId));
                Assert.Equal([100, 300], roster.Users.Select(value => (int)value.X));
            });
    }

    [Fact]
    public async Task Existing_character_receives_identity_visibility_position_then_full_roster()
    {
        var source = SelectCharacterPayloadTestData.CreateComplete();
        var newcomer = Participant(source, new CharacterId(60), x: 100);
        var existing = Participant(source, new CharacterId(61), x: 300);
        var state = new TownPresenceSynchronizationState(1, 0, newcomer, [existing]);

        var messages = await new TownParticipantPublishedHandler().ComposeAsync(
            new NotificationRecipient(new CharacterId(61), new NetworkConnectionId("Game:61")),
            new TownParticipantPublished(state),
            CancellationToken.None);

        Assert.Collection(
            messages,
            message => Assert.IsType<CurrentUserStateMessage>(message),
            message => Assert.IsType<UserVisibilityMessage>(message),
            message => Assert.IsType<UserAreaNotification>(message),
            message =>
            {
                var roster = Assert.IsType<AreaUsersNotification>(message);
                Assert.Equal([61, 60], roster.Users.Select(value => (int)value.WireUserId));
                Assert.Equal([300, 100], roster.Users.Select(value => (int)value.X));
            });
    }

    [Fact]
    public async Task Position_event_composes_the_A21_position_notification()
    {
        var messages = await new TownPositionChangedHandler().ComposeAsync(
            new NotificationRecipient(new CharacterId(61), new NetworkConnectionId("Game:61")),
            new TownPositionChanged(60, 123, 456, 5, 104),
            CancellationToken.None);

        var message = Assert.IsType<UserPositionNotification>(Assert.Single(messages));
        Assert.Equal((ushort)60, message.WireUserId);
        Assert.Equal((short)123, message.X);
        Assert.Equal((short)456, message.Y);
        Assert.Equal((byte)5, message.Direction);
        Assert.Equal((ushort)104, message.Trailer);
    }

    [Fact]
    public async Task Area_roster_reconciliation_composes_the_scoped_authoritative_roster()
    {
        var messages = await new TownAreaRosterReconciledHandler().ComposeAsync(
            new NotificationRecipient(new CharacterId(61), new NetworkConnectionId("Game:61")),
            new TownAreaRosterReconciled(
                1,
                2,
                [new AreaUserPresence(61, 300, 200, 5, 0)]),
            CancellationToken.None);

        var message = Assert.IsType<AreaUsersNotification>(Assert.Single(messages));
        Assert.Equal((byte)1, message.TownId);
        Assert.Equal((byte)2, message.AreaId);
        var remaining = Assert.Single(message.Users);
        Assert.Equal((ushort)61, remaining.WireUserId);
        Assert.Equal((short)300, remaining.X);
        Assert.Equal((short)200, remaining.Y);
        Assert.Equal((byte)5, remaining.Direction);
    }

    private static TownParticipantEntryState Participant(
        Dfo.Server.Data.Game.SelectCharacterResult source,
        CharacterId characterId,
        short x)
    {
        var character = source.Character with
        {
            Identity = source.Character.Identity with { CharacterId = characterId },
        };
        return new TownParticipantEntryState(
            character,
            source.CharacterEntry.Public,
            [],
            source.CharacterEntry.Features.Pets,
            source.World.Channel.Spawn with
            {
                TownId = 1,
                AreaId = 0,
                X = x,
                Y = 200,
                Direction = 5,
            });
    }
}
