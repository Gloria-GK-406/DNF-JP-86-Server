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
    private static readonly byte[] LuckyStarRequest = Convert.FromHexString("03000000000000690000000000000000000000");
    private static readonly byte[] AuraTicketRequest = Convert.FromHexString("040000000000007B0000000000000000000000");
    private static readonly byte[] AbyssTicketRequest = Convert.FromHexString("080000000000007E0000000000000000000000");

    [PvfFact]
    public async Task Captured_abyss_ticket_clears_listed_quests_and_spends_material_once()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        host.SetLevel(86);
        host.Sql("INSERT INTO character_active_quests VALUES(1,0,4391,0),(1,1,2002,0);");
        await host.Seed(8, AbyssTicket, 2);
        await host.Seed(124, AbyssCoin, 9);

        await Send(host, 1, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: false);
        Assert.Equal(new byte[] { 0, 22, 8, 0, 0, 0 },
            QuestClearCouponPayloadCodec.Encode(Assert.Single(host.Sender.Messages.OfType<QuestClearCouponResponse>())));
        Assert.Equal(2, host.Count(AbyssTicket));
        Assert.Equal(9, host.Count(AbyssCoin));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_invisible_flags WHERE character_id=1"));
        Assert.Equal(2, host.Scalar("SELECT COUNT(*) FROM character_active_quests WHERE character_id=1"));

        await host.Seed(125, AbyssCoin, 11);
        host.Sender.Messages.Clear();
        await Send(host, 2, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: true);
        Assert.Equal(new byte[] { 1, 8, 0, 0, 126, 0, 0, 0 },
            QuestClearCouponPayloadCodec.Encode(Assert.Single(host.Sender.Messages.OfType<QuestClearCouponResponse>())));
        Assert.Equal(1, host.Count(AbyssTicket));
        Assert.Equal(10, host.Count(AbyssCoin));
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
        await Send(host, 2, AbyssTicketRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is QuestVisibilityMessage visibility
            && visibility.Flags[4391] == 1 && visibility.Flags[4539] == 1);

        host.Sender.Messages.Clear();
        await Send(host, 3, AbyssTicketRequest);
        AssertAck(host, 8, 126, succeeded: false);
        Assert.Equal(1, host.Count(AbyssTicket));
        Assert.Equal(10, host.Count(AbyssCoin));
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
        Assert.Equal(10, host.Count(AbyssCoin));
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

    [PvfFact]
    public async Task Captured_aura_action_requires_material_and_opens_only_once()
    {
        using var shop = CreateQuestFixture();
        var host = shop.Host;
        await host.Seed(4, AuraTicket, 2);
        await host.Seed(121, AuraMaterial, 79);

        await Send(host, 1, AuraTicketRequest);
        AssertAck(host, 4, 123, succeeded: false);
        var refusal = Assert.Single(host.Sender.Messages.OfType<QuestClearCouponResponse>());
        Assert.Equal(new byte[] { 0, 22, 4, 0, 0, 0 }, QuestClearCouponPayloadCodec.Encode(refusal));
        Assert.Equal(2, host.Count(AuraTicket));
        Assert.Equal(79, host.Count(AuraMaterial));
        Assert.Equal(0, host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));

        await host.Seed(122, AuraMaterial, 81);
        host.Sender.Messages.Clear();
        await Send(host, 2, AuraTicketRequest);
        AssertAck(host, 4, 123, succeeded: true);
        Assert.Equal(1, host.Count(AuraTicket));
        Assert.Equal(80, host.Count(AuraMaterial));
        Assert.Equal(1, host.Scalar("SELECT aura_skin_slot FROM characters WHERE character_id=1"));
        Assert.Contains(host.Sender.Messages, message => message is InventoryDeltaMessage);
        Assert.Contains(host.Sender.Messages, message => message is ExtendedUserStateMessage
            { Public.Extras.AuraSkinSlotOpen: true });

        host.Sender.Messages.Clear();
        await Send(host, 2, AuraTicketRequest);
        Assert.DoesNotContain(host.Sender.Messages, message => message is QuestClearCouponResponse);
        Assert.Contains(host.Sender.Messages, message => message is ExtendedUserStateMessage
            { Public.Extras.AuraSkinSlotOpen: true });

        await Send(host, 3, AuraTicketRequest);
        AssertAck(host, 4, 123, succeeded: false);
        Assert.Equal(1, host.Count(AuraTicket));
        Assert.Equal(80, host.Count(AuraMaterial));
    }

    private static CeraShopFixture CreateQuestFixture()
    {
        var shop = new CeraShopFixture(useArchive: true, questProtocol: true,
            configure: services => services.AddSingleton<ICharacterSkillPointDomainService, CharacterSkillPointDomainService>());
        shop.Host.Sql("""
            UPDATE characters SET job=0;
            INSERT INTO character_condition(character_id,pc_room_state) VALUES(1,0);
            INSERT INTO character_pvp(character_id) VALUES(1);
            INSERT INTO character_settings(character_id) VALUES(1);
            INSERT INTO character_expert_job(character_id) VALUES(1);
            """);
        return shop;
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
