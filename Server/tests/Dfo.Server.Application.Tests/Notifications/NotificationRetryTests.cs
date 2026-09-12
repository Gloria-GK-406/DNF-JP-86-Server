using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// A second go for the recipients a publish could not reach, carrying nothing from the first.
/// </summary>
/// <remarks>
/// The interesting cases here are all the same case from different sides: a retry asks both
/// questions again -- where are they, and what should they be told -- rather than keeping the
/// answers it had. Keeping them is the natural implementation and the wrong one, because a queued
/// frame that succeeds on its second attempt lands on top of whatever was published in between.
/// </remarks>
public sealed class NotificationRetryTests
{
    private static readonly CharacterId First = new(8001);
    private static readonly CharacterId Second = new(8002);

    /// <summary>The retry sends what is true now, not what was true when it failed.</summary>
    /// <remarks>
    /// This is the one that a cache would break, and it would break it silently: the frame would
    /// go out, delivery would report success, and the value on the client would be the older one.
    /// </remarks>
    [Fact]
    public async Task A_retry_asks_the_handler_again_rather_than_resending_what_it_had()
    {
        var world = new NotificationWorld(retryLimit: 1);
        world.Play(First);
        world.Compose = recipient => [new TownBellMessage("first answer")];
        world.Sender.FailFor(world.ConnectionOf(First));

        var delivery = await world.PublishToAsync([First]);
        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));

        world.Sender.StopFailing();
        world.Compose = recipient => [new TownBellMessage("second answer")];
        await world.DrainAsync();

        Assert.Equal(
            ["second answer"],
            world.Sender.Sent.Select(sent => NotificationWorld.Label(sent.Message)));
    }

    /// <summary>The retry goes where they are now, not where they were.</summary>
    /// <remarks>
    /// Their connection is not in the queue, so this needs no forgetting: the entry names a
    /// character, and resolving one is what turns a character into somewhere to send.
    ///
    /// A reconnection is delivered to here, while the same reconnection mid-publish is
    /// <see cref="NotificationOutcome.Gone"/>. Not a contradiction -- see
    /// <c>NotificationBus.RetryAsync</c>: mid-publish, the recipient this publish resolved to has
    /// been replaced while the publish was running, and that resolution is what the publish is
    /// about. Here there is no earlier resolution to be replaced: the question starts at who this
    /// character is and where they are.
    /// </remarks>
    [Fact]
    public async Task A_retry_goes_to_the_connection_they_are_on_now()
    {
        var world = new NotificationWorld(retryLimit: 1);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);
        world.Reconnect(First);
        world.Sender.StopFailing();
        await world.DrainAsync();

        Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
    }

    [Fact]
    public async Task An_exact_session_retry_does_not_follow_a_replacement_session()
    {
        var world = new NotificationWorld(retryLimit: 1);
        world.Play(First);
        var exact = world.SessionOf(First);
        world.Sender.FailFor(exact.ConnectionId);

        await world.PublishToExactAsync([exact]);
        world.Reconnect(First);
        world.Sender.StopFailing();
        await world.DrainAsync();

        Assert.Empty(world.Sender.Sent);
        Assert.Single(world.Sender.Attempted);
        Assert.Equal(0, world.Waiting);
    }

    /// <summary>Failing again puts them back, until the allowance the notification declared runs out.</summary>
    [Fact]
    public async Task A_recipient_who_keeps_failing_is_dropped_at_the_declared_limit()
    {
        var world = new NotificationWorld(retryLimit: 2);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);
        Assert.Equal(1, world.Waiting);

        await world.DrainAsync();
        Assert.Equal(1, world.Waiting);

        // The second pass spends the last go, so nothing goes back: the queue never holds a
        // recipient nobody will try again.
        await world.DrainAsync();
        Assert.Equal(0, world.Waiting);

        // One publish and the two retries it declared.
        Assert.Equal(3, world.Sender.Attempted.Count);

        // And a further pass finds nothing rather than trying a fourth time.
        await world.DrainAsync();
        Assert.Equal(3, world.Sender.Attempted.Count);
    }

    /// <summary>A notification that declares no second go queues nobody.</summary>
    /// <remarks>
    /// The degenerate end of the rule the case above exercises: a recipient goes back only while
    /// they have a go left, so a notification that declares none never puts anybody in. Nothing
    /// ever waits in there for a pass that will only discard it.
    /// </remarks>
    [Fact]
    public async Task A_notification_that_declares_no_retries_queues_nobody()
    {
        var world = new NotificationWorld(retryLimit: 0);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        var delivery = await world.PublishToAsync([First]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));
        Assert.Equal(0, world.Waiting);
    }

    /// <summary>Somebody who left before their retry is let go, not tried again.</summary>
    [Fact]
    public async Task Somebody_who_left_before_the_retry_is_let_go()
    {
        var world = new NotificationWorld(retryLimit: 3);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);
        world.Leave(First);
        await world.DrainAsync();

        Assert.Equal(0, world.Waiting);
        Assert.Single(world.Sender.Attempted);
    }

    /// <summary>Giving up on somebody is the last thing said about them, and it is said.</summary>
    /// <remarks>
    /// This is the moment a notification is definitively lost. Everything before it is "not yet";
    /// after it, nothing will ever mention this recipient again.
    /// </remarks>
    [Fact]
    public async Task Giving_up_on_a_recipient_is_said_out_loud()
    {
        var world = new NotificationWorld(retryLimit: 1);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);
        await world.DrainAsync();

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains("gave up", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains($"{First.Value}", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>A pass called off keeps what it has not tried.</summary>
    /// <remarks>
    /// Cancellation is asked about before a recipient is taken out of the queue, not after. Taking
    /// first and then noticing would drop that recipient's remaining goes on the floor -- and it
    /// would look like nothing at all, because a queue is exactly as quiet one entry shorter.
    /// </remarks>
    [Fact]
    public async Task A_pass_called_off_leaves_what_it_had_not_reached()
    {
        var world = new NotificationWorld(retryLimit: 3);
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));
        await world.PublishToAsync([First]);
        Assert.Equal(1, world.Waiting);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.DrainAsync(cancelled.Token).AsTask());
        Assert.Equal(1, world.Waiting);
        Assert.Single(world.Sender.Attempted);
    }

    /// <summary>A pass works through what was waiting, and does not chase what it adds.</summary>
    /// <remarks>
    /// Without this the allowance would be spent in one instant: a recipient who fails goes to the
    /// back, and a pass that kept draining would meet them again immediately and burn every retry
    /// they had before the connection could possibly have recovered.
    /// </remarks>
    [Fact]
    public async Task A_pass_works_through_what_was_waiting_and_no_further()
    {
        var world = new NotificationWorld(retryLimit: 5);
        world.Play(First);
        world.Play(Second);
        world.Sender.FailFor(world.ConnectionOf(First));
        world.Sender.FailFor(world.ConnectionOf(Second));

        await world.PublishToAsync([First, Second]);
        Assert.Equal(2, world.Sender.Attempted.Count);

        await world.DrainAsync();

        Assert.Equal(4, world.Sender.Attempted.Count);
        Assert.Equal(2, world.Waiting);
    }
}
