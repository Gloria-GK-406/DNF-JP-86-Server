using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Scheduling;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Application.Events;

internal sealed class OnlineTimeRewardRuleService(IServerConfigService settings) : IOnlineTimeRewardRuleService
{
    internal const int MaximumIntervalMinutes = 1440;
    internal const int MaximumAmount = 1_000_000;

    public OnlineTimeRewardRule Rule { get; } = Read(settings);

    private static OnlineTimeRewardRule Read(IServerConfigService settings)
    {
        if (!settings.GetConfig<bool>("OnlineTimeReward:Enabled")) return OnlineTimeRewardRule.Disabled;
        var minutes = settings.GetConfig<int>("OnlineTimeReward:IntervalMinutes");
        var cera = settings.GetConfig<int>("OnlineTimeReward:Cera");
        var token = settings.GetConfig<int>("OnlineTimeReward:TokenCera");
        if (minutes is < 1 or > MaximumIntervalMinutes)
            throw new InvalidOperationException($"OnlineTimeReward:IntervalMinutes must be between 1 and {MaximumIntervalMinutes}.");
        if (cera is < 0 or > MaximumAmount || token is < 0 or > MaximumAmount)
            throw new InvalidOperationException($"OnlineTimeReward:Cera and OnlineTimeReward:TokenCera must be between 0 and {MaximumAmount}.");
        if (cera == 0 && token == 0)
            throw new InvalidOperationException("OnlineTimeReward is enabled but pays neither Cera nor TokenCera.");
        return new(true, TimeSpan.FromMinutes(minutes), cera, token, settings.GetConfig<bool>("OnlineTimeReward:Notice"));
    }
}

/// <remarks>
/// A due time is advanced before the caller's transaction commits. A grant that then rolls back loses
/// that one period instead of paying it twice; the job reports the failure.
/// </remarks>
internal sealed class OnlineTimeRewardDomainService(IOnlineTimeRewardRuleService rules, ISelectedSessionDomainService sessions,
    IAccountWalletDomainService wallets, IEventService events, IDomainTransactionAccessor transactions, TimeProvider clock)
    : IOnlineTimeRewardDomainService
{
    private readonly object gate = new();
    private readonly Dictionary<SelectedSessionIdentity, DateTimeOffset> due = [];

    public void Retain(IReadOnlyCollection<SelectedSessionIdentity> inWorld)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineTimeRewardDomainService) + "." + nameof(Retain));
        var keep = inWorld as IReadOnlySet<SelectedSessionIdentity> ?? inWorld.ToHashSet();
        lock (gate)
            foreach (var session in due.Keys.Where(session => !keep.Contains(session)).ToArray())
                due.Remove(session);
    }

    public async ValueTask<bool> AccrueAsync(SelectedSessionIdentity session, CancellationToken ct)
    {
        GameplayWriteAssertion.Writing(nameof(OnlineTimeRewardDomainService) + "." + nameof(AccrueAsync));
        var rule = rules.Rule;
        if (!rule.Enabled) return false;
        if (!sessions.IsInWorld(session))
        {
            lock (gate) due.Remove(session);
            return false;
        }
        var now = clock.GetUtcNow();
        lock (gate)
        {
            if (!due.TryGetValue(session, out var at))
            {
                due[session] = now + rule.Interval;
                return false;
            }
            if (now < at) return false;
            // Time the server was not looking (a stalled round, a long pause) is not paid twice.
            while (at <= now) at += rule.Interval;
            due[session] = at;
        }
        if (transactions.Current is null) throw new InvalidOperationException("Online-time rewards require a domain transaction.");
        var cera = await AddAsync(session, AccountCurrency.Cera, rule.Cera, ct).ConfigureAwait(false);
        var token = await AddAsync(session, AccountCurrency.TokenCera, rule.TokenCera, ct).ConfigureAwait(false);
        if (cera == 0 && token == 0) return false;
        await events.EnqueueAsync(new OnlineTimeRewardGranted(session.AccountId, session.CharacterId, session.ConnectionId.Value,
            session.ConnectionGeneration, cera, token, rule.Notice), ct).ConfigureAwait(false);
        return true;
    }

    /// <returns>The amount paid; zero when the rule pays none of it or the balance is already at its limit.</returns>
    private async ValueTask<int> AddAsync(SelectedSessionIdentity session, AccountCurrency currency, int amount, CancellationToken ct)
    {
        if (amount == 0) return 0;
        try
        {
            await wallets.AddAsync(session.AccountId, currency, amount, ct).ConfigureAwait(false);
            return amount;
        }
        catch (WalletException error) when (error.Rejection is WalletRejection.BalanceLimit) { return 0; }
    }
}

/// <summary>Walks the characters in the world once a minute and pays each one whose period has completed.</summary>
/// <remarks>Each character gets its own work unit, so one failed grant neither rolls back nor stops the rest.</remarks>
internal sealed class OnlineTimeRewardJob(ISelectedSessionSnapshotQuery online, ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, IOnlineTimeRewardDomainService rewards, ICommandWorkUnits units,
    ILogger<OnlineTimeRewardJob> logger) : IScheduledGameplayTask
{
    internal const string TaskId = "activity.online-time-reward";

    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled, CancellationToken ct)
    {
        var inWorld = online.Snapshot().Where(sessions.IsInWorld).ToHashSet();
        rewards.Retain(inWorld);
        var failures = new List<Exception>();
        foreach (var session in inWorld)
        {
            ct.ThrowIfCancellationRequested();
            if (!sessions.IsInWorld(session)) continue;
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new(TaskId, [ScheduledSessionRecipient.Capture(session, users)]), async (unit, token) =>
            {
                try { return await rewards.AccrueAsync(session, token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(error)
                    && !DomainEventScopeCleanupException.Contains(error))
                {
                    execution.DemandRequest();
                    unit.RequireRollback(error);
                    actionFailure = error;
                    return false;
                }
            }, ct).ConfigureAwait(false);
            var error = result is CommandWriteResult<bool>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
            if (error is not null && sessions.IsInWorld(session))
            {
                logger.LogWarning("Online-time reward failed character={CharacterId}", session.CharacterId);
                failures.Add(error);
            }
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Online-time reward failed for some characters.", failures);
    }
}

internal static class OnlineTimeRewardRegistration
{
    internal static IServiceCollection AddOnlineTimeReward(this IServiceCollection services)
    {
        services.TryAddSingleton<IOnlineTimeRewardRuleService, OnlineTimeRewardRuleService>();
        services.TryAddSingleton<IOnlineTimeRewardDomainService, OnlineTimeRewardDomainService>();
        services.AddDomainEvent<OnlineTimeRewardGranted>("activity.online-time-reward-granted", 1);
        services.AddScoped<OnlineTimeRewardJob>();
        services.AddScheduledTaskFactory(_ => new(OnlineTimeRewardJob.TaskId, new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), typeof(OnlineTimeRewardJob)));
        return services;
    }
}
