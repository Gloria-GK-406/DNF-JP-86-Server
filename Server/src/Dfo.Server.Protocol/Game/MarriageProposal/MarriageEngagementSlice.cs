using System.Buffers.Binary;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

internal sealed record WeddingInfoMessage(MarriageEngagementSnapshot Snapshot) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal override ushort Type => GameProtocol.WeddingInfoType;
    internal override byte[] EncodeBody() => MarriageEngagementPayload.Info(Snapshot);
}

internal sealed record WeddingPartnerQuery(GameMessageMetadata Metadata, uint Target) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameWeddingPartnerQuery;
}

internal sealed record WeddingPartnerAck(MarriagePartnerProfile? Partner) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameWeddingPartnerQuery;
}

internal sealed class WeddingPartnerDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        if (body.Length != 18) throw new InvalidDataException("WEDDING_CHARAC requires 18 body bytes.");
        return new WeddingPartnerQuery(metadata, BinaryPrimitives.ReadUInt32LittleEndian(body[14..]));
    }
}

internal static class MarriageEngagementPayload
{
    public static byte[] Info(MarriageEngagementSnapshot snapshot)
    {
        var result = new byte[15];
        if (!snapshot.IsEngaged) return result;
        BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)snapshot.Partner!.CharacterId));
        // Native stages 1..3 have the engagement menu; 4 is married. This step has no ceremony.
        result[4] = 1;
        return result;
    }

    public static byte[] Partner(MarriagePartnerProfile? profile)
    {
        if (profile is null) return [0, 1];
        if (profile.Equipment.Count != 13)
            throw new InvalidDataException("Wedding partner profile exceeds the native fixed fields.");
        var result = new byte[98];
        result[0] = 1;
        // The handler reads 97 bytes after the result envelope: an unused DWORD, id, another unused DWORD,
        // name[30], job, level, encoded grow type, and 13 equipment template IDs.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(5), checked((uint)profile.CharacterId));
        var nameBytes = Math.Min(profile.Name.Length, 29);
        // Character names use the existing UTF-8 wire representation; keep a complete prefix
        // and a terminal zero in the fixed C string instead of splitting a multi-byte character.
        if (nameBytes < profile.Name.Length)
            while (nameBytes > 0 && (profile.Name[nameBytes] & 0xC0) == 0x80) nameBytes--;
        profile.Name.AsSpan(0, nameBytes).CopyTo(result.AsSpan(13, 30));
        result[43] = profile.Job;
        result[44] = checked((byte)profile.Level);
        result[45] = profile.GrowType;
        for (var slot = 0; slot < 13; slot++)
        {
            // This native appearance reader skips an absent part only for -1.
            // Inventory's empty 0 would be looked up as an asset and dereferenced even if missing.
            var item = profile.Equipment[slot];
            BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(46 + slot * 4), item > 0 ? item : -1);
        }
        return result;
    }
}

internal sealed class WeddingInfoEncoder : GameNotiMessageEncoder<WeddingInfoMessage>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingInfoMessage message) =>
        OnePayload(GameProtocol.WeddingInfoType, MarriageEngagementPayload.Info(message.Snapshot));
}

internal sealed class WeddingPartnerEncoder : GameCmdMessageEncoder<WeddingPartnerAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingPartnerAck message) =>
        OnePayload(GameProtocol.WeddingPartnerType, MarriageEngagementPayload.Partner(message.Partner));
}

internal sealed class WeddingPartnerHandler(ISelectedSessionDomainService sessions, IMarriageEngagementDomainService marriage,
    IGameMessageSender sender, SelectedSessionTransitionGate gate) : ClientMessageHandler<WeddingPartnerQuery>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingPartnerQuery message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        await WeddingProposalExecution.RunAsync(gate, session, session, async () =>
        {
            var snapshot = session is null || sessions.Find(session.AccountId) != session ? MarriageEngagementSnapshot.Empty
                : await marriage.ReadAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
            var peer = snapshot.Partner;
            if (peer is not null && message.Target != 0 && message.Target != peer.CharacterId) peer = null;
            await sender.SendExplicitAsync(context.Connection.ConnectionId,
                new([new WeddingPartnerAck(peer)], []), ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }
}
