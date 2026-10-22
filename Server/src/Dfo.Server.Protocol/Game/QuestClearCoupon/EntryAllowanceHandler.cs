using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

public sealed record UseEntryAllowanceCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int SourceSlot)
    : ICommand<OpeningInventoryReceipt>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed class UseEntryAllowanceCommandHandler(IOpeningInventoryDomainService inventory,
    IEntryAllowanceItemDomainService items, TimeProvider clock)
    : ICommandHandler<UseEntryAllowanceCommand, OpeningInventoryReceipt>
{
    public async ValueTask<OpeningInventoryReceipt> HandleAsync(CommandExecutionContext context, UseEntryAllowanceCommand command, CancellationToken ct)
    {
        var slot = OpeningWireSlots.FromMain(command.SourceSlot);
        var source = await inventory.SourceAsync(command.Session, slot, clock.GetUtcNow(), ct).ConfigureAwait(false);
        var result = await items.TryUseAsync(command.Session, slot, source, ct).ConfigureAwait(false);
        return result?.OpeningReceipt ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Not a usable Heras entry coupon.");
    }
}

/// <summary>The Action byte only releases the client operation; the owned slot's PVF determines the effect.</summary>
public sealed class EntryAllowanceHandler(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICommandBus commands, IEventService notifications,
    IGameMessageSender sender, ILogger<EntryAllowanceHandler> logger,
    UsageQuotaProjectionDelivery quotas, IBusinessInventorySlotDomainService slots,
    IEntryAllowanceItemRuleService definitions)
{
    public async ValueTask<bool> TryHandleAsync(NetworkDispatchContext context, QuestClearCouponRequest request, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
        {
            await RefuseAsync(context, request, ct).ConfigureAwait(false);
            return true;
        }
        if (!sessions.IsInWorld(session)) return true;
        OpeningInventoryReceipt receipt;
        try
        {
            var slot = OpeningWireSlots.FromMain(request.SourceSlot);
            var source = await slots.Get(session.CharacterId, slot).Container.GetAsync(slot.Index, ct).ConfigureAwait(false);
            if (source is null || !definitions.Supports(source.ItemId)) return false;
            receipt = await commands.SendAsync(new UseEntryAllowanceCommand(online.OnlineUserId, session, request.SourceSlot), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(ex))
        {
            logger.LogWarning(ex, "Heras entry coupon refused character={CharacterId} slot={Slot}", session.CharacterId, request.SourceSlot);
            await RefuseAsync(context, request, ct).ConfigureAwait(false);
            return true;
        }
        await notifications.ProjectAsync(new NotificationAudience.TheseSessions([session]),
            new OpeningCompleted(receipt, [new QuestClearCouponResponse(true, request.SourceSlot, request.Container) { Action = request.Action }]), ct).ConfigureAwait(false);
        await quotas.SendAsync(session, UsageQuotaFamily.HerasDungeon, ct).ConfigureAwait(false);
        return true;
    }

    private ValueTask RefuseAsync(NetworkDispatchContext context, QuestClearCouponRequest request, CancellationToken ct) =>
        sender.SendCmdAsync(context.Connection.ConnectionId, new QuestClearCouponResponse(false, request.SourceSlot, request.Container) { Action = request.Action }, ct);
}
