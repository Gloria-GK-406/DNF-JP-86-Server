using System.Text;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Mail;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Mail;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Mailbox;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Mail;

public sealed class MailboxProtocolIntegrationTests
{
    [Fact]
    public async Task System_and_prepared_delivery_refresh_only_the_online_recipient_after_commit()
    {
        using var shop = CreateShop();
        var scope = shop.Services.GetRequiredService<DomainTransactionScope>();
        shop.Sender.BeforeSend = () =>
        {
            Assert.Null(scope.Current);
            Assert.Equal(2, shop.Scalar("SELECT count(*) FROM mailbox_messages WHERE receiver_character_id=1"));
        };
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using (var tx = scope.Begin())
            {
                await SendDomain(shop, MailBuilder.FromSystem(new(1)).Build());
                await Mail(shop).SendPreparedAsync(MailBuilder.FromSystem(new(1)).Build(), []);
                await SendDomain(shop, MailBuilder.FromSystem(new(2)).Build());
                Assert.Empty(shop.Sender.Messages);
                await tx.CommitAsync(default);
            }
        });
        Assert.Empty(shop.Sender.Messages);
        await shop.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync();
        Assert.Equal((ushort)2, Assert.IsType<MailboxAlarm>(Assert.Single(shop.Sender.Messages)).Count);
        shop.Sender.BeforeSend = null;
        shop.Sender.Messages.Clear();
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await using (scope.Begin())
                await SendDomain(shop, MailBuilder.FromSystem(new(1)).Build());
        });
            Assert.Equal(0, await shop.Services.GetRequiredService<DomainEventDispatcher>().DrainOnceAsync());
        Assert.Empty(shop.Sender.Messages);
        Assert.Equal(2, (await Mail(shop).InboxAsync(new(1))).Count);
    }

    [Fact]
    public async Task Player_send_rejects_a_temporary_binding_without_reading_it_as_persistent_storage()
    {
            using var shop = CreateShop();
            await shop.Seed(121, 1000, 5, 3);
            var containers = shop.Services.GetRequiredService<ICharacterContainerDomainService>();
            var own = containers.GetContainer(shop.Session.CharacterId, InventoryContainerType.Main);
            var temporary = new TemporaryMailContainer(own);
            TestGameplay.Run(shop.Services, () => containers.Register(shop.Session.CharacterId, temporary));
            Assert.True(TestGameplay.Run(shop.Services, () => containers.UseContainer(shop.Session.CharacterId, temporary.Id)));

            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), 0, "temporary",
                [new(new(BusinessInventorySpace.Material, 0), 1000, 1)]);
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, true, request));

            Assert.NotEqual(0, Assert.IsType<MailboxSendResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Equal(5, shop.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=121"));
            Assert.Empty(await Mail(shop).InboxAsync(new(2)));

    }

    private sealed class TemporaryMailContainer(IItemContainer backing) : IItemContainer
    {
        public ContainerId Id { get; } = new("temporary:mail");
        public InventoryContainerType Type => InventoryContainerType.Main;
        public ContainerShape Shape => backing.Shape;
        public bool IsRemovable => true;
        public ValueTask<ItemReference?> GetAsync(int index, CancellationToken ct) => backing.GetAsync(index, ct);
        public ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken ct) => backing.GetAllAsync(ct);
        public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct) => backing.TakeAsync(reference, count, ct);
        public ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct) => backing.PutAsync(index, record, ct);
        public ValueTask<ItemReference> UpdateAsync(ItemReference reference, Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct) => backing.UpdateAsync(reference, change, ct);
        public ValueTask FlushAsync(CancellationToken ct) => backing.FlushAsync(ct);
    }

    [Fact]
    public async Task System_builder_projects_zero_sender_and_empty_name()
    {
        using var shop = CreateShop();
        var mail = MailBuilder.FromSystem(new(1)).WithText("reward", "test").AddGold(1000).Build();
        Assert.Null(mail.Source.Character);
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await SendDomain(shop, mail);
        });
        var view = Assert.Single(await Mail(shop).InboxAsync(new(1)));
        Assert.Equal(0, view.SenderCharacterId);
        Assert.Empty(view.SenderName);
        var row = Assert.Single((await Command(shop, MailboxOperation.Open)).Inbox!.Messages);
        Assert.Equal(0, row.SenderCharacterId);
        Assert.Empty(row.SenderName);
        Assert.Equal(100000, shop.Gold);
    }

    [Fact]
    public async Task Player_send_takes_only_the_selected_stack_and_rejects_stale_or_duplicate_slots()
    {
            using var shop = CreateShop();
            await shop.Seed(121, 1000, 20, 3); await shop.Seed(122, 1000, 10, 3);
            var selection = new MailSendSelection(new(BusinessInventorySpace.Material, 1), 1000, 3);
            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), 0, "selected", [selection]);
            var handler = shop.Services.GetRequiredService<MailboxSendHandler>();
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, false, request));
            Assert.Equal(0, Assert.IsType<MailboxSendResponse>(shop.Sender.Messages.First()).Error);
            Assert.Equal(20, shop.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=121"));
            Assert.Equal(7, shop.Scalar("SELECT count FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=122"));
            foreach (var selections in new IReadOnlyList<MailSendSelection>[] { [selection, selection], [selection with { ItemId = 2000 }], [selection with { Count = 8 }] })
            {
                shop.Sender.Messages.Clear();
                await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, true, request with { Attachments = selections }));
                Assert.NotEqual(0, Assert.IsType<MailboxSendResponse>(Assert.Single(shop.Sender.Messages)).Error);
                Assert.Equal(27, shop.Count(1000));
                Assert.Single(await Mail(shop).InboxAsync(new(2)));
            }

    }

    [Fact]
    public async Task Mail_insert_failure_rolls_back_equipment_stack_and_gold()
    {
            using var shop = CreateShop();
            await shop.Seed(9, 3000, 0, 1); await shop.Seed(121, 1000, 20, 3);
            shop.Sql("CREATE TRIGGER fail_mail BEFORE INSERT ON mailbox_messages BEGIN SELECT RAISE(ABORT,'test insert failure'); END;");
            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), 30, "rollback",
                [new(new(BusinessInventorySpace.CarriedEquipment, 0), 3000, 1), new(new(BusinessInventorySpace.Material, 0), 1000, 3)]);
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, true, request));
            Assert.NotEqual(0, Assert.IsType<MailboxSendResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Equal(1, shop.Count(3000)); Assert.Equal(20, shop.Count(1000)); Assert.Equal(100000, shop.Gold);
            Assert.Empty(await Mail(shop).InboxAsync(new(2)));

    }

    [Fact]
    public async Task Player_send_moves_equipment_and_materials_with_gold_and_delivers_authoritative_updates()
    {
            using var shop = CreateShop();
            await shop.Seed(9, 3000, 0, 1);
            await shop.Seed(121, 1000, 20, 3);
            var uid = shop.Scalar("SELECT core_uid FROM item_slots WHERE owner_id=1 AND list_type=0 AND slot_index=9");
            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), 30, "邮件测试ABC123",
                [new(new(BusinessInventorySpace.CarriedEquipment, 0), 3000, 1), new(new(BusinessInventorySpace.Material, 0), 1000, 3)]);
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, true, request));
            Assert.Equal(0, Assert.IsType<MailboxSendResponse>(shop.Sender.Messages.First()).Error);
            Assert.Equal(99970, shop.Gold);
            Assert.Equal(17, shop.Count(1000));
            Assert.Equal(0, shop.Count(3000));
            var sent = Assert.Single(await Mail(shop).InboxAsync(new(2)));
            Assert.Equal("邮件测试ABC123", sent.Body);
            Assert.Equal(1, sent.SenderCharacterId);
            Assert.Equal(uid, Assert.IsType<MailAsset.Equipment>(sent.Attachments[0].Asset).Identity.Value);
            Assert.Equal(3, sent.Attachments.Length);
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, true, request));
            Assert.NotEqual(0, Assert.IsType<MailboxSendResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Equal(99970, shop.Gold);
            Assert.Equal(17, shop.Count(1000));
            Assert.Single(await Mail(shop).InboxAsync(new(2)));

    }

    [Fact]
    public async Task Player_send_rolls_back_materials_when_gold_is_insufficient()
    {
            using var shop = CreateShop();
            await shop.Seed(121, 1000, 20, 3);
            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), 100001, "rollback",
                [new(new(BusinessInventorySpace.Material, 0), 1000, 3)]);
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, false, request));
            Assert.NotEqual(0, Assert.IsType<MailboxSendResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Equal(20, shop.Count(1000)); Assert.Equal(100000, shop.Gold);
            Assert.Empty(await Mail(shop).InboxAsync(new(2)));

    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 100)]
    [InlineData(true, 0)]
    [InlineData(true, 100)]
    public async Task Send_ack_is_followed_by_authoritative_gold_even_without_gold_attachment(bool multi, int amount)
    {
            using var shop = CreateShop();
            var request = new PlayerMailRequest(Encoding.UTF8.GetBytes("shop2"), amount, "postage", []);
            shop.Sender.BeforeSend = () => Assert.Equal(100000 - amount, shop.Gold);
            await MailboxTestWire.Dispatch(shop, new MailboxSendRequest(GameMessageMetadata.Empty, multi, request));
            Assert.Equal(0, Assert.IsType<MailboxSendResponse>(shop.Sender.Messages[0]).Error);
            Assert.Equal(2, shop.Sender.Messages.Count);
            Assert.Equal(ServerMessageType.GameStateSynchronization, shop.Sender.Messages[1].MessageType);
            Assert.Equal(100000 - amount, shop.Gold);

    }

    [Fact]
    public async Task Delete_is_idempotent_and_never_deletes_another_recipients_mail()
    {
            using var shop = CreateShop();
            var receipt = await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("delete", "empty letter").Build());
            var actions = shop.Services.GetRequiredService<MailboxActionHandler>();
            foreach (var id in new[] { (uint)receipt.MailId, (uint)receipt.MailId, uint.MaxValue })
            {
                shop.Sender.Messages.Clear();
                await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, id, 0));
                var reply = Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages));
                Assert.Equal(0, reply.Error);
                Assert.Equal(id, reply.MailId);
                Assert.Equal(0, reply.Action);
            }
            Assert.Empty(await Mail(shop).InboxAsync(new(1)));
            var other = await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("private", "do not delete").Build());
            shop.Select(2);
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)other.MailId, 0));
            Assert.Equal(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Single(await Mail(shop).InboxAsync(new(1)));


    }

    [Fact]
    public async Task Opening_a_mixed_letter_projects_separate_item_and_gold_claim_keys_with_its_body()
    {
        using var shop = CreateShop();
        var receipt = await TestGameplay.RunAsync(shop.Services, async () => await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
            .WithText("reward", "mixed body").AddStackable(1000, 2).AddGold(17).Build()));

        var projection = (await Command(shop, MailboxOperation.Open)).Inbox!;
        var row = Assert.Single(projection.Messages);
        Assert.Equal(receipt.MailId, row.MessageId);
        Assert.Equal("mixed body", Encoding.UTF8.GetString(row.Body));
        var item = Assert.Single(row.Attachments);
        var gold = Assert.Single(row.GoldAttachments);
        Assert.Equal(17, gold.Amount);
        Assert.NotEqual(item.AttachmentId, gold.AttachmentId);
        shop.Sender.Messages.Clear();

                await MailboxTestWire.Dispatch(shop, new MailboxOpenRequest(GameMessageMetadata.Empty));
        Assert.IsType<MailboxOpenResponse>(shop.Sender.Messages[0]);
        Assert.Equal(ServerMessageType.GameStateSynchronization, shop.Sender.Messages[1].MessageType);
        Assert.Equal(2, shop.Sender.Messages.Count);
    }

    [Fact]
    public async Task Claim_commits_before_inventory_delivery_and_acks_the_requested_gold_slot_once()
    {
        using var shop = CreateShop();
        var sent = await TestGameplay.RunAsync(shop.Services, async () => await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
            .WithText("coins", "take these").AddGold(17).Build()));
        var gold = Assert.Single(Assert.Single((await Command(shop, MailboxOperation.Open)).Inbox!.Messages).GoldAttachments);
        var wire = checked(0x40000000u + (uint)gold.AttachmentId);
        shop.Sender.Messages.Clear();
        shop.Sender.BeforeSend = () =>
        {
            Assert.Equal(100017, shop.Gold);
            Assert.Equal(1, shop.Scalar($"SELECT claimed_flag FROM mailbox_attachments WHERE attachment_id={gold.AttachmentId}"));
        };

                await MailboxTestWire.Dispatch(shop, new MailboxClaimRequest(GameMessageMetadata.Empty, wire));

        Assert.Equal(2, shop.Sender.Messages.Count);
        Assert.Equal(ServerMessageType.GameStateSynchronization, shop.Sender.Messages[0].MessageType);
        Assert.Equal(wire, Assert.IsType<MailboxClaimResponse>(shop.Sender.Messages[1]).AttachmentId);
        Assert.Equal(sent.MailId, Assert.Single(await Mail(shop).InboxAsync(new(1))).Id);
        shop.Sender.BeforeSend = null;
        shop.Sender.Messages.Clear();
                await MailboxTestWire.Dispatch(shop, new MailboxClaimRequest(GameMessageMetadata.Empty, wire));
        Assert.NotEqual(0, Assert.IsType<MailboxClaimResponse>(Assert.Single(shop.Sender.Messages)).Error);
        Assert.Equal(100017, shop.Gold);
    }

    [Fact]
    public async Task Another_character_cannot_claim_and_a_previous_selection_cannot_enter_the_mail_command()
    {
        Task31SelectionProxy? transition = null;
        var observed = new Task31MailTrace();
        using var shop = CreateShop(services =>
        {
            transition = Task31SelectionProxy.Decorate(services);
            var descriptor = services.Last(d => d.ServiceType == typeof(ICommandWorkUnits));
            services.AddSingleton<ICommandWorkUnits>(provider => new Task31ObservedUnits((ICommandWorkUnits)(descriptor.ImplementationInstance
                ?? descriptor.ImplementationFactory?.Invoke(provider) ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), provider, observed));
        });
        await TestGameplay.RunAsync(shop.Services, async () =>
        {
            await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("private", "owner only").AddGold(9).Build());
        });
        var old = shop.Session;
        var attachment = Assert.Single(Assert.Single(await Mail(shop).InboxAsync(new(1))).Attachments).Id;
        var wire = checked(0x40000000u + (uint)attachment);
        shop.Select(2);
        shop.Sender.Messages.Clear();

                await MailboxTestWire.Dispatch(shop, new MailboxClaimRequest(GameMessageMetadata.Empty, wire));
        Assert.Equal(21, Assert.IsType<MailboxClaimResponse>(Assert.Single(shop.Sender.Messages)).Error);
        Assert.Equal(100000, shop.Gold);
        Assert.Equal(0, shop.Scalar($"SELECT claimed_flag FROM mailbox_attachments WHERE attachment_id={attachment}"));
        shop.Sender.Messages.Clear();
        var priorEntries = shop.Sender.Outbound.Entries.Count;
        transition!.AfterWorldRead = () => transition.PublishReplacement(old, shop.Services.GetRequiredService<IOnlineUserQueryDomainService>().GetRequired(shop.OnlineUser).Generation);
        var stale = await Assert.ThrowsAsync<MailException>(() => MailboxTestWire.Command(shop,
            new MailboxClaimRequest(GameMessageMetadata.Empty, wire)));
        Assert.Equal(transition.Replacement, shop.Services.GetRequiredService<ISelectedSessionDomainService>().FindByConnection(shop.Connection.ConnectionId));
        Assert.Equal(1, transition.Replacement!.CharacterId.Value);
        Assert.Empty(shop.Sender.Messages);
        var closed = Assert.Single(shop.Sender.Outbound.Entries.Skip(priorEntries));
        Assert.True(closed.IsClose); Assert.Same(shop.Connection, closed.Connection);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, observed.ScopeTransaction!.Completion);
        Assert.Null(observed.ScopeTransaction.CommitFailure); Assert.False(observed.ScopeTransaction.IsRollbackOnly);
        Assert.Null(shop.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>().Current);
        Assert.Equal(MailFailure.NotOwned, stale.Reason);
        Assert.Equal(0, shop.Scalar($"SELECT claimed_flag FROM mailbox_attachments WHERE attachment_id={attachment}"));
    }

    [Fact]
    public async Task Delete_requires_claim_but_read_and_empty_letter_follow_the_mail_id()
    {
            using var shop = CreateShop();
            var receipt = await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("kept", "until claim").AddGold(3).Build());
            var attachment = Assert.Single(Assert.Single(await Mail(shop).InboxAsync(new(1))).Attachments).Id;
            var actions = shop.Services.GetRequiredService<MailboxActionHandler>();
            shop.Sender.Messages.Clear();

            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)receipt.MailId, 3));
            Assert.NotEqual(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Single(await Mail(shop).InboxAsync(new(1)));
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)receipt.MailId, 0));
            Assert.NotEqual(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Single(await Mail(shop).InboxAsync(new(1)));
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)receipt.MailId, 2));
            Assert.Equal(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.True(Assert.Single(await Mail(shop).InboxAsync(new(1))).Read);
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxClaimRequest(GameMessageMetadata.Empty, checked(0x40000000u + (uint)attachment)));
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)receipt.MailId, 0));
            Assert.Equal(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Empty(await Mail(shop).InboxAsync(new(1)));

            var empty = await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("notice", "no attachment").Build());
            shop.Sender.Messages.Clear();
            await MailboxTestWire.Dispatch(shop, new MailboxActionRequest(GameMessageMetadata.Empty, (uint)empty.MailId, 0));
            Assert.Equal(0, Assert.IsType<MailboxActionResponse>(Assert.Single(shop.Sender.Messages)).Error);
            Assert.Empty(await Mail(shop).InboxAsync(new(1)));

    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    public async Task Material_claim_ignores_main_virtual_slots_and_preserves_their_values(int existing)
    {
            using var shop = CreateShop();
            if (existing > 0) await shop.Seed(121, 1000, existing, 3);
            var core = new byte[82];
            core[0] = 2; core[1] = 1; core[5] = 6;
            shop.Sql($"INSERT INTO items(item_core) VALUES(X'{Convert.ToHexString(core)}'); INSERT INTO item_slots(owner_scope,owner_id,list_type,slot_index,kind,id,core_uid,count) VALUES('character',1,0,1,'template',1,last_insert_rowid(),6);");
            await SendDomain(shop, new MailBuilder(MailSource.FromSystem(), new(1))
                .WithText("material", "virtual slot regression").AddStackable(1000, 2).Build());
            var attachment = Assert.Single(Assert.Single(await Mail(shop).InboxAsync(new(1))).Attachments).Id;
            await MailboxTestWire.Dispatch(shop, new MailboxClaimRequest(GameMessageMetadata.Empty, checked(0x40000000u + (uint)attachment)));
            Assert.Equal(0, Assert.IsType<MailboxClaimResponse>(shop.Sender.Messages.Last()).Error);
            Assert.Equal(existing + 2, shop.Count(1000));
            Assert.Equal(6, shop.Scalar("SELECT count FROM item_slots WHERE owner_scope='character' AND owner_id=1 AND list_type=0 AND slot_index=1"));
            Assert.Equal(1, shop.Scalar($"SELECT claimed_flag FROM mailbox_attachments WHERE attachment_id={attachment}"));

    }

    [Fact]
    public async Task Recipient_query_returns_the_stored_character_identity()
    {
            using var shop = CreateShop();
            await MailboxTestWire.Dispatch(shop, new MailboxQueryRequest(GameMessageMetadata.Empty, Encoding.UTF8.GetBytes("shop2")));
            var reply = Assert.IsType<MailboxQueryResponse>(Assert.Single(shop.Sender.Messages));
            Assert.Equal(0, reply.Error);
            Assert.Equal(new CharacterId(2), reply.Recipient!.CharacterId);
            Assert.Equal("shop2", Encoding.UTF8.GetString(reply.Recipient.Name));

    }

    private static ShopFixture CreateShop(Action<IServiceCollection>? configure = null) => new(services =>
    {
        services.AddSingleton<IMailItemDefinitionSource>(new FreeMailItemDefinitions());
        services.AddSingleton<IGoldCarryLimitSource>(new HighGoldLimit());
        configure?.Invoke(services);
    });
    private static Task<MailSendReceipt> SendDomain(ShopFixture shop, Dfo.Server.Data.Mail.Mail request) =>
        TestGameplay.RunAsync(shop.Services, async () => await Mail(shop).SendAsync(request));
    private static IMailboxDomainService Mail(ShopFixture shop) => shop.Services.GetRequiredService<IMailboxDomainService>();
    private static async Task<MailboxResult> Command(ShopFixture shop, MailboxOperation operation) =>
        (await MailboxTestWire.Command(shop, new MailboxOpenRequest(GameMessageMetadata.Empty))).Result;
    private sealed class FreeMailItemDefinitions : IMailItemDefinitionSource
    {
        public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken token) =>
            ValueTask.FromResult<MailItemDefinition?>(new(MailItemAttachType.Free, "free", null));
    }
    private sealed class HighGoldLimit : IGoldCarryLimitSource
    {
        public int BaseLimitFor(int characterLevel) => int.MaxValue;
    }
}

internal static class MailboxTestWire
{
    private sealed class Counter { internal ushort Sequence; internal readonly GameInboundFrameCounter Frames = new(); }
    private static readonly ConditionalWeakTable<object, Counter> counters = new();
    internal static async Task Dispatch(ShopFixture shop, IClientNetworkMessage input) =>
        await shop.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(shop.Connection), Decode(shop.Services, shop.Connection, input), default);
    internal static Task<GameMailboxOutcome> Command(ShopFixture shop, IClientNetworkMessage input) =>
        shop.Services.GetRequiredService<ICommandBus>().SendAsync(new GameMailboxCommand(new(shop.Connection), Decode(shop.Services, shop.Connection, input))).AsTask();
    internal static IClientNetworkMessage Decode(IServiceProvider services, object connection, IClientNetworkMessage input)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        ushort type;
        switch (input)
        {
            case MailboxOpenRequest: type = 0x0060; break;
            case MailboxClaimRequest claim: type = 0x005F; writer.Write(claim.AttachmentId); break;
            case MailboxActionRequest action: type = 0x0086; writer.Write(action.MailId); writer.Write(action.Action); break;
            case MailboxQueryRequest query: type = 0x0144; Text(query.Name); break;
            case MailboxSendRequest send:
                type = send.Multi ? (ushort)0x013B : (ushort)0x005E;
                Text(send.Mail.RecipientName); writer.Write(send.Mail.Gold);
                if (send.Multi) writer.Write(checked((ushort)send.Mail.Attachments.Count));
                var selections = send.Mail.Attachments;
                for (var index = 0; index < selections.Count; index++)
                {
                    var selection = selections[index];
                    var slot = A21InventoryLayout.ToGameSlot(selection.Slot);
                    if (!send.Multi || index != 0) writer.Write((byte)slot.Space);
                    writer.Write(checked((ushort)slot.Index)); writer.Write(selection.ItemId); writer.Write(selection.Count);
                }
                if (!send.Multi && selections.Count == 0) { writer.Write((byte)0); writer.Write((ushort)0); writer.Write(0); writer.Write(0); }
                Text(Encoding.UTF8.GetBytes(send.Mail.Body)); writer.Write(0u); break;
            default: throw new InvalidOperationException("Unknown mailbox test input.");
        }
        var body = bytes.ToArray(); var counter = counters.GetOrCreateValue(connection); var sequence = ++counter.Sequence;
        var (ordinal, fresh) = counter.Frames.Observe(sequence);
        var metadata = new GameMessageMetadata(1, type, 0, sequence) { InboundOrdinal = ordinal, SequenceIsFresh = fresh };
        metadata = metadata.WithRawBody(body);
        var registration = services.GetServices<GameMessageDecoderRegistration>().Single(row => row.Selector == 1 && row.Type == type);
        return ((IGameMessageDecoder)services.GetRequiredService(registration.DecoderType)).Decode(metadata, body) ?? throw new InvalidDataException("The registered mailbox decoder rejected the test body.");
        void Text(byte[] value) { writer.Write(value.Length); writer.Write(value); }
    }
}

public class Task31SelectionProxy : DispatchProxy
{
    private ISelectedSessionDomainService original = null!;
    internal Action? AfterWorldRead;
    private Task31SelectionProxy? controller;
    internal SelectedSessionIdentity? Replacement;
    internal void PublishReplacement(SelectedSessionIdentity previous, long generation)
    {
        Replacement = SelectedSessionIdentity.Create(previous.AccountId, previous.CharacterId, previous.ConnectionId,
            generation, previous.ListenerPort);
        original.Select(Replacement);
        original.EnterWorld(Replacement);
    }
    internal static Task31SelectionProxy Decorate(IServiceCollection services)
    {
        var descriptor = services.Last(d => d.ServiceType == typeof(ISelectedSessionDomainService));
        var holder = new Task31SelectionProxy();
        services.AddSingleton<ISelectedSessionDomainService>(provider =>
        {
            var service = descriptor.ImplementationInstance as ISelectedSessionDomainService
                ?? (ISelectedSessionDomainService)(descriptor.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!));
            var wrapped = Create<ISelectedSessionDomainService, Task31SelectionProxy>();
            var proxy = (Task31SelectionProxy)wrapped; proxy.original = service; proxy.controller = holder; holder.original = service; return wrapped;
        });
        return holder;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        try
        {
            var result = method!.Invoke(original, arguments);
            if (method.Name == nameof(ISelectedSessionDomainService.IsInWorld) && result is true && controller!.AfterWorldRead is { } action)
            { controller.AfterWorldRead = null; action(); }
            return result;
        }
        catch (TargetInvocationException error) when (error.InnerException is { } cause)
        { ExceptionDispatchInfo.Throw(cause); throw; }
    }
}
