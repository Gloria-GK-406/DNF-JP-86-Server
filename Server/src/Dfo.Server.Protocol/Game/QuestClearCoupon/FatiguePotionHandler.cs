using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Fatigue;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

public sealed record UseFatiguePotionCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int SourceSlot)
    : ICommand<OpeningInventoryReceipt>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class UseFatiguePotionCommandHandler(IFatiguePotionDomainService potions, TimeProvider clock)
    : ICommandHandler<UseFatiguePotionCommand, OpeningInventoryReceipt>
{
    public ValueTask<OpeningInventoryReceipt> HandleAsync(CommandExecutionContext context,
        UseFatiguePotionCommand command, CancellationToken ct) =>
        potions.UseAsync(command.Session, OpeningWireSlots.FromMain(command.SourceSlot), clock.GetUtcNow(), ct);
}

public sealed class FatiguePotionHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICommandBus commands, INotificationBus notifications,
    ICharacterFatigueDomainService fatigue, IGameMessageSender sender, ILogger<FatiguePotionHandler> logger,
    ItemUsageProjectionDelivery itemUsageProjection, SelectedSessionTransitionGate transitions)
{
    public async ValueTask HandleAsync(NetworkDispatchContext context, QuestClearCouponRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!request.IsFatiguePotion || session is null
            || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        {
            await RefuseAsync(context, request, ct).ConfigureAwait(false);
            return;
        }
        await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, ct).ConfigureAwait(false);
        if (sessions.FindByCharacter(session.CharacterId) != session
            || sessions.FindByConnection(session.ConnectionId) != session) return;
        OpeningInventoryReceipt receipt;
        try
        {
            receipt = await commands.SendAsync(new UseFatiguePotionCommand(online.OnlineUserId,
                session, request.SourceSlot), ct).ConfigureAwait(false);
        }
        catch (OpeningRejectedException ex)
        {
            logger.LogInformation("Fatigue potion refused character={CharacterId} slot={Slot}: {Reason}",
                session.CharacterId, request.SourceSlot, ex.Message);
            await RefuseAsync(context, request, ct).ConfigureAwait(false);
            await itemUsageProjection.SendAsync(session, ct).ConfigureAwait(false);
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Fatigue potion failed character={CharacterId} slot={Slot}", session.CharacterId, request.SourceSlot);
            await RefuseAsync(context, request, ct).ConfigureAwait(false);
            return;
        }
        // Absolute inventory follows ACK so local decrement cannot leave the bag out of sync.
        var balance = await fatigue.BalanceOfAsync(session.CharacterId, ct).ConfigureAwait(false);
        await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]),
            new OpeningCompleted(receipt,
                [new QuestClearCouponResponse(true, request.SourceSlot, request.Container) { Action = request.Action }],
                FollowingMessages: [new FatigueNotification(balance)]), ct).ConfigureAwait(false);
        await itemUsageProjection.SendAsync(session, ct).ConfigureAwait(false);
        logger.LogInformation("Fatigue potion used character={CharacterId} slot={Slot} remaining={Remaining}",
            session.CharacterId, request.SourceSlot, balance.Remaining);
    }

    private ValueTask RefuseAsync(NetworkDispatchContext context, QuestClearCouponRequest request, CancellationToken ct) =>
        sender.SendCmdAsync(context.Connection.ConnectionId,
            new QuestClearCouponResponse(false, request.SourceSlot, request.Container) { Action = request.Action }, ct);
}
