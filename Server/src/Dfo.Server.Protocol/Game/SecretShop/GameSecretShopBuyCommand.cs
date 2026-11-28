using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.SecretShop;

public sealed record GameSecretShopBuyCommand(NetworkDispatchContext Dispatch, SecretShopBuyRequest Input)
    : IProtocolCommand<SecretShopPurchaseResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class SecretShopBuyHandler(ICommandBus commands)
    : ClientMessageHandler<SecretShopBuyRequest>, IProtocolCommandMessageHandler<GameSecretShopBuyCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, SecretShopBuyRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameSecretShopBuyCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed class SecretShopWindowHandler(ILogger<SecretShopWindowHandler> logger)
    : ClientMessageHandler<SecretShopWindowReport>
{
    protected override ValueTask HandleMessageAsync(
        NetworkDispatchContext context, SecretShopWindowReport message, CancellationToken ct)
    {
        logger.LogDebug("Secret shop window open={Open} connection={ConnectionId}",
            message.Open, context.Connection.ConnectionId);
        return ValueTask.CompletedTask;
    }
}

internal sealed class GameSecretShopBuyCommandHandler(INpcShopDomainService shops, IQuestProgressDomainService quests,
    TimeProvider clock, ICommandWorkUnits units, IGameCommandOutbox outbox)
    : ICommandHandler<GameSecretShopBuyCommand, SecretShopPurchaseResult>
{
    public async ValueTask<SecretShopPurchaseResult> HandleAsync(CommandExecutionContext context,
        GameSecretShopBuyCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection.ConnectionId;
        if (context.Request is not CommandRequestContext.ProtocolEffectSelected selected)
        {
            var refusal = new SecretShopPurchaseResult(NpcShopRejection.NoSession);
            await outbox.RegisterCmdAsync(connection, new SecretShopBuyResponse(refusal), ct).ConfigureAwait(false);
            return refusal;
        }

        var actor = selected.Recipient.Session;
        var input = command.Input;
        var (result, progress) = await units.WriteAsync(context,
            new("secret-shop-purchase", [selected.Recipient], CommandWorkUnitKind.MainSettlement),
            async (unit, token) =>
            {
                try
                {
                    var purchase = await shops.PurchaseSecretAsync(actor, input.ItemId, input.Count,
                        clock.GetUtcNow(), unit.Footprint, token).ConfigureAwait(false);
                    if (purchase.Receipt is null)
                        return (purchase, (QuestProgressSnapshot?)null);
                    var change = await quests.ReportAsync(actor.AccountId, actor.CharacterId,
                        [new QuestHoldingsChanged()], token).ConfigureAwait(false);
                    return (purchase, change.NotificationSnapshot);
                }
                catch (NpcShopPurchaseRejectedException refusal)
                {
                    unit.RequireRollback();
                    return (new SecretShopPurchaseResult(refusal.Reason), (QuestProgressSnapshot?)null);
                }
            }, ct).ConfigureAwait(false);

        await outbox.RegisterExplicitAsync(connection, new(Project(result, progress), []), ct).ConfigureAwait(false);
        return result;
    }

    private static IReadOnlyList<IGameServerMessage> Project(SecretShopPurchaseResult result, QuestProgressSnapshot? progress)
    {
        var messages = new List<IGameServerMessage> { new SecretShopBuyResponse(result) };
        if (result.Receipt is not { } receipt)
            return messages;

        if (receipt.GoldSpent)
            messages.Add(GameInventoryDelivery.CarriedGoldNotification(receipt.Gold, receipt.PreparedAt));
        messages.AddRange(NpcShopInventoryProjection.SpentItems(receipt));
        messages.AddRange(NpcShopInventoryProjection.GrantedItems(receipt));
        if (progress is not null)
        {
            messages.Add(new AcceptedQuestProgressMessage([.. progress.Quests.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]));
        }
        return messages;
    }
}
