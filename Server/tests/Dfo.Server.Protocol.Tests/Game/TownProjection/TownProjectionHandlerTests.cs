using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

/// <summary>
/// The two handlers that move a character: which area it walks into, and where it stands.
/// </summary>
/// <remarks>
/// Both ask the service that owns where a character is standing, and both report their movement to
/// it. The session snapshot still carries a copy of the position, but nothing refreshes that copy
/// any more -- it is whatever entry put there, for as long as the session lasts. So a handler that
/// went back to reading it would not throw and would not crash: it would quietly answer with the
/// place the character entered at. That is the mistake these two tests exist to catch, and it is
/// why the service here is arranged to hold a position that differs from the snapshot's in every
/// field. Reading the wrong one produces a recognisably wrong answer rather than coinciding with
/// the right one.
///
/// The real <see cref="CharacterPositionDomainService"/> is used rather than a double, because what
/// is under test is these handlers being wired to the thing that actually owns the position.
/// </remarks>
public sealed class TownProjectionHandlerTests
{
    /// <summary>
    /// Walking into another area moves the character the service owns, facing as they were.
    /// </summary>
    /// <remarks>
    /// The facing is the assertion that separates the two sources. This command does not carry one,
    /// so the direction the client is told belongs to whoever holds the character's position; the
    /// stale snapshot's copy still says whatever entry placed them at.
    /// </remarks>
    [Fact]
    public async Task A_crossing_moves_the_character_the_service_owns()
    {
        var positions = Standing();
        var sender = new RecordingSender();
        var handler = new SetUserAreaHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            positions,
            new OneChannel(),
            new EveryTownDeclared(),
            sender,
            NullLogger<SetUserAreaHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserAreaRequest(
                GameMessageMetadata.Empty, CrossedTownId, CrossedAreaId, CrossedX, CrossedY),
            CancellationToken.None);

        var area = sender.Sent.OfType<UserAreaNotification>().Single();
        Assert.Equal(CrossedAreaId, area.AreaId);
        Assert.Equal(CrossedTownId, area.Spawn.TownId);
        Assert.Equal(CrossedX, area.Spawn.X);
        Assert.Equal(CrossedY, area.Spawn.Y);

        var users = sender.Sent.OfType<AreaUsersNotification>().Single();
        Assert.Equal(CrossedTownId, users.TownId);
        Assert.Equal(CrossedAreaId, users.AreaId);
        var presence = Assert.Single(users.Users);
        Assert.Equal(CrossedX, presence.X);
        Assert.Equal(CrossedY, presence.Y);
        Assert.Equal(WalkedDirection, presence.Direction);

        Assert.Equal(
            new CharacterPositionState(
                CrossedTownId, CrossedAreaId, CrossedX, CrossedY, WalkedDirection),
            positions.Find(Identity.CharacterId));
    }

    /// <summary>
    /// A step moves the character the service owns, in the area the service says they are in.
    /// </summary>
    /// <remarks>
    /// The answer to this frame is the client's own report handed back, so it cannot tell the two
    /// sources apart. The town and area can: this command carries neither, so they come from
    /// wherever the step is applied, and the snapshot's copy is still the town the character
    /// entered in rather than the one they have since crossed into.
    /// </remarks>
    [Fact]
    public async Task A_step_moves_the_character_the_service_owns()
    {
        var positions = Standing();
        var sender = new RecordingSender();
        var handler = new SetUserPositionHandler(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            positions,
            sender,
            NullLogger<SetUserPositionHandler>.Instance);

        await handler.HandleAsync(
            Dispatch(),
            new SetUserPositionRequest(
                GameMessageMetadata.Empty, SteppedX, SteppedY, SteppedDirection, Trailer),
            CancellationToken.None);

        Assert.Equal(
            new CharacterPositionState(
                WalkedTownId, WalkedAreaId, SteppedX, SteppedY, SteppedDirection),
            positions.Find(Identity.CharacterId));

        var answer = Assert.IsType<UserPositionNotification>(Assert.Single(sender.Sent));
        Assert.Equal(SteppedX, answer.X);
        Assert.Equal(SteppedY, answer.Y);
        Assert.Equal(SteppedDirection, answer.Direction);
        Assert.Equal(Trailer, answer.Trailer);
    }

    /// <summary>Where entry placed them, which is the copy the session snapshot keeps.</summary>
    private const byte PlacedTownId = 9;
    private const byte PlacedAreaId = 8;
    private const short PlacedX = 77;
    private const short PlacedY = 66;
    private const byte PlacedDirection = 4;

    /// <summary>Where they have since walked to, which only the service knows.</summary>
    private const byte WalkedTownId = 3;
    private const byte WalkedAreaId = 6;
    private const short WalkedX = 902;
    private const short WalkedY = 388;
    private const byte WalkedDirection = 7;

    /// <summary>The area the crossing under test asks for.</summary>
    private const byte CrossedTownId = 5;
    private const byte CrossedAreaId = 2;
    private const short CrossedX = 100;
    private const short CrossedY = 200;

    /// <summary>The step under test.</summary>
    private const short SteppedX = 410;
    private const short SteppedY = 275;
    private const byte SteppedDirection = 5;
    private const ushort Trailer = 104;

    /// <summary>The area state the channel's spawn carries, which the crossing reuses.</summary>
    private const byte SpawnAreaState = 7;

    /// <summary>
    /// The service, holding a character who entered in one town and has since crossed and walked.
    /// </summary>
    /// <remarks>
    /// Built by reporting movement rather than by placing the answer directly, because that is how
    /// the position gets there in production: entry places it once and every later frame reports a
    /// move. It is also what leaves the snapshot behind -- the copy entry took is never revisited.
    /// </remarks>
    private static CharacterPositionDomainService Standing()
    {
        var positions = new CharacterPositionDomainService();
        positions.Place(
            Identity,
            new CharacterPositionState(
                PlacedTownId, PlacedAreaId, PlacedX, PlacedY, PlacedDirection));
        positions.CrossInto(Identity, WalkedTownId, WalkedAreaId, 500, 400);
        positions.StepTo(Identity, WalkedX, WalkedY, WalkedDirection);
        return positions;
    }

    /// <summary>
    /// A published session whose copy of the position is the one entry took, and stale.
    /// </summary>
    private static SelectCharacterResult Entry()
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Position = new CharacterPositionState(
                    PlacedTownId, PlacedAreaId, PlacedX, PlacedY, PlacedDirection),
            },
            World = result.World with
            {
                Channel = new ChannelEntryPolicyState(
                    ChannelId: 10,
                    Spawn: new EntrySpawnState(
                        TownId: 1, AreaId: 1, X: 100, Y: 100,
                        Direction: 0, AreaState: SpawnAreaState, IsTransient: false),
                    PersistsPosition: true,
                    PvpSkillProfileApplies: false),
            },
        };
    }

    private static NetworkDispatchContext Dispatch() =>
        new(new NetworkConnectionContext(
            new NetworkConnectionId("one"), NetworkEndpointKind.Game, 10010));

    private static SelectedSessionIdentity Identity { get; } = SelectedSessionIdentity.Create(
        new AccountId(1),
        new CharacterId(60),
        new NetworkConnectionId("one"),
        connectionGeneration: 1,
        listenerPort: 10010);

    private sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => identity;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => identity;

        public SelectedSessionIdentity? Find(AccountId accountId) => identity;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => identity;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    /// <summary>
    /// The runtime session, which accepts a rewrite rather than refusing one.
    /// </summary>
    /// <remarks>
    /// Deliberately permissive. A fixture that threw here would fail a handler that wrote the
    /// position back into the snapshot for the wrong reason -- with a fixture's exception instead
    /// of a wrong coordinate -- and would say nothing about which position the handler used.
    /// </remarks>
    private sealed class OneRuntimeSession(SelectCharacterResult entry)
        : ISelectedSessionRuntimeDirectory
    {
        public SelectCharacterResult Current { get; private set; } = entry;

        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = this.Current;
            shouldPersistPosition = false;
            return true;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => this.Current;

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            this.Current = result;

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class OneChannel : IChannelEntryPolicy
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(
            int listenerPort, CancellationToken cancellationToken) =>
            ValueTask.FromResult(
                new ChannelEntryIdentity(10, listenerPort, ChannelEntryKind.General));

        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) =>
            throw new NotSupportedException("Crossing an area does not re-admit anyone.");
    }

    /// <summary>Content that declares every area and a channel that serves every town.</summary>
    private sealed class EveryTownDeclared : ITownMovementDomainService
    {
        public ValueTask<bool> DeclaresAreaAsync(
            int townId, int areaId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public ValueTask<bool> CanEnterTownAsync(
            ChannelEntryIdentity channel, int townId, CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);
    }

    private sealed class RecordingSender : IGameMessageSender
    {
        public List<IGameServerMessage> Sent { get; } = [];

        public ValueTask SendCmdAsync(
            NetworkConnectionId connectionId, IGameCmdMessage message,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Neither command answers on the CMD side.");

        public ValueTask SendNotiAsync(
            NetworkConnectionId connectionId, IGameNotiMessage message,
            CancellationToken cancellationToken)
        {
            this.Sent.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendExplicitAsync(
            NetworkConnectionId requiredDirectConnectionId,
            GameExplicitMessageDelivery delivery,
            CancellationToken cancellationToken)
        {
            this.Sent.AddRange(delivery.RequiredDirectMessages);
            return ValueTask.CompletedTask;
        }
    }
}
