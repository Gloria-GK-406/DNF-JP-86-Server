using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TitleBook;

public sealed record GameTitleBookMutationCommand(NetworkDispatchContext Dispatch, TitleBookMutationRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameTitleBookMutationCommandHandler(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users, ITitleBookDomainService titles,
    ICommandWorkUnits units, IGameCommandOutbox outbox, ILogger<GameTitleBookMutationCommandHandler> logger)
    : ICommandHandler<GameTitleBookMutationCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameTitleBookMutationCommand command, CancellationToken ct)
    {
        var context = command.Dispatch;
        var message = command.Input;
        var replaceMirror = !message.Store && message.Space == 0x314D4254;
        ValueTask Refuse() => outbox.RegisterCmdAsync(context.Connection.ConnectionId,
            new TitleBookMutationResponse(message.Store, replaceMirror ? 3 : message.Space,
                replaceMirror ? 13 : message.Slot, message.Category, message.Index, 0x0A), ct);
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        if (!message.Complete || (message.Space is not (0 or 3) && !replaceMirror) || message.Slot < 0
            || message.Category is < 0 or > 4 || message.Index < 0 || session is null
            || !users.TryGetByConnection(context.Connection.ConnectionId, out var user) || user is null)
        { await Refuse().ConfigureAwait(false); return false; }

        BusinessSlot slot;
        // Wire-address validation stays outside the original transaction, inside its original refusal policy.
        try { slot = A21InventoryLayout.ToBusinessSlot(new(replaceMirror ? InventoryContainerType.Main : (InventoryContainerType)message.Space, message.Slot)); }
        catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
        {
            logger.LogWarning(failure, "Title book request refused character={CharacterId} store={Store}", session.CharacterId, message.Store);
            await Refuse().ConfigureAwait(false); return false;
        }

        Exception? actionFailure = null;
        var write = await units.WriteResultAsync(execution, new("TitleBookMutation", [new(session, context.Connection, message.Metadata)]),
            async (unit, token) => {
                try
                {
                    return replaceMirror
                        ? await titles.EquipCarriedTitleReplacingMirrorAsync(session.CharacterId, slot, message.ItemId, message.Category, message.Index, token).ConfigureAwait(false)
                        : message.Store
                            ? await titles.StoreAsync(session.CharacterId, slot, message.ItemId, message.Category, message.Index, token).ConfigureAwait(false)
                            : await titles.WithdrawAsync(session.CharacterId, slot, message.ItemId, message.Category, message.Index, token).ConfigureAwait(false);
                }
                catch (Exception failure) when (failure is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(failure))
                {
                    actionFailure = failure;
                    unit.RequireRollback(failure);
                    return new TitleBookMutationResult(false, null, message.Category, message.Index, slot);
                }
            }, ct).ConfigureAwait(false);
        var cause = write is CommandWriteResult<TitleBookMutationResult>.CommitRolledBack rollback ? rollback.Cause : actionFailure;
        if (cause is not null)
        {
            logger.LogWarning(cause, "Title book request refused character={CharacterId} store={Store}", session.CharacterId, message.Store);
            await Refuse().ConfigureAwait(false); return false;
        }
        var result = ((CommandWriteResult<TitleBookMutationResult>.Returned)write).Value;
        // This conversion has always followed the business catch; do not turn its failure into an ACK.
        var actual = A21InventoryLayout.ToGameSlot(result.Slot);
        await outbox.RegisterCmdAsync(context.Connection.ConnectionId, new TitleBookMutationResponse(message.Store,
            (int)actual.Space, actual.Index, result.Category, result.Index, result.Success ? null : (byte)0x0A), ct).ConfigureAwait(false);
        return result.Success;
    }
}
