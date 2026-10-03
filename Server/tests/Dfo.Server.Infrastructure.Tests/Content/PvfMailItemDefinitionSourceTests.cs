using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfMailItemDefinitionSourceTests
{
    [SkippableFact]
    public async Task RealArchive_DeclaresDistinctMailAttachPolicies()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfMailItemDefinitionSource(provider);
        Assert.Equal(MailItemAttachType.Free,
            (await source.GetAsync(10018, CancellationToken.None))?.AttachType);
        Assert.Equal(MailItemAttachType.Sealing,
            (await source.GetAsync(10020, CancellationToken.None))?.AttachType);
        Assert.Equal(MailItemAttachType.Account,
            (await source.GetAsync(10194, CancellationToken.None))?.AttachType);
        Assert.Equal(MailItemAttachType.Trade,
            (await source.GetAsync(9, CancellationToken.None))?.AttachType);
        var limited = await source.GetAsync(10002395, CancellationToken.None);
        Assert.Equal(MailItemAttachType.TradeLimit, limited?.AttachType);
        Assert.Equal(1, limited?.TradeLimitMax);

        var rule = new MailItemRuleService(source);
        Assert.True((await rule.CanSendAsync(
            StackableItemCore.Create(1, 10018, 1), false, CancellationToken.None)).Allowed);
        Assert.Equal(MailItemSendRefusal.Unsealed,
            (await rule.CanSendAsync(StackableItemCore.Create(1, 10020, 1), false,
                CancellationToken.None)).Refusal);
        Assert.True((await rule.CanSendAsync(
            StackableItemCore.Create(1, 10020, 1) with { SealFlag = 1 }, false,
            CancellationToken.None)).Allowed);
        Assert.Equal(MailItemSendRefusal.AccountBound,
            (await rule.CanSendAsync(StackableItemCore.Create(1, 10194, 1), false,
                CancellationToken.None)).Refusal);
        Assert.Equal(MailItemSendRefusal.TemplateTradeRestricted,
            (await rule.CanSendAsync(StackableItemCore.Create(2, 9, 1), true,
                CancellationToken.None)).Refusal);
    }
}
