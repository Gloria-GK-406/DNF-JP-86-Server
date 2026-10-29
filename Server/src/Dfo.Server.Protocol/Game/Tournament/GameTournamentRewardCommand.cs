using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.Tournament;

public sealed record GameTournamentRewardCommand(NetworkDispatchContext Dispatch, TournamentRewardRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}
public sealed class GameTournamentRewardCommandHandler(ISelectedSessionDomainService sessions, IDungeonRunQueryDomainService runs,
    ITournamentDungeonDomainService tournaments, ICommandWorkUnits units, IGameCommandOutbox outbox,
    IInventorySlotProjectionDomainService slots, TimeProvider clock) : ICommandHandler<GameTournamentRewardCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameTournamentRewardCommand command, CancellationToken ct)
    {
        var message = command.Input;
        var session = sessions.FindByConnection(command.Dispatch.Connection.ConnectionId);
        if (session is null) return false;
        var run = runs.Find(session.CharacterId);
        if (run is null)
        {
            if (message.Row is null) await outbox.RegisterCmdAsync(session.ConnectionId, new TournamentStatusResponse(false, 0), ct).ConfigureAwait(false);
            else await outbox.RegisterCmdAsync(session.ConnectionId, new TournamentSelectionResponse(null, 0), ct).ConfigureAwait(false);
            return false;
        }
        if (!sessions.IsInWorld(session)) return false;
        var state = tournaments.Find(session.CharacterId);
        var slot = run.Participant?.Slot ?? 0;
        var participants = new[] { new SelectedSessionRecipient(session, command.Dispatch.Connection, message.Metadata) };
        if (message.Row is null)
        {
            var started = await units.WriteAsync(execution, new("BeginTournamentRewards", participants),
                (_, _) => ValueTask.FromResult(tournaments.BeginRewardSelection(session, run.RunId)), ct).ConfigureAwait(false);
            await outbox.RegisterCmdAsync(session.ConnectionId, new TournamentStatusResponse(started, slot,
                state is { Rewards.Count: > 1 } && state.Rewards[1].Count > 0), ct).ConfigureAwait(false);
            return started;
        }
        var action = (await units.WriteAsync(execution, new("SelectTournamentReward", participants),
            async (_, token) => new TournamentCommandResult(await tournaments.SelectRewardAsync(session, run.RunId,
                message.Row.Value, message.Index!.Value, token).ConfigureAwait(false)), ct).ConfigureAwait(false)).Action;
        var frames = action is null ? new List<IGameServerMessage>() : TournamentCompletionFrames.CompletionFrames(action, slots, clock).ToList();
        frames.Add(new TournamentSelectionResponse(action?.State, slot));
        await outbox.RegisterExplicitAsync(session.ConnectionId, new(frames, []), ct).ConfigureAwait(false);
        return action is not null;
    }
}
