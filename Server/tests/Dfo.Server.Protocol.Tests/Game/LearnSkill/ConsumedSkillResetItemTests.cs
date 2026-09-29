using System.Buffers.Binary;
using Dfo.Server.Data.Game;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.LearnSkill;
namespace Dfo.Server.Protocol.Tests.Game.LearnSkill;

public sealed class ConsumedSkillResetItemTests
{
    [Fact]
    public async Task Last_water_is_cleared_after_skill_ack_with_empty_item_sentinel()
    {
        var session=SelectedSessionIdentity.Create(new(1),new(2),new("water"),1,10011);
        var receipt=new OpeningInventoryReceipt(session,ImmutableArray<OpeningInventoryUpdate>.Empty,
            [new(BusinessInventorySpace.Consumable, 4)],ImmutableArray<OpeningRewardDisplay>.Empty,DateTimeOffset.UnixEpoch);
        var delivery=new OpeningCompletedHandler(new global::Dfo.Server.Protocol.Tests.Game.OneSession(session));
        var messages=await delivery.ComposeAsync(new NotificationRecipient(session.CharacterId,session.ConnectionId,session),
            new OpeningCompleted(receipt,[new LearnSkillResponse(null)]),default);
        Assert.IsType<LearnSkillResponse>(messages[0]);
        var body=Assert.IsType<InventoryDeltaMessage>(messages[1]).EncodeBody();
        Assert.Equal(104,body.Length);
        Assert.Equal((ushort)1,BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1)));
        Assert.Equal((short)69,BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(3)));
        Assert.Equal(-1,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(0,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(2,messages.Count);
    }
    [Fact]
    public void Empty_slot_sentinel_does_not_change_the_gold_item_id()
    {
        var body=Assert.IsType<InventoryDeltaMessage>(GameInventoryDelivery.CarriedGoldNotification(1234,DateTimeOffset.UnixEpoch)).EncodeBody();
        Assert.Equal(0,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(1234,BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
    }
}
