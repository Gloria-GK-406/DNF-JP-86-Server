using Dfo.Server.Abstractions.Auction;
using Dfo.Server.Abstractions.Scheduling;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Application.Auction;

/// <summary>What the Auction domain registers; called once from <see cref="Dfo.Server.Application.ApplicationModule"/>.</summary>
internal static class AuctionRegistration
{
    internal static IServiceCollection AddAuction(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IAuctionRuleService>(sp => new AuctionRuleService(
            sp.GetRequiredService<IAuctionItemDefinitionSource>(),
            sp.GetRequiredService<Dfo.Server.Abstractions.Inventory.IMailItemRuleService>(), configuration));
        services.TryAddSingleton<IAuctionLotHandling, AuctionLotHandling>();
        services.TryAddSingleton<IAuctionPurse, AuctionPurse>();
        // One owner behind two faces, as with relations: readers take the query face only.
        services.TryAddSingleton<AuctionHouseDomainService>();
        services.TryAddSingleton<IAuctionHouseDomainService>(sp => sp.GetRequiredService<AuctionHouseDomainService>());
        services.TryAddSingleton<IAuctionQueryDomainService>(sp => sp.GetRequiredService<AuctionHouseDomainService>());
        services.AddScoped<AuctionExpiryTask>();
        services.AddScheduledTask(new("auction.expiry", new IntervalTaskSchedule(TimeSpan.FromMinutes(1)), typeof(AuctionExpiryTask))
        { MissedRun = ScheduledTaskMissedRunPolicy.RunOnce });
        return services;
    }
}

/// <summary>Ends the listings whose time is up, one unit each, so one failure costs only its own listing.</summary>
/// <remarks>
/// The expiry time stored on each listing is the only clock: a minute's tick, and the first tick after
/// a restart, settle whatever is due. 70 settles at most 50 per tick; 64 here.
/// </remarks>
internal sealed class AuctionExpiryTask(IAuctionHouseDomainService house, ICommandWorkUnits units, TimeProvider clock)
    : IScheduledGameplayTask
{
    internal const int BatchLimit = 64;

    public async ValueTask<ScheduledTaskBusinessResult> RunAsync(CommandExecutionContext execution, ScheduledTaskContext scheduled,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // A write unit that writes nothing: a read-only unit opens the database through its separate
        // read-only connection string, whose pool outlives a host that clears only its own.
        var due = await units.WriteAsync(execution, new("auction.expiry-due", []),
            (_, token) => house.DueAsync(now, BatchLimit, token), ct).ConfigureAwait(false);
        var failures = new List<Exception>();
        foreach (var listing in due)
        {
            Exception? actionFailure = null;
            var result = await units.WriteResultAsync(execution, new("auction.expiry-settle", []), async (unit, token) =>
            {
                try { return await house.SettleExpiredAsync(listing, now, token).ConfigureAwait(false); }
                catch (Exception error) when (error is not OperationCanceledException
                    && !TransactionOutcomeUncertainException.Contains(error))
                {
                    execution.DemandRequest();
                    unit.RequireRollback(error);
                    actionFailure = error;
                    return false;
                }
            }, ct).ConfigureAwait(false);
            var failure = result is CommandWriteResult<bool>.CommitRolledBack rolledBack ? rolledBack.Cause : actionFailure;
            if (failure is not null) failures.Add(failure);
        }
        return ScheduledTaskBusinessResult.Handled(execution, "Auction expiry failed for some listings.", failures);
    }
}
