using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.Inventory;
public sealed class PremiumTimerOrderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_contract_expiries_keep_crystal_zero_then_growth_zero_then_the_state_and_do_not_repeat(bool expiredBeforeFirstTick)
    {
        using var shop = new ShopFixture();
        var end = shop.Clock.GetUtcNow().AddMinutes(expiredBeforeFirstTick ? -1 : 1).ToUnixTimeSeconds();
        shop.Sql($"INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,97,{end}),(1,84,{end})");
        var refresh = ActivatorUtilities.CreateInstance<Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshService>(shop.Services, new Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshTopics([Dfo.Server.Abstractions.Sessions.SelectedSessionRefreshTopic.Premium]));
        if (!expiredBeforeFirstTick)
        { await refresh.RefreshAsync(default); shop.Sender.Messages.Clear(); shop.Clock.Advance(TimeSpan.FromMinutes(1)); }
        await refresh.RefreshAsync(default); await refresh.RefreshAsync(default);
        Assert.Equal(new[] { "PremiumGrantedMessage", "PremiumGrantedMessage", "PremiumServiceStateMessage" }, shop.Sender.Messages.Select(message => message.GetType().Name));
        var notices = shop.Sender.Messages.Take(2).Select(message =>
        {
            var registration = Assert.Single(shop.Services.GetServices<ServerMessageEncoderRegistration>(), item => item.MessageType == message.GetType());
            return Assert.Single(((IServerMessageEncoder)shop.Services.GetRequiredService(registration.EncoderType)).Encode(message));
        }).ToArray();
        Assert.Equal(new byte[] { 97, 84 }, notices.Select(frame => frame[17]));
        Assert.All(notices, frame => { Assert.Equal(0, frame[0]); Assert.Equal(0x0042, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1))); Assert.Equal(0, BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(18))); });
    }
}
