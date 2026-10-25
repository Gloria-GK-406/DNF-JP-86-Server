using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Persistence.Database;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Support;

internal static class SqliteFailureAssertions
{
    // Optimized async virtual calls can inline Commit; the participant-recorded failure fact
    // identifies the real phase independently of which runtime frames remain in StackTrace.
    internal static void FromCommit(DomainTransaction transaction, Exception cause)
    {
        Assert.IsType<SqliteException>(cause);
        var failure = Assert.IsType<TransactionFailureFact>(transaction.CommitFailure);
        Assert.Equal(TransactionFailurePhase.FallibleCommit, failure.Phase);
        Assert.Equal(TransactionFailureKind.KnownStorage, failure.Kind);
        Assert.IsType<SqliteDatabaseParticipant>(failure.Source);
        Assert.Same(cause, failure.Cause);
    }

    internal static void FromAction(DomainTransaction transaction, Exception cause)
    {
        Assert.IsType<SqliteException>(cause);
        Assert.Null(transaction.CommitFailure);
    }
}

internal sealed class SqliteTransactionProbe
{
    internal List<DomainTransaction> Transactions { get; } = [];

    internal void Configure(IServiceCollection services)
    {
        var descriptor = services.Last(value => value.ServiceType == typeof(ICommandWorkUnits));
        services.Remove(descriptor);
        services.AddScoped<ICommandWorkUnits>(provider => new ObservedUnits(
            (ICommandWorkUnits)(descriptor.ImplementationInstance ?? descriptor.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType!)), provider, this));
    }

    private sealed class ObservedUnits(ICommandWorkUnits actual, IServiceProvider provider, SqliteTransactionProbe observed) : ICommandWorkUnits
    {
        private void Capture() => observed.Transactions.Add(
            Assert.IsType<DomainTransaction>(provider.GetRequiredService<IDomainTransactionAccessor>().Current));

        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull =>
            actual.WriteAsync(context, declaration, (unit, token) => { Capture(); return action(unit, token); }, ct);

        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull =>
            actual.WriteResultAsync(context, declaration, (unit, token) => { Capture(); return action(unit, token); }, ct);

        public ValueTask<CommandSelfSettledWriteResult<T>> WriteSelfSettledResultAsync<T>(CommandExecutionContext context,
            CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action,
            CancellationToken ct = default) where T : notnull =>
            actual.WriteSelfSettledResultAsync(context, declaration, action, ct);

        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration,
            Func<CancellationToken, ValueTask<T>> action, CancellationToken ct = default) where T : notnull =>
            actual.ReadOnlyAsync(context, declaration, action, ct);
    }
}
