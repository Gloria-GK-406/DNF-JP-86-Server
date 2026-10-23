using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// One publish, walked one recipient at a time, where nobody's trouble is anybody else's.
/// </summary>
/// <remarks>
/// The four outcomes are the subject here. Three of them are not failures, and two of those --
/// somebody the handler had nothing for, and somebody who had already left -- are the ones that
/// look identical to a silent failure from outside. A summary that could not tell those apart
/// would be one nobody could act on, so most of these cases are about telling them apart.
/// </remarks>
public sealed class NotificationDeliveryTests
{
    private static readonly CharacterId First = new(7001);
    private static readonly CharacterId Second = new(7002);
    private static readonly CharacterId Third = new(7003);

    /// <summary>Two people, two different frames, because the handler is asked about each.</summary>
    [Fact]
    public async Task Each_recipient_is_told_what_the_handler_made_for_them()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal(
            [
                (world.ConnectionOf(First), $"for-{First.Value}"),
                (world.ConnectionOf(Second), $"for-{Second.Value}"),
            ],
            world.Sender.Sent.Select(sent => (sent.ConnectionId, NotificationWorld.Label(sent.Message))));
        Assert.All(delivery.Attempts, attempt =>
            Assert.Equal(NotificationOutcome.Delivered, attempt.Outcome));
    }

    /// <summary>One failed delivery does not cost the people behind it theirs.</summary>
    /// <remarks>
    /// This is the property the whole shape exists for. Before there was one place doing this,
    /// every hand-written fan-out had to remember it, and each of them remembered differently.
    /// The delivery itself now happens in the outbound sender after the publishing job: everyone
    /// is registered, the connection that fails is closed (RQ-036), and the others still get theirs.
    /// </remarks>
    [Fact]
    public async Task A_recipient_whose_delivery_fails_does_not_stop_the_others()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Play(Third);
        world.Sender.FailFor(world.ConnectionOf(Second));

        var delivery = await world.PublishToAsync([First, Second, Third]);

        Assert.Equal([First, Second, Third], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal(
            [world.ConnectionOf(First), world.ConnectionOf(Third)],
            world.Sender.Sent.Select(sent => sent.ConnectionId));
        Assert.True(world.Sender.IsClosed(world.ConnectionOf(Second)));
        Assert.False(world.Sender.IsClosed(world.ConnectionOf(Third)));
    }

    /// <summary>
    /// Leaving between the audience resolving and your turn is not a failure, and is told apart
    /// from one.
    /// </summary>
    /// <remarks>
    /// The two go together in one case because the whole point is the difference: they end up in
    /// separate buckets of the same summary. The first recipient's delivery is what makes the
    /// second one leave, which is the ordinary shape of it -- somebody logs out while a fan-out is
    /// halfway through.
    /// </remarks>
    [Fact]
    public async Task Someone_who_left_is_told_apart_from_someone_whose_delivery_failed()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Play(Third);
        world.Compose = recipient => [new TownBellMessage(recipient.CharacterId == Third
            ? "unencodable for the third"
            : $"for-{recipient.CharacterId.Value}")];
        world.Composing = recipient =>
        {
            if (recipient.CharacterId == First) world.Leave(Second);
        };

        var delivery = await world.PublishToAsync([First, Second, Third]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Gone));
        Assert.Equal([Third], delivery.With(NotificationOutcome.Failed));
    }

    /// <summary>A recipient the handler had nothing for hears nothing, and that is not a fault.</summary>
    [Fact]
    public async Task A_recipient_the_handler_declines_hears_nothing_and_is_not_a_failure()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Compose = recipient => recipient.CharacterId == Second
            ? []
            : [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Declined));
        Assert.Empty(delivery.With(NotificationOutcome.Failed));
    }

    /// <summary>A handler that throws is that recipient's failure, not everyone's.</summary>
    [Fact]
    public async Task A_handler_that_throws_for_one_recipient_fails_only_that_one()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Compose = recipient => recipient.CharacterId == First
            ? throw new InvalidOperationException("nothing to say")
            : [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal([world.ConnectionOf(Second)], world.Sender.Registered.Select(entry => entry.Connection.ConnectionId));
        Assert.False(world.Sender.IsClosed(world.ConnectionOf(First)));
    }

    /// <summary>The caller calling it off is not a recipient failing.</summary>
    /// <remarks>
    /// Swallowing this would keep working through an audience after the answer stopped mattering,
    /// and would report the remainder as failures nobody caused.
    /// </remarks>
    [Fact]
    public async Task The_callers_own_cancellation_is_not_reported_as_a_failed_recipient()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        using var cancellation = new CancellationTokenSource();
        world.Composing = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.PublishToAsync([First, Second], cancellation.Token).AsTask());

        // The first recipient's composition is what cancelled it, and the second was never asked:
        // swallowing the cancellation would have carried on and called them failed. Nothing is
        // registered, because the first was called off before his entry was.
        Assert.Equal([First], world.Composed);
        Assert.Empty(world.Sender.Registered);
    }

    /// <summary>Coming back on a new connection is being gone, not being failed.</summary>
    /// <remarks>
    /// The third way a recipient stops being reachable, and the one that is not obvious: they are
    /// online the whole time, on a connection this publish has never heard of. Nothing is sent to
    /// it, deliberately -- see <see cref="NotificationOutcome.Gone"/> for the reasoning.
    ///
    /// It is written down here because the alternative reading is defensible enough to be chosen
    /// by accident later. What is wrong with calling it a failure is that nothing failed: a
    /// publish would report a delivery that went wrong when none was attempted, and would put a
    /// live player in the retry queue on the strength of it. The summary is meant to be readable,
    /// and a bucket that holds both "the socket died" and "they logged back in" is not.
    /// </remarks>
    [Fact]
    public async Task Someone_who_came_back_on_a_new_connection_is_gone_rather_than_failed()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Composing = recipient =>
        {
            if (recipient.CharacterId == First) world.Reconnect(Second);
        };

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([Second], delivery.With(NotificationOutcome.Gone));
        Assert.Empty(delivery.With(NotificationOutcome.Failed));
        Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
    }

    [Fact]
    public async Task An_exact_session_is_gone_when_its_generation_changes_on_the_same_connection()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        var first = world.SessionOf(First);
        var second = world.SessionOf(Second);
        world.Composing = recipient =>
        {
            if (recipient.CharacterId == First) world.ReplaceKeepingConnection(Second, listenerPort: 10012);
        };

        var delivery = await world.PublishToExactAsync([first, second]);

        Assert.Equal([Second], delivery.With(NotificationOutcome.Gone));
        Assert.Empty(delivery.With(NotificationOutcome.Failed));
        Assert.Equal([first.ConnectionId], world.Sender.Sent.Select(sent => sent.ConnectionId));
    }

    /// <summary>Even asking where somebody is can fail, and it fails for them alone.</summary>
    /// <remarks>
    /// The presence check runs inside the same guard as composing and sending, and this is the
    /// case that says so. It was outside it once: a lookup that threw would have taken down the
    /// whole publish, so one recipient's trouble would have become everybody's -- the one thing
    /// this method exists to prevent.
    /// <para>
    /// The refusal is armed by the first delivery rather than set up in advance, because the
    /// audience resolves through the same directory. Refusing from the start would fail the
    /// resolution instead, which is a different moment with a different answer, and would leave
    /// the moment under test untouched.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_presence_lookup_that_throws_costs_only_that_recipient()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Composing = recipient =>
        {
            if (recipient.CharacterId == First) world.Sessions.ThrowFor(Second);
        };

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Failed));
        Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
    }

    /// <summary>A delivery that failed says so somewhere a person will see it.</summary>
    /// <remarks>
    /// The summary carries the same fact, and in production nobody reads it: no publish site looks
    /// at what comes back. So the summary is for callers who want it, and this is for the evening
    /// somebody asks why a frame never arrived. What can fail in the publish is composing it
    /// (AC-011): the recipient's entries are dropped, his connection stays open, the publish goes on.
    /// </remarks>
    [Fact]
    public async Task A_failed_delivery_is_said_out_loud()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Compose = _ => throw new IOException("nothing to say");

        var delivery = await world.PublishToAsync([First]);

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains($"{First.Value}", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<IOException>(entry.Error);
        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));
        Assert.Empty(world.Sender.Registered);
        Assert.False(world.Sender.IsClosed(world.ConnectionOf(First)));
    }

    /// <summary>
    /// A message that cannot be encoded fails its recipient where it is registered, on the publishing
    /// job, after the messages before it; the others are registered as usual (TASK-23 ruling 1).
    /// </summary>
    [Fact]
    public async Task A_recipient_whose_message_cannot_be_encoded_fails_and_the_others_are_registered()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Compose = recipient => recipient.CharacterId == First
            ? [new TownBellMessage("first part"), new TownBellMessage("unencodable second part")]
            : [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal(
            [(world.ConnectionOf(First), "first part"), (world.ConnectionOf(Second), $"for-{Second.Value}")],
            world.Sender.Registered.SelectMany(entry => entry.Messages.Select(message =>
                (entry.Connection.ConnectionId, NotificationWorld.Label(message)))));
        var warning = Assert.Single(world.Log.Entries, value => value.Level == LogLevel.Warning);
        Assert.IsType<InvalidDataException>(warning.Error);
        Assert.False(world.Sender.IsClosed(world.ConnectionOf(First)));
    }

    /// <summary>A recipient whose composition failed is not asked again, now or by a later publish (VC-5).</summary>
    [Fact]
    public async Task A_recipient_whose_composition_failed_is_never_composed_again()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);
        world.Compose = recipient => recipient.CharacterId == First
            ? throw new InvalidOperationException("nothing to say")
            : [new TownBellMessage($"for-{recipient.CharacterId.Value}")];

        var first = await world.PublishToAsync([First, Second]);
        var second = await world.PublishToAsync([Second]);

        Assert.Equal([First], first.With(NotificationOutcome.Failed));
        Assert.Equal([Second], second.With(NotificationOutcome.Delivered));
        Assert.Equal([First, Second, Second], world.Composed);
        Assert.Equal(
            [world.ConnectionOf(Second), world.ConnectionOf(Second)],
            world.Sender.Registered.Select(entry => entry.Connection.ConnectionId));
    }

    /// <summary>RQ-061, the outbox half, for projections, and its counterexample (RQ-072): off the executor nothing is composed.</summary>
    [Fact]
    public async Task A_projection_off_the_executor_is_a_program_error_reported_as_evidence()
    {
        var world = new NotificationWorld();
        world.Play(First);
        var seen = Execution.ViolationCapture.Attach();

        var error = await Assert.ThrowsAsync<Dfo.Server.Abstractions.Execution.GameplayOffExecutorException>(
            () => world.ProjectHereAsync([First]).AsTask());

        Assert.Contains("GameplayOutbox.Register", error.Message, StringComparison.Ordinal);
        var violation = Assert.Single(seen);
        Assert.Equal(Dfo.Server.Abstractions.Execution.GameplayWriteAssertion.OffExecutorKind, violation.Kind);
        Assert.Empty(world.Composed);
        Assert.Empty(world.Sender.Registered);
    }

    /// <summary>The summary answers for every recipient the audience resolved to.</summary>
    [Fact]
    public async Task The_summary_accounts_for_every_recipient()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Play(Second);

        var delivery = await world.PublishToAsync([First, Second, Third]);

        Assert.Equal([First, Second], delivery.Attempts.Select(attempt => attempt.CharacterId));
    }

}
