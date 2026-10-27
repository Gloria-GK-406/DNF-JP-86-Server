using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Settlement;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

/// <summary>The observed 19-byte quest-clear coupon variant of CMD 0x0204.</summary>
public sealed record QuestClearCouponRequest(GameMessageMetadata Metadata, short SourceSlot, byte Container,
    uint Reserved1, uint Action, uint Reserved2, uint Reserved3, bool Complete) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.ItemUsage | (Action == 58 ? ReplayProjectionNeeds.Fatigue : ReplayProjectionNeeds.None);
    public ClientMessageType MessageType => ClientMessageType.GameQuestClearCoupon;
    public bool IsSupported => Complete && SourceSlot >= 0 && Container == 0 && Reserved1 == 0
        && Action == 77 && Reserved2 == 0 && Reserved3 == 0;
    public bool IsFatiguePotion => Complete && SourceSlot >= 0 && Container == 0 && Reserved1 == 0
        && Action == 58 && Reserved2 == 0 && Reserved3 == 0;
}

internal static class QuestClearCouponPayloadCodec
{
    public const int RequestLength = 19;
    public const byte SilentFailure = 2;

    public static QuestClearCouponRequest Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var slot = body.Length >= 2 ? BinaryPrimitives.ReadInt16LittleEndian(body) : (short)0;
        var container = body.Length >= 3 ? body[2] : (byte)0;
        if (body.Length != RequestLength)
            return new(metadata, slot, container, 0, 0, 0, 0, false);
        return new(metadata, slot, container,
            BinaryPrimitives.ReadUInt32LittleEndian(body[3..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[7..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[11..]),
            BinaryPrimitives.ReadUInt32LittleEndian(body[15..]), true);
    }

    public static byte[] Encode(QuestClearCouponResponse response)
    {
        if (!response.Succeeded)
        {
            var failure = new byte[6];
            failure[1] = SilentFailure;
            BinaryPrimitives.WriteInt16LittleEndian(failure.AsSpan(2), response.SourceSlot);
            failure[4] = response.Container;
            return failure;
        }
        var success = new byte[8];
        success[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(success.AsSpan(1), response.SourceSlot);
        success[3] = response.Container;
        BinaryPrimitives.WriteUInt32LittleEndian(success.AsSpan(4), response.Action);
        return success;
    }
}

internal sealed class QuestClearCouponDecoder(ILogger<QuestClearCouponDecoder>? logger = null) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (logger?.IsEnabled(LogLevel.Debug) == true)
            logger.LogDebug("STACKABLE_ACTION capture metadata={Metadata} body({Length}B)={Body}",
                metadata, body.Length, Convert.ToHexString(body));
        return QuestClearCouponPayloadCodec.Decode(metadata, body);
    }
}

public sealed record QuestClearCouponResponse(bool Succeeded, short SourceSlot, byte Container) : IGameCmdMessage
{
    public uint Action { get; init; } = 77;
    public ServerMessageType MessageType => ServerMessageType.GameQuestClearCoupon;
}

internal sealed class QuestClearCouponEncoder : GameCmdMessageEncoder<QuestClearCouponResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(QuestClearCouponResponse message) =>
        OnePayload(GameProtocol.QuestClearCouponType, QuestClearCouponPayloadCodec.Encode(message));
}

internal static class QuestClearCouponProjection
{
    public static IReadOnlyList<IServerNetworkMessage> FromLog(
        IReadOnlyList<ClearedQuest> cleared, IReadOnlyList<CarriedQuest> carried)
    {
        var flags = new byte[30_000];
        foreach (var quest in cleared)
            if ((uint)quest.QuestId < flags.Length) flags[quest.QuestId] = quest.Mark;
        return
        [
            new QuestVisibilityMessage(flags),
            new AcceptedQuestProgressMessage([.. carried.OrderBy(quest => quest.Slot)
                .Select(quest => new AcceptedQuestProgressState(
                    checked((ushort)quest.QuestId), unchecked((uint)quest.TriggerValue)))]),
        ];
    }
}

public sealed class QuestClearCouponHandler(ICommandBus commands) : ClientMessageHandler<QuestClearCouponRequest>, Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameQuestClearCouponCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        QuestClearCouponRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameQuestClearCouponCommand(context, message), ct).ConfigureAwait(false);
}
