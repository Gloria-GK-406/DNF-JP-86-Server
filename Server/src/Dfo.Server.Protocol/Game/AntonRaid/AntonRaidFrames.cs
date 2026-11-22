using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Raids;

namespace Dfo.Server.Protocol.Game.AntonRaid;

internal sealed record AntonRaidRequest(
    GameMessageMetadata Metadata, ClientMessageType MessageType,
    uint Value = 0, uint Target = 0, byte[]? Title = null, byte Channel = 0, uint Operation = 0,
    IReadOnlyList<uint>? Values = null)
    : IClientNetworkMessage;

internal sealed class AntonRaidRequestDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var kind = (ClientMessageType)((metadata.Selector << 16) | metadata.Type);
        return kind switch
        {
            ClientMessageType.GameCreateAntonRaid when body.Length is >= 5 and <= 260 && body[0] == 0
                && BinaryPrimitives.ReadInt32LittleEndian(body[1..]) == body.Length - 5 =>
                new AntonRaidRequest(metadata, kind, Title: body[5..].ToArray()),
            ClientMessageType.GameLeaveAntonRaid when body.Length == 2 =>
                new AntonRaidRequest(metadata, kind, Target: BinaryPrimitives.ReadUInt16LittleEndian(body)),
            ClientMessageType.GameStartAntonRaid when body.IsEmpty => new AntonRaidRequest(metadata, kind),
            ClientMessageType.GameRejoinAntonRaid when body.Length == 4 =>
                new AntonRaidRequest(metadata, kind, BinaryPrimitives.ReadUInt32LittleEndian(body)),
            ClientMessageType.GameRenameAntonRaid when body.Length is >= 5 and <= 260 && body[0] == 0
                && BinaryPrimitives.ReadInt32LittleEndian(body[1..]) == body.Length - 5 =>
                new AntonRaidRequest(metadata, kind, Title: body[5..].ToArray()),
            ClientMessageType.GameRequestAntonRaidTickets when body.Length == 1 && body[0] <= 1 =>
                new AntonRaidRequest(metadata, kind, body[0]),
            ClientMessageType.GameSetAntonRaidSymbol when body.Length == 9 && body[8] <= 2 =>
                new AntonRaidRequest(metadata, kind, BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(body), Operation: body[8]),
            ClientMessageType.GameActivateAntonRaidBuff when body.Length is >= 3 and <= 11 && body.Length % 2 == 1
                && body[0] <= 4 =>
                new AntonRaidRequest(metadata, kind, body[0], BinaryPrimitives.ReadUInt16LittleEndian(body[1..]),
                    Values: ReadTargets(body[3..])),
            ClientMessageType.GameReportAntonRaidMonster when !body.IsEmpty && body[0] <= 5 && body.Length == 1 + 4 * body[0] =>
                new AntonRaidRequest(metadata, kind, Values: ReadHitPoints(body[1..])),
            ClientMessageType.GameManageAntonRaid when body.Length == 12 =>
                new AntonRaidRequest(metadata, kind, BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(body[4..]), Operation: BinaryPrimitives.ReadUInt32LittleEndian(body)),
            ClientMessageType.GameCheckAntonRaidUser when body.Length == 3 =>
                new AntonRaidRequest(metadata, kind, Target: BinaryPrimitives.ReadUInt16LittleEndian(body[1..]), Channel: body[0]),
            ClientMessageType.GameRequestAntonRaidMembers when body.Length == 4 =>
                new AntonRaidRequest(metadata, kind, BinaryPrimitives.ReadUInt32LittleEndian(body)),
            ClientMessageType.GameRequestAntonRaidDirectory when body.Length == 1 =>
                new AntonRaidRequest(metadata, kind, body[0]),
            _ => null,
        };
    }

    private static uint[] ReadTargets(ReadOnlySpan<byte> body)
    {
        var values = new uint[body.Length / 2];
        for (var index = 0; index < values.Length; index++)
            values[index] = BinaryPrimitives.ReadUInt16LittleEndian(body[(index * 2)..]);
        return values;
    }

    private static uint[] ReadHitPoints(ReadOnlySpan<byte> body)
    {
        var values = new uint[body.Length / 4];
        for (var index = 0; index < values.Length; index++)
            values[index] = BinaryPrimitives.ReadUInt32LittleEndian(body[(index * 4)..]);
        return values;
    }
}

internal sealed record AntonRaidReply(ServerMessageType MessageType, bool Accepted, uint Value = 0, byte Error = 1)
    : IGameCmdMessage;

internal sealed class AntonRaidReplyEncoder : GameCmdMessageEncoder<AntonRaidReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidReply message)
    {
        byte[] body;
        if (!message.Accepted)
        {
            body = [0, message.Error];
        }
        else if (message.MessageType is ServerMessageType.GameCreateAntonRaidReply or ServerMessageType.GameCheckAntonRaidUserReply)
        {
            body = new byte[5];
            body[0] = 1;
            BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(1), message.Value);
        }
        else
        {
            body = message.MessageType is ServerMessageType.GameLeaveAntonRaidReply
                ? [1, checked((byte)message.Value)] : [1];
        }
        return OnePayload(message, body);
    }
}

internal sealed record AntonRaidTownLoaded : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAntonRaidTownLoaded;
}

internal sealed record AntonRaidDirectoryNotice(IReadOnlyList<AntonRaidRecruitmentSnapshot> Raids) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAntonRaidDirectory;
}

internal sealed record AntonRaidChangedNotice(uint Number, AntonRaidRecruitmentSnapshot? Raid) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameAntonRaidChanged;
}

internal sealed record AntonRaidApprovalNotice(ushort Sender, int PeerValue) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePartyInviteRequest;
}

internal sealed class AntonRaidTownLoadedEncoder : GameNotiMessageEncoder<AntonRaidTownLoaded>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidTownLoaded message) =>
        OnePayload(message, ReadOnlyMemory<byte>.Empty);
}

internal sealed class AntonRaidDirectoryEncoder : GameNotiMessageEncoder<AntonRaidDirectoryNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidDirectoryNotice message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Raids.Count);
        foreach (var raid in message.Raids)
        {
            AntonRaidFrameFields.WriteRaid(writer, raid);
            writer.Write(checked((byte)raid.Members.Count));
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed class AntonRaidChangedEncoder : GameNotiMessageEncoder<AntonRaidChangedNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidChangedNotice message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(message.Number);
        if (message.Raid is { } raid)
        {
            writer.Write(0u);
            AntonRaidFrameFields.WriteRaid(writer, raid);
            writer.Write(checked((byte)raid.Members.Count));
            foreach (var member in raid.Members)
                AntonRaidFrameFields.WriteMember(writer, member);
        }
        else
        {
            writer.Write(1u);
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed class AntonRaidApprovalEncoder : GameNotiMessageEncoder<AntonRaidApprovalNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(AntonRaidApprovalNotice message)
    {
        var body = new byte[13];
        BinaryPrimitives.WriteUInt16LittleEndian(body, message.Sender);
        body[2] = 10;
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(3), message.PeerValue);
        return OnePayload(message, body);
    }
}

internal static class AntonRaidFrameFields
{
    internal static void WriteRaid(BinaryWriter writer, AntonRaidRecruitmentSnapshot raid)
    {
        writer.Write(raid.Number);
        writer.Write(raid.Title.Length);
        writer.Write(raid.Title.Span);
        writer.Write((byte)0);
        writer.Write(raid.Stage switch
        {
            AntonRaidStage.Preparing => raid.PreparingFromIntermission ? (byte)5 : (byte)0,
            AntonRaidStage.Failed => (byte)4,
            _ => (byte)raid.Stage,
        });
        writer.Write(raid.Stage == AntonRaidStage.Attacking ? raid.Phase : raid.Stage == AntonRaidStage.Failed ? (byte)1 : (byte)0);
        writer.Write(0u);
        WriteMember(writer, raid.Leader);
    }

    internal static void WriteMember(BinaryWriter writer, AntonRaidMember member)
    {
        writer.Write(member.DisconnectedAt.HasValue ? (ushort)0 : member.Session.WireUserId);
        writer.Write((byte)1);
        writer.Write(member.Name.Length);
        writer.Write(member.Name.Span);
        writer.Write(member.Job);
        writer.Write(member.GrowthType);
        writer.Write(member.Squad);
        writer.Write((byte)0);
        writer.Write(checked((uint)(member.DisconnectedAt?.ToUnixTimeSeconds() ?? member.Session.CharacterId.Value)));
        writer.Write((byte)0);
        writer.Write((byte)0);
    }
}
