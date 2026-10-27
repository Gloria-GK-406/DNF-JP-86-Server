using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterDeath;
public sealed class DungeonPartyWipeService(ICommandBus commands, TimeProvider clock, ILogger<DungeonPartyWipeService> logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override string SourceName => "timer:DungeonPartyWipeService";
    protected override bool SerializeRounds => true;
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var due = await SubmitAsync(new MaintainDungeonReturnsCommand(), root, SourceName, token).ConfigureAwait(false);
        foreach (var command in due) await ReturnRunAsync(command, root, token).ConfigureAwait(false);
    }
    internal ValueTask ReturnAsync(DungeonRunId runId, CancellationToken token, bool cleared = false) =>
        ReturnRunAsync(new(runId, cleared), GameplayCallChain.NewRoot(GameplayCallSource.Timer), token);

    private async ValueTask ReturnRunAsync(ReturnDungeonRunCommand command, GameplayCallChain root, CancellationToken token)
    {
        System.Collections.Immutable.ImmutableArray<SelectedSessionIdentity> recipients;
        try { recipients = await SubmitAsync(command, root.Child(root.Source), "timer:dungeon-return-run", token).ConfigureAwait(false); }
        catch (Exception error) when (GameplayJob.EndedAJob(error))
        {
            logger.LogWarning("Dungeon return failed run={RunId}; continuing with the next captured run", command.RunId);
            return;
        }
        // Ending the run commits the return obligation. Drain waits for all accepted recipient jobs,
        // including when the producer's stop token was cancelled after the end command completed.
        foreach (var session in recipients)
            await ItemAsync(new ProjectDungeonReturnCommand(session, command.RunId, command.Cleared), root,
                "timer:dungeon-return-member", CancellationToken.None).ConfigureAwait(false);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
