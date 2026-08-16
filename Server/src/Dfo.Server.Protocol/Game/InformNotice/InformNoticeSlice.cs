using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.InformNotice;

/// <summary>
/// NOTI 0x01A1, the legacy <c>INFORM_NOTICE</c>, sent last in the login batch.
/// </summary>
/// <remarks>
/// Outbound only. The reference server sends a single zero byte, which is what this sends; the
/// body's meaning beyond "nothing to inform" is unknown, and no attempt is made to guess at a
/// richer encoding from one constant.
///
/// This is very likely the packet behind the in-game notice window, but that link is inferred from
/// the name and the timing rather than observed, so it is recorded here and not asserted.
/// </remarks>
public sealed record InformNoticeNotice(byte State) : IGameNotiMessage
{
    /// <summary>The reference server's login-time value: nothing to inform.</summary>
    public static InformNoticeNotice Empty { get; } = new(0x00);

    public ServerMessageType MessageType => ServerMessageType.GameInformNotice;
}

internal sealed class InformNoticeNoticeEncoder : GameNotiMessageEncoder<InformNoticeNotice>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        InformNoticeNotice message) =>
        OnePayload(GameProtocol.InformNoticeNotificationType, new[] { message.State });
}
