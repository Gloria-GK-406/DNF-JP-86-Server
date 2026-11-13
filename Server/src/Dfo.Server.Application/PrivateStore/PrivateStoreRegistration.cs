using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.PrivateStore;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.PrivateStore;

/// <summary>Private store terms (#43, user decisions P1-P5).</summary>
/// <remarks>
/// The licence stall holds 14 lines (A21 PVF item 16 "最多陈列 14 种"); a shop doll holds its own
/// number (items 2600471-2600473, <c>[shop doll]</c> 0/1/2 → 1/2/3 lines). The fee is the licence
/// text's 1% on an ordinary channel; this server has no trading-channel kind, so it is 1% everywhere.
/// </remarks>
internal sealed class PrivateStoreRuleService : IPrivateStoreRuleService
{
    private static readonly IReadOnlyDictionary<int, int> Dolls = new Dictionary<int, int>
    {
        [2600471] = 1, [2600472] = 2, [2600473] = 3,
    };

    public int Capacity(int? dollItemId) => dollItemId is { } doll ? Dolls[doll] : 14;

    public int? DollCapacity(int itemId) => Dolls.TryGetValue(itemId, out var lines) ? lines : null;

    public long Fee(long amount) => amount / 100;

    /// <remarks>
    /// The trade licence (premium 8, item 16; 70 IsPermissionPrivateStore reads premium 8) or the
    /// auction coupon, whose PVF explanation includes the licence (user decision #43 P1).
    /// </remarks>
    public bool Permits(IReadOnlyCollection<PremiumServiceType> activeServices) =>
        activeServices.Contains(PremiumServiceType.PrivateStoreLicense) || activeServices.Contains(PremiumServiceType.AuctionCoupon);
}

/// <summary>What the PrivateStore domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class PrivateStoreRegistration
{
    internal static IServiceCollection AddPrivateStore(this IServiceCollection services)
    {
        services.TryAddSingleton<IPrivateStoreRuleService, PrivateStoreRuleService>();
        // One owner behind two faces: readers take the query face only.
        services.TryAddSingleton<PrivateStoreDomainService>();
        services.TryAddSingleton<IPrivateStoreDomainService>(sp => sp.GetRequiredService<PrivateStoreDomainService>());
        services.TryAddSingleton<IPrivateStoreQueryDomainService>(sp => sp.GetRequiredService<PrivateStoreDomainService>());
        services.AddScoped<PrivateStoreReturnTask>();
        services.AddScheduledTask(new("private-store.return", new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), typeof(PrivateStoreReturnTask))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        return services;
    }
}

/// <summary>Mails back the goods of stalls whose seller left without closing them (70 LoadPrivateStore returns them by mail too).</summary>
internal sealed class PrivateStoreReturnTask(IPrivateStoreDomainService stores, ICommandWorkUnits units) : IScheduledGameplayTask
{
    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled,
        CancellationToken ct)
    {
        Exception? actionFailure = null;
        var result = await units.WriteResultAsync(execution, new("private-store.return", []), async (unit, token) =>
        {
            try { return await stores.ReturnAbandonedAsync(32, token).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException && !TransactionOutcomeUncertainException.Contains(error))
            {
                execution.DemandRequest();
                unit.RequireRollback(error);
                actionFailure = error;
                return 0;
            }
        }, ct).ConfigureAwait(false);
        var failure = result is CommandWriteResult<int>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
        return ScheduledTaskBusinessResult.Handled(execution, "Private store return failed.", failure is null ? [] : [failure]);
    }
}
