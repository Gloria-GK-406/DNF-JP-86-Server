using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.DeleteItem;

public sealed record DeleteItemCommand(
    OnlineUserId OnlineUserId,
    SelectedSessionIdentity Session,
    BusinessSlot Slot,
    byte EntryCount,
    ushort OperationType,
    int ItemId,
    int Count) : ICommand<DeleteItemOutcome>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}

public sealed record DeleteItemOutcome(ItemDiscardResult Discard, QuestProgressSnapshot? QuestProgress)
{
    public ItemDiscardReceipt? Receipt => Discard.Receipt;
    public ItemDiscardRejection Rejection => Discard.Rejection;
}

public sealed class DeleteItemCommandHandler(
    IItemDiscardDomainService discards,
    IQuestProgressDomainService quests,
    TimeProvider clock)
    : ICommandHandler<DeleteItemCommand, DeleteItemOutcome>
{
    public async ValueTask<DeleteItemOutcome> HandleAsync(
        CommandExecutionContext context,
        DeleteItemCommand command,
        CancellationToken cancellationToken)
    {
        var discard = await discards.DiscardAsync(
            command.Session, command.Slot, command.EntryCount,
            command.OperationType, command.ItemId, command.Count,
            clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        if (discard.Receipt is null || !command.Slot.Space.IsCarriedItemSpace())
            return new DeleteItemOutcome(discard, null);

        var progress = await quests.ReportAsync(
            command.Session.AccountId, command.Session.CharacterId,
            [new QuestHoldingsChanged()], cancellationToken).ConfigureAwait(false);
        return new DeleteItemOutcome(discard, progress.NotificationSnapshot);
    }
}
