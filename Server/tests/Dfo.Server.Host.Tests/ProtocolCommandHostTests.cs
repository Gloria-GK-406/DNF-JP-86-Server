using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Administration;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Administration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class ProtocolCommandHostTests
{
    [Fact]
    public async Task Real_gm_bootstrap_hashes_off_executor_and_persists_inside_one_explicit_startup_unit()
    {
        var capture = new Capture();
        await using var host = await GmTestHost.StartAsync(configureServices: capture.Decorate);
        Assert.False(Assert.Single(capture.PreparationOnExecutor));
        var unit = Assert.Single(capture.BootstrapUnits);
        Assert.NotNull(unit.Transaction);
        Assert.Equal(GameplayCallSource.StartupRecovery, unit.Chain.Source);
        Assert.Equal("startup:GmBootstrapService", unit.Chain.CommandType);
        Assert.Null(unit.Chain.SourceConnection);
        Assert.Single(await host.ReadAsync("SELECT id FROM gm_administrators;"));
        Assert.Null(host.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
        var record = Assert.Single(await host.ReadAsync("SELECT password_record FROM gm_administrators;"));
        Assert.DoesNotContain(record, string.Join("|", host.Logs.Messages));
        Assert.DoesNotContain(GmTestHost.Password, string.Join("|", host.Logs.Messages));
    }

    [Fact]
    public async Task A_null_prepared_record_is_a_normal_false_result_and_never_creates_an_administrator()
    {
        var capture = new Capture();
        await using var host = await GmTestHost.StartAsync(password: "short", configureServices: capture.Decorate);
        Assert.False(Assert.Single(capture.PreparationOnExecutor));
        Assert.Empty(await host.ReadAsync("SELECT id FROM gm_administrators;"));
        var result = await host.Services.GetRequiredService<ICommandBus>().SendInternalAsync(
            new BootstrapGmAdministratorCommand("operator", null, GmPermission.All),
            new(GameplayCallSource.StartupRecovery, GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery), "test:null-bootstrap"));
        Assert.False(result);
        Assert.Empty(await host.ReadAsync("SELECT id FROM gm_administrators;"));
        Assert.Null(host.Services.GetRequiredService<IDomainTransactionAccessor>().Current);
    }

    private sealed class Capture
    {
        public List<bool> PreparationOnExecutor { get; } = [];
        public List<(GameplayCallChain Chain, IDomainTransaction? Transaction)> BootstrapUnits { get; } = [];
        public void Decorate(IServiceCollection services)
        {
            var credentials = services.Last(x => x.ServiceType == typeof(IGmCredentialReader));
            var access = services.Last(x => x.ServiceType == typeof(IGmAccessDomainService));
            services.Remove(credentials); services.Remove(access);
            services.AddSingleton<IGmCredentialReader>(p => new CredentialProbe((IGmCredentialReader)Create(credentials, p), this));
            services.AddSingleton<IGmAccessDomainService>(p => new AccessProbe((IGmAccessDomainService)Create(access, p),
                p.GetRequiredService<IDomainTransactionAccessor>(), this));
        }
        private static object Create(ServiceDescriptor descriptor, IServiceProvider services) => descriptor.ImplementationInstance
            ?? descriptor.ImplementationFactory?.Invoke(services)
            ?? ActivatorUtilities.CreateInstance(services, descriptor.ImplementationType!);
    }
    private sealed class CredentialProbe(IGmCredentialReader inner, Capture capture) : IGmCredentialReader
    {
        public ValueTask<GmCredentialCheck> CheckLoginAsync(string name, string password, CancellationToken ct) => inner.CheckLoginAsync(name, password, ct);
        public ValueTask<string?> PrepareBootstrapAsync(string password, CancellationToken ct)
        { capture.PreparationOnExecutor.Add(GameplayJob.IsOnExecutor); return inner.PrepareBootstrapAsync(password, ct); }
    }
    private sealed class AccessProbe(IGmAccessDomainService inner, IDomainTransactionAccessor transactions, Capture capture) : IGmAccessDomainService
    {
        public ValueTask<bool> BootstrapAsync(string name, string? record, GmPermission permissions, CancellationToken ct)
        {
            capture.BootstrapUnits.Add((GameplayJob.Current ?? throw new InvalidOperationException("Bootstrap must run on executor."), transactions.Current));
            return inner.BootstrapAsync(name, record, permissions, ct);
        }
        public ValueTask<bool> IsInitializedAsync(CancellationToken ct) => inner.IsInitializedAsync(ct);
        public ValueTask<GmLoginResult> LoginAsync(GmCredentialCheck check, CancellationToken ct) => inner.LoginAsync(check, ct);
        public ValueTask<GmIdentity?> IdentifyAsync(string? secret, CancellationToken ct) => inner.IdentifyAsync(secret, ct);
        public ValueTask<bool> LogoutAsync(string secret, CancellationToken ct) => inner.LogoutAsync(secret, ct);
    }
}
