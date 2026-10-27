using System.Net.Http.Json;
using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Data.Administration;
using Dfo.Server.Data.Inventory;
using Dfo.Server.HttpApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmRequestedCancellationAuditTests
{
    [Theory]
    [InlineData(false, "Rejected")]
    [InlineData(true, "RolledBack")]
    public async Task Cera_requested_cancellation_keeps_the_independent_durable_failure_audit(bool duringGrant, string outcome)
    {
        var state = new CancellationState("/api/gm/v1/cera/grant", duringGrant);
        await using var host = await GmTestHost.StartAsync(wrapFeature: feature => new AbortFeature(feature, state), configureServices: services =>
        {
            var original = services.Single(x => x.ServiceType == typeof(IGmCeraDomainService));
            services.AddSingleton<IGmCeraDomainService>(p => new Cera((IGmCeraDomainService)ActivatorUtilities.CreateInstance(p, original.ImplementationType!), state));
        });
        await GmCeraHttpTests.SeedAsync(host);
        var csrf = await GmCeraHttpTests.Login(host);
        var id = Guid.NewGuid(); state.Armed = true;
        await SendCancelled(host, state, csrf, GmCeraHttpTests.Payload(id));
        Assert.Equal(new[] { outcome }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='GrantCera' AND target='" + id.ToString("N") + "';"));
        Assert.Equal(new[] { "10", "100" }, await host.ReadAsync("SELECT cera FROM accounts ORDER BY account_id;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_cera_operations;"));
        Assert.True(state.SawRequestedToken); Assert.True(state.AuditUsedIndependentToken);
    }

    [Theory]
    [InlineData(false, "Rejected")]
    [InlineData(true, "RolledBack")]
    public async Task Mail_requested_cancellation_keeps_the_independent_durable_failure_audit(bool duringSend, string outcome)
    {
        var state = new CancellationState("/api/gm/v1/mail/send", duringSend);
        await using var host = await GmTestHost.StartAsync(wrapFeature: feature => new AbortFeature(feature, state), configureServices: services =>
        {
            var original = services.Single(x => x.ServiceType == typeof(IGmOfficialMailDomainService));
            services.AddSingleton<IGmOfficialMailDomainService>(p => new Mail((IGmOfficialMailDomainService)ActivatorUtilities.CreateInstance(p, original.ImplementationType!), state));
        });
        await GmPlayerDirectoryHttpTests.SeedAsync(host);
        var csrf = await GmCeraHttpTests.Login(host);
        var id = Guid.NewGuid(); state.Armed = true;
        await SendCancelled(host, state, csrf, new { requestId = id, title = "补偿", body = "正文", recipientIds = new[] { 1 }, items = Array.Empty<object>(), gold = 1 });
        Assert.Equal(new[] { outcome }, await host.ReadAsync("SELECT outcome FROM gm_audit WHERE action='SendOfficialMail' AND target='" + id.ToString("N") + "';"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_mail_operations;"));
        Assert.Equal(new[] { "0" }, await host.ReadAsync("SELECT COUNT(*) FROM mailbox_messages;"));
        Assert.True(state.SawRequestedToken); Assert.True(state.AuditUsedIndependentToken);
    }

    private static async Task SendCancelled(GmTestHost host, CancellationState state, string csrf, object payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, state.Path) { Content = JsonContent.Create(payload) };
        request.Headers.Add("X-GM-CSRF", csrf);
        try { using var response = await host.Client.SendAsync(request); }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
        await state.Audited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.Services.GetRequiredService<IGameplayQueue>().RunAsync(new GameplayWork(GameplayShardKey.World,
            GameplayCallChain.NewRoot(GameplayCallSource.Gm), "test:cancel-audit-committed", _ => ValueTask.CompletedTask), default);
    }
    private sealed class CancellationState(string path, bool duringWrite)
    {
        internal string Path => path;
        internal bool DuringWrite => duringWrite;
        internal bool Armed, SawRequestedToken, AuditUsedIndependentToken;
        internal Action Abort = null!;
        internal TaskCompletionSource Audited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Trigger(CancellationToken token)
        {
            if (!Armed) return;
            Armed = false; Abort(); SawRequestedToken = token.IsCancellationRequested;
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The decorator failed to cancel the actual request token.");
        }
        internal void Audit(CancellationToken token) => AuditUsedIndependentToken = !token.IsCancellationRequested;
    }
    private sealed class AbortFeature(IHttpApiFeature inner, CancellationState state) : IHttpApiFeature
    {
        public bool Enabled => inner.Enabled;
        public Uri? ListenUrl => inner.ListenUrl;
        public void ConfigureServices(IServiceCollection services) => inner.ConfigureServices(services);
        public void MapEndpoints(IEndpointRouteBuilder endpoints) => inner.MapEndpoints(endpoints);
        public void ConfigureMiddleware(IApplicationBuilder app)
        {
            app.Use(next => async context =>
            {
                if (context.Request.Path != state.Path) { await next(context); return; }
                using var aborted = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                context.RequestAborted = aborted.Token;
                state.Abort = aborted.Cancel;
                await next(context);
            });
            inner.ConfigureMiddleware(app);
        }
    }
    private sealed class Cera(IGmCeraDomainService inner, CancellationState state) : IGmCeraDomainService
    {
        public ValueTask<GmCeraQueryResult> QueryAsync(string secret, GmCeraQuery kind, GmPlayerFilter filter, Guid id, GmCeraInput? input, CancellationToken token)
        { if (!state.DuringWrite && kind == GmCeraQuery.Normalize) state.Trigger(token); return inner.QueryAsync(secret, kind, filter, id, input, token); }
        public ValueTask<GmCeraGrantResult> GrantAsync(string secret, Guid id, GmCeraInput input, CancellationToken token)
        { if (state.DuringWrite) state.Trigger(token); return inner.GrantAsync(secret, id, input, token); }
        public async ValueTask<bool> RecordFailureAsync(string secret, Guid id, string outcome, string reason, CancellationToken token)
        { state.Audit(token); var result = await inner.RecordFailureAsync(secret, id, outcome, reason, token); state.Audited.TrySetResult(); return result; }
    }
    private sealed class Mail(IGmOfficialMailDomainService inner, CancellationState state) : IGmOfficialMailDomainService
    {
        public ValueTask<ItemCatalogPage> SearchItemsAsync(string secret, ItemCatalogFilter filter, CancellationToken token) => inner.SearchItemsAsync(secret, filter, token);
        public ValueTask<ItemCatalogDetails> ItemDetailsAsync(string secret, int id, CancellationToken token) => inner.ItemDetailsAsync(secret, id, token);
        public ValueTask<GmMailQueryResult> QueryAsync(string secret, GmMailQuery kind, GmPlayerFilter filter, Guid id, GmMailInput? input, CancellationToken token)
        { if (!state.DuringWrite && kind == GmMailQuery.Prepare) state.Trigger(token); return inner.QueryAsync(secret, kind, filter, id, input, token); }
        public ValueTask<GmMailDocument> EditAsync(string secret, GmMailEdit kind, Guid id, GmMailEditorRequest request, CancellationToken token) => inner.EditAsync(secret, kind, id, request, token);
        public ValueTask<GmMailSendResult> SendAsync(string secret, GmMailPreparation preparation, CancellationToken token)
        { if (state.DuringWrite) state.Trigger(token); return inner.SendAsync(secret, preparation, token); }
        public async ValueTask<bool> RecordFailureAsync(string secret, Guid id, string outcome, string reason, CancellationToken token)
        { state.Audit(token); var result = await inner.RecordFailureAsync(secret, id, outcome, reason, token); state.Audited.TrySetResult(); return result; }
    }
}
