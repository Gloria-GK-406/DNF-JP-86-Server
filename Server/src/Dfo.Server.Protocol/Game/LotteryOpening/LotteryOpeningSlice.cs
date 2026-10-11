using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.StateSynchronization;
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
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.LotteryOpening;

public sealed record LotteryOpeningRequest(GameMessageMetadata Metadata, ushort Mode, short Slot) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | (Mode == 1 ? ReplayProjectionNeeds.Premium : ReplayProjectionNeeds.None);
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
public sealed record LotteryOpeningResponse(LotteryOpeningResult? Result, byte Error = 0) : IGameCmdMessage
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
        if (result is null) { writer.Write(message.Error); return stream.ToArray(); }
        var receipt = result.Receipt;
        var reward = receipt.Rewards[0];
        var core = reward.Snapshot ?? receipt.Updates.Single(x => x.Slot == reward.Slot).Core;
        writer.Write(checked((short)OpeningWireSlots.RewardIndex(result.SourceSlot))); writer.Write(checked((short)OpeningWireSlots.RewardIndex(reward.Slot)));
        writer.Write(reward.ItemId); writer.Write(receipt.Rewards.Sum(x => x.Count)); writer.Write(core.Durability);
        writer.Write(core.Attribute); writer.Write(core.AmplifyType); writer.Write(core.AmplifyValue);
        // A21 0x0113F780 reads these fields unconditionally, including two length-prefixed values.
        // Empty optional properties; absolute inventory projection follows this native result.
        writer.Write(0); // unknown item scalar
        writer.Write((byte)0); // extra u32 array count (0x01637A80)
        writer.Write((ushort)0); // nested reader 0x012612F0
        writer.Write((byte)0); // nested list count (0x01088BC0)
        writer.Write(new byte[10]); // u8 x3, u16, u8 x5
        writer.Write(4); writer.Write(0); // destination constructor initializes a 4-byte scalar
        writer.Write(8); writer.Write(0L); // second destination initializes an 8-byte scalar
        writer.Write((byte)0); writer.Write(0);
        return stream.ToArray();
    }
}
public sealed record LotteryOpeningCommand(OnlineUserId OnlineUserId, SelectedSessionIdentity Session, int Slot, ushort Mode)
    : ICommand<LotteryOpeningResult>, IRequestSettlementCommand, IOnlineUserCommand, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed class LotteryOpeningCommandHandler(ILotteryOpeningDomainService opening) : ICommandHandler<LotteryOpeningCommand, LotteryOpeningResult>
{
    public ValueTask<LotteryOpeningResult> HandleAsync(CommandExecutionContext context, LotteryOpeningCommand command, CancellationToken token) =>
        opening.ExecuteAsync(command.Session, OpeningWireSlots.FromMain(command.Slot), command.Mode, token);
}

public sealed class LotteryOpeningDelivery(ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService users,
    SelectedSessionTransitionGate transitions, ICommandBus commands, IEventService notifications, IGameMessageSender sender,
    ILogger<LotteryOpeningDelivery> logger)
{
    public async ValueTask HandleAsync(NetworkDispatchContext context, int slot, ushort mode, ushort sequence, CancellationToken token)
    {
        var connection = context.Connection.ConnectionId;
        var session = sessions.FindByConnection(connection);
        if (session is null) return;
        await using var lease = await transitions.AcquireAsync(session.AccountId, UserAccessMode.Exclusive, token).ConfigureAwait(false);
        if (sessions.FindByConnection(connection) != session || !users.TryGetByConnection(connection, out var user) || user is null) return;
        LotteryOpeningResult? result = null;
        byte error = 0;
        try
        {
            result = await commands.SendAsync(new LotteryOpeningCommand(user.OnlineUserId, session, slot, mode), token).ConfigureAwait(false);
        }
        catch (OpeningRejectedException ex) when (!Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(ex)) { error = ex.Reason switch { OpeningRejection.PremiumUnavailable => 0x73, OpeningRejection.NoRoom => 0x7E, _ => (byte)0 }; logger.LogInformation("Lottery rejected character={Character} slot={Slot}: {Reason}", session.CharacterId, slot, ex.Message); }
        catch (Exception ex) when (ex is not OperationCanceledException && !Dfo.Server.Abstractions.Transactions.TransactionOutcomeUncertainException.Contains(ex)) { logger.LogError(ex, "Lottery transaction failed character={Character}", session.CharacterId); }
        var response = new LotteryOpeningResponse(result, error);
        logger.LogInformation("Lottery character={Character} mode={Mode} seq={Sequence} slot={Slot} committed={Committed} reward={Reward} quantity={Quantity} error={Error}",
            session.CharacterId, mode, sequence, slot, result is not null, result?.Receipt.Rewards[0].ItemId, result?.Receipt.Rewards.Sum(x => x.Count), error);
        if (result?.Receipt is { } receipt)
        {
            // State was committed by the command; native result precedes absolute inventory updates.
            IReadOnlyList<IGameCmdMessage> replies = [response];
            IReadOnlyList<IServerNetworkMessage> following = [];
            if (mode == 1)
            {
                var snapshot = await commands.SendAsync(new ReadPremiumSnapshotCommand(session), token).ConfigureAwait(false);
                following = [new PremiumServiceStateMessage(snapshot.Entitlements, snapshot.Usage, snapshot.PreparedAt)];
            }
            await notifications.ProjectAsync(new NotificationAudience.TheseCharacters([session.CharacterId]), new OpeningCompleted(receipt, replies, result.GoldAfter, following), token).ConfigureAwait(false);
        }
        else await sender.SendCmdAsync(connection, response, token).ConfigureAwait(false);
    }
}
public sealed class LotteryOpeningHandler(LotteryOpeningDelivery delivery) : ClientMessageHandler<LotteryOpeningRequest>
{
    protected override ValueTask HandleMessageAsync(NetworkDispatchContext context, LotteryOpeningRequest message, CancellationToken token) =>
        delivery.HandleAsync(context, message.Slot, message.Mode, message.Metadata.Sequence, token);
}
