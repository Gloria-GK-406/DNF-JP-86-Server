using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Game.Party;

/// <summary>
/// The reason bytes A21's party CMD handlers branch on, and which reason each party command answers
/// a refusal with. Every refusal body is <c>00 reason</c>; REQUEST_PEER and RESPONSE_PEER also read
/// the request type after the reason (A21 RVA 0xD20C5E, 0xD2173E), so theirs is
/// <c>00 reason requestType</c>: a two-byte body would make the client read past it.
/// </summary>
/// <remarks>
/// Client texts (static, A21 dstr): REQUEST_PEER and RESPONSE_PEER show 3 "the other player has left
/// the game", 4 "the party is full", 0x12 "the other player is already in a party" and 0x13 "the
/// other player cannot join"; REQUEST_PEER shows 8 as "parties can be formed only in town".
/// SET_PARTY_INFO 8, LEAVE_PARTY 0x12/0x13 and WALKOUT 4/8/0x13 reach only the client log, and
/// CHANGE_HOST shows text only for 0x5F, which no party rule here produces. 70 answers every refusal
/// of these commands to the caller (DisPatcher_ReqPeer, DisPatcher_ResPeer, DisPatcher_SetPartyInfo,
/// DisPatcher_LeaveParty, DisPatcher_WalkoutParty, Dispatcher_ChangeHost).
/// </remarks>
internal static class PartyRefusalCode
{
    public const byte PeerLeftGame = 0x03;
    public const byte PartyFullOrEmptySlot = 0x04;
    public const byte TownOnlyOrNoAuthority = 0x08;
    public const byte AlreadyInParty = 0x12;
    public const byte Refused = 0x13;

    public static byte ForInvite(PartyFailureReason failure) => failure switch
    {
        PartyFailureReason.TargetNotFound => PeerLeftGame,
        PartyFailureReason.PartyFull => PartyFullOrEmptySlot,
        PartyFailureReason.TargetAlreadyInParty => AlreadyInParty,
        PartyFailureReason.NotStandingInTown or PartyFailureReason.NotInSameGameChannel
            or PartyFailureReason.NotInSameTownArea => TownOnlyOrNoAuthority,
        _ => Refused,
    };

    public static byte ForAcceptance(PartyFailureReason failure) => failure switch
    {
        PartyFailureReason.TargetNotFound => PeerLeftGame,
        PartyFailureReason.PartyFull => PartyFullOrEmptySlot,
        PartyFailureReason.TargetAlreadyInParty => AlreadyInParty,
        _ => Refused,
    };

    public static byte ForSettings(PartyFailureReason failure) =>
        failure == PartyFailureReason.NotPartyLeader ? TownOnlyOrNoAuthority : Refused;

    public static byte ForLeave(PartyFailureReason failure) =>
        failure == PartyFailureReason.NotInParty ? AlreadyInParty : Refused;

    public static byte ForRemoval(PartyFailureReason failure) => failure switch
    {
        PartyFailureReason.TargetNotFound => PartyFullOrEmptySlot,
        PartyFailureReason.NotPartyLeader => TownOnlyOrNoAuthority,
        _ => Refused,
    };
}

public sealed record RequestPeerRefusal(byte Reason, byte RequestType) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRequestPeerRefusal;
}

public sealed record ResponsePeerRefusal(byte Reason, byte RequestType) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameResponsePeerRefusal;
}

public sealed record SetPartyInfoRefusal(byte Reason) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameSetPartyInfoRefusal;
}

public sealed record LeavePartyRefusal(byte Reason) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameLeavePartyRefusal;
}

public sealed record RemovePartyMemberRefusal(byte Reason = PartyRefusalCode.Refused) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameRemovePartyMemberRefusal;
}

public sealed record ChangePartyLeaderRefusal(byte Reason = PartyRefusalCode.Refused) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameChangePartyLeaderRefusal;
}

internal sealed class RequestPeerRefusalEncoder : GameCmdMessageEncoder<RequestPeerRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RequestPeerRefusal message) =>
        OnePayload(message, new byte[] { 0x00, message.Reason, message.RequestType });
}

internal sealed class ResponsePeerRefusalEncoder : GameCmdMessageEncoder<ResponsePeerRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ResponsePeerRefusal message) =>
        OnePayload(message, new byte[] { 0x00, message.Reason, message.RequestType });
}

internal sealed class SetPartyInfoRefusalEncoder : GameCmdMessageEncoder<SetPartyInfoRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(SetPartyInfoRefusal message) =>
        OnePayload(message, new byte[] { 0x00, message.Reason });
}

internal sealed class LeavePartyRefusalEncoder : GameCmdMessageEncoder<LeavePartyRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(LeavePartyRefusal message) =>
        OnePayload(message, new byte[] { 0x00, message.Reason });
}

internal sealed class RemovePartyMemberRefusalEncoder : GameCmdMessageEncoder<RemovePartyMemberRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(RemovePartyMemberRefusal message) =>
        OnePayload(message, EncodeBody(message.Reason));

    internal static byte[] EncodeBody(byte reason = PartyRefusalCode.Refused) => [0x00, reason];
}

internal sealed class ChangePartyLeaderRefusalEncoder : GameCmdMessageEncoder<ChangePartyLeaderRefusal>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(ChangePartyLeaderRefusal message) =>
        OnePayload(message, new byte[] { 0x00, message.Reason });
}
