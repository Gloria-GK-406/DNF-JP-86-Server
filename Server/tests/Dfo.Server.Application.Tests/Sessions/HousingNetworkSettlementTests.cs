using System.Buffers.Binary;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Marriage;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Protocol.Game.BuyItem;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed partial class HousingNetworkSettlementTests
{
    private static async Task<Guid> Marry(ShopFixture f)
    {
        return await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'house-peer'); INSERT INTO characters(character_id,account_id,name,job,level,slot_index) VALUES(3,2,'peer',0,50,0)");
            var peer = SelectedSessionIdentity.Create(new(2), new(3), new("offline-peer"), 1, 10011);
            var now = f.Clock.GetUtcNow(); var p = new MarriageProposal(Guid.NewGuid(), f.Session, peer, MarriageProposalState.Pending, 1, now, now.AddMinutes(1));
            var c = new MarriageCeremony(Guid.NewGuid(), p.Id, f.Session, peer, 0, 0, MarriageCeremonyState.Pending, 1, now, now.AddMinutes(1));
            await using (var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin())
            {
                var proposals = f.Services.GetRequiredService<IMarriageProposalDomainStore>(); await proposals.CreateAsync(p, default); await proposals.TransitionAsync(p, MarriageProposalState.Engaged, 0, default);
                await f.Services.GetRequiredService<IMarriageCeremonyDomainStore>().CreateAsync(c, default);
                var weddings = f.Services.GetRequiredService<IMarriageWeddingDomainStore>(); await weddings.CreateAsync(new(c, MarriageWeddingPhase.Preparing, 0), default); await weddings.FinishAsync(c.Id, MarriageWeddingPhase.Completed, now, default);
                await tx.CommitAsync(default);
            }
            f.Sql($"INSERT INTO marriage_relations(id,stage,revision,stage_since,ceremony_id) VALUES('{p.Id:N}',4,1,{now.ToUnixTimeMilliseconds()},'{c.Id:N}'); UPDATE character_stats SET carried_gold=1000000 WHERE character_id=1");
            await using (var tx = f.Services.GetRequiredService<DomainTransactionScope>().Begin())
            { await f.Services.GetRequiredService<IMarriageHousingDomainService>().InitializeAsync(f.Session.AccountId, f.Session.CharacterId, default); await tx.CommitAsync(default); }
            f.Sql("UPDATE marriage_personal_points SET points=100 WHERE character_id=1");
            var room = (await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(f.Session.AccountId, f.Session.CharacterId, default))!;
            f.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(f.Session.CharacterId, f.Session.TownAreaLease(), new(1, 101, 0, room.Instance), new(101, 0, 900, 250, 5), true);
            return p.Id;
        });
    }
    private static ShopFixture Fixture() => new(s =>
    {
        s.AddSingleton<IMarriageHousingContentSource, MarriageHousingTests.Content>();
        s.AddSingleton<INpcShopDefinitionSource, MarriageHousingTests.Content>();
        s.AddSingleton<IItemPlacementDefinitionSource, MarriageHousingTests.Content>();
        s.AddSingleton<IMarriageCapsuleContentSource, MarriageHousingTests.CapsuleContent>();
    });
    [Fact]
    public async Task Full_bag_purchase_rolls_back_gold_and_points_returns_native_failure_and_allows_the_next_request()
    {
        using var f = Fixture(); await Marry(f);
        foreach (var slot in Enumerable.Range(3, 6).Concat(Enumerable.Range(65, 55))) await f.Seed(slot, 1000, 1000);
        var counter = new GameInboundFrameCounter();
        var (ordinal, fresh) = counter.Observe(1); var body = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(body, 10002611); BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(8), 1207); BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(12), 20011);
        var message = new HousingBuyItemRequest((new GameMessageMetadata(1, 0x0015, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(body), 10002611, 1, 1207, 20011);
        await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), message, default);
        Assert.Equal(1000000, f.Gold); Assert.Equal(100, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1")); Assert.Equal(0, f.Count(10002611));
        var refusal = Assert.Single(f.Sender.Messages.OfType<BuyItemResponse>()); Assert.Null(refusal.Receipt); Assert.Equal(4, refusal.ErrorCode);
        // A business refusal must not fence the session as an uncertain durable settlement.
        var (_, nextFresh) = counter.Observe(2); Assert.True(nextFresh);
        Assert.NotNull(await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(f.Session.AccountId, f.Session.CharacterId, default));
        await f.Services.GetRequiredService<Dfo.Server.Application.Commands.ICommandBus>().SendAsync(new Dfo.Server.Protocol.Game.MarriageHousing.InitializeHousingCommand(f.Session), default);
    }
    [Fact]
    public async Task Lost_furniture_purchase_ack_replays_current_storage_without_a_second_charge()
    {
        using var f = Fixture(); await Marry(f);
        var counter = new GameInboundFrameCounter();
        HousingBuyItemRequest Request()
        {
            var (ordinal, fresh) = counter.Observe(1); var b = new byte[16]; BinaryPrimitives.WriteInt32LittleEndian(b, 10002688); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 1); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), 1206); BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), 20011);
            return new((new GameMessageMetadata(1, 0x0015, 0, 1) { InboundOrdinal = ordinal, SequenceIsFresh = fresh }).WithRawBody(b), 10002688, 1, 1206, 20011);
        }
        var dispatch = f.Services.GetRequiredService<INetworkMessageDispatcher>(); f.Sender.FailBatch = true;
        await dispatch.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(98, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1")); Assert.Equal(1, f.Scalar("SELECT SUM(count) FROM marriage_housing_storage"));
        f.Sender.FailBatch = false; f.Sender.Batches.Clear(); f.Sender.Messages.Clear();
        await dispatch.DispatchAsync(new(f.Connection), Request(), default);
        Assert.Equal(98, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=1")); Assert.Equal(1, f.Scalar("SELECT SUM(count) FROM marriage_housing_storage"));
        Assert.Contains(f.Sender.Batches.SelectMany(x => x.Frames), b => b[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(1)) == 0x03D9);
        Assert.DoesNotContain(f.Sender.Messages, m => m is BuyItemResponse);
    }
}
