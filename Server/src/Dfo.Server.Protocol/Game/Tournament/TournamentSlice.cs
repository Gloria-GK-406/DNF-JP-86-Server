using Dfo.Server.Application.Dispatching;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Game.Tournament;

internal sealed record TournamentInfoMessage(TournamentPlan Plan) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameTournamentInfo; }
internal sealed record TournamentMapMessage(TournamentPlan Plan) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameTournamentMap; }
internal sealed record TournamentRewardMessage(TournamentDungeonState State) : IGameNotiMessage
{ public ServerMessageType MessageType => ServerMessageType.GameTournamentReward; }
public sealed record TournamentRewardRequest(GameMessageMetadata Metadata, byte? Row = null, byte? Index = null) : IClientNetworkMessage
{ public ClientMessageType MessageType => Row.HasValue ? ClientMessageType.GameTournamentSelectReward : ClientMessageType.GameTournamentRewardStatus; }
internal sealed record TournamentStatusResponse(bool Success, byte Slot, bool SecondRow = true) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameTournamentRewardStatus; }
internal sealed record TournamentSelectionResponse(TournamentDungeonState? State, byte Slot) : IGameCmdMessage
{ public ServerMessageType MessageType => ServerMessageType.GameTournamentSelectReward; }

internal static class TournamentPayloadCodec
{
    public static byte[] Info(TournamentPlan plan)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(plan.Definition.DungeonId); writer.Write(plan.Definition.Difficulty); writer.Write((byte)1);
        foreach (var round in plan.Rounds)
        {
            writer.Write(round.Number); writer.Write(checked((byte)round.Teams.Count));
            foreach (var team in round.Teams)
            { writer.Write(team.Position); writer.Write(team.Code); writer.Write(checked((ushort)team.Strength)); }
        }
        for (var i = 0; i < plan.PathActors.Count; i++)
        {
            var actor = plan.PathActors[i]; writer.Write(checked((byte)(i + 1))); writer.Write(actor.Sequence);
            writer.Write(actor.Code); writer.Write(actor.Level); writer.Write(actor.Type);
        }
        return stream.ToArray();
    }
    public static byte[] Map(TournamentPlan plan)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write((byte)0); writer.Write((byte)0); writer.Write(plan.Seed & 0x7fffffffU);
        writer.Write((byte)0); writer.Write((byte)1); writer.Write(plan.Definition.MapId);
        writer.Write(new byte[3]); return stream.ToArray();
    }
    public static byte[] Reward(TournamentDungeonState state)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(checked((byte)state.CompletedRounds)); writer.Write(state.Plan.Definition.ExperienceByRound[state.CompletedRounds]);
        writer.Write((byte)0);
        foreach (var row in state.Rewards)
        {
            writer.Write(checked((byte)row.Count));
            foreach (var card in row) { writer.Write(card.ItemId); writer.Write(card.Count); writer.Write((ushort)0); }
        }
        return stream.ToArray();
    }
    public static byte[] Status(TournamentStatusResponse value)
    {
        // Native 01CA reads all eight slots even on an invalid status: retain its full shape.
        var body = new byte[9]; body[0] = value.Success ? (byte)1 : (byte)0;
        Array.Fill(body, (byte)0xff, 1, 8);
        if (value.Success) { body[1 + value.Slot] = 1; if (value.SecondRow) body[5 + value.Slot] = 1; }
        return body;
    }
    public static byte[] Selection(TournamentSelectionResponse value)
    {
        if (value.State is null) return [0, 1];
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream); writer.Write((byte)1);
        for (var row = 0; row < value.State.Rewards.Count; row++)
        {
            writer.Write(checked((byte)value.State.Rewards[row].Count));
            for (var index = 0; index < value.State.Rewards[row].Count; index++)
                writer.Write(value.State.Selections[row] == index ? value.Slot : (byte)0xff);
        }
        return stream.ToArray();
    }
}
internal sealed class TournamentInfoEncoder : GameNotiMessageEncoder<TournamentInfoMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TournamentInfoMessage m) => OnePayload(0x0183, TournamentPayloadCodec.Info(m.Plan)); }
internal sealed class TournamentMapEncoder : GameNotiMessageEncoder<TournamentMapMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TournamentMapMessage m) => OnePayload(0x0184, TournamentPayloadCodec.Map(m.Plan)); }
internal sealed class TournamentRewardEncoder : GameNotiMessageEncoder<TournamentRewardMessage>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TournamentRewardMessage m) => OnePayload(0x0185, TournamentPayloadCodec.Reward(m.State)); }
internal sealed class TournamentStatusEncoder : GameCmdMessageEncoder<TournamentStatusResponse>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TournamentStatusResponse m) => OnePayload(0x01ca, TournamentPayloadCodec.Status(m)); }
internal sealed class TournamentSelectionEncoder : GameCmdMessageEncoder<TournamentSelectionResponse>
{ protected override IReadOnlyList<GameMessagePayload> EncodePayloads(TournamentSelectionResponse m) => OnePayload(0x01cb, TournamentPayloadCodec.Selection(m)); }
internal sealed class TournamentStatusDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata meta, ReadOnlySpan<byte> body) => body.Length == 0 ? new TournamentRewardRequest(meta) : null; }
internal sealed class TournamentSelectionDecoder : IGameMessageDecoder
{ public IClientNetworkMessage? Decode(GameMessageMetadata meta, ReadOnlySpan<byte> body) => body.Length == 2 ? new TournamentRewardRequest(meta, body[0], body[1]) : null; }

public sealed record TournamentCommandResult(TournamentActionResult? Action);
public sealed record SelectTournamentRewardCommand(SelectedSessionIdentity Session, DungeonRunId RunId, byte Row, byte Index)
    : ICommand<TournamentCommandResult>, ISelectedSessionCommand, IAccountCommand
{
    SelectedSessionIdentity? ISelectedSessionCommand.Session => Session;
    public Dfo.Server.Data.Accounts.AccountId AccountId => Session.AccountId;
}
public sealed class SelectTournamentRewardCommandHandler(ITournamentDungeonDomainService tournaments)
    : ICommandHandler<SelectTournamentRewardCommand, TournamentCommandResult>
{
    public async ValueTask<TournamentCommandResult> HandleAsync(CommandExecutionContext context, SelectTournamentRewardCommand command, CancellationToken ct) =>
        new(await tournaments.SelectRewardAsync(command.Session, command.RunId, command.Row, command.Index, ct).ConfigureAwait(false));
}
public sealed record EliminateTournamentCommand(SelectedSessionIdentity Session, DungeonRunId RunId)
    : ICommand<TournamentCommandResult>, ISelectedSessionCommand, IAccountCommand
{
    SelectedSessionIdentity? ISelectedSessionCommand.Session => Session;
    public Dfo.Server.Data.Accounts.AccountId AccountId => Session.AccountId;
}
public sealed class EliminateTournamentCommandHandler(ITournamentDungeonDomainService tournaments)
    : ICommandHandler<EliminateTournamentCommand, TournamentCommandResult>
{
    public async ValueTask<TournamentCommandResult> HandleAsync(CommandExecutionContext context, EliminateTournamentCommand command, CancellationToken ct) =>
        new(await tournaments.EliminateAsync(command.Session, command.RunId, ct).ConfigureAwait(false));
}

public sealed class TournamentProjectionDelivery(ITournamentDungeonDomainService tournaments,
    ISelectedSessionDomainService sessions, IDungeonRunDomainService runs, ICommandBus commands,
    IGameMessageSender sender, IInventorySlotProjectionDomainService slots, TimeProvider clock)
{
    internal IReadOnlyList<IGameServerMessage> CompletionFrames(TournamentActionResult action) =>
        TournamentCompletionFrames.CompletionFrames(action, slots, clock);
    public async ValueTask<bool> EliminateAsync(DungeonRunId runId, CancellationToken ct)
    {
        var run = runs.Find(runId);
        if (run?.Layout.Tournament is null) return false;
        var session = sessions.FindByCharacter(run.Participants[0].CharacterId);
        if (session is null || tournaments.Find(session.CharacterId) is not { IsFinished: false }) return true;
        var action = (await commands.SendAsync(new EliminateTournamentCommand(session, runId), ct).ConfigureAwait(false)).Action;
        if (action is not null && sessions.IsInWorld(session))
            await sender.SendExplicitAsync(session.ConnectionId, new(CompletionFrames(action), []), ct).ConfigureAwait(false);
        return true;
    }
}

public sealed class TournamentRewardHandler(ICommandBus commands) : ClientMessageHandler<TournamentRewardRequest>, IProtocolCommandMessageHandler<GameTournamentRewardCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TournamentRewardRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameTournamentRewardCommand(context, message), ct).ConfigureAwait(false);
}
