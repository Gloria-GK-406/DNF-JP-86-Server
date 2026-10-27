using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Effects;
using Dfo.Server.Application.Commands;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Rental;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class LuckyStarRentalWireTests
{
    private static GameMessageMetadata Metadata(ushort type, byte[] body, long ordinal=1, bool fresh=true) =>
        new GameMessageMetadata(1,type,0,1) {InboundOrdinal=ordinal,SequenceIsFresh=fresh}.WithRawBody(body);

    [Dfo.Server.Testing.PvfFact]
    public async Task Full_bag_normal_rollback_sends_result_two_and_same_frame_can_rent_once_after_space_is_freed()
    {
        var observation = new RollbackObservation();
        using var f = new CeraShopFixture(useArchive: true, configure: services =>
        {
            services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService,
                Dfo.Server.Application.Characters.CharacterSkillPointDomainService>();
            services.AddSingleton(observation);
            var descriptor=services.Last(x=>x.ServiceType==typeof(IEquipmentRentalDomainService));
            services.AddSingleton<IEquipmentRentalDomainService>(provider=>new ObserveRental(
                (IEquipmentRentalDomainService)(descriptor.ImplementationInstance??descriptor.ImplementationFactory?.Invoke(provider)??ActivatorUtilities.CreateInstance(provider,descriptor.ImplementationType!)),
                provider.GetRequiredService<IDomainTransactionAccessor>(),provider.GetRequiredService<IAccountLuckyStarDomainService>(),observation));
        });
        f.Host.Sql("UPDATE accounts SET lucky_star=100; UPDATE characters SET job=0; INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1);");
        for (var slot = 9; slot <= 40; slot++)
            await f.Host.SeedCore(slot, StackableItemCore.Create(1, 401000037, 123));
        var before = DatabaseRows(f.Host);
        var body = new byte[22];
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), 401000037);
        var registration = f.Host.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x03CF);
        var decoder = (IGameMessageDecoder)f.Host.Services.GetRequiredService(registration.DecoderType);
        var counter = new Dfo.Server.Networking.Runtime.GameInboundFrameCounter();
        IClientNetworkMessage Request()
        {
            var (ordinal, fresh) = counter.Observe(1);
            return decoder.Decode(Metadata(0x03CF, body, ordinal, fresh), body)!;
        }
        var dispatch = f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        await dispatch.DispatchAsync(new(f.Host.Connection), Request(), default);
        Assert.Equal(97, observation.StagedStars);
        Assert.True(observation.Transaction!.IsRollbackOnly);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, observation.Transaction.Completion);
        Assert.Equal(before, DatabaseRows(f.Host));
        var response = Assert.Single(f.Host.Sender.Messages.OfType<RentEquipmentResponse>());
        Assert.Equal(new RentEquipmentResponse(2), response);
        Assert.Equal(new byte[] { 1, 2, 0, 0, 0 }, Assert.Single(f.Host.Sender.Outbound.Frames).AsSpan(15).ToArray());
        Assert.Empty(f.Host.Sender.Batches);
        Assert.Empty(f.Host.Sender.Outbound.Closes);
        var entry = RentalEntry(f.Host);
        Assert.Equal(EffectOperationState.ConfirmedRollback, entry.State);
        Assert.Null(entry.Projection);
        Assert.Equal(1, entry.Ordinal);
        var safety = f.Host.Services.GetRequiredService<SelectedSessionSafety>();
        Assert.False(safety.IsBlocked(f.Host.Connection));
        Assert.False(f.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().StandingOf(f.Host.Session)!.Fenced);
        Assert.NotNull(f.Host.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>().FindByConnection(f.Host.Connection.ConnectionId));
        f.Host.Sql("DELETE FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND slot_index=9 AND list_type=0;");
        await dispatch.DispatchAsync(new(f.Host.Connection), Request(), default);
        Assert.Same(entry, RentalEntry(f.Host));
        Assert.Equal(1, entry.Ordinal);
        Assert.Equal(EffectOperationState.Committed, entry.State);
        var rental = f.Host.Services.GetRequiredService<IEquipmentRentalDomainService>();
        var panel = await rental.PanelAsync(new(1), new(1), default);
        Assert.Equal(97, panel.LuckyStar);
        var item = Assert.Single(panel.Items);
        Assert.Equal(32, f.Host.Count(401000037));
        await dispatch.DispatchAsync(new(f.Host.Connection), Request(), default);
        Assert.Equal(item, Assert.Single((await rental.PanelAsync(new(1), new(1), default)).Items));
        Assert.Equal(97, (await rental.PanelAsync(new(1), new(1), default)).LuckyStar);
        Assert.Equal(2, f.Host.Sender.Messages.OfType<RentEquipmentResponse>().Count());
        Assert.Empty(f.Host.Sender.Outbound.Closes);
        var beforeInvalidQuote = DatabaseRows(f.Host);
        var invalidBody = new byte[22];
        var (invalidOrdinal, invalidFresh) = counter.Observe(2);
        var invalidMetadata = new GameMessageMetadata(1, 0x03CF, 0, 2)
            { InboundOrdinal = invalidOrdinal, SequenceIsFresh = invalidFresh }.WithRawBody(invalidBody);
        await dispatch.DispatchAsync(new(f.Host.Connection), decoder.Decode(invalidMetadata, invalidBody)!, default);
        Assert.Equal(beforeInvalidQuote, DatabaseRows(f.Host));
        Assert.Equal((byte)1, f.Host.Sender.Messages.OfType<RentEquipmentResponse>().Last().Error);
        Assert.Equal(EffectOperationState.Committed, RentalEntry(f.Host, 2).State);
        Assert.NotNull(RentalEntry(f.Host, 2).Projection);
        Assert.Empty(f.Host.Sender.Outbound.Closes);
    }

    private sealed class RollbackObservation
    {
        public DomainTransaction? Transaction;
        public int StagedStars;
    }
    private sealed class ObserveRental(IEquipmentRentalDomainService actual,IDomainTransactionAccessor transactions,
        IAccountLuckyStarDomainService stars,RollbackObservation observation) : IEquipmentRentalDomainService
    {
        public ValueTask<EquipmentRentalPanel> PanelAsync(Dfo.Server.Data.Accounts.AccountId account,Dfo.Server.Data.Characters.CharacterId character,CancellationToken ct) => actual.PanelAsync(account,character,ct);
        public async ValueTask<EquipmentRentalReceipt> RentAsync(Dfo.Server.Data.Accounts.AccountId account,Dfo.Server.Data.Characters.CharacterId character,int item,CancellationToken ct)
        {
            var result=await actual.RentAsync(account,character,item,ct);
            observation.Transaction=(DomainTransaction)transactions.Current!;
            observation.StagedStars=await stars.BalanceAsync(account,ct);
            return result;
        }
    }

    private static EffectOperationEntry RentalEntry(ShopFixture f, ushort sequence = 1)
    {
        // Observe identity without admitting another request or mutating the ledger under test.
        var ledger = f.Services.GetRequiredService<EffectOperationLedger>();
        var ledgers = typeof(EffectOperationLedger).GetField("ledgers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(ledger)!;
        object?[] arguments = [f.Connection, null];
        Assert.True((bool)ledgers.GetType().GetMethod("TryGetValue")!.Invoke(ledgers, arguments)!);
        var entries = (System.Collections.IDictionary)arguments[1]!.GetType().GetProperty("Entries")!.GetValue(arguments[1])!;
        return Assert.Single(entries.Values.Cast<EffectOperationEntry>(), x => x.Sequence == sequence);
    }

    internal static string[] DatabaseRows(ShopFixture f)
    {
        var connectionString = (string)typeof(ShopFixture).GetProperty("ConnectionString", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f)!;
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();
        using var tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_schema WHERE type='table' ORDER BY name";
        var names = new List<string>();
        using (var reader = tables.ExecuteReader()) while (reader.Read()) names.Add(reader.GetString(0));
        var rows = new List<string>();
        foreach (var name in names)
        {
            using var command = connection.CreateCommand();
            var quoted = "\"" + name.Replace("\"", "\"\"") + "\"";
            command.CommandText = "SELECT " + (name == "item_slots" ? "rowid," : "") + "* FROM " + quoted;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(name + ":" + System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i =>
                    reader.IsDBNull(i) ? null : reader.GetValue(i)).ToArray()));
        }
        return rows.Order(StringComparer.Ordinal).ToArray();
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Purchase_then_rental_retry_preserves_gold_stars_uid_and_absolute_deadline()
    {
        using var f=new CeraShopFixture(useArchive:true,configure:services=>services.AddSingleton<Dfo.Server.Abstractions.Characters.ICharacterSkillPointDomainService,Dfo.Server.Application.Characters.CharacterSkillPointDomainService>());
        f.Host.Sql("UPDATE characters SET job=0; UPDATE character_stats SET carried_gold=5000000; INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0); INSERT INTO character_pvp(character_id) VALUES(1); INSERT INTO character_settings(character_id) VALUES(1); INSERT INTO character_expert_job(character_id) VALUES(1);");
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var body=new byte[22];Array.Fill(body,(byte)0xA5,0,14);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14),1);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(18),40);
        await dispatch.DispatchAsync(new(f.Host.Connection),new ChargeRentPointRequest(Metadata(0x03D0,body),1,40),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<ChargeRentPointResponse>()).Error);
        Assert.Equal(1000000,f.Host.Gold);
        var rental=f.Host.Services.GetRequiredService<IEquipmentRentalDomainService>();
        Assert.Equal(40,(await rental.PanelAsync(new(1),new(1),default)).LuckyStar);
        await dispatch.DispatchAsync(new(f.Host.Connection),new ChargeRentPointRequest(Metadata(0x03D0,body,2,false),1,40),default);
        Assert.Equal(1000000,f.Host.Gold);
        Assert.Equal(40,(await rental.PanelAsync(new(1),new(1),default)).LuckyStar);
        var rentBody=new byte[22];BinaryPrimitives.WriteInt32LittleEndian(rentBody.AsSpan(14),401000037);
        var rentMeta=new GameMessageMetadata(1,0x03CF,0,2){InboundOrdinal=3,SequenceIsFresh=true}.WithRawBody(rentBody);
        await dispatch.DispatchAsync(new(f.Host.Connection),new RentEquipmentRequest(rentMeta,401000037,0),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<RentEquipmentResponse>()).Error);
        var first=await rental.PanelAsync(new(1),new(1),default);
        Assert.Equal(37,first.LuckyStar);var weapon=Assert.Single(first.Items);
        f.Host.Clock.Advance(TimeSpan.FromMinutes(5));
        var retry=new GameMessageMetadata(1,0x03CF,0,2){InboundOrdinal=4,SequenceIsFresh=false}.WithRawBody(rentBody);
        await dispatch.DispatchAsync(new(f.Host.Connection),new RentEquipmentRequest(retry,401000037,0),default);
        var second=await rental.PanelAsync(new(1),new(1),default);
        Assert.Equal(37,second.LuckyStar);Assert.Equal(weapon,Assert.Single(second.Items));
        Assert.Equal(1,f.Host.Count(401000037));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Real_star_consumable_credit_and_consumption_are_once_only_and_cap_refuses_consumption()
    {
        using var f=new CeraShopFixture(useArchive:true);
        await f.Host.Seed(69,10002998,2);
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var stars=f.Host.Services.GetRequiredService<IAccountLuckyStarDomainService>();
        var body=new byte[11];BinaryPrimitives.WriteInt16LittleEndian(body,69);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7),10002998);
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(Metadata(0x002C,body),new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.Null(Assert.Single(f.Host.Sender.Messages.OfType<UseStackableResponse>()).ErrorCode);
        Assert.Equal(1,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(Metadata(0x002C,body,2,false),new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.Equal(1,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
        f.Host.Sql("UPDATE accounts SET lucky_star=400;");
        var next=new GameMessageMetadata(1,0x002C,0,2){InboundOrdinal=3,SequenceIsFresh=true}.WithRawBody(body);
        await dispatch.DispatchAsync(new(f.Host.Connection),new UseStackableRequest(next,new(InventoryContainerType.Main,69),0,10002998),default);
        Assert.NotNull(f.Host.Sender.Messages.OfType<UseStackableResponse>().Last().ErrorCode);
        Assert.Equal(400,await stars.BalanceAsync(new(1),default));Assert.Equal(1,f.Host.Count(10002998));
    }
    [Dfo.Server.Testing.PvfFact]
    public async Task Consumable_storage_failure_rolls_back_the_credited_stars_and_item()
    {
        using var f=new CeraShopFixture(useArchive:true);
        await f.Host.Seed(69,10002998,2);
        f.Host.Sql("CREATE TRIGGER reject_star_consume BEFORE UPDATE OF count ON item_slots WHEN OLD.owner_scope='character' AND OLD.owner_id=1 AND OLD.slot_index=69 BEGIN SELECT RAISE(ABORT,'forced consumption failure'); END;");
        var dispatch=f.Host.Services.GetRequiredService<INetworkMessageDispatcher>();
        var body=new byte[11];BinaryPrimitives.WriteInt16LittleEndian(body,69);BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(7),10002998);
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async ()=>await dispatch.DispatchAsync(new(f.Host.Connection),
            new UseStackableRequest(Metadata(0x002C,body),new(InventoryContainerType.Main,69),0,10002998),default));
        Assert.Equal(2,f.Host.Count(10002998));
        Assert.Equal(0,await f.Host.Services.GetRequiredService<IAccountLuckyStarDomainService>().BalanceAsync(new(1),default));
    }

}
