using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Data.Networking;
using Team = Dfo.Server.Abstractions.Pvp.PvpTotalMatchTeam;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal sealed record CheckPvpTotalMatchTeamNameRequest(GameMessageMetadata Metadata, byte[] Name) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCheckPvpTotalMatchTeamName;
}

internal sealed record SetPvpTotalMatchTeamRequest(GameMessageMetadata Metadata, byte[] Name, ImmutableArray<byte> Positions)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSetPvpTotalMatchTeam;
}

internal sealed record PvpTotalMatchTeamInfo(PvpTotalMatchTeamView Team) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpTotalMatchTeamInfo;
}

internal sealed record PvpTotalMatchTeamReply(ServerMessageType MessageType, ImmutableArray<byte> Body) : IGameCmdMessage;

internal static class PvpTotalMatchTeamPayloadCodec
{
    public static (byte[] Name, ImmutableArray<byte> Positions) Decode(ReadOnlySpan<byte> body, int positionCount)
    {
        if (body.Length < sizeof(int)) return ([], []);
        var size = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (size is < 1 or > Team.NameByteLimit || body.Length != sizeof(int) + size + positionCount)
            return ([], []);
        return (body.Slice(sizeof(int), size).ToArray(), [.. body[(sizeof(int) + size)..]]);
    }

    public static byte[] Encode(PvpTotalMatchTeamView team)
    {
        var name = Encoding.UTF8.GetBytes(team.Name);
        var result = new byte[sizeof(int) + name.Length + Team.MemberCount];
        BinaryPrimitives.WriteInt32LittleEndian(result, name.Length);
        name.CopyTo(result, sizeof(int));
        team.Positions.AsSpan().CopyTo(result.AsSpan(sizeof(int) + name.Length));
        return result;
    }
}

internal sealed class CheckPvpTotalMatchTeamNameDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new CheckPvpTotalMatchTeamNameRequest(metadata, PvpTotalMatchTeamPayloadCodec.Decode(body, positionCount: 0).Name);
}

internal sealed class SetPvpTotalMatchTeamDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var (name, positions) = PvpTotalMatchTeamPayloadCodec.Decode(body, Team.MemberCount);
        return new SetPvpTotalMatchTeamRequest(metadata, name, positions);
    }
}

internal sealed class PvpTotalMatchTeamInfoEncoder : GameNotiMessageEncoder<PvpTotalMatchTeamInfo>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchTeamInfo message) =>
        OnePayload(message, PvpTotalMatchTeamPayloadCodec.Encode(message.Team));
}

internal sealed class PvpTotalMatchTeamReplyEncoder : GameCmdMessageEncoder<PvpTotalMatchTeamReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchTeamReply message) =>
        OnePayload(message, message.Body.ToArray());
}
