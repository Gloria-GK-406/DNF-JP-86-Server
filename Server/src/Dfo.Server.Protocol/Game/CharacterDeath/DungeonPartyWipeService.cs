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
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token) =>
        _ = await SubmitAsync(new MaintainDungeonReturnsCommand(), root, SourceName, token).ConfigureAwait(false);
    internal async ValueTask ReturnAsync(DungeonRunId runId, CancellationToken token, bool cleared = false) =>
        _ = await SubmitAsync(new ReturnDungeonRunCommand(runId, cleared), GameplayCallChain.NewRoot(GameplayCallSource.Timer), SourceName, token).ConfigureAwait(false);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250), clock);
        try { while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) await HostedAsync(stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
