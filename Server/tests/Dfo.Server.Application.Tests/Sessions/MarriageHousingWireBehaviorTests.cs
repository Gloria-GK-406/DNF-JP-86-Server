using Dfo.Server.Abstractions.Content;

using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.MarriageHousing;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class MarriageHousingWireBehaviorTests
{
    [Theory]
    [InlineData(0x03FD, HousingAction.Storage)] [InlineData(0x03FE, HousingAction.Move)]
    [InlineData(0x03FF, HousingAction.Delete)] [InlineData(0x0400, HousingAction.Plant)]
    [InlineData(0x0401, HousingAction.Water)] [InlineData(0x0402, HousingAction.Harvest)]
    [InlineData(0x0403, HousingAction.Clear)] [InlineData(0x03F4, HousingAction.UpgradeRing)]
    public async Task Unmarried_housing_refusals_preserve_native_envelope_and_delete_has_no_status(int opcode, HousingAction action)
    {
        using var f = HousingNetworkSettlementTests.Fixture();
        var metadata = Metadata((ushort)opcode);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new HousingNetworkRequest(metadata, (ushort)opcode, new(action)), default);
        var frame = Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal((ushort)opcode, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal((byte)1, frame[15]);
        if (opcode == 0x03FF) Assert.Equal(16, frame.Length);
        else Assert.Equal(-4, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(16)));
        Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Housing_open_plant_water_ring_and_clear_keep_ack_before_absolute_slot_and_house_projection()
    {
        using var f = HousingNetworkSettlementTests.Fixture(); await HousingNetworkSettlementTests.Marry(f);
        await f.Seed(65, 10002766, 1); await f.Seed(66, 10002765, 1); await f.Seed(67, 10002611, 1);
        ushort sequence = 1;
        async Task<byte[][]> Send(ushort opcode, HousingRequest request)
        {
            f.Sender.Outbound.Clear();
            await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
                new HousingNetworkRequest(Metadata(opcode, sequence++), opcode, request), default);
            return f.Sender.Outbound.Frames.ToArray();
        }
        var open = await Send(0x03FD, new(HousingAction.Storage));
        Assert.Equal(new ushort[] { 0x03FD, 0x03D9 }, Types(open));
        var plant = await Send(0x0400, new(HousingAction.Plant, 65, 10002766));
        Assert.Equal(new ushort[] { 0x0400, 0x000E, 0x03DC }, Types(plant));
        Assert.Equal(0, f.Count(10002766));
        var water = await Send(0x0401, new(HousingAction.Water, 66, 10002765));
        Assert.Equal(new ushort[] { 0x0401, 0x000E, 0x03DC }, Types(water));
        Assert.Equal(0, f.Count(10002765));
        var ring = await Send(0x03F4, new(HousingAction.UpgradeRing, 67, Count: 1));
        Assert.Equal(new ushort[] { 0x03F4, 0x000E }, Types(ring));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(ring[0].AsSpan(16)));
        Assert.Equal((byte)1, ring[0][20]); Assert.Equal(0, f.Count(10002611));
        var clear = await Send(0x0403, new(HousingAction.Clear));
        Assert.Equal(new ushort[] { 0x0403, 0x03DC }, Types(clear));
        Assert.Null((await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(f.Session.AccountId, f.Session.CharacterId, default))!.Tree);
        Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Capsule_take_registers_each_absolute_item_before_native_ack_and_decrements_pending_once()
    {
        using var f = HousingNetworkSettlementTests.Fixture(); var relation = await HousingNetworkSettlementTests.Marry(f);
        f.Sql($"INSERT INTO marriage_capsules VALUES('{relation:N}',1,0,0,1)");
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new CapsuleRequest(Metadata(0x0406), MarriageCapsuleAction.Take), default);
        Assert.Equal(new ushort[] { 0x000E, 0x0406 }, Types(f.Sender.Outbound.Frames));
        Assert.Equal(1, f.Count(10002910)); Assert.Equal(0, f.Scalar("SELECT pending FROM marriage_capsules WHERE character_id=1"));
        Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Love_coupon_invalid_wire_slot_returns_only_native_minus_one_without_touching_inventory()
    {
        using var f = HousingNetworkSettlementTests.Fixture(); await HousingNetworkSettlementTests.Marry(f);
        await f.Seed(65, 10002901, 2);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new WeddingLovePointRequest(Metadata(0x03F5), uint.MaxValue), default);
        var frame = Assert.Single(f.Sender.Outbound.Frames);
        Assert.Equal(new ushort[] { 0x03F5 }, Types([frame]));
        Assert.Equal(new byte[] { 1, 255, 255, 255, 255 }, frame[15..]); Assert.Equal(2, f.Count(10002901));
        Assert.Empty(f.Sender.Outbound.Closes);
    }
    [Fact]
    public async Task Love_coupon_success_sends_actor_ack_then_slot_then_points_without_spouse_broadcast()
    {
        using var f = HousingNetworkSettlementTests.Fixture(s => s.AddSingleton<IMarriageLovePointRuleService, LoveRules>());
        await HousingNetworkSettlementTests.Marry(f); await f.Seed(65, 10002901, 2);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection),
            new WeddingLovePointRequest(Metadata(0x03F5), 65), default);
        Assert.Equal(new ushort[] { 0x03F5, 0x000E, 0x03D3 }, Types(f.Sender.Outbound.Frames));
        Assert.Equal(1, f.Count(10002901)); Assert.Equal(101, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1"));
        Assert.All(f.Sender.Outbound.GameExplicits, delivery => Assert.Empty(delivery.BestEffortTargets));
        Assert.Empty(f.Sender.Outbound.Closes);
    }
    private sealed class LoveRules : IMarriageLovePointRuleService
    {
        public ValueTask<MarriageLovePointItemRule?> RuleForAsync(int item, CancellationToken ct) =>
            ValueTask.FromResult<MarriageLovePointItemRule?>(item == 10002901
                ? new(new(true, 0, 0, 1, ItemJobRestriction.Any, ItemPlaceRestriction.Anywhere, Action: "[love point coupon]"), 1, 999) : null);
    }
    private static ushort[] Types(IEnumerable<byte[]> frames) => frames.Select(x => BinaryPrimitives.ReadUInt16LittleEndian(x.AsSpan(1))).ToArray();
    private static GameMessageMetadata Metadata(ushort opcode, ushort sequence = 1) =>
        (new GameMessageMetadata(1, opcode, 0, sequence) { InboundOrdinal = sequence, SequenceIsFresh = true }).WithRawBody([]);
}
