using Dfo.Server.Abstractions.Effects;
using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Creatures;

internal sealed record CreatureExperienceNotification(
    byte Level, byte Mode, uint Experience, byte Mode1Extra0 = 0, byte Mode1Extra1 = 0)
    : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureExperience;
}

internal sealed record CreatureStateNotification(uint CreatureKey, uint StateValue) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureState;
}

internal sealed record CreatureDeathNotification(ushort ActorId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureDeath;
}

internal sealed record CreatureRevivalNotification(ushort ActorId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureRevival;
}

internal sealed record CreatureResponseNotification(ushort ActorId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureResponse;
}

internal sealed record CreatureRenameNotification(ushort ActorId, byte[] NameBytes) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureRename;
}

internal sealed record CreatureEvolutionNotification(ushort EvolutionValue, ushort SceneUniqueId) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCreatureEvolution;
}

internal sealed record RenameCreatureRequest(short Slot, byte List, byte[] NameBytes) : IEffectSettlementRequest
{
    public ReplayProjectionNeeds MinimumReplayNeeds => ReplayProjectionNeeds.Inventory | ReplayProjectionNeeds.Pets;
    public ClientMessageType MessageType => ClientMessageType.GameRenameCreature;

    public GameMessageMetadata Metadata { get; init; } = GameMessageMetadata.Empty;
}

internal sealed record HatchCreatureRequest(byte List, short Slot) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameHatchCreature;
    public GameMessageMetadata Metadata { get; init; } = GameMessageMetadata.Empty;
}

internal sealed record HatchCreatureResponse(bool Success, byte ErrorCode = 0) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameHatchCreature;
}

internal sealed record RenameCreatureResponse(short? Slot = null, byte List = 7, byte ErrorCode = 0)
    : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRenameCreature;
}

/// <summary>
/// A21 creature command and notification bodies. Notification type IDs are deliberately
/// kept at the integration boundary until their message registrations are added.
/// </summary>
internal static class CreaturePayloadCodec
{
    internal const ushort DeathNotificationType = 0x0064;
    internal const ushort RenameNotificationType = 0x0065;
    internal const ushort ExperienceNotificationType = 0x0066;
    internal const ushort StateNotificationType = 0x0067;
    internal const ushort ResponseNotificationType = 0x0068;
    internal const ushort EvolutionNotificationType = 0x006A;
    internal const ushort RevivalNotificationType = 0x006B;
    internal const ushort RenameCommandType = 0x0064;
    internal const ushort HatchCommandType = 0x0066;

    public static HatchCreatureRequest DecodeHatchRequest(ReadOnlySpan<byte> body)
    {
        if (body.Length != 3)
            throw new InvalidDataException("Creature hatch request must contain exactly three bytes.");
        return new(body[0], BinaryPrimitives.ReadInt16LittleEndian(body[1..]));
    }

    public static byte[] EncodeHatchResult(HatchCreatureResponse response) =>
        response.Success ? [1] : [0, response.ErrorCode];

    public static byte[] EncodeExperience(CreatureExperienceNotification notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var body = new byte[notice.Mode == 1 ? 8 : 6];
        body[0] = notice.Level;
        body[1] = notice.Mode;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(2), notice.Experience);
        if (notice.Mode == 1)
        {
            body[6] = notice.Mode1Extra0;
            body[7] = notice.Mode1Extra1;
        }

        return body;
    }

    public static byte[] EncodeState(CreatureStateNotification notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var body = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(body, notice.CreatureKey);
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), notice.StateValue);
        return body;
    }

    public static byte[] EncodeDeath(ushort actorId) => EncodeSingleU16(actorId);

    public static byte[] EncodeResponse(ushort actorId) => EncodeSingleU16(actorId);

    public static byte[] EncodeRevival(ushort actorId) => EncodeSingleU16(actorId);

    public static byte[] EncodeEvolution(ushort evolutionValue, ushort sceneUniqueId)
    {
        var body = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, evolutionValue);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), sceneUniqueId);
        return body;
    }

    public static byte[] EncodeRenameNotification(ushort actorId, byte[] nameBytes)
    {
        ArgumentNullException.ThrowIfNull(nameBytes);
        var body = new byte[checked(6 + nameBytes.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(body, actorId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2), nameBytes.Length);
        nameBytes.CopyTo(body, 6);
        return body;
    }

    public static RenameCreatureRequest DecodeRenameRequest(ReadOnlySpan<byte> body)
    {
        if (body.Length < 7)
        {
            throw new InvalidDataException("Creature rename request is shorter than its fixed fields.");
        }

        var slot = BinaryPrimitives.ReadInt16LittleEndian(body);
        var list = body[2];
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(body[3..]);
        if (list != 7 || nameLength < 0 || nameLength != body.Length - 7)
        {
            throw new InvalidDataException("Creature rename request has an invalid list or dstring length.");
        }

        return new RenameCreatureRequest(slot, list, body[7..].ToArray());
    }

    public static byte[] EncodeRenameSuccess(short slot, byte list)
    {
        var body = new byte[4];
        body[0] = 1;
        BinaryPrimitives.WriteInt16LittleEndian(body.AsSpan(1), slot);
        body[3] = list;
        return body;
    }

    public static byte[] EncodeRenameFailure(byte reason) => [0, reason];

    private static byte[] EncodeSingleU16(ushort value)
    {
        var body = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(body, value);
        return body;
    }
}

internal sealed class CreatureExperienceEncoder : GameNotiMessageEncoder<CreatureExperienceNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureExperienceNotification message) =>
        OnePayload(CreaturePayloadCodec.ExperienceNotificationType, CreaturePayloadCodec.EncodeExperience(message));
}

internal sealed class CreatureStateEncoder : GameNotiMessageEncoder<CreatureStateNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureStateNotification message) =>
        OnePayload(CreaturePayloadCodec.StateNotificationType, CreaturePayloadCodec.EncodeState(message));
}

internal sealed class CreatureDeathEncoder : GameNotiMessageEncoder<CreatureDeathNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureDeathNotification message) =>
        OnePayload(CreaturePayloadCodec.DeathNotificationType, CreaturePayloadCodec.EncodeDeath(message.ActorId));
}

internal sealed class CreatureRevivalEncoder : GameNotiMessageEncoder<CreatureRevivalNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureRevivalNotification message) =>
        OnePayload(CreaturePayloadCodec.RevivalNotificationType, CreaturePayloadCodec.EncodeRevival(message.ActorId));
}

internal sealed class CreatureResponseEncoder : GameNotiMessageEncoder<CreatureResponseNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureResponseNotification message) =>
        OnePayload(CreaturePayloadCodec.ResponseNotificationType, CreaturePayloadCodec.EncodeResponse(message.ActorId));
}

internal sealed class CreatureRenameEncoder : GameNotiMessageEncoder<CreatureRenameNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureRenameNotification message) =>
        OnePayload(CreaturePayloadCodec.RenameNotificationType,
            CreaturePayloadCodec.EncodeRenameNotification(message.ActorId, message.NameBytes));
}

internal sealed class CreatureEvolutionEncoder : GameNotiMessageEncoder<CreatureEvolutionNotification>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CreatureEvolutionNotification message) =>
        OnePayload(CreaturePayloadCodec.EvolutionNotificationType,
            CreaturePayloadCodec.EncodeEvolution(message.EvolutionValue, message.SceneUniqueId));
}

internal sealed class RenameCreatureMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CreaturePayloadCodec.DecodeRenameRequest(body) with { Metadata = metadata };
}

internal sealed class RenameCreatureResponseEncoder : GameCmdMessageEncoder<RenameCreatureResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RenameCreatureResponse message) =>
        OnePayload(CreaturePayloadCodec.RenameCommandType, message.Slot is { } slot
            ? CreaturePayloadCodec.EncodeRenameSuccess(slot, message.List)
            : CreaturePayloadCodec.EncodeRenameFailure(message.ErrorCode));
}

internal sealed class HatchCreatureMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        CreaturePayloadCodec.DecodeHatchRequest(body) with { Metadata = metadata };
}

internal sealed class HatchCreatureResponseEncoder : GameCmdMessageEncoder<HatchCreatureResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(HatchCreatureResponse message) =>
        OnePayload(CreaturePayloadCodec.HatchCommandType, CreaturePayloadCodec.EncodeHatchResult(message));
}
