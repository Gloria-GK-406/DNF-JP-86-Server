using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Application.Execution;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.StateSynchronization;

public sealed record ReadPremiumSnapshotCommand(SelectedSessionIdentity Session)
    : ICommand<PremiumSnapshotResult>, IAccountCommand
{
    public AccountId AccountId => Session.AccountId;
}
public sealed record PremiumSnapshotResult(AccountEntitlementEntryState Entitlements,
    IReadOnlyList<CharacterPremiumUsageSlotState> Usage, DateTimeOffset PreparedAt, bool IsCurrent = true);

public sealed class ReadPremiumSnapshotCommandHandler(ISelectedSessionDomainService sessions,
    IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService usage, TimeProvider clock)
    : ICommandHandler<ReadPremiumSnapshotCommand, PremiumSnapshotResult>
{
    public async ValueTask<PremiumSnapshotResult> HandleAsync(CommandExecutionContext context,
        ReadPremiumSnapshotCommand command, CancellationToken token)
    {
        return await PremiumSnapshotReader.ReadAsync(sessions, premiums, usage, clock, command.Session, token).ConfigureAwait(false);
    }
}

internal static class PremiumSnapshotReader
{
    internal static async ValueTask<PremiumSnapshotResult> ReadAsync(ISelectedSessionDomainService sessions,
        IPremiumServiceDomainService premiums, IPremiumServiceUsageDomainService usage, TimeProvider clock,
        SelectedSessionIdentity session, CancellationToken token)
    {
        if (!sessions.IsInWorld(session)) return new(new([]), [], clock.GetUtcNow(), false);
        return new(new(await premiums.For(session.AccountId).ReadAsync(token).ConfigureAwait(false)),
            await usage.ReadAsync(session.CharacterId, token).ConfigureAwait(false), clock.GetUtcNow());
    }
}

// One server timer refreshes day and entitlement changes; counters reset lazily.
// A21 effect gates only test nonzero end, so expired slots must be projected as zero.
public sealed class PremiumDailyRefreshService(ISelectedSessionSnapshotQuery directory,
    ISelectedSessionDomainService sessions,
    IPremiumServiceUsageDomainService usage, ICommandBus commands, IGameMessageSender sender, TimeProvider clock,
    ILogger<PremiumDailyRefreshService> logger, IGameplayQueue queue) : BackgroundService
{
    private readonly Dictionary<SelectedSessionIdentity, (DateTimeOffset Period, string Active, bool CrystalActive, bool GrowthActive)> delivered = [];
    private const string Command = "timer:PremiumDailyRefreshService";
    private readonly CoalescingGameplaySource ticks = new(queue);
    // One refresh is one timer CMD on the executor; the entry enqueues it directly, the loop through its source.
    public Task RefreshAsync(CancellationToken token) => GameplayTimerWork.RunAsync(queue, Command, RefreshCoreAsync, token);
    private async ValueTask RefreshCoreAsync(CancellationToken token)
    {
        GameplayWriteAssertion.Writing(nameof(PremiumDailyRefreshService) + "." + nameof(RefreshCoreAsync));
        var current = directory.Snapshot();
        foreach (var stale in delivered.Keys.Except(current).ToArray()) delivered.Remove(stale);
        foreach (var session in current)
        {
            var period = usage.CurrentPeriodStart;
            try
            {
                if (!sessions.IsInWorld(session)) continue;
                var state = await commands.SendAsync(new ReadPremiumSnapshotCommand(session), token).ConfigureAwait(false);
                if (!state.IsCurrent) continue;
                var active = string.Join(";", state.Entitlements.Premiums
                    .Where(x => x.ExpiresAtUnixSeconds > state.PreparedAt.ToUnixTimeSeconds())
                    .OrderBy(x => x.PremiumType).Select(x => x.PremiumType == (int)PremiumServiceType.BlackDiamond
                        ? $"{x.PremiumType}:default" : $"{x.PremiumType}:{x.ExpiresAtUnixSeconds}"));
                var crystal = state.Entitlements.Premiums.SingleOrDefault(x => x.PremiumType == (int)PremiumServiceType.CrystalContract);
                var crystalActive = crystal is not null && crystal.ExpiresAtUnixSeconds > state.PreparedAt.ToUnixTimeSeconds();
                var growth = state.Entitlements.Premiums.SingleOrDefault(x => x.PremiumType == (int)PremiumServiceType.GrowthContract);
                var growthActive = growth is not null && growth.ExpiresAtUnixSeconds > state.PreparedAt.ToUnixTimeSeconds();
                var hadPrevious = delivered.TryGetValue(session, out var previous);
                if (hadPrevious && previous == (period, active, crystalActive, growthActive)) continue;
                // The eight-slot snapshot cannot clear ordinary contracts. Also cover
                // expiry before the first scheduler tick; subsequent ticks must not repeat it.
                if (!crystalActive && (previous.CrystalActive || !hadPrevious && crystal is not null))
                    await sender.SendNotiAsync(session.ConnectionId,
                        new PremiumGrantedMessage(new((int)PremiumServiceType.CrystalContract, 0), state.PreparedAt), token)
                        .ConfigureAwait(false);
                if (!growthActive && (previous.GrowthActive || !hadPrevious && growth is not null))
                    await sender.SendNotiAsync(session.ConnectionId,
                        new PremiumGrantedMessage(new((int)PremiumServiceType.GrowthContract, 0), state.PreparedAt), token)
                        .ConfigureAwait(false);
                await sender.SendCmdAsync(session.ConnectionId,
                    new PremiumServiceStateMessage(state.Entitlements, state.Usage, state.PreparedAt), token).ConfigureAwait(false);
                delivered[session] = (period, active, crystalActive, growthActive);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) { logger.LogWarning(ex, "Premium daily projection failed character={CharacterId}", session.CharacterId); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await GameplayTimerWork.TickAsync(ticks, Command, RefreshCoreAsync, logger, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
