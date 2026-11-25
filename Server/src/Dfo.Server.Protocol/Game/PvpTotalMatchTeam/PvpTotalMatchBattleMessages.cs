using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

internal sealed record SelectPvpTotalMatchCandidateRequest(GameMessageMetadata Metadata, byte Ordinal, bool WellFormed)
    : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameSelectPvpTotalMatchCandidate;
}

internal sealed record GameSelectPvpTotalMatchCandidateCommand(
    NetworkDispatchContext Dispatch, SelectPvpTotalMatchCandidateRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

internal sealed class SelectPvpTotalMatchCandidateDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        body.Length == 1
            ? new SelectPvpTotalMatchCandidateRequest(metadata, body[0], true)
            : new SelectPvpTotalMatchCandidateRequest(metadata, 0, false);
}

internal sealed class SelectPvpTotalMatchCandidateHandler(ICommandBus commands)
    : ClientMessageHandler<SelectPvpTotalMatchCandidateRequest>,
      IProtocolCommandMessageHandler<GameSelectPvpTotalMatchCandidateCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, SelectPvpTotalMatchCandidateRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameSelectPvpTotalMatchCandidateCommand(context, message), cancellationToken)
            .ConfigureAwait(false);
}

internal sealed record PvpTotalMatchParticipant(ushort WireUserId, PvpTotalMatchLineup Lineup);

internal sealed record PvpTotalMatchInfo(ImmutableArray<PvpTotalMatchParticipant> Participants) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpTotalMatchInfo;
}

internal sealed record PvpTotalMatchSelectionStarted : IGameNotiMessage
{
    public static PvpTotalMatchSelectionStarted Instance { get; } = new();
    public ServerMessageType MessageType => ServerMessageType.GamePvpTotalMatchSelectionStarted;
}

internal sealed record PvpTotalMatchFight(
    ushort FirstWireUserId, byte FirstOrdinal, ushort SecondWireUserId, byte SecondOrdinal) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GamePvpTotalMatchFight;
}

internal sealed class PvpTotalMatchInfoEncoder : GameNotiMessageEncoder<PvpTotalMatchInfo>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchInfo message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)message.Participants.Length));
        foreach (var participant in message.Participants)
        {
            writer.Write((uint)participant.WireUserId);
            var teamName = Encoding.UTF8.GetBytes(participant.Lineup.TeamName);
            writer.Write(teamName.Length);
            writer.Write(teamName);
            foreach (var candidate in participant.Lineup.Candidates)
            {
                var identity = candidate.Character.Identity;
                writer.Write(identity.Name.Length);
                writer.Write(identity.Name);
                writer.Write(identity.Job);
                writer.Write(identity.GrowType);
            }
            writer.Write(checked((uint)participant.Lineup.GroupPoints));
        }
        return OnePayload(message, stream.ToArray());
    }
}

internal sealed class PvpTotalMatchSelectionStartedEncoder : GameNotiMessageEncoder<PvpTotalMatchSelectionStarted>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchSelectionStarted message) =>
        OnePayload(message, ReadOnlyMemory<byte>.Empty);
}

internal sealed class PvpTotalMatchFightEncoder : GameNotiMessageEncoder<PvpTotalMatchFight>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(PvpTotalMatchFight message)
    {
        var body = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(body, message.FirstWireUserId);
        body[4] = message.FirstOrdinal;
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(5), message.SecondWireUserId);
        body[9] = message.SecondOrdinal;
        return OnePayload(message, body);
    }
}
