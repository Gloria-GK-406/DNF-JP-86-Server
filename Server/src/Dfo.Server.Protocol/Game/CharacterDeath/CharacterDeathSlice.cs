using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

public sealed record CharacterDeathRequest(
    GameMessageMetadata Metadata, ushort First, ushort Second, ushort? Third) : IClientNetworkMessage
{
    public ClientMessageType MessageType => ClientMessageType.GameCharacterDeath;
}

internal sealed class CharacterDeathDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        // A21 has two native builders: two words, or three words. Field meanings remain unproven.
        if (body.Length is not (4 or 6))
            throw new InvalidDataException($"DIE_CHARACTER requires 4 or 6 bytes; received {body.Length}.");
        return new CharacterDeathRequest(metadata,
            BinaryPrimitives.ReadUInt16LittleEndian(body),
            BinaryPrimitives.ReadUInt16LittleEndian(body[2..]),
            body.Length == 6 ? BinaryPrimitives.ReadUInt16LittleEndian(body[4..]) : null);
    }
}

internal sealed record CharacterDeathResponse : IGameCmdMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterDeath;
}

internal sealed class CharacterDeathResponseEncoder : GameCmdMessageEncoder<CharacterDeathResponse>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterDeathResponse message)
        => OnePayload(GameProtocol.CharacterDeathType, new byte[] { 0x01 });
}

internal sealed record CharacterLifeStateNotification(ushort ActorId, byte State = 0) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameCharacterLifeState;
}

internal sealed class CharacterLifeStateEncoder : GameNotiMessageEncoder<CharacterLifeStateNotification>
{
    internal static byte[] EncodeDeath(ushort actorId)
    {
        byte[] body = new byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(body, actorId);
        // A21 NOTI 0020: actor, state 0 (dead), trailing byte 0. No countdown duration field.
        return body;
    }

    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(CharacterLifeStateNotification message)
    {
        var body = EncodeDeath(message.ActorId);
        body[2] = message.State;
        return OnePayload(GameProtocol.CharacterLifeStateNotificationType, body);
    }
}

public sealed record MarkDungeonParticipantDeadCommand(
    CharacterId CharacterId, DungeonRunId RunId, long RoomVersion) : ICommand<MarkDungeonParticipantDeadResult>;

public sealed record MarkDungeonParticipantDeadResult(DungeonRunState? Run);

public sealed class MarkDungeonParticipantDeadCommandHandler(IDungeonRunDomainService runs)
    : ICommandHandler<MarkDungeonParticipantDeadCommand, MarkDungeonParticipantDeadResult>
{
    public ValueTask<MarkDungeonParticipantDeadResult> HandleAsync(
        CommandExecutionContext context, MarkDungeonParticipantDeadCommand command,
        CancellationToken cancellationToken) => ValueTask.FromResult(new MarkDungeonParticipantDeadResult(
            runs.TryMarkParticipantDead(command.CharacterId, command.RunId, command.RoomVersion)));
}

public sealed class CharacterDeathHandler(
    ISelectedSessionDomainService selectedSessions,
    IDungeonRunDomainService runs,
    DungeonRunDeliveryRegistry deliveryRegistry,
    SelectedSessionTransitionGate transitionGate,
    DungeonProjectionGate projectionGate,
    ICommandBus commands,
    IGameMessageSender sender,
    ILogger<CharacterDeathHandler> logger,
    IllusionTowerProjectionDelivery? illusionTower = null, BossTowerProjectionDelivery? bossTower = null) : ClientMessageHandler<CharacterDeathRequest>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, CharacterDeathRequest message, CancellationToken cancellationToken)
    {
        var connectionId = context.Connection.ConnectionId;
        var session = selectedSessions.FindByConnection(connectionId);
        if (session is null) return;
        if (bossTower is not null && await bossTower.DieAsync(connectionId, message.Metadata, cancellationToken).ConfigureAwait(false)) return;
        if (illusionTower is not null
            && await illusionTower.DieAsync(connectionId, message.Metadata, cancellationToken).ConfigureAwait(false))
            return;
        var expected = runs.Find(session.CharacterId);
        if (expected is null) return;

        // Same lock order as dungeon entry/loading: projection, then selected-session transition.
        await using var projection = await projectionGate.AcquireAsync(
            expected.RunId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        await using var transition = await transitionGate.AcquireAsync(
            session.AccountId, UserAccessMode.Exclusive, cancellationToken).ConfigureAwait(false);
        if (selectedSessions.FindByConnection(connectionId) != session
            || selectedSessions.FindByCharacter(session.CharacterId) != session
            || deliveryRegistry.Find(expected.RunId).SingleOrDefault(x => x.CharacterId == session.CharacterId) != session)
            return;

        var result = await commands.SendAsync(new MarkDungeonParticipantDeadCommand(
            session.CharacterId, expected.RunId, expected.RoomVersion), cancellationToken).ConfigureAwait(false);
        if (result.Run is not { } committed) return;

        var notification = new CharacterLifeStateNotification(session.WireUserId);
        var secondary = deliveryRegistry.Find(committed.RunId)
            .Where(x => x.CharacterId != session.CharacterId)
            .Where(x => committed.Participants.Any(p => p.CharacterId == x.CharacterId))
            .Where(x => selectedSessions.FindByCharacter(x.CharacterId) == x)
            .Where(x => runs.Find(x.CharacterId)?.RunId == committed.RunId)
            .Select(x => new GameExplicitMessageTarget(x.ConnectionId, [notification])).ToArray();
        await sender.SendExplicitAsync(connectionId,
            new GameExplicitMessageDelivery([new CharacterDeathResponse(), notification], secondary), cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Character death user={WireUserId} character={CharacterId} run={RunId}: CMD 0028 ACK + NOTI 0020 sent; dead={DeadCount}/{MemberCount}, report=({First},{Second},{Third})",
            session.WireUserId, session.CharacterId, committed.RunId,
            committed.DeadParticipants.Count, committed.Participants.Count, message.First, message.Second, message.Third);
    }
}
