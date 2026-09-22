using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.LotteryOpening;

public sealed record LotteryOpeningRequest(GameMessageMetadata Metadata, ushort Phase, short Slot) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameLotteryOpening;
}
internal sealed class LotteryOpeningDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 4 || BinaryPrimitives.ReadUInt16LittleEndian(body) > 1 || BinaryPrimitives.ReadInt16LittleEndian(body[2..]) < 3)
            throw new InvalidDataException("Invalid lottery opening request.");
        return new LotteryOpeningRequest(metadata, BinaryPrimitives.ReadUInt16LittleEndian(body), BinaryPrimitives.ReadInt16LittleEndian(body[2..]));
    }
}
public sealed record LotteryOpeningResponse(LotteryOpeningResult? Result) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLotteryOpening;
}
internal sealed class LotteryOpeningEncoder : GameCmdMessageEncoder<LotteryOpeningResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LotteryOpeningResponse message) => OnePayload(0x001B, Encode(message));
    internal static byte[] Encode(LotteryOpeningResponse message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var result = message.Result;
        writer.Write(result is null ? (byte)0 : (byte)1);
        if (result?.Receipt is not { } receipt)
        {
            writer.Write((short)-1); writer.Write((ushort)0); writer.Write(0); writer.Write(0);
            return stream.ToArray();
        }
        var reward = receipt.Rewards.Single();
        var core = receipt.Updates.Single(x => x.ContainerType == InventoryContainerType.Main).Items.Single(x => x.SlotIndex == reward.Slot).Core;
        writer.Write(checked((short)result.SourceSlot)); writer.Write(checked((short)reward.Slot));
        writer.Write(reward.ItemId); writer.Write(reward.Count); writer.Write(core.Durability);
        writer.Write(core.Attribute); writer.Write(core.AmplifyType); writer.Write(core.AmplifyValue);
        // Upstream ordinary equipment native result; A21 effect awaits the controlled client test.
        writer.Write((byte)0xEF); writer.Write(25); writer.Write(new byte[25]);
        writer.Write(new byte[3]);
        return stream.ToArray();
    }
}
public sealed record LotteryOverflowResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLotteryOverflow;
}
internal sealed class LotteryOverflowEncoder : GameCmdMessageEncoder<LotteryOverflowResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LotteryOverflowResponse message) => OnePayload(0x00D9, new byte[] {1, 0x1B, 0});
}
public sealed record LotteryOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int? Slot, bool Start)
    : ICommand<LotteryOpeningResult>, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class LotteryOpeningCommandHandler(ILotteryOpeningDomainService opening) : ICommandHandler<LotteryOpeningCommand, LotteryOpeningResult>
{
    public ValueTask<LotteryOpeningResult> HandleAsync(CommandExecutionContext context, LotteryOpeningCommand command, CancellationToken token) =>
        opening.ExecuteAsync(command.Session, command.Slot, command.Start, token);
}

public sealed class LotteryOpeningDelivery(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    SelectedSessionTransitionGate transitions, ICommandBus commands, INotificationBus notifications, IGameMessageSender sender,
    ILogger<LotteryOpeningDelivery> logger)
{
    public async ValueTask HandleAsync(NetworkDispatchContext context, int? slot, bool start, bool overflow, CancellationToken token)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null) return;
        await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
        if (sessions.FindByConnection(connection) != session || !users.TryGetByConnection(connection, out var user) || user is null) return;
        LotteryOpeningResult? result = null;
        try
        {
            result = await commands.SendAsync(new LotteryOpeningCommand(user.OnlineUserId, session, slot, start), token).ConfigureAwait(false);
        }
        catch (OpeningRejectedException ex) { logger.LogInformation("Lottery rejected character={Character} slot={Slot}: {Reason}", session.CharacterId, slot, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Lottery transaction failed character={Character}", session.CharacterId); }
        var response = new LotteryOpeningResponse(result);
        logger.LogInformation("Lottery character={Character} start={Start} overflow={Overflow} slot={Slot} committed={Committed}",
            session.CharacterId, start, overflow, slot, result?.Receipt is not null);
        if (result?.Receipt is { } receipt)
        {
            // State was committed by the command; native result precedes absolute inventory updates.
            IReadOnlyList<IGameCmdMessage> replies = overflow ? [new LotteryOverflowResponse(), response] : [response];
            await notifications.PublishAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new OpeningCompleted(receipt, replies, result.GoldAfter), token).ConfigureAwait(false);
        }
        else await sender.SendCmdAsync(connection, response, token).ConfigureAwait(false);
    }
}
public sealed class LotteryOpeningHandler(LotteryOpeningDelivery delivery) : ClientMessageHandler<LotteryOpeningRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, LotteryOpeningRequest message, CancellationToken token) =>
        delivery.HandleAsync(context, message.Slot, message.Phase == 0, false, token);
}
public sealed class LotteryOverflowHandler(LotteryOpeningDelivery delivery, UnansweredReportHandler fallback) : ClientMessageHandler<OverflowInfoReport>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, OverflowInfoReport message, CancellationToken token) =>
        message.Body.AsSpan().SequenceEqual(new byte[] { 1, 0x1B, 0 })
            ? delivery.HandleAsync(context, null, false, true, token)
            : fallback.HandleAsync(context, message, token);
}
