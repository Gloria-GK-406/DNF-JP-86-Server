using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class MailItemRuleServiceTests
{
    [Theory]
    [InlineData(MailItemAttachType.Free, false, 0, MailItemSendRefusal.None)]
    [InlineData(MailItemAttachType.Trade, false, 0, MailItemSendRefusal.TemplateTradeRestricted)]
    [InlineData(MailItemAttachType.TradeDelete, false, 0, MailItemSendRefusal.TemplateTradeRestricted)]
    [InlineData(MailItemAttachType.Sealing, false, 0, MailItemSendRefusal.Unsealed)]
    [InlineData(MailItemAttachType.Sealing, false, 1, MailItemSendRefusal.None)]
    [InlineData(MailItemAttachType.Account, false, 0, MailItemSendRefusal.AccountBound)]
    [InlineData(MailItemAttachType.Account, true, 0, MailItemSendRefusal.None)]
    [InlineData(MailItemAttachType.TradeLimit, false, 0, MailItemSendRefusal.UnsupportedTradeLimit)]
    public async Task TemplateAndInstanceFactsDecideSending(
        MailItemAttachType attach, bool sameAccount, byte sealFlag, MailItemSendRefusal expected)
    {
        var rule = new MailItemRuleService(new DefinitionSource(attach));
        var core = StackableItemCore.Create(1, 10018, 1) with { SealFlag = sealFlag };
        var result = await rule.CanSendAsync(ItemSnapshot.FromCore(core, false), sameAccount, CancellationToken.None);
        Assert.Equal(expected, result.Refusal);
    }

    [Fact]
    public async Task InstanceTradeRestrictionOverridesFreeTemplate()
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.Free));
        var core = StackableItemCore.Create(1, 10018, 1) with { TradeRestriction = 1 };
        Assert.Equal(MailItemSendRefusal.InstanceTradeRestricted,
            (await rule.CanSendAsync(ItemSnapshot.FromCore(core, false), true, CancellationToken.None)).Refusal);
    }

    [Fact]
    public async Task EquipmentLockOverridesFreeTemplate()
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.Free));
        var core = StackableItemCore.Create(1, 10018, 1) with { EquipmentLockId = 7 };
        Assert.Equal(MailItemSendRefusal.InstanceLocked,
            (await rule.CanSendAsync(ItemSnapshot.FromCore(core, false), true, CancellationToken.None)).Refusal);
    }

    [Theory]
    [InlineData(MailItemAttachType.Free, 0, true)]
    [InlineData(MailItemAttachType.Sealing, 0, false)]
    [InlineData(MailItemAttachType.Sealing, 1, true)]
    [InlineData(MailItemAttachType.Account, 0, false)]
    public async Task DirectTradeEnforcesAttachmentRules(MailItemAttachType attach, byte seal, bool allowed)
    {
        var rule = new MailItemRuleService(new DefinitionSource(attach));
        var item = ItemSnapshot.FromCore(StackableItemCore.Create(1, 10018, 1) with { SealFlag = seal }, false);
        var result = await rule.CanTradeAsync(item, BusinessInventorySpace.Consumable, DateTimeOffset.UnixEpoch,
            CancellationToken.None);
        Assert.Equal(new PlayerTradeItemDecision(allowed), result);
    }

    [Theory]
    [InlineData(0x1F, null)]
    [InlineData(0x25, 0x05)]
    [InlineData(0x67, 0x47)]
    public async Task OnlyDirectTradeCanSpendOneTransferOnALimitedStack(byte original, int? after)
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.TradeLimit));
        var item = ItemSnapshot.FromCore(StackableItemCore.Create(1, 10018, 4) with { Attribute = original }, isPile: true);
        var trade = await rule.CanTradeAsync(item, BusinessInventorySpace.Consumable, DateTimeOffset.UnixEpoch,
            CancellationToken.None);
        Assert.Equal(new PlayerTradeItemDecision(after.HasValue, (byte?)after), trade);
        var mail = await rule.CanSendAsync(item, false, CancellationToken.None);
        Assert.Equal(MailItemSendRefusal.UnsupportedTradeLimit, mail.Refusal);
    }

    [Theory]
    [InlineData(3036, 0, true)]
    [InlineData(3036, 1, false)]
    [InlineData(10100115, 0, false)]
    public async Task DirectTradeAllowsOnlyEligibleAccountCrystals(int itemId, byte tradeRestriction, bool allowed)
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.Account));
        var item = ItemSnapshot.FromCore(StackableItemCore.Create(1, itemId, 1) with
        {
            TradeRestriction = tradeRestriction,
        }, false);
        var result = await rule.CanTradeAsync(item, BusinessInventorySpace.SharedMaterial, DateTimeOffset.UnixEpoch,
            CancellationToken.None);
        Assert.Equal(new PlayerTradeItemDecision(allowed), result);
    }

    private sealed class DefinitionSource(MailItemAttachType attach) : IMailItemDefinitionSource
    {
        public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MailItemDefinition?>(new(attach, attach.ToString(), null));
    }
}
