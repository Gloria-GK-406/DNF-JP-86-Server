using System.Buffers.Binary;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.ContentsPlayInfo;

/// <summary>A21 roster hover query. The byte indexes the account roster, not a persistent slot.</summary>
public sealed record ContentsPlayInfoRequest(
    GameMessageMetadata Metadata,
    byte RosterIndex) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameContentsPlayInfo;
}

public sealed record ContentsPlayInfoState(DespairTowerStatus Despair, DimensionQuotaSnapshot Dimension,
    MirrorDailyQuota Mirror, DarkElfRuinsSelection Ruins);

/// <summary>A null state is a refused query, not a fabricated all-zero character.</summary>
public sealed record ContentsPlayInfoResponse(byte RosterIndex, ContentsPlayInfoState? State) : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameContentsPlayInfo;
}

internal static class ContentsPlayInfoPayloadCodec
{
    public const int BodyLength = 1;

    public static ContentsPlayInfoRequest Decode(
        GameMessageMetadata metadata,
        ReadOnlySpan<byte> body)
    {
        if (body.Length != BodyLength)
        {
            throw new InvalidDataException(
                $"CONTENTS_PLAY_INFO requires exactly {BodyLength} body byte; "
                    + $"received '{body.Length}'.");
        }

        return new ContentsPlayInfoRequest(metadata, body[0]);
    }

    // A21 VA 0110D580: index, mode, then six i32 values, after the success byte.
    public const int ResponsePayloadLength = 26;

    public static byte[] EncodeResponse(ContentsPlayInfoResponse response)
    {
        if (response.State is not { } state) return GameCmdEnvelope.Refuse(0);
        var payload = new byte[ResponsePayloadLength];
        payload[0] = response.RosterIndex;
        payload[1] = 0; // Full record; modes 1/2 replace subsets with client-side defaults.
        // The ordinary-character client displays 1-used and (progress%100)+1. Project the
        // domain's available free attempt and next floor, including a finished tower cycle.
        Write(2, 1 - state.Despair.FreeEntriesRemaining);
        Write(6, state.Despair.NextFloor - 1);
        Write(10, state.Dimension.Remaining);
        Write(14, state.Dimension.ExtraRemaining);
        Write(18, state.Mirror.Entries);
        Write(22, state.Ruins.Remaining);
        return GameCmdEnvelope.Accept(payload);

        void Write(int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), value);
    }

}

internal sealed class ContentsPlayInfoMessageDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        ContentsPlayInfoPayloadCodec.Decode(metadata, body);
}

public sealed class ContentsPlayInfoHandler(ICommandBus commands)
    : ClientMessageHandler<ContentsPlayInfoRequest>, IProtocolCommandMessageHandler<GameContentsPlayInfoCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        ContentsPlayInfoRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameContentsPlayInfoCommand(context, message), cancellationToken).ConfigureAwait(false);
}

internal sealed class ContentsPlayInfoResponseEncoder
    : GameCmdMessageEncoder<ContentsPlayInfoResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(
        ContentsPlayInfoResponse message) =>
        OnePayload(message,
            ContentsPlayInfoPayloadCodec.EncodeResponse(message));
}
