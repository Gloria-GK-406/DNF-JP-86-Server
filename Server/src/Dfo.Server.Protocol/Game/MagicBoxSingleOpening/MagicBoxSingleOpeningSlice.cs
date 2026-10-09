using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.MagicBoxSingleOpening;

public sealed record MagicBoxSingleOpeningRequest(GameMessageMetadata Metadata, MagicBoxOpeningInput? Input) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory;
    public ClientMessageType MessageType => ClientMessageType.GameMagicBoxSingleOpening;
}
internal sealed class MagicBoxSingleOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        MagicBoxOpeningInput? input = body.Length == 5 ? new(body[0], BinaryPrimitives.ReadInt16LittleEndian(body[1..]), 0,
            BinaryPrimitives.ReadInt16LittleEndian(body[3..]), 0, 1) : null;
        return new MagicBoxSingleOpeningRequest(metadata, input);
    }
}
public sealed record MagicBoxSingleOpeningResponse(MagicBoxOpeningInput? Input, MagicBoxOpeningResult Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameMagicBoxSingleOpening;
}
internal sealed class MagicBoxSingleOpeningEncoder : GameCmdMessageEncoder<MagicBoxSingleOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(MagicBoxSingleOpeningResponse message) =>
        OnePayload(0x00D0, OpeningPayloads.Box(message.Input ?? new(0, 0, 0, -1, 0, 0), message.Result, false));
}
public sealed record MagicBoxSingleOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, MagicBoxOpeningInput Input)
    : ICommand<MagicBoxOpeningResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class MagicBoxSingleOpeningCommandHandler(IMagicBoxOpeningDomainService boxes, TimeProvider clock)
    : ICommandHandler<MagicBoxSingleOpeningCommand, MagicBoxOpeningResult>
{
    public ValueTask<MagicBoxOpeningResult> HandleAsync(CommandExecutionContext context, MagicBoxSingleOpeningCommand command, CancellationToken ct) =>
        boxes.OpenAsync(command.Session, OpeningWireSlots.Business(command.Input), clock.GetUtcNow(), ct);
}
public sealed class MagicBoxSingleOpeningHandler(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICommandBus commands, IEventService notifications, IGameMessageSender sender, ILogger<MagicBoxSingleOpeningHandler> logger)
    : ClientMessageHandler<MagicBoxSingleOpeningRequest>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, MagicBoxSingleOpeningRequest message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        MagicBoxOpeningResult result;
        if (message.Input is null) result = new(OpeningRejection.InvalidRequest);
        else if (session is null || !onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var online) || online is null)
            result = new(OpeningRejection.NoSession);
        else
        {
            try { result = await commands.SendAsync(new MagicBoxSingleOpeningCommand(online.OnlineUserId, session, message.Input), ct).ConfigureAwait(false); }
            catch (OpeningRejectedException failure) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure)) { result = new(failure.Reason, Detail: failure.Message); }
            catch (Exception failure) when ((failure is not OperationCanceledException) && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(failure))
            {
                logger.LogError(failure, "Box opening storage failure for {ConnectionId}", context.Connection.ConnectionId);
                result = new(OpeningRejection.StorageFailure);
            }
        }
        logger.LogInformation("MagicBoxSingleOpening connection={ConnectionId} slot={Slot} count={Count} result={Result} detail={Detail} luck={Before}->{After}",
            context.Connection.ConnectionId, message.Input?.SourceSlot, message.Input?.Count, result.Rejection, result.Detail, result.LuckBefore, result.LuckAfter);
        var response = new MagicBoxSingleOpeningResponse(message.Input, result);
        if (result.Receipt is { } receipt)
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([receipt.Session.CharacterId]),
                new OpeningCompleted(receipt, [response]), ct).ConfigureAwait(false);
        else await sender.SendCmdAsync(context.Connection.ConnectionId, response, ct).ConfigureAwait(false);
    }
}
