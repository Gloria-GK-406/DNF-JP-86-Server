using System.Diagnostics;
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

        Assert.Equal([First, Third], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Failed));
        Assert.Equal(
            [world.ConnectionOf(First), world.ConnectionOf(Third)],
            world.Sender.Sent.Select(sent => sent.ConnectionId));
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
        world.Sender.FailFor(world.ConnectionOf(Third));
        world.Sender.OnSend = _ => world.Leave(Second);

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
    }

    /// <summary>A recipient who takes too long is a failure, and the rest still go out.</summary>
    /// <remarks>
    /// Delivery is sequential, so a recipient who never answers would otherwise hold the audience.
    /// The deadline is the same number for every notification, deliberately: the worst case should
    /// not depend on which notification happened to be in flight. What it bounds is what this code
    /// enforces on the token it hands the transport -- see NotificationDeliveryOptions for where
    /// that stops.
    /// </remarks>
    [Fact]
    public async Task A_recipient_who_runs_out_of_time_is_a_failure()
    {
        var world = new NotificationWorld(timeout: TimeSpan.FromMilliseconds(50));
        world.Play(First);
        world.Play(Second);
        world.Sender.StallFor(world.ConnectionOf(First));

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Failed));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Delivered));
    }

    /// <summary>The deadline a stalled recipient runs into is the configured one.</summary>
    /// <remarks>
    /// The case above passes under any finite deadline, so on its own it does not say the
    /// configured value is the one being applied -- a hard-coded number would satisfy it just as
    /// well. This one stalls everybody and reads the clock.
    /// <para>
    /// The upper bound is the half that does the work, and what it decides is one thing: that the
    /// production default is not what ran. Two recipients at the default two seconds is four, so a
    /// bound of five would have proved nothing. What it does not decide is every hard-coded value
    /// -- one in the neighbourhood of a hundred milliseconds a recipient would sit inside this
    /// window too, and no clock reading here would separate it from the real thing.
    /// </para>
    /// <para>
    /// Headroom, measured rather than guessed, and widened once after it was measured wrong. Two
    /// fifty-millisecond deadlines first came in at 197-315 ms across four parallel full-suite
    /// runs, and a one-second bound looked like three times the worst of them. It was not: a later
    /// run took 1045 ms and turned this red at 4.5% over. So the bound is two and a half seconds
    /// now, which still sits below what the production default would cost here -- two recipients
    /// at two seconds each is four -- and that is the only thing this half has to decide.
    /// </para>
    /// <para>
    /// What widening it cost, said plainly: a hard-coded value anywhere in roughly 50 ms to 1.25 s
    /// per recipient now passes here, where the window used to stop at 500 ms. And the corridor is
    /// narrow -- the measured ceiling on this machine is about a second, the bound is 2.5, and the
    /// value it has to exclude lands at 4. There is no room for a third widening. If this goes red
    /// again at a plausible number, the answer is not a bigger bound; it is to stop reading the
    /// wall clock and assert the deadline some other way.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_deadline_a_stalled_recipient_runs_into_is_the_configured_one()
    {
        var world = new NotificationWorld(timeout: TimeSpan.FromMilliseconds(50));
        world.Play(First);
        world.Play(Second);
        world.Sender.StallFor(world.ConnectionOf(First));
        world.Sender.StallFor(world.ConnectionOf(Second));

        var started = Stopwatch.GetTimestamp();
        var delivery = await world.PublishToAsync([First, Second]);
        var elapsed = Stopwatch.GetElapsedTime(started);

        Assert.Equal([First, Second], delivery.With(NotificationOutcome.Failed));
        Assert.True(
            elapsed >= TimeSpan.FromMilliseconds(100),
            $"Two 50ms deadlines took {elapsed.TotalMilliseconds:F0}ms, so they were not applied.");
        Assert.True(
            elapsed < TimeSpan.FromSeconds(2.5),
            $"Two 50ms deadlines took {elapsed.TotalMilliseconds:F0}ms, so some other value was "
                + "applied -- the production default of two seconds would land near four.");
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
        world.Sender.OnSend = _ => cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => world.PublishToAsync([First, Second], cancellation.Token).AsTask());

        // The first recipient's send is what cancelled it, and no send was started for the second:
        // swallowing the cancellation would have carried on and called them failed. Nothing
        // reaches Sent, because that send was itself cancelled before it recorded anything.
        Assert.Equal([world.ConnectionOf(First)], world.Sender.Attempted);
        Assert.Empty(world.Sender.Sent);
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
        world.Sender.OnSend = _ => world.Reconnect(Second);

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
        world.Sender.OnSend = _ =>
            world.ReplaceKeepingConnection(Second, listenerPort: 10012);

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
        world.Sender.OnSend = _ => world.Sessions.ThrowFor(Second);

        var delivery = await world.PublishToAsync([First, Second]);

        Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
        Assert.Equal([Second], delivery.With(NotificationOutcome.Failed));
        Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
    }

    /// <summary>A delivery that failed says so somewhere a person will see it.</summary>
    /// <remarks>
    /// The summary carries the same fact, and in production nobody reads it: no publish site looks
    /// at what comes back. So the summary is for callers who want it, and this is for the evening
    /// somebody asks why a frame never arrived.
    /// </remarks>
    [Fact]
    public async Task A_failed_delivery_is_said_out_loud()
    {
        var world = new NotificationWorld();
        world.Play(First);
        world.Sender.FailFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains($"{First.Value}", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.IsType<IOException>(entry.Error);
    }

    /// <summary>Running out of time is told apart from failing, in what is said.</summary>
    [Fact]
    public async Task A_recipient_who_ran_out_of_time_is_named_as_that_and_not_as_an_error()
    {
        var world = new NotificationWorld(timeout: TimeSpan.FromMilliseconds(50));
        world.Play(First);
        world.Sender.StallFor(world.ConnectionOf(First));

        await world.PublishToAsync([First]);

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains("ran out of", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Error);
    }

    /// <summary>A real failure after the deadline is still a failure, not a timeout.</summary>
    /// <remarks>
    /// Matching on the clock alone would file this as running out of time and lose the exception
    /// with it: the deadline has genuinely passed by then. What says which one happened is the
    /// kind of thing that was thrown, and a handler that ignores the token throws something real.
    /// </remarks>
    [Fact]
    public async Task Something_thrown_after_the_deadline_is_still_reported_as_what_it_was()
    {
        var world = new NotificationWorld(timeout: TimeSpan.FromMilliseconds(30));
        world.Play(First);
        world.Compose = recipient =>
        {
            Thread.Sleep(80);
            throw new InvalidOperationException("nothing to say after all");
        };

        await world.PublishToAsync([First]);

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains($"{First.Value}", StringComparison.Ordinal));
        Assert.IsType<InvalidOperationException>(entry.Error);
    }

    /// <summary>Being cancelled is not the same as running out of time.</summary>
    /// <remarks>
    /// The other half of the condition that tells the two apart. A handler can throw a
    /// cancellation of its own while the deadline still has room -- and that is a failure with a
    /// reason, not a recipient who was too slow. Matching on the exception alone would file it as
    /// the latter and say the deadline elapsed when it had not.
    /// </remarks>
    [Fact]
    public async Task A_cancellation_raised_before_the_deadline_is_a_failure_with_a_reason()
    {
        var world = new NotificationWorld(timeout: TimeSpan.FromSeconds(30));
        world.Play(First);
        world.Compose = recipient => throw new OperationCanceledException("the handler gave up");

        await world.PublishToAsync([First]);

        var entry = Assert.Single(
            world.Log.Entries,
            value => value.Message.Contains($"{First.Value}", StringComparison.Ordinal));
        Assert.IsType<OperationCanceledException>(entry.Error);
        Assert.DoesNotContain("ran out of", entry.Message, StringComparison.Ordinal);
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
