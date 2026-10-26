using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Execution;
public sealed class TimerRegistrationAcceptanceTests
{
    [Fact]
    public async Task Actual_outbox_acceptance_distinguishes_a_registered_entry_a_missing_target_and_an_empty_batch()
    {
        var outbound = new RecordedOutbound();
        outbound.Connections.AddOnFirstSight = false;
        var connection = new NetworkConnectionContext(new("timer-accepted"), NetworkEndpointKind.Game, 10011);
        outbound.Connections.Add(connection);
        var registration = Assert.IsAssignableFrom<ICommandMessageOutbox>(outbound.Network);
        await TestGameplay.RunAsync(async () =>
        {
            Assert.True(await registration.RegisterWithOutcomeAsync(connection.ConnectionId, new FatigueNotification(new(156, 1)), default));
            Assert.False(await registration.RegisterWithOutcomeAsync(new("timer-missing"), new FatigueNotification(new(156, 1)), default));
            Assert.False(await registration.RegisterBatchWithOutcomeAsync(connection, [], default));
            Assert.True(await registration.RegisterBatchWithOutcomeAsync(connection, [new FatigueNotification(new(156, 2))], default));
        });
        Assert.Equal(2, outbound.Messages.Count);
    }
    [Theory]
    [InlineData(SelectedSessionRefreshTopic.Fatigue)]
    [InlineData(SelectedSessionRefreshTopic.ReviveCoin)]
    public async Task A_current_session_without_a_registered_physical_target_does_not_advance_the_topic_watermark(SelectedSessionRefreshTopic topic)
    {
        using var shop = new ShopFixture();
        shop.Sender.Outbound.Connections.AddOnFirstSight = false;
        shop.Sender.Outbound.Connections.Remove(shop.Connection.ConnectionId);
        var source = topic == SelectedSessionRefreshTopic.Fatigue
            ? (SelectedSessionRefreshSource)ActivatorUtilities.CreateInstance<FatigueDailyRefreshService>(shop.Services)
            : ActivatorUtilities.CreateInstance<ReviveCoinQuotaDailyRefreshService>(shop.Services);
        var state = shop.Services.GetRequiredService<ISelectedSessionRefreshDomainService>();
        await source.RefreshAsync(default);
        Assert.Null(state.Previous(topic, shop.Session)); Assert.Empty(shop.Sender.Messages);
        shop.Sender.Outbound.Connections.Add(shop.Connection);
        await source.RefreshAsync(default);
        Assert.NotNull(state.Previous(topic, shop.Session)); Assert.NotEmpty(shop.Sender.Messages);
    }
}
