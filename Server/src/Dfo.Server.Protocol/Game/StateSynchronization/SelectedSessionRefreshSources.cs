using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization
{
public abstract class SelectedSessionRefreshSource(ICommandBus commands, TimeProvider clock, ILogger logger)
    : TypedGameplayTimerSource(commands, logger)
{
    protected override bool SerializeRounds => true;
    protected abstract SelectedSessionRefreshTopic Topic { get; }
    protected override string SourceName => "timer:" + GetType().Name;
    public Task RefreshAsync(CancellationToken token) => RunAsync(token);
    protected override async Task RunRoundAsync(GameplayCallChain root, CancellationToken token)
    {
        var scan = await SubmitAsync(new CaptureSelectedSessionRefreshCommand(Topic), root, SourceName, token).ConfigureAwait(false);
        foreach (var session in scan.Sessions)
        {
            ICommand<bool> command = Topic switch
            {
                SelectedSessionRefreshTopic.Premium => new RefreshPremiumSessionCommand(scan.Period, session),
                SelectedSessionRefreshTopic.ItemUsage => new RefreshItemUsageSessionCommand(scan.Period, session),
                SelectedSessionRefreshTopic.Fatigue => new RefreshFatigueSessionCommand(scan.Period, session),
                _ => new RefreshQuotaSessionCommand(Topic, scan.Period, session),
            };
            await ItemAsync(command, root, SourceName + "/session", token).ConfigureAwait(false);
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try { while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false)) await HostedAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
public sealed class DimensionQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<DimensionQuotaDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Dimension; }
public sealed class GoldSilverHornQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<GoldSilverHornQuotaDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.GoldSilverHorn; }
public sealed class ReviveCoinQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<ReviveCoinQuotaDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.ReviveCoin; }
public sealed class MirrorQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<MirrorQuotaDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Mirror; }
public sealed class ItemUsageDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<ItemUsageDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.ItemUsage; }
public sealed class PremiumDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<PremiumDailyRefreshService> logger)
    : SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Premium; }
}
namespace Dfo.Server.Protocol.Game.Altar
{
public sealed class AltarQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<AltarQuotaDailyRefreshService> logger)
    : Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Altar; }
}
namespace Dfo.Server.Protocol.Game.Tournament
{
public sealed class HerasQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<HerasQuotaDailyRefreshService> logger)
    : Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Heras; }
}
namespace Dfo.Server.Protocol.Game.Tournament
{
public sealed class TournamentQuotaDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<TournamentQuotaDailyRefreshService> logger)
    : Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Tournament; }
}
namespace Dfo.Server.Protocol.Game.Fatigue
{
public sealed class FatigueDailyRefreshService(ICommandBus commands, TimeProvider clock, ILogger<FatigueDailyRefreshService> logger)
    : Dfo.Server.Protocol.Game.StateSynchronization.SelectedSessionRefreshSource(commands, clock, logger)
{ protected override SelectedSessionRefreshTopic Topic => SelectedSessionRefreshTopic.Fatigue; }
}
