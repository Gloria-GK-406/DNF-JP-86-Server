using System.Buffers.Binary;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.MarriageProposal;

internal sealed record WeddingInfoMessage(MarriageInfoSnapshot? Snapshot) : GameStateSynchronizationMessage, IGameNotiMessage
{
    internal WeddingInfoMessage(MarriageEngagementSnapshot snapshot) : this(MarriageInfoSnapshot.From(snapshot)) { }
    internal override ushort Type => GameProtocol.WeddingInfoType;
    internal override byte[] EncodeBody() => MarriageEngagementPayload.InfoState(Snapshot);
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
        => InfoState(MarriageInfoSnapshot.From(snapshot));

    public static byte[] InfoState(MarriageInfoSnapshot? snapshot)
    {
        var result = new byte[15];
        if (snapshot is null) return result;
        BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)snapshot.PartnerId));
        result[4] = WireState(snapshot.Stage);
        // Native raw[14] reaches marriage mgr+0x70 / actor+0x386. raw[5] is a separate unknown byte.
        result[14] = snapshot.RingLevel;
        if (snapshot.MarriedAt is { } marriedAt)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(6), checked((uint)marriedAt.ToUnixTimeSeconds()));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(10), checked((uint)snapshot.LovePoints));
        return result;
    }

    /// <summary>The 03D0 state byte for a relation stage; no relation projects as 0, the same as a closed one.</summary>
    /// <remarks>Native NPC dispatcher blocks another reservation only at state 2 (RVA021AB558); 4 is married.</remarks>
    public static byte WireState(MarriageStage? stage) => stage switch
    {
        null or MarriageStage.Proposed or MarriageStage.Closed => 0,
        MarriageStage.Engaged or MarriageStage.Requested => 1,
        MarriageStage.Preparing => 2,
        MarriageStage.Married => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "No 03D0 state is defined for this stage."),
    };

    public static byte[] Partner(MarriagePartnerProfile? profile)
    {
        if (profile is null) return [0, 1];
        if (profile.Equipment.Count != 13)
            throw new InvalidDataException("Wedding partner profile exceeds the native fixed fields.");
        var result = new byte[98];
        result[0] = 1;
        // The handler reads 97 bytes after the result envelope: an unused DWORD, id, another unused DWORD,
        // name[30], level, job, encoded grow type, and 13 equipment template IDs.
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(5), checked((uint)profile.CharacterId));
        var nameBytes = Math.Min(profile.Name.Length, 29);
        // Character names use the existing UTF-8 wire representation; keep a complete prefix
        // and a terminal zero in the fixed C string instead of splitting a multi-byte character.
        if (nameBytes < profile.Name.Length)
            while (nameBytes > 0 && (profile.Name[nameBytes] & 0xC0) == 0x80) nameBytes--;
        profile.Name.AsSpan(0, nameBytes).CopyTo(result.AsSpan(13, 30));
        // 00892D20 stores raw42 in encrypted level (+14); 00892DCE stores raw43 in job (+4).
        result[43] = checked((byte)profile.Level);
        result[44] = profile.Job;
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
        OnePayload(GameProtocol.WeddingInfoType, MarriageEngagementPayload.InfoState(message.Snapshot));
}

internal sealed class WeddingPartnerEncoder : GameCmdMessageEncoder<WeddingPartnerAck>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(WeddingPartnerAck message) =>
        OnePayload(GameProtocol.WeddingPartnerType, MarriageEngagementPayload.Partner(message.Partner));
}

internal sealed class WeddingPartnerHandler(ISelectedSessionDomainService sessions, IMarriageQueryDomainService marriage,
    IGameMessageSender sender, ICommandBus commands) : ClientMessageHandler<WeddingPartnerQuery>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, WeddingPartnerQuery message, CancellationToken ct)
    {
        var session = sessions.FindByConnection(context.Connection.ConnectionId);
        var snapshot = session is null || !sessions.IsInWorld(session) ? MarriageEngagementSnapshot.Empty
            : await marriage.SnapshotAsync(session.AccountId, session.CharacterId, ct).ConfigureAwait(false);
        var peer = snapshot.Partner;
        if (peer is not null && message.Target != 0 && message.Target != peer.CharacterId) peer = null;
        var messages = new List<IGameServerMessage> { new WeddingPartnerAck(peer) };
        if (peer is not null && snapshot.IsCeremonyReserved && session is not null)
        {
            var prepared = await commands.SendAsync(new PrepareMarriageChurchCommand(session), ct).ConfigureAwait(false);
            if (prepared.Snapshot is { } church)
            {
                messages.Add(new WeddingChurchPreparationNotification(church, await marriage.RemainingInvitationsAsync(church.Room.CeremonyId, session.CharacterId, ct).ConfigureAwait(false)));
                messages.Add(new WeddingInvitedGuestsNotification(await marriage.InvitedAsync(church.Room.CeremonyId, ct).ConfigureAwait(false)));
            }
        }
        if (session is not null && await marriage.AppearanceAsync(session, ct).ConfigureAwait(false) is { } appearance)
            messages.Add(new WeddingAvatarNotification(appearance));
        await sender.SendExplicitAsync(context.Connection.ConnectionId, new(messages, []), ct).ConfigureAwait(false);
    }
}
