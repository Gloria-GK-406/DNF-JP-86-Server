using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Notifications;

/// <summary>
/// Who counts as reachable: a character in the world on the connection it entered on. A selection
/// still under way, or one whose connection has been revoked, is nobody to tell.
/// </summary>
public sealed class NotificationInWorldTests
{
    private static readonly CharacterId First = new(7101);
    private static readonly CharacterId Second = new(7102);

    [Fact]
    public async Task A_character_still_entering_is_not_in_a_character_audience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new NotificationWorld();
            world.Play(First);
            world.SelectOnly(Second);

            var delivery = await world.PublishToAsync([First, Second]);

            Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
            Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
        });
    }

    [Fact]
    public async Task A_revoked_connection_is_not_in_a_character_audience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new NotificationWorld();
            world.Play(First);
            world.Play(Second);
            world.Revoke(Second);

            var delivery = await world.PublishToAsync([First, Second]);

            Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
            Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
        });
    }

    [Fact]
    public async Task A_revoked_exact_session_is_not_in_an_exact_audience()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new NotificationWorld();
            world.Play(First);
            world.Play(Second);
            world.Revoke(Second);

            var delivery = await world.PublishToExactAsync([world.SessionOf(First), world.SessionOf(Second)]);

            Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
            Assert.Equal([world.ConnectionOf(First)], world.Sender.Sent.Select(sent => sent.ConnectionId));
        });
    }

    [Fact]
    public async Task A_connection_revoked_after_resolution_is_gone_at_delivery()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new NotificationWorld();
            world.Play(First);
            world.Play(Second);
            world.Sender.OnSend = _ => world.Revoke(Second);

            var delivery = await world.PublishToExactAsync([world.SessionOf(First), world.SessionOf(Second)]);

            Assert.Equal([First], delivery.With(NotificationOutcome.Delivered));
            Assert.Equal([Second], delivery.With(NotificationOutcome.Gone));
        });
    }

    [Fact]
    public async Task A_character_still_on_its_resolved_connection_is_told()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var world = new NotificationWorld();
            world.Play(First);
            world.Play(Second);

            var delivery = await world.PublishToAsync([First, Second]);

            Assert.Equal([First, Second], delivery.With(NotificationOutcome.Delivered));
        });
    }
}
