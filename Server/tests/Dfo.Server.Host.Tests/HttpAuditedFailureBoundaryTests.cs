using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class HttpAuditedFailureBoundaryTests
{
    [Fact]
    public async Task Http_entry_rethrows_a_previous_real_carrier_even_when_the_command_object_is_reused_on_a_new_chain()
    {
        await using var host = await GmTestHost.StartAsync(configureServices: services =>
            services.AddSingleton<IGmAuditDomainService>(p => new Audit(p.GetRequiredService<IGmAdministrationPersistence>(), p.GetRequiredService<IDomainTransactionAccessor>())));
        await GmCeraHttpTests.SeedAsync(host);
        using var login = await host.LoginAsync(await host.CsrfAsync());
        login.EnsureSuccessStatusCode();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("Dfo.Gm.Session=", StringComparison.Ordinal));
        var secret = cookie.Split(';')[0].Split('=')[1];
        var command = new HttpRouteCommand(HttpRouteOperation.CeraGrant,
            new HttpRouteInput.CeraGrantRequest(secret, new([1], "Cera", 1, "reason", Guid.NewGuid())));
        var firstOrigin = new InternalCommandOrigin(GameplayCallSource.Gm, GameplayCallChain.NewRoot(GameplayCallSource.Gm),
            "gm:POST /api/gm/v1/cera/grant", InternalCommandCancellation.BodyOnly);
        var first = await Assert.ThrowsAsync<CommandHttpAuditedFailureException>(() => host.Services.GetRequiredService<ICommandBus>()
            .SendInternalAsync(command, firstOrigin).AsTask());
        Assert.Equal(new[] { "Succeeded", "Unknown" }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='GrantCera' ORDER BY rowid;"));
        Assert.Equal(new[] { "11" }, await host.ReadAsync("SELECT cera FROM accounts WHERE account_id=1;"));
        var replay = new ReplayBus(first);
        await using var producerServices = new ServiceCollection().AddSingleton<ICommandBus>(replay).BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = producerServices };
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse("/api/gm/v1/cera/grant"), 0,
            new EndpointMetadataCollection(new HttpMethodMetadata(["POST"]), new EnqueuesItself(GameplayCallSource.Gm, HttpRouteOperation.CeraGrant)), "grant"));
        var second = await Assert.ThrowsAsync<CommandHttpAuditedFailureException>(() => HttpGameplay.RunAsync(context, GameplayCallSource.Gm, command));
        Assert.Same(first, second); // The actual HTTP boundary did not consume the old reply.
        Assert.Same(command, replay.Command);
        Assert.NotEqual(firstOrigin.Chain.Id, replay.Origin!.Chain.Id);
        Assert.Equal(1, replay.Calls);
    }
    private sealed class ReplayBus(CommandHttpAuditedFailureException previous) : ICommandBus
    {
        internal ICommand? Command;
        internal InternalCommandOrigin? Origin;
        internal int Calls;
        public ValueTask<T> SendAsync<T>(ICommand<T> command, CancellationToken ct = default) where T : notnull => throw new NotSupportedException();
        public ValueTask<T> SendInternalAsync<T>(ICommand<T> command, InternalCommandOrigin origin, CancellationToken ct = default) where T : notnull
        { Command = command; Origin = origin; Calls++; return ValueTask.FromException<T>(previous); }
    }
    private sealed class Audit(IGmAdministrationPersistence persistence, IDomainTransactionAccessor transactions) : IGmAuditDomainService
    {
        public async ValueTask AppendAsync(GmAuditRecord record, CancellationToken ct)
        {
            await persistence.AppendAuditAsync(record, ct);
            if (record.Action == "GrantCera" && record.Outcome == "Succeeded") transactions.Current!.Enlist(new object(), () => new Poison());
        }
        public ValueTask<GmAuditPage> QueryAsync(int page, int size, GmAuditFilter filter, CancellationToken ct) => persistence.QueryAuditAsync(page, size, filter, ct);
    }
    private sealed class Poison : ITransactionParticipant
    {
        public bool CommitCanFail => false;
        public ValueTask PrepareAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask CommitAsync(CancellationToken ct) => ValueTask.FromException(new IOException("actual post-durable commit fault"));
        public ValueTask RollbackAsync(CancellationToken ct) => ValueTask.CompletedTask;
    }
}
