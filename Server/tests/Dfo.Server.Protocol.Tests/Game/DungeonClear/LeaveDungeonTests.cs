using Dfo.Server.Application.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Protocol.Tests.Support;
using Dfo.Server.Protocol.Tests.Game.Party;
using Dfo.Server.Abstractions.DomainEvents;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Framing;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonClear;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Protocol.Game.Altar;
using Dfo.Server.Protocol.Tests.Game.SelectCharacter;
using Dfo.Server.Protocol.Tests.Game.TownProjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.DungeonClear;

/// <summary>
/// Walking out of a finished dungeon: the command that does it, and what follows it.
/// </summary>
/// <remarks>
/// The command is <c>CMD 0x0048 EPLP_COMMAND</c> and not <c>GIVEUP_GAME</c>. A complete reference
/// session that cleared a dungeon and returned to town sent <c>0x0048</c> twice and <c>0x002A</c>
/// never; this server had no such type, which is what a player reported as returning to town not
/// working.
/// </remarks>
public sealed class LeaveDungeonTests
{
    [Fact]
    public async Task Ordinary_rechallenge_retains_ordinary_selection_after_the_run_ends()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var intents = new Dfo.Server.Application.World.DungeonEntryIntentDomainService();
            await StoryHandler(runs, new RecordingSender(), intents: intents).HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, 0), default);

            Assert.True(runs.Ended);
            var intent = intents.Find(Identity);
            Assert.NotNull(intent);
            Assert.Equal(144, intent.Value.DirectedDungeonId);
            Assert.Equal(DungeonEntryIntentKind.OrdinaryRechallenge, intent.Value.Kind);
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public async Task Only_ordinary_retry_retains_the_rechallenge_intent(byte option, bool story)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var intents = new Dfo.Server.Application.World.DungeonEntryIntentDomainService();
            await StoryHandler(new OneRun(story: story), new RecordingSender(), intents: intents).HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, option), default);
            Assert.Null(intents.Find(Identity));
        });
    }

    [Theory]
    [InlineData("sao")]
    [InlineData("mirror")]
    [InlineData("hell")]
    [InlineData("despair")]
    public async Task Specialized_retries_do_not_gain_the_ordinary_retry_intent(string kind)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun(layoutChange: layout => kind switch
            {
                "sao" => layout with { Mode = new DungeonRunMode.Sao(new(new("test"))) },
                "mirror" => layout with { Mode = new DungeonRunMode.Mirror(new(8000, 31, 14299)) },
                "despair" => layout with { Mode = new DungeonRunMode.Despair(new(1, new DateOnly(2026, 10, 13))) },
                _ => layout with { Mode = new DungeonRunMode.Hell(new(0, layout.BossCell, layout.StartRoomMapId, [])) },
            });
            var intents = new Dfo.Server.Application.World.DungeonEntryIntentDomainService();
            await StoryHandler(runs, new RecordingSender(), intents: intents).HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, 0), default);
            Assert.True(runs.Ended);
            Assert.Null(intents.Find(Identity));
        });
    }

    /// <summary>
    /// The exchange as captured, transcribed.
    /// </summary>
    /// <remarks>
    /// From <c>packet_log.upstream-killexp-20260827.txt</c>, the second of the two the session
    /// sent -- the one the town state follows:
    /// <code>
    /// RECV cmd=0x01 type=0x0048 len=16 body=(2B) [01 02]
    /// SEND cmd=0x01 type=0x0048 len=18 body=(3B) [01 01 02]
    /// </code>
    /// Transcribed rather than derived from the constants under test: request and answer share one
    /// type, so a wrong constant changes both and no self-consistent test would notice.
    /// </remarks>
    private static readonly byte[] CapturedLeaveBody = [0x01, 0x02];

    private static readonly byte[] CapturedAnswerBody = [0x01, 0x01, 0x02];

    private const ushort ObservedType = 0x0048;

    /// <summary>The state and option of that captured request.</summary>
    private const byte LeaveState = 0x01;

    private const byte CapturedOption = 0x02;

    [Fact]
    public async Task Next_story_preflight_and_confirmation_project_the_town_only_once()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var sender = new RecordingSender();
            var handler = StoryHandler(runs, sender);
            foreach (var (state, option) in new (byte, byte)[] { (2, 3), (1, 1), (1, 3) })
                await handler.HandleAsync(Dispatch(), new EplpCommandRequest(
                    GameMessageMetadata.Empty, state, option), CancellationToken.None);

            Assert.True(runs.Ended);
            Assert.Equal(new byte[] { 2, 1 }, sender.Sent.OfType<EplpCommandResponse>().Select(x => x.State));
            Assert.Equal(new byte[] { 3, 1 }, sender.Sent.OfType<EplpCommandResponse>().Select(x => x.Option));
            Assert.Single(sender.Sent.OfType<UserAreaNotification>());
            Assert.Single(sender.Sent.OfType<AreaUsersNotification>());
        });
    }

    [Fact]
    public async Task Next_story_confirmation_with_a_run_only_acknowledges()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var sender = new RecordingSender();
            await StoryHandler(runs, sender).HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, 3), CancellationToken.None);
            Assert.False(runs.Ended);
            Assert.IsType<EplpCommandResponse>(Assert.Single(sender.Sent));
        });
    }

    private static EplpDriver StoryHandler(OneRun runs, RecordingSender sender,
        IDungeonEntryIntentDomainService? intents = null)
    {
        var areas = new SessionPresenceDomainService();
        return new(
            new OneSession(Identity), new OneRuntimeSession(Entry()), new StandingWhereTheyAre(areas),
            runs, new DungeonRunDeliveryRegistry(), sender,
            TownAreaProjectionTestFactory.Create(new OneSession(Identity), new OneRuntimeSession(Entry()), sender, areas),
            NullLogger<EplpCommandHandler>.Instance, entryIntents: intents);
    }

    [Fact]
    public async Task Failed_exit_delivery_releases_the_transition_without_repeating_the_exit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var sender = new RecordingSender { Failure = () => new IOException("send failed") };
            var handler = StoryHandler(runs, sender);
            await Assert.ThrowsAsync<IOException>(async () => await handler.HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, 1), default));
            Assert.True(runs.Ended);
            await handler.HandleAsync(Dispatch(), new EplpCommandRequest(GameMessageMetadata.Empty, 1, 3), default);
            Assert.Empty(sender.Sent);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_new_selection_intent_prevents_an_old_town_projection(bool hasRun)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun(hasRun);
            var sender = new RecordingSender();
            await StoryHandler(runs, sender, intents: new SelectedIntent()).HandleAsync(Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, 1, 2), default);
            Assert.False(runs.Ended);
            Assert.Empty(sender.Sent);
        });
    }

    private sealed class SelectedIntent : IDungeonEntryIntentDomainService
    {
        public DungeonEntryIntent? Find(SelectedSessionIdentity session) => DungeonEntryIntent.Directed(148);
        public void Record(SelectedSessionIdentity session, DungeonEntryIntent intent) => throw new NotSupportedException();
        public DungeonEntryIntent? Consume(SelectedSessionIdentity session) => throw new NotSupportedException();
        public void RestoreIfAbsent(SelectedSessionIdentity session, DungeonEntryIntent intent) => throw new NotSupportedException();
        public bool ClearExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Exit_command_ends_the_run_through_the_explicit_domain_entry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCommandDispatching();
        var runs = new OneRun();
        services.AddDungeonRunsAs(runs);
        services.AddDungeonRunViews();
        services.AddSingleton<ISelectedSessionDomainService>(new OneSession(Identity));
        services.AddFixtureCommand<LeaveDungeonAlgorithmCommand, LeaveDungeonAlgorithmResult, LeaveDungeonAlgorithmHandler>(
            FixtureUnitKind.Direct);
        await using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        var result = await DomainCommandFixture.Bus(provider).SendAsync(
            new LeaveDungeonAlgorithmCommand(Identity, [Identity], runs.RunId)).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal([Identity.CharacterId], result.EndedCharacters);
        Assert.True(runs.Ended);
    }

    [Fact]
    public void The_captured_request_decodes_to_its_state_and_option()
    {
        var request = (EplpCommandRequest)new EplpCommandDecoder()
            .Decode(GameMessageMetadata.Empty, CapturedLeaveBody)!;

        Assert.Equal(LeaveState, request.State);
        Assert.Equal(CapturedOption, request.Option);
    }

    /// <summary>
    /// A request missing its option is declined, and declining is not throwing.
    /// </summary>
    /// <remarks>
    /// A throwing decoder takes the connection with it, and this is the frame a player sends to
    /// leave a finished dungeon. Dropping them for a malformed press is the one answer that must
    /// not happen.
    /// </remarks>
    [Fact]
    public void A_request_missing_its_option_is_declined_not_thrown()
    {
        var thrown = Record.Exception(() =>
            Assert.Null(new EplpCommandDecoder().Decode(GameMessageMetadata.Empty, new byte[1])));

        Assert.Null(thrown);
    }

    /// <summary>The answer is the captured one, under the captured type.</summary>
    /// <remarks>
    /// The two echoed bytes are not decoration. The client's reply handler reads the success byte
    /// and then the state, and runs the leave path only when the state it gets back is 1 -- so an
    /// answer that dropped or altered them would be answered and still not leave.
    /// </remarks>
    [Fact]
    public void The_answer_echoes_the_state_and_option_under_the_captured_type()
    {
        var frame = Assert.Single(
            new EplpCommandResponseEncoder()
                .Encode(new EplpCommandResponse(LeaveState, CapturedOption)));

        Assert.Equal(0x01, frame[0]);
        Assert.Equal(ObservedType, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(CapturedAnswerBody, frame[GameServerFrameEncoder.HeaderLength..]);
    }

    /// <summary>Leaving ends the run and projects the town, in that order behind the answer.</summary>
    [Fact]
    public async Task Leaving_ends_the_run_and_projects_the_town()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var sent = await Leave(LeaveState, runs);

            Assert.True(runs.Ended);
            Assert.Equal(
                [typeof(EplpCommandResponse), typeof(UserStateNotification),
                 typeof(UserAreaNotification), typeof(AreaUsersNotification), typeof(WeaknessProjectionNotification)],
                sent.Select(frame => frame.GetType()));
            Assert.Equal((byte)37, Assert.Single(sent.OfType<WeaknessProjectionNotification>()).Value);
        });
    }

    /// <summary>The projection carries where the character is standing, not the channel's spawn.</summary>
    /// <remarks>
    /// A player who walked to the dungeon gate should come back to the gate. This is the assertion
    /// that catches projecting the spawn point instead, which would silently teleport them across
    /// town every time they finished a run.
    /// </remarks>
    [Fact]
    public async Task The_projection_carries_where_the_character_is_standing()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sent = await Leave(LeaveState, new OneRun());

            var area = sent.OfType<UserAreaNotification>().Single();
            Assert.Equal(StandingAreaId, area.AreaId);

            var users = sent.OfType<AreaUsersNotification>().Single();
            Assert.Equal(StandingTownId, users.TownId);
            Assert.Equal(StandingAreaId, users.AreaId);
            var presence = Assert.Single(users.Users);
            Assert.Equal(StandingX, presence.X);
            Assert.Equal(StandingY, presence.Y);
        });
    }

    /// <summary>Every other state is answered and moves nothing.</summary>
    /// <remarks>
    /// The captured session sent <c>02 02</c> before <c>01 02</c> and only the second was followed
    /// by the town state. Treating every state as an exit would end the run under the player's feet
    /// the first time the client used this command for anything else.
    /// </remarks>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)2)]
    [InlineData((byte)9)]
    public async Task A_state_that_is_not_the_exit_is_answered_and_moves_nothing(byte state)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var sent = await Leave(state, runs);

            Assert.False(runs.Ended);
            var answer = Assert.IsType<EplpCommandResponse>(Assert.Single(sent));
            Assert.Equal(state, answer.State);
        });
    }

    /// <summary>
    /// Leaving with no run left to end is still answered and still projects the town.
    /// </summary>
    /// <remarks>
    /// The button has already been pressed by the time this arrives, and the client cannot ask
    /// again. Refusing would leave it exactly as unanswered did -- which is the defect.
    /// </remarks>
    [Fact]
    public async Task Leaving_without_a_run_is_still_answered_and_still_projects_the_town()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun(hasRun: false);
            var sent = await Leave(LeaveState, runs);

            Assert.Equal(5, sent.Count);
            Assert.Contains(sent, frame => frame is AreaUsersNotification);
            Assert.Equal(
                [typeof(EplpCommandResponse), typeof(UserStateNotification), typeof(UserAreaNotification),
                 typeof(AreaUsersNotification), typeof(WeaknessProjectionNotification)],
                sent.Select(frame => frame.GetType()));
            Assert.Equal((byte)37, Assert.Single(sent.OfType<WeaknessProjectionNotification>()).Value);
        });
    }

    [Fact]
    public async Task Leaving_removes_only_this_participant_from_the_frozen_delivery_set()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var helper = SelectedSessionIdentity.Create(
                new AccountId(2),
                new CharacterId(61),
                new NetworkConnectionId("helper"),
                connectionGeneration: 1,
                listenerPort: 10010);
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(runs.RunId, [Identity, helper]);
            var areas = new SessionPresenceDomainService();
            var handler = new EplpDriver(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                new StandingWhereTheyAre(areas),
                runs,
                deliveries,
                new RecordingSender(),
                TownAreaProjectionTestFactory.Create(
                    new OneSession(Identity),
                    new OneRuntimeSession(Entry()),
                    new RecordingSender(),
                    areas),
                NullLogger<EplpCommandHandler>.Instance);

            await handler.HandleAsync(
                Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, LeaveState, CapturedOption),
                CancellationToken.None);

            Assert.Equal([helper], deliveries.Find(runs.RunId));
        });
    }

    [Theory]
    [InlineData((byte)10)]
    [InlineData((byte)100)]
    public async Task GiveUp_sends_weakness_once_after_success_and_before_town(byte weaknessValue)
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(runs.RunId, [Identity]);
            var sender = new RecordingSender();
            var parties = new RecordingPartyMutations();
            var areas = new SessionPresenceDomainService();
            var handler = new GiveUpDriver(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                new StandingWhereTheyAre(areas),
                runs,
                deliveries,
                sender,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.TownProjection.TownAreaProjectionDelivery>(areas, new OneSession(Identity), new OneRuntimeSession(Entry()), new NoopNotificationBus(), new NoopPartyTownProjection(), sender, SharedTownMovementDomainService.Instance, new FixedWeakness(weaknessValue)),
                NullLogger<GiveUpGameHandler>.Instance,
                areas,
                parties,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(new NoopNotificationBus()));

            await handler.HandleAsync(
                Dispatch(),
                new GiveUpGameRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            Assert.True(runs.Ended);
            Assert.Equal(
                [typeof(GiveUpGameResponse), typeof(WeaknessProjectionNotification), typeof(UserStateNotification),
                 typeof(UserAreaNotification), typeof(AreaUsersNotification)],
                sender.Sent.Select(frame => frame.GetType()));
            Assert.Equal(weaknessValue, Assert.Single(sender.Sent.OfType<WeaknessProjectionNotification>()).Value);
            Assert.Equal(
                StandingAreaId,
                Assert.Single(sender.Sent.OfType<UserAreaNotification>()).AreaId);
            Assert.Empty(deliveries.Find(runs.RunId));
            Assert.Equal(
                Identity.CharacterId,
                Assert.Single(parties.Left));
        });
    }

    [Fact]
    public async Task GiveUp_without_an_active_run_does_not_leave_the_party()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var parties = new RecordingPartyMutations();
            var areas = new SessionPresenceDomainService();
            var handler = new GiveUpDriver(
                new OneSession(Identity),
                new OneRuntimeSession(Entry()),
                new StandingWhereTheyAre(areas),
                new OneRun(hasRun: false),
                new DungeonRunDeliveryRegistry(),
                new RecordingSender(),
                TownAreaProjectionTestFactory.Create(
                    new OneSession(Identity),
                    new OneRuntimeSession(Entry()),
                    new RecordingSender(), areas),
                NullLogger<GiveUpGameHandler>.Instance,
                areas,
                parties,
                global::Dfo.Server.Testing.ProtocolAlgorithmFixture.Create<global::Dfo.Server.Protocol.Game.Party.PartyNotificationDelivery>(new NoopNotificationBus()));

            await handler.HandleAsync(
                Dispatch(),
                new GiveUpGameRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            Assert.Empty(parties.Left);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Give_up_completes_through_the_real_command_bus_and_releases_the_session(bool isAltar)
    {
        var runs = new OneRun(isAltar: isAltar);
        var deliveries = new DungeonRunDeliveryRegistry(); TestGameplay.Run(() => deliveries.Register(runs.RunId, [Identity]));
        var sessions = new OneSession(Identity); var sender = new RecordingSender(); var trace = new ExitCommandTrace();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System); services.AddSingleton<ISelectedSessionDomainService>(sessions);
        services.AddCommandDispatching();
        services.AddScoped<ExitBusHandler>(provider => new(sessions, runs, deliveries, sender,
            new RecordingPartyMutations(trace.PartyLeaves.Add), provider.GetRequiredService<ICommandWorkUnits>(),
            new TracedAltar(runs.RunId, trace, provider.GetRequiredService<IDomainTransactionAccessor>())));
        services.AddProtocolCommandHandler<GameGiveUpGameCommand, bool, ExitBusHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameGiveUpGame, typeof(GiveUpGameRequest)));
        services.AddScoped<ICommandHandler<GameGiveUpGameCommand, bool>>(p => p.GetRequiredService<ExitBusHandler>());
        using var provider = services.AddSessionSafetyStandIns().BuildServiceProvider();
        var commands = DomainCommandFixture.Bus(provider);
        runs.BeforeEnd = () =>
        {
            if (isAltar) Assert.Equal(AltarOperation.SettleParticipantBeforeExit, Assert.Single(trace.Commands));
            else Assert.Empty(trace.Commands);
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await commands.SendAsync(new GameGiveUpGameCommand(Dispatch(), new GiveUpGameRequest(GameMessageMetadata.Empty)), deadline.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(runs.Ended); Assert.Empty(deliveries.Find(runs.RunId));
        Assert.Single(sender.Sent.OfType<GiveUpGameResponse>()); Assert.Single(sender.Sent.OfType<UserAreaNotification>());
        await commands.SendAsync(new GameGiveUpGameCommand(Dispatch(), new GiveUpGameRequest(GameMessageMetadata.Empty)), default);
        Assert.Equal(isAltar ? 1 : 0, trace.Commands.Count); Assert.Equal(Identity.CharacterId, Assert.Single(trace.PartyLeaves));
    }
    private sealed class ExitBusHandler(OneSession selected, OneRun runs, DungeonRunDeliveryRegistry registry, RecordingSender sender,
        RecordingPartyMutations parties, ICommandWorkUnits units, IAltarDungeonDomainService altars)
        : ICommandHandler<GameGiveUpGameCommand, bool>
    {
        private readonly GamePartyTransitionCommandHandler handler = new SessionPresenceDomainService() is var areas ? new(selected,new OneRuntimeSession(Entry()),new StandingWhereTheyAre(areas),runs,ProtocolAlgorithmFixture.Exit(parties,runs),registry,            parties,areas,ProtocolAlgorithmFixture.Default<ICommandProjections>(),ProtocolAlgorithmFixture.Default<IPartyProjectionStateDomainService>(),ProtocolAlgorithmFixture.Default<ICharacterStateProjectionDomainService>(),ProtocolAlgorithmFixture.Default<ICharacterDomainService>(),ProtocolAlgorithmFixture.TownTransit(null, selected, areas, new StandingWhereTheyAre(areas)),ProtocolAlgorithmFixture.Default<IBossTowerDomainService>(),ProtocolAlgorithmFixture.Default<IIllusionTowerDomainService>(),new DerivedDungeonParticipation(ProtocolAlgorithmFixture.Default<IBossTowerDomainService>(),ProtocolAlgorithmFixture.Default<IIllusionTowerDomainService>(),runs,selected),ProtocolAlgorithmFixture.NoOnline(),units,            new Dfo.Server.Protocol.Tests.Support.GameOutboxFixture(sender),ProtocolAlgorithmFixture.Default<ICommandConnectionClosures>(),ProtocolAlgorithmFixture.Default<IDungeonCardPresentationControl>(),ProtocolAlgorithmFixture.Default<IDungeonEntryIntentDomainService>(),altars,ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(),TimeProvider.System,            NullLogger<GamePartyTransitionCommandHandler>.Instance,ordering: ProtocolAlgorithmFixture.Default<IOutboundOrdering>(),guardian: ProtocolAlgorithmFixture.Default<IGuardianAltarDomainService>()) : null!;
        public ValueTask<bool> HandleAsync(CommandExecutionContext context, GameGiveUpGameCommand command, CancellationToken ct) =>
            handler.HandleAsync(context, command, ct);
    }
    private sealed class TracedAltar(DungeonRunId id, ExitCommandTrace trace, IDomainTransactionAccessor transactions) : AltarView(id)
    {
        public override ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId run, CancellationToken token, CharacterId? participant = null)
        {
            Assert.NotNull(transactions.Current); Assert.Equal(Identity.CharacterId, participant);
            trace.Commands.Add(participant is null ? AltarOperation.SettleBeforeExit : AltarOperation.SettleParticipantBeforeExit);
            return ValueTask.FromResult<AltarActionResult?>(null);
        }
    }

    private sealed class ExitCommandTrace { public List<AltarOperation> Commands { get; } = []; public List<CharacterId> PartyLeaves { get; } = []; }

    [Fact]
    public async Task Stale_give_up_stops_before_any_current_run_or_party_capture()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var stale = new GameMessageMetadata(1, 0x002A, 0, 9)
            {
                InboundOrdinal = 2,
                SequenceIsFresh = false,
            };
            var handler = new GiveUpDriver(
                null!, null!, null!, null!, null!, null!, null!, null!, null!);

            await handler.HandleAsync(
                Dispatch(), new GiveUpGameRequest(stale), CancellationToken.None);
        });
    }

    /// <summary>Nothing is answered on a connection with no published session.</summary>
    [Fact]
    public async Task A_command_without_a_published_session_is_ignored()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var sender = new RecordingSender();
            var handler = new EplpDriver(
                new NoSession(),
                new NoRuntimeSession(),
                new StandingNowhere(),
                new OneRun(),
                new DungeonRunDeliveryRegistry(),
                sender,
                TownAreaProjectionTestFactory.Create(
                    new NoSession(),
                    new NoRuntimeSession(),
                    sender),
                NullLogger<EplpCommandHandler>.Instance);

            await handler.HandleAsync(
                Dispatch(),
                new EplpCommandRequest(GameMessageMetadata.Empty, LeaveState, CapturedOption),
                CancellationToken.None);

            Assert.Empty(sender.Sent);
        });
    }

    /// <summary>
    /// Cancelling out of the selection screen projects the same town frames, from the same source.
    /// </summary>
    /// <remarks>
    /// The projection was extracted out of that handler so this one could use it. This is the test
    /// that keeps them one thing: written twice they drift, and the drift shows up as returning to
    /// town working from the gate and not from the dungeon.
    /// </remarks>
    [Fact]
    public async Task Cancelling_out_of_the_selection_screen_projects_the_same_frames()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var runs = new OneRun();
            var deliveries = new DungeonRunDeliveryRegistry();
            deliveries.Register(runs.RunId, [Identity]);
            var sender = new RecordingSender();
            var areas = new SessionPresenceDomainService();
            var handler = Dfo.Server.Protocol.Tests.Game.DungeonEntry.DungeonSelectionTestDriver.Back(
                new OneSession(Identity),
                new RuntimeCurrentView(new OneRuntimeSession(Entry())),
                new StandingWhereTheyAre(areas),
                runs,
                deliveries,
                sender,
                ProtocolAlgorithmFixture.Create<TownAreaProjectionDelivery>(areas,
                    new OneSession(Identity), new OneRuntimeSession(Entry()), sender,
                    new NoopNotificationBus(), new NoopPartyTownProjection(), SharedTownMovementDomainService.Instance,
                    new FixedWeakness(37)),
                NullLogger<BackToVillageHandler>.Instance,
                new VirtualOnlyPartyService());

            await handler.HandleAsync(
                Dispatch(),
                new BackToVillageRequest(GameMessageMetadata.Empty),
                CancellationToken.None);

            Assert.Equal(
                [typeof(BackToVillageResponse), typeof(UserStateNotification),
                 typeof(UserAreaNotification), typeof(AreaUsersNotification), typeof(WeaknessProjectionNotification)],
                sender.Sent.Select(frame => frame.GetType()));
            Assert.Equal((byte)37, Assert.Single(sender.Sent.OfType<WeaknessProjectionNotification>()).Value);

            // The same values the dungeon's exit projects, asserted the same way rather than by
            // comparing the two message objects: the roster notification holds a list, and records
            // compare lists by reference, so an object-level equality here would pass or fail for a
            // reason that has nothing to do with the projection agreeing.
            var users = sender.Sent.OfType<AreaUsersNotification>().Single();
            Assert.Equal(StandingTownId, users.TownId);
            Assert.Equal(StandingAreaId, users.AreaId);
            var presence = Assert.Single(users.Users);
            Assert.Equal(StandingX, presence.X);
            Assert.Equal(StandingY, presence.Y);
            Assert.Equal(SpawnAreaState, presence.AreaState);
            Assert.Empty(deliveries.Find(runs.RunId));
        });
    }

    private static async Task<IReadOnlyList<IGameServerMessage>> Leave(byte state, OneRun runs)
    {
        var sender = new RecordingSender();
        var areas = new SessionPresenceDomainService();
        if (runs.Find(Identity.CharacterId) is null)
            areas.Admit(new SessionPresence(Identity.CharacterId, Identity.ListenerPort, StandingTownId, StandingAreaId,
                true, Identity, true, Entry().World.Channel.ChannelId));
        var handler = new EplpDriver(
            new OneSession(Identity),
            new OneRuntimeSession(Entry()),
            new StandingWhereTheyAre(areas),
            runs,
            new DungeonRunDeliveryRegistry(),
            sender,
            ProtocolAlgorithmFixture.Create<TownAreaProjectionDelivery>(areas,
                new OneSession(Identity), new OneRuntimeSession(Entry()), sender,
                new NoopNotificationBus(), new NoopPartyTownProjection(), SharedTownMovementDomainService.Instance,
                new FixedWeakness(37)),
            NullLogger<EplpCommandHandler>.Instance, townAreas: areas);

        await handler.HandleAsync(
            Dispatch(),
            new EplpCommandRequest(GameMessageMetadata.Empty, state, CapturedOption),
            CancellationToken.None);

        return sender.Sent;
    }

    private const byte StandingTownId = 3;
    private const byte StandingAreaId = 6;
    private const short StandingX = 902;
    private const short StandingY = 388;

    /// <summary>The area state the channel's spawn carries, which the projection reuses.</summary>
    private const byte SpawnAreaState = 7;

    /// <summary>Where the character actually is, which the projection reads.</summary>
    private static CharacterPositionState Standing { get; } = new(
        StandingTownId, StandingAreaId, StandingX, StandingY, Direction: 1);

    /// <summary>
    /// An entry whose spawn and whose stale snapshot are both somewhere else.
    /// </summary>
    /// <remarks>
    /// Three different places, deliberately, and every coordinate differs. The spawn is one wrong
    /// answer -- projecting it would teleport a player across town for finishing a run. The copy of
    /// the position the entry snapshot still carries is the other: it is the one the projection
    /// used to read, and it stops being corrected the moment a step is reported to the service that
    /// owns where the character stands. Either mistake fails every assertion below rather than
    /// coinciding with the right answer.
    /// </remarks>
    private static SelectCharacterResult Entry()
    {
        var result = SelectCharacterPayloadTestData.Create();
        return result with
        {
            Character = result.Character with
            {
                Position = new CharacterPositionState(
                    TownId: 9, AreaId: 8, X: 77, Y: 66, Direction: 4),
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
        connectionGeneration: 2,
        listenerPort: 10010);

    /// <summary>A run that records whether it was ended, and refuses everything else.</summary>
    private sealed class OneRun(bool hasRun = true, bool isAltar = false, bool story = false,
        Func<DungeonLayout, DungeonLayout>? layoutChange = null) : DungeonRunServiceStub
    {
        private static readonly DungeonLayout Layout = new(
            144, 0, 1, 1,
            new MazeCell(0, 0), new MazeCell(0, 0), 8153, 7,
            new Dictionary<MazeCell, DungeonCellDefinition> { [new MazeCell(0, 0)] = DungeonCellDefinition.Fixed(8153)},
            5);

        public bool Ended { get; private set; }
        public Action? BeforeEnd { get; set; }

        public DungeonRunId RunId { get; private set; } = DungeonRunId.New();
        public void Replace() => RunId = DungeonRunId.New();

        public override bool End(CharacterId characterId)
        {
            BeforeEnd?.Invoke();
            this.Ended = true;
            return hasRun;
        }

        /// <summary>The fixture's run has the one identity as its only member.</summary>
        public override ValueTask<IReadOnlyList<CharacterId>> EndRunAsync(DungeonRunId runId) =>
            ValueTask.FromResult<IReadOnlyList<CharacterId>>(
                this.Find(Identity.CharacterId)?.RunId == runId && this.End(Identity.CharacterId)
                    ? [Identity.CharacterId]
                    : []);

        public override DungeonRunState? Find(CharacterId characterId) => hasRun && !this.Ended
            ? new DungeonRunState(
                layoutChange is not null ? layoutChange(Layout)
                    : isAltar ? Layout with { Mode = new DungeonRunMode.Altar(new(AltarDungeonKind.Ultimate, new(2026, 10, 13))) }
                    : story ? Layout with { Purpose = DungeonRunPurpose.QuestLinked(1776, DungeonQuestConnectionState.Active) } : Layout,
                Layout.StartCell,
                Layout.StartRoomMapId,
                new HashSet<MazeCell> { Layout.StartCell },
                DateTimeOffset.UnixEpoch,
                RunId: this.RunId,
                Participants: isAltar ? [new(Identity.AccountId, Identity.CharacterId, 0, true)] : [],
                CurrentCellState: DungeonCellSeed.CompleteEmpty(
                    Layout.StartCell, Layout.StartRoomMapId).Snapshot())
            : null;

        public DungeonRunState Begin(CharacterId characterId, DungeonLayout layout, DungeonDifficulty difficulty) =>
            throw new NotSupportedException("This fixture only ends.");

        public override DungeonRunState? TryMove(CharacterId characterId, MazeCell target) =>
            throw new NotSupportedException("This fixture only ends.");

        public override DungeonRunState? TrySettle(CharacterId characterId) =>
            throw new NotSupportedException("This fixture only ends.");

        public override DungeonRunState? FixClearRewardCards(
            CharacterId characterId,
            ClearRewardCardFace free,
            ClearRewardCardFace paid,
            int? paidFlipCost) =>
            throw new NotSupportedException("This fixture only ends.");

        public override ClearRewardCardSlot? TryClaimClearRewardCard(
            CharacterId characterId, ClearRewardCardKind kind, int index) =>
            throw new NotSupportedException("This fixture only ends.");}

    private sealed class OneSession(SelectedSessionIdentity identity) : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => identity;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => identity;

        public SelectedSessionIdentity? Find(AccountId accountId) => identity;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => identity;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class NoSession : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => null;

        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => null;

        public SelectedSessionIdentity? Find(AccountId accountId) => null;

        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => null;

        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool ReleaseExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class OneRuntimeSession(SelectCharacterResult entry)
        : ILegacySessionSnapshotFixture
    {
        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = entry;
            shouldPersistPosition = false;
            return true;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) => entry;

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("Leaving does not rewrite the session.");

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture only answers.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture only answers.");
    }

    private sealed class NoRuntimeSession : ILegacySessionSnapshotFixture
    {
        public bool TryGet(
            SelectedSessionIdentity session,
            out SelectCharacterResult? result,
            out bool shouldPersistPosition)
        {
            result = null;
            shouldPersistPosition = false;
            return false;
        }

        public SelectCharacterResult GetRequired(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public void ReplaceExact(SelectedSessionIdentity session, SelectCharacterResult result) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public void Publish(
            SelectedSessionIdentity session, SelectCharacterResult result, bool persist) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public bool RemoveExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");
    }

    /// <summary>The service that owns where the character is, answering the one place they are.</summary>
    /// <remarks>
    /// The real runtime over the town areas the handler reads, so the return to town lands in the
    /// same store the projection is built from. The character left that place for the dungeon,
    /// unless the test already stood it in town; any other character answers the same place.
    /// </remarks>
    private sealed class StandingWhereTheyAre(SessionPresenceDomainService? areas = null)
        : IOnlineCharacterRuntimeDomainService
    {
        private readonly Dfo.Server.Application.World.OnlineCharacterLocationDomainService locations =
            areas?.Locations ?? new Dfo.Server.Application.World.OnlineCharacterLocationDomainService();
        private Dfo.Server.Application.Characters.OnlineCharacterRuntimeDomainService? inner;

        private Dfo.Server.Application.Characters.OnlineCharacterRuntimeDomainService Inner
        {
            get
            {
                if (inner is not null)
                    return inner;
                inner = new(TimeProvider.System, locations);
                if (locations.PositionOf(Identity) is null)
                {
                    locations.Begin(Identity, new TownAreaKey(
                        Entry().World.Channel.ChannelId, Standing.TownId, Standing.AreaId), Standing, true);
                    locations.Depart(Identity, TownAreaDepartureReason.DungeonEntry);
                }
                return inner;
            }
        }

        public CharacterPositionState? PositionOf(CharacterId characterId) =>
            Inner.PositionOf(characterId) ?? Standing;

        public TownAreaMembershipChange Begin(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence, byte hpPercent) =>
            throw new NotSupportedException("Leaving a dungeon does not place anyone.");

        public TownAreaMembershipChange? CrossInto(SelectedSessionIdentity session, TownAreaKey destination, short x, short y, bool sharesTownPresence, byte? direction = null) =>
            throw new NotSupportedException("Leaving a dungeon reports no movement.");

        public TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException("Leaving a dungeon reports no movement.");

        public bool EndExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("Leaving a dungeon does not end the session.");

        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) =>
            Inner.PositionOf(session) ?? Standing;
        public TownAreaMember? StandingOf(SelectedSessionIdentity session) => Inner.StandingOf(session);
        public TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason) =>
            Inner.LeaveTown(session, reason);
        public TownAreaMembershipChange? ReturnToTown(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence) =>
            Inner.ReturnToTown(session, area, position, sharesTownPresence);
        public byte HpPercentOf(CharacterId characterId) => 0;
        public bool IsCoolingDown(CharacterId characterId, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key) => false;
        public void StartCooldown(SelectedSessionIdentity session, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key, TimeSpan duration) { }
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) { }
        public double SpiritPotionBonusRateOf(CharacterId characterId) => 0;
    }

    /// <summary>Nobody is standing anywhere, which is what no published session looks like.</summary>
    private sealed class StandingNowhere : IOnlineCharacterRuntimeDomainService
    {
        public CharacterPositionState? PositionOf(CharacterId characterId) => null;

        public TownAreaMembershipChange Begin(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence, byte hpPercent) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public TownAreaMembershipChange? CrossInto(SelectedSessionIdentity session, TownAreaKey destination, short x, short y, bool sharesTownPresence, byte? direction = null) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public TownAreaMovement? StepTo(SelectedSessionIdentity session, short x, short y, byte direction) =>
            throw new NotSupportedException("This fixture holds nothing.");

        public bool EndExact(SelectedSessionIdentity session) =>
            throw new NotSupportedException("This fixture holds nothing.");

        // Test double: only the position is modelled; the rest of the runtime is empty.
        public CharacterPositionState? PositionOf(SelectedSessionIdentity session) => PositionOf(session.CharacterId);
        public TownAreaMember? StandingOf(SelectedSessionIdentity session) => null;
        public TownAreaMembershipChange? LeaveTown(SelectedSessionIdentity session, TownAreaDepartureReason reason) =>
            throw new NotSupportedException();
        public TownAreaMembershipChange? ReturnToTown(SelectedSessionIdentity session, TownAreaKey area, CharacterPositionState position, bool sharesTownPresence) =>
            throw new NotSupportedException();
        public byte HpPercentOf(CharacterId characterId) => 0;
        public bool IsCoolingDown(CharacterId characterId, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key) => false;
        public void StartCooldown(SelectedSessionIdentity session, Dfo.Server.Abstractions.Inventory.ItemCooldownKey key, TimeSpan duration) { }
        public void ApplySpiritPotion(SelectedSessionIdentity session, double bonusRate, TimeSpan duration) { }
        public double SpiritPotionBonusRateOf(CharacterId characterId) => 0;
    }

    /// <summary>
    /// What a real registration adapter registered, read from its outbox (an explicit delivery with its direct messages
    /// only, as before); every call goes to the real protocol sender over it (TASK-25). <see cref="Failure"/> makes the
    /// direct messages of an explicit delivery fail to encode where they are registered; a CMD fails there as the old
    /// double refused one.
    /// </summary>
    private sealed class RecordingSender : IGameMessageSender
    {
        private readonly RecordedOutbound outbound = new();

        public RecordingSender()
        {
            outbound.FailAny(message => outbound.ExplicitInFlight is null && message is IGameCmdMessage,
                () => new InvalidOperationException("These frames are sent explicitly."));
            outbound.FailAny(message => outbound.ExplicitInFlight is not null && outbound.IsDirect(message) && Failure is not null,
                () => Failure!());
        }

        public List<IGameServerMessage> Sent => [.. outbound.CallMessages.Cast<IGameServerMessage>()];
        public Func<Exception>? Failure { get; init; }

        public ValueTask SendCmdAsync(NetworkConnectionId connectionId, IGameCmdMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendCmdAsync(connectionId, message, cancellationToken);
        public ValueTask SendNotiAsync(NetworkConnectionId connectionId, IGameNotiMessage message, CancellationToken cancellationToken) =>
            outbound.Game.SendNotiAsync(connectionId, message, cancellationToken);
        public ValueTask SendExplicitAsync(NetworkConnectionId requiredDirectConnectionId, GameExplicitMessageDelivery delivery, CancellationToken cancellationToken) =>
            outbound.Game.SendExplicitAsync(requiredDirectConnectionId, delivery, cancellationToken);
    }

    private sealed class FixedWeakness(byte value) : IWeaknessDomainService, IWeakness
    {
        public byte Value => value;
        public IWeakness For(CharacterId characterId) => this;
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];
        public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask SetWeakness() => throw new NotSupportedException();
        public void Recover() => throw new NotSupportedException();
        public void PauseRecovery() { }
        public void ResumeRecovery() { }
    }

    private sealed class RecordingCommandBus : ICommandBus
    {
        public ValueTask<TInternal> SendInternalAsync<TInternal>(ICommand<TInternal> command, InternalCommandOrigin origin, CancellationToken ct = default) where TInternal : notnull =>
            ValueTask.FromException<TInternal>(new NotSupportedException("This protocol-only test double does not accept internal commands."));

        public List<ICommand> Commands { get; } = [];

        public ValueTask<TResult> SendAsync<TResult>(
            ICommand<TResult> command,
            CancellationToken cancellationToken = default)
            where TResult : notnull
        {
            Commands.Add(command);
            return ValueTask.FromResult(
                (TResult)(object)PartyOperationResult.Success());
        }
    }

    private class AltarView(DungeonRunId runId) : IAltarDungeonDomainService
    {
        public bool Supports(int id) => true;
        public AltarDungeonState? Find(DungeonRunId id) => id == runId ? new(runId,
            new(11007, AltarDungeonKind.Ultimate, 65, 70, 7, 7, [], null!, 3), [], DateTimeOffset.UnixEpoch, 0, 17030) : null;
        public IReadOnlyList<AltarDeadline> Pending() => [];
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext context, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity session, DungeonRunId id, long room, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId id, long room, long generation, DateTimeOffset now, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity session, DungeonRunId id, long room, long generation, byte difficulty, CancellationToken ct) => throw new NotSupportedException();
        public ValueTask<AltarActionResult?> FailAsync(DungeonRunId id, CancellationToken ct) => throw new NotSupportedException();
        public virtual ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId id, CancellationToken ct, CharacterId? participant = null) => throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId id, CancellationToken ct) => throw new NotSupportedException();
        public bool CanRevive(CharacterId character, DungeonRunId id) => false;
        public bool StageRevival(CharacterId character, DungeonRunId id) => false;
    }

    private sealed class NoopNotificationBus : Dfo.Server.Testing.ProjectionEventService
    {
        public override ValueTask<NotificationDelivery> ProjectAsync<TNotification>(
            NotificationAudience audience,
            TNotification notification,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new NotificationDelivery([]));
    }

    private sealed class NoopPartyTownProjection : IPartyTownProjectionDelivery
    {
        public ValueTask RefreshObservedPartiesAsync(
            IReadOnlyList<SelectedSessionIdentity> recipients,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask RefreshMovedPartyAsync(
            SelectedSessionIdentity mover,
            PartyDirectoryScope? previousScope,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
