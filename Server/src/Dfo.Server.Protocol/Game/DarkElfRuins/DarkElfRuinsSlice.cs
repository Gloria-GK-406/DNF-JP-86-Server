using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.DarkElfRuins;

public sealed record RuinsPlayResultRequest(GameMessageMetadata Metadata, byte[] Report) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameRuinsPlayResult; }
public sealed record RuinsRewardRequest(GameMessageMetadata Metadata) : IClientNetworkMessage
{ public ClientMessageType MessageType => ClientMessageType.GameRuinsReward; }

internal sealed class RuinsPlayResultDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // A21 VA 01941DB2..01941E90: u8 count, up to eight u16/u32 rows, final u32.
        if (body.Length < 5 || body[0] > 8 || body.Length != 5 + body[0] * 6)
            throw new InvalidDataException("Ruins play result requires 5 + count*6 bytes (count <= 8).");
        return new RuinsPlayResultRequest(metadata, body.ToArray());
    }
}
internal sealed class RuinsRewardDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) => body.IsEmpty
        ? new RuinsRewardRequest(metadata) : throw new InvalidDataException("Ruins reward request has no body.");
}
internal sealed class RuinsPlayResultHandler(ICommandBus commands) : ClientMessageHandler<RuinsPlayResultRequest>,
    IProtocolCommandMessageHandler<GameRuinsPlayResultCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, RuinsPlayResultRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRuinsPlayResultCommand(context, message), ct).ConfigureAwait(false);
}
internal sealed class RuinsRewardHandler(ICommandBus commands) : ClientMessageHandler<RuinsRewardRequest>,
    IProtocolCommandMessageHandler<GameRuinsRewardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, RuinsRewardRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRuinsRewardCommand(context, message), ct).ConfigureAwait(false);
}

internal sealed record RuinsByteNotification(ushort WireType, byte Value) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => WireType;
    internal override byte[] EncodeBody() => [Value];
}
internal sealed record RuinsLicenseNotification(DarkElfRuinsSelection Selection) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x02F7;
    internal override byte[] EncodeBody()
    {
        var body = new byte[2 + Selection.DungeonIds.Count * 12];
        BinaryPrimitives.WriteUInt16LittleEndian(body, checked((ushort)Selection.DungeonIds.Count));
        for (var i = 0; i < Selection.DungeonIds.Count; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(2 + i * 12), Selection.DungeonIds[i]);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(6 + i * 12), Selection.License);
            // A21 condition window matches this dungeon and reads row+8 for its clear counter.
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(10 + i * 12), Selection.Clears);
        }
        return body;
    }
}
internal sealed record RuinsClearNotification(DarkElfRuinsReward Reward) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x02F9;
    internal override byte[] EncodeBody()
    {
        var clear = Reward.Clear.FirstOrDefault() ?? throw new InvalidDataException("Missing ruins clear reward.");
        var body = new byte[21]; body[0] = Reward.GuardianDefeated ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(1), Reward.ElapsedMilliseconds);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(5), Reward.Daily.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(9), Reward.Daily.Count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(13), clear.ItemId);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(17), clear.Count);
        return body;
    }
}
internal sealed record RuinsAcknowledgement(ushort WireType) : GameStateSynchronizationMessage, IGameCmdMessage
{
    internal override ushort Type => WireType;
    internal override byte[] EncodeBody() => GameCmdEnvelope.Accept();
}
internal sealed record RuinsBossNotification(ushort Sequence) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => 0x0073;
    internal override byte[] EncodeBody()
    {
        var body = new byte[] { 1, 1, 0, 0 };
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), Sequence);
        return body;
    }
}

internal static class RuinsProjection
{
    internal static IReadOnlyList<IGameServerMessage> ClearPreamble(DungeonRunState run)
    {
        var boss = run.CurrentCellState.Actors.FirstOrDefault(x => x.IsDead && x.Actor.ActorType == 3);
        return boss is null ? [new Dfo.Server.Protocol.Game.DungeonClear.EnableClearDungeonNotification()]
            : [new RuinsBossNotification(boss.Sequence), new Dfo.Server.Protocol.Game.DungeonClear.EnableClearDungeonNotification()];
    }
    internal static IReadOnlyList<IGameServerMessage> Selection(DarkElfRuinsSelection value) =>
        value.DungeonIds.Count == 0 ? [] : [new RuinsByteNotification(0x02F8, value.DayIndex), new RuinsByteNotification(0x02FB, value.Remaining),
            new RuinsLicenseNotification(value), new RuinsByteNotification(0x02FA, 0)];
    internal static IReadOnlyList<IGameServerMessage> Result(DarkElfRuinsReward reward, DarkElfRuinsSelection value, ushort requestType) =>
        reward.PresentationStage == 1 ? [new RuinsClearNotification(reward)]
            : [new RuinsAcknowledgement(requestType), new RuinsLicenseNotification(value)];
}
internal readonly record struct RuinsResultRead(DarkElfRuinsReward? Reward);
