using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

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
        var result = await rule.CanSendAsync(core, sameAccount, CancellationToken.None);
        Assert.Equal(expected, result.Refusal);
    }

    [Fact]
    public async Task InstanceTradeRestrictionOverridesFreeTemplate()
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.Free));
        var core = StackableItemCore.Create(1, 10018, 1) with { TradeRestriction = 1 };
        Assert.Equal(MailItemSendRefusal.InstanceTradeRestricted,
            (await rule.CanSendAsync(core, true, CancellationToken.None)).Refusal);
    }

    [Fact]
    public async Task EquipmentLockOverridesFreeTemplate()
    {
        var rule = new MailItemRuleService(new DefinitionSource(MailItemAttachType.Free));
        var core = StackableItemCore.Create(1, 10018, 1) with { EquipmentLockId = 7 };
        Assert.Equal(MailItemSendRefusal.InstanceLocked,
            (await rule.CanSendAsync(core, true, CancellationToken.None)).Refusal);
    }

    private sealed class DefinitionSource(MailItemAttachType attach) : IMailItemDefinitionSource
    {
        public ValueTask<MailItemDefinition?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<MailItemDefinition?>(new(attach, attach.ToString(), null));
    }
}
