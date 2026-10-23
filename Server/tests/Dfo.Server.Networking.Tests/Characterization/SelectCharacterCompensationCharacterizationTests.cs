using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Xunit.Abstractions;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>
/// RQ-080 (b): SELECT_CHARACTER fails part-way and is compensated. A second player already stands
/// in the shared town area 1:1 in both tests, so a cleanup notice reaching other players would show.
/// </summary>
/// <remarks>
/// The requester always lands on the town gate, which does not share presence, so at baseline no
/// other player is told anything; the peer transcripts pin that.
/// </remarks>
[Collection(CharacterizationCollection.Name)]
public sealed class SelectCharacterCompensationCharacterizationTests(ITestOutputHelper output)
{
    /// <summary>
    /// The peer creates character 1; the requester creates character 2. Faults are keyed on it,
    /// so the peer's own entry and the online expired-item sweep never trip them.
    /// </summary>
    private static readonly CharacterId Requester = new(2);


    private const string CleanupScenario = "b-select-cleanup-failure";

    /// <summary>
    /// Expired-item cleanup runs after preparation and before any state is sent. Its failure is the
    /// one compensated path that answers with the adjacent CMD 01/0004 refusal and NOTI 00/01F7,
    /// and then closes the connection.
    /// </summary>
    /// <remarks>
    /// At baseline the close can overtake the pair: SendAsync returns once a frame is in
    /// TouchSocket's send pipe, and the session teardown (the handler exception rethrown out of
    /// GameNetworkPlugin.OnTcpReceived, and the detached close) can complete before the pipe is
    /// flushed. So this always-green form accepts any prefix of the recorded pair -- zero, one or
    /// both frames, byte for byte -- followed by the close. Everything before the selection, and
    /// the peer's transcript, are compared in full. The strict form is
    /// <see cref="A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close"/>.
    /// </remarks>
    [PvfFact]
    public async Task A_failed_expired_item_cleanup_refuses_the_entry_and_closes_the_connection()
    {
        var fault = new OneShotFault();
        await RunAsync(
            CleanupScenario,
            CleanupFault(fault),
            fault,
            requester =>
            {
                var frames = requester.Frames.SkipWhile(frame => !frame.Is(0x01, 0x0004)).ToArray();
                // Reported so a run under load shows how often the baseline race truncated the pair.
                output.WriteLine($"refusal frames read before the close: {frames.Length} of 2; close: {requester.CloseKind}");
                Assert.InRange(frames.Length, 0, 2);
            },
            requesterMayEndEarly: true);
    }

    /// <summary>The whole refusal pair reaches the client before the close (RQ-031(c), AC-011).</summary>
    [PvfFact]
    public async Task A_failed_expired_item_cleanup_delivers_the_whole_refusal_pair_before_the_close()
    {
        var fault = new OneShotFault();
        await RunAsync(
            CleanupScenario,
            CleanupFault(fault),
            fault,
            requester =>
            {
                var frames = requester.Frames.SkipWhile(frame => !frame.Is(0x01, 0x0004)).ToArray();
                Assert.True(frames.Length == 2,
                    $"Expected the refusal pair before the close, read {frames.Length} frame(s); close: {requester.CloseKind}");
                Assert.Equal(new byte[] { 0x00, 0x02 }, frames[0].Body.ToArray());
                Assert.True(frames[1].Is(0x00, 0x01F7));
            },
            requesterMayEndEarly: false);
    }

    private const string QuestScenario = "b-select-quest-failure";

    /// <summary>
    /// Quest synchronization runs after the session is published into the world. Its failure is
    /// compensated without a refusal: the accepted batch has already gone out, then the close.
    /// </summary>
    /// <remarks>
    /// The same baseline race as the refusal pair: the close can overtake the tail of the entry
    /// batch still in TouchSocket's send pipe. This always-green form therefore accepts any prefix
    /// of the recorded selection step, byte for byte, followed by the close; login, creation and
    /// the peer are compared in full. The strict form is
    /// <see cref="A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close"/>.
    /// </remarks>
    [PvfFact]
    public async Task A_failed_quest_synchronization_after_publication_compensates_and_closes_the_connection()
    {
        var fault = new OneShotFault();
        await RunAsync(
            QuestScenario,
            QuestFault(fault),
            fault,
            requester =>
            {
                var step = requester.Frames.SkipWhile(frame => !frame.Is(0x01, 0x0004)).ToArray();
                output.WriteLine($"selection frames read before the close: {step.Length}; close: {requester.CloseKind}");
                Assert.DoesNotContain(step, frame => frame.Is(0x00, 0x01F7));
            },
            requesterMayEndEarly: true);
    }

    /// <summary>The whole accepted batch and tail reach the client before the close (RQ-031(c), AC-007).</summary>
    [PvfFact]
    public async Task A_failed_quest_synchronization_delivers_the_whole_entry_batch_before_the_close()
    {
        var fault = new OneShotFault();
        await RunAsync(
            QuestScenario,
            QuestFault(fault),
            fault,
            requester =>
            {
                var accepted = requester.Frames.First(frame => frame.Is(0x01, 0x0004));
                Assert.Equal(1, accepted.Body[0]);
                Assert.DoesNotContain(requester.Frames, frame => frame.Is(0x00, 0x01F7));
            },
            requesterMayEndEarly: false);
    }

    private static CharacterizationOptions QuestFault(OneShotFault fault) => new()
    {
        Decorate = services => services.Decorate<ISelectCharacterQuestEntryDomainService>(
            (_, inner) => new FailingQuestEntry(inner, fault)),
    };

    private static CharacterizationOptions CleanupFault(OneShotFault fault) => new()
    {
        Decorate = services => services.Decorate<IExpiredCharacterItemCleanupDomainService>(
            (_, inner) => new FailingExpiredItemCleanup(inner, fault)),
    };

    private static async Task RunAsync(
        string scenario,
        CharacterizationOptions options,
        OneShotFault fault,
        Action<WireClient> assertRequester,
        bool requesterMayEndEarly)
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(options, timeout.Token);
        using var peer = await server.EnterWorldAsync("peer", "peer-player", "Peer", timeout.Token);
        await peer.MoveToSharedAreaAsync(timeout.Token);

        var (mId, ticket) = await server.RegisterAndIssueTicketAsync("failing-player", timeout.Token);
        using var requester = await server.ConnectAsync("requester", timeout.Token);
        await requester.LoginAsync(mId, ticket, timeout.Token);
        await requester.CreateCharacterAsync("Failing", timeout.Token);

        fault.Arm(Requester);
        requester.Step("select character (fault armed)");
        peer.Step("requester selects with the fault armed");
        await requester.SendAsync(0x0004, [0, 0], timeout.Token);
        await requester.ReadUntilClosedAsync(timeout.Token);
        Assert.True(fault.Fired, "The injected fault was never reached.");
        Assert.True(requester.IsClosed, "The server did not close the requester's connection.");
        await peer.FenceAsync(timeout.Token);

        assertRequester(requester);
        // After a post-publication failure the Southern Dale quest offers race the compensation
        // that closes the connection, so at baseline they may or may not reach the requester.
        await CharacterizationTranscript.AssertMatchesAsync(scenario, requester, timeout.Token,
            optionalFloating: CharacterizationTranscript.Floating,
            lastStepMayEndEarly: requesterMayEndEarly);
        await CharacterizationTranscript.AssertMatchesAsync(scenario, peer, timeout.Token);
    }


    private sealed class OneShotFault
    {
        private int armed;
        private int target;

        public bool Fired { get; private set; }

        public void Arm(CharacterId character)
        {
            Volatile.Write(ref target, character.Value);
            Volatile.Write(ref armed, 1);
        }

        public void ThrowIfArmedFor(CharacterId character)
        {
            if (character.Value != Volatile.Read(ref target) || Interlocked.Exchange(ref armed, 0) != 1) return;
            Fired = true;
            throw new IOException("characterization: injected failure");
        }
    }

    private sealed class FailingExpiredItemCleanup(IExpiredCharacterItemCleanupDomainService inner, OneShotFault fault)
        : IExpiredCharacterItemCleanupDomainService
    {
        public ValueTask<ExpiredItemCleanupResult> CleanupAsync(
            AccountId accountId, CharacterId characterId, CancellationToken cancellationToken)
        {
            fault.ThrowIfArmedFor(characterId);
            return inner.CleanupAsync(accountId, characterId, cancellationToken);
        }
    }

    private sealed class FailingQuestEntry(ISelectCharacterQuestEntryDomainService inner, OneShotFault fault)
        : ISelectCharacterQuestEntryDomainService
    {
        public ValueTask<SelectCharacterQuestSynchronization?> SynchronizeAsync(
            SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken cancellationToken)
        {
            fault.ThrowIfArmedFor(session.CharacterId);
            return inner.SynchronizeAsync(session, result, cancellationToken);
        }
    }
}
