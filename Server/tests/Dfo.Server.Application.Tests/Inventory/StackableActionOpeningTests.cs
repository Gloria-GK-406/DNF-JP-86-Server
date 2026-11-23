using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Application.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class StackableActionOpeningTests
{
    private const int LuckyStar = 10002998;
    private const int AuraTicket = 10157209;
    private const int AuraMaterial = 10157157;
    private const int AbyssTicket = 10005122;
    private const int AbyssCoin = 10005117;
    private const int SideTicket = 10157196;
    private static readonly byte[] LuckyStarRequest = Convert.FromHexString("03000000000000690000000000000000000000");
    private static readonly byte[] AuraTicketRequest = Convert.FromHexString("040000000000007B0000000000000000000000");
    private static readonly byte[] AbyssTicketRequest = Convert.FromHexString("080000000000007E0000000000000000000000");
    private static readonly byte[] SideTicketRequest = Convert.FromHexString("030000000000004D0000000000000000000000");
    private static readonly int[] SideQuestStatRewards = [3043,12683,12689,12690,12694,13502,13504,13507,13510,13513];

    [PvfFact]
    public async Task Captured_side_ticket_catches_up_side_quests_and_projects_permanent_attributes()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        host.SetLevel(86);
        host.Sql("INSERT INTO character_active_quests VALUES(1,0,2655,0),(1,1,13511,0),(1,2,1016,0);");
        host.Sql("INSERT INTO character_invisible_flags VALUES(1,13502,1);");
        await host.Seed(3, SideTicket, 1);

        await Send(host, 1, SideTicketRequest);
        AssertAck(host, 3, 77, succeeded: true);
        Assert.Equal(0, host.Count(SideTicket));
        Assert.Equal(2, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1 AND slot_index IN (2655,2656) AND flag_value=1"));
        Assert.Equal(33, host.Scalar("""
            SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1
                AND flag_value=1 AND (slot_index BETWEEN 13501 AND 13513 OR slot_index BETWEEN 12680 AND 12694
                    OR slot_index IN (2510,2513,3041,3042,3043))
            """));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1 AND slot_index=1016"));
        Assert.Equal(1016, host.Scalar("SELECT quest_id FROM character_active_quests WHERE character_id=1"));
        Assert.Equal(1, host.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1"));
        Assert.Equal(100000, host.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
        Assert.Equal(checked((int)host.ExperienceForLevel(86)), host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Equal(0, host.Count(10088439));
        Assert.Contains(host.Sender.Messages, message => message is InventoryDeltaMessage);
        Assert.Contains(host.Sender.Messages, message => message is QuestVisibilityMessage visibility
            && visibility.Flags[2655] == 1 && visibility.Flags[2656] == 1 && visibility.Flags[13513] == 1);
        AssertSideQuestAttributes(Assert.Single(host.Sender.Messages.OfType<ExtendedUserStateMessage>()));

        host.Sender.Messages.Clear();
        await Send(host, 1, SideTicketRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is QuestVisibilityMessage visibility && visibility.Flags[2656] == 1);
        AssertSideQuestAttributes(Assert.Single(host.Sender.Messages.OfType<ExtendedUserStateMessage>()));

        await host.Seed(3, SideTicket, 1);
        host.Sender.Messages.Clear();
        await Send(host, 2, SideTicketRequest);
        AssertAck(host, 3, 77, succeeded: false);
        Assert.Equal(1, host.Count(SideTicket));
    }

    [PvfFact]
    public async Task Side_ticket_rejects_ordinary_use_and_disallowed_character_conditions()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        host.SetLevel(86);
        await host.Seed(3, SideTicket, 1);

        await EffectRequestTransport.SendRaw(host, new(1, 0x002C, 0, 1), Convert.FromHexString("03000000000000"));
        Assert.Equal(1, host.Count(SideTicket));

        host.Sql("UPDATE character_condition SET is_hardcore_mode=1 WHERE character_id=1;");
        host.Sender.Messages.Clear();
        await Send(host, 2, SideTicketRequest);
        AssertAck(host, 3, 77, succeeded: false);
        Assert.Equal(1, host.Count(SideTicket));

        host.Sql("UPDATE character_condition SET is_hardcore_mode=0 WHERE character_id=1;");
        host.SetLevel(4);
        host.Sender.Messages.Clear();
        await Send(host, 3, SideTicketRequest);
        AssertAck(host, 3, 77, succeeded: false);
        Assert.Equal(1, host.Count(SideTicket));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1"));
    }

    [PvfFact]
    public async Task Captured_abyss_ticket_clears_listed_quests_without_exchange_coins()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        host.SetLevel(86);
        host.Sql("INSERT INTO character_active_quests VALUES(1,0,4391,0),(1,1,2002,0);");
        await host.Seed(8, AbyssTicket, 2);

        await Send(host, 1, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: true);
        Assert.Equal(new byte[] { 1, 8, 0, 0, 126, 0, 0, 0 },
            QuestClearCouponPayloadCodec.Encode(Assert.Single(host.Sender.Messages.OfType<QuestClearCouponResponse>())));
        Assert.Equal(1, host.Count(AbyssTicket));
        Assert.Equal(0, host.Count(AbyssCoin));
        Assert.Equal(2, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1 AND slot_index IN (4391,4539) AND flag_value=1"));
        Assert.Equal(1, host.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1"));
        Assert.Equal(2002, host.Scalar("SELECT quest_id FROM character_active_quests WHERE character_id=1"));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1 AND slot_index=2002"));
        Assert.Equal(100000, host.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=1"));
        Assert.Equal(checked((int)host.ExperienceForLevel(86)), host.Scalar("SELECT exp FROM characters WHERE character_id=1"));
        Assert.Contains(host.Sender.Messages, message => message is InventoryDeltaMessage);
        Assert.Contains(host.Sender.Messages, message => message is QuestVisibilityMessage visibility
            && visibility.Flags[4391] == 1 && visibility.Flags[4539] == 1 && visibility.Flags[2002] == 0);

        host.Sender.Messages.Clear();
        await Send(host, 1, AbyssTicketRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is QuestVisibilityMessage visibility
            && visibility.Flags[4391] == 1 && visibility.Flags[4539] == 1);

        host.Sender.Messages.Clear();
        await Send(host, 2, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: false);
        Assert.Equal(1, host.Count(AbyssTicket));
        Assert.Equal(0, host.Count(AbyssCoin));
    }

    [PvfFact]
    public async Task Abyss_ticket_requires_its_own_action_and_leaves_unavailable_quests_alone()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        host.SetLevel(80);
        await host.Seed(8, AbyssTicket, 2);
        await host.Seed(124, AbyssCoin, 20);

        var storyRequest = AbyssTicketRequest.ToArray();
        storyRequest[7] = 77;
        await Send(host, 1, storyRequest);
        AssertAck(host, 8, 77, succeeded: false);
        Assert.Equal(2, host.Count(AbyssTicket));
        Assert.Equal(20, host.Count(AbyssCoin));

        host.Sender.Messages.Clear();
        await Send(host, 2, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: true);
        Assert.Equal(1, host.Count(AbyssTicket));
        Assert.Equal(20, host.Count(AbyssCoin));
        Assert.Equal(1, host.Scalar("SELECT flag_value FROM character_invisible_flags WHERE character_id=1 AND slot_index=4391"));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1 AND slot_index=4539"));
    }

    [PvfFact]
    public async Task Captured_lucky_star_action_credits_the_account_and_debits_only_once()
    {
        using var shop = new CeraShopFixture(useArchive: true);
        var host = shop.Host;
        await host.Seed(3, LuckyStar, 2);

        await Send(host, 1, LuckyStarRequest);

        AssertAck(host, 3, 105, succeeded: true);
        Assert.Equal(1, host.Count(LuckyStar));
        Assert.Equal(1, host.Scalar("SELECT lucky_star FROM accounts WHERE account_id=1"));
        Assert.Contains(host.Sender.Messages, message => message is InventoryDeltaMessage);

        host.Sender.Messages.Clear();
        await Send(host, 1, LuckyStarRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is RentalStateMessage { LuckyStar: 1 });
        Assert.Equal(1, host.Count(LuckyStar));
        Assert.Equal(1, host.Scalar("SELECT lucky_star FROM accounts WHERE account_id=1"));

        var cap = host.Services.GetRequiredService<ILuckyStarRuleService>().Catalog.Limit;
        host.Sql($"UPDATE accounts SET lucky_star={cap} WHERE account_id=1");
        host.Sender.Messages.Clear();
        await Send(host, 2, LuckyStarRequest);
        AssertAck(host, 3, 105, succeeded: false);
        Assert.Equal(1, host.Count(LuckyStar));
        Assert.Equal(cap, host.Scalar("SELECT lucky_star FROM accounts WHERE account_id=1"));
    }

    [PvfTheory]
    [InlineData(0)]
    [InlineData(160)]
    public async Task Captured_aura_action_leaves_exchange_material_untouched_and_opens_only_once(int materials)
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        await host.Seed(4, AuraTicket, 2);
        if (materials > 0) await host.Seed(121, AuraMaterial, materials);

        await Send(host, 1, AuraTicketRequest);
        AssertAck(host, 4, 123, succeeded: true);
        Assert.Equal(1, host.Count(AuraTicket));
        Assert.Equal(materials, host.Count(AuraMaterial));
        Assert.Equal(1, host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));
        Assert.Contains(host.Sender.Messages, message => message is InventoryDeltaMessage);
        Assert.Contains(host.Sender.Messages, message => message is ExtendedUserStateMessage
            { Public.Extras.AuraSkinSlotOpen: true });

        host.Sender.Messages.Clear();
        await Send(host, 1, AuraTicketRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is ExtendedUserStateMessage
            { Public.Extras.AuraSkinSlotOpen: true });

        await Send(host, 2, AuraTicketRequest);
        AssertAck(host, 4, 123, succeeded: false);
        Assert.Equal(1, host.Count(AuraTicket));
        Assert.Equal(materials, host.Count(AuraMaterial));
    }

    private static CeraShopFixture CreateQuestFixture()
    {
        var shop = new CeraShopFixture(useArchive: true, questProtocol: true,
            configure: services => services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>());
        shop.Host.Sql("""
            UPDATE characters SET job=0;
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0);
            INSERT INTO character_pvp_record(character_id) VALUES(1);
            INSERT INTO character_pvp_skill_state(character_id) VALUES(1);
            INSERT INTO character_settings(character_id) VALUES(1);
            INSERT INTO character_expert_job(character_id) VALUES(1);
            """);
        return shop;
    }

    private static void AssertSideQuestAttributes(ExtendedUserStateMessage message)
    {
        var body = message.EncodeBody();
        var idsOffset = body.Length - 21 - SideQuestStatRewards.Length * sizeof(uint);
        Assert.Equal((uint)SideQuestStatRewards.Length, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(idsOffset - sizeof(uint))));
        var ids = Enumerable.Range(0, SideQuestStatRewards.Length)
            .Select(index => BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(idsOffset + index * sizeof(uint))))
            .Order();
        Assert.Equal(SideQuestStatRewards, ids);
    }

    private static ValueTask Send(ShopFixture host, ushort sequence, byte[] body) =>
        EffectRequestTransport.SendRaw(host, new(1, 0x0204, 0, sequence), body);

    private static void AssertAck(ShopFixture host, short slot, uint action, bool succeeded)
    {
        var response = Assert.Single(host.Sender.Messages.OfType<QuestClearCouponResponse>());
        Assert.Equal(succeeded, response.Succeeded);
        Assert.Equal(slot, response.SourceSlot);
        Assert.Equal(action, response.Action);
    }
}
