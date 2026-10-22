using Dfo.Server.Data.Inventory;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Dfo.Server.Application.Authentication;
using Dfo.Server.Abstractions.Initialization;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Authentication;
using Dfo.Server.Data.Initialization;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.CreateCharacter;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Host.Tests;

public sealed class GmInitializationHttpTests
{
    private const string Prefix = "/api/gm/v1/initialization";
    private static AccountInitialization Account(int cera = 100) => new(cera, 20, 5, 0, [new(97, 2)]);
    private static CharacterInitialization Character() => new(1, 24, [new(3, 20)]);

    [Theory]
    [InlineData("QueryPlayers")]
    [InlineData("GrantCera")]
    [InlineData("SendMail,ViewAudit")]
    public async Task Partial_permissions_cannot_read_edit_or_search_initialization(string permissions)
    {
        await using var host = await GmTestHost.StartAsync(permissions: permissions);
        var csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(Prefix)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync(Prefix + "/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, Account()))).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT kind FROM initialization_settings"));
    }

    [Fact]
    public async Task Saves_are_independent_versioned_persistent_and_audited_without_modifying_existing_assets()
    {
        var directory = GmTestHost.NewDirectory();
        await using (var host = await GmTestHost.StartAsync(directory: directory))
        {
            await GmCeraHttpTests.SeedAsync(host);
            var csrf = await GmCeraHttpTests.Login(host);
            var defaults = (await host.Client.GetFromJsonAsync<InitializationView>(Prefix))!;
            Assert.Equal(0, defaults.Account.Revision);
            Assert.Equal(0, defaults.Account.Input.Cera);
            Assert.Equal(1, defaults.Character.Input.Level);
            Assert.Equal(8, defaults.Character.Input.PersonalCargoLevel);
            Assert.Equal(HttpStatusCode.Forbidden, (await host.PostAsync(Prefix + "/account", null, new SaveInitializationRequest<AccountInitialization>(0, Account()))).StatusCode);
            using var saved = await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, Account()));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(1, (await saved.Content.ReadFromJsonAsync<InitializationDocument<AccountInitialization>>())!.Revision);
            using var role = await host.PostAsync(Prefix + "/character", csrf, new SaveInitializationRequest<CharacterInitialization>(0, Character()));
            Assert.Equal(HttpStatusCode.OK, role.StatusCode);
            using var stale = await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, Account(999)));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(new[] { "10:20:30", "100:200:300" }, await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera FROM accounts ORDER BY account_id"));
            Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM initialization_settings"));
            Assert.Equal(new[] { "2" }, await host.ReadAsync("SELECT COUNT(*) FROM gm_audit WHERE action='SaveInitialization'"));
        }
        await using var restarted = await GmTestHost.StartAsync(directory: directory);
        await GmCeraHttpTests.Login(restarted);
        var retained = (await restarted.Client.GetFromJsonAsync<InitializationView>(Prefix))!;
        Assert.Equal(100, retained.Account.Input.Cera);
        Assert.Equal(24, retained.Character.Input.PersonalCargoLevel);
        Assert.Equal(new InitialItem(3, 20), Assert.Single(retained.Character.Input.Items));
    }

    [Fact]
    public async Task Invalid_snapshots_capacity_and_failed_audit_do_not_replace_the_active_configuration()
    {
        var fault = new GmAuditFault();
        await using var host = await GmTestHost.StartAsync(fault: fault);
        var csrf = await GmCeraHttpTests.Login(host);
        foreach (var input in new[] { Account(-1), Account() with { Contracts = [new(56, 1)] }, Account() with { Contracts = [new(97, 1), new(97, 2)] }, Account() with { AccountCargoLevel = -1 } })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, input))).StatusCode);
        foreach (var input in new[] { Character() with { Level = 0 }, Character() with { PersonalCargoLevel = 9 }, Character() with { Items = [new(3, 100000)] }, Character() with { Items = [new(999999, 1)] } })
            Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix + "/character", csrf, new SaveInitializationRequest<CharacterInitialization>(0, input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.PostAsync(Prefix + "/account", csrf, new { input = Account() })).StatusCode);
        fault.Throw = true;
        Assert.Equal(HttpStatusCode.InternalServerError, (await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, Account()))).StatusCode);
        Assert.Empty(await host.ReadAsync("SELECT kind FROM initialization_settings"));
        Assert.Empty(await host.ReadAsync("SELECT action FROM gm_audit WHERE action='SaveInitialization'"));
    }

    [PvfFact]
    public async Task Real_registration_and_creation_apply_current_templates_once_in_the_creation_transaction()
    {
        await using var host = await GmTestHost.StartAsync(realContent: true);
        var csrf = await GmCeraHttpTests.Login(host);
        using var savedAccount = await host.PostAsync(Prefix + "/account", csrf, new SaveInitializationRequest<AccountInitialization>(0, Account() with { AccountCargoLevel = 8 }));
        Assert.Equal(HttpStatusCode.OK, savedAccount.StatusCode);
        var catalog = (await host.Client.GetFromJsonAsync<Dfo.Server.Data.Inventory.ItemCatalogPage>(Prefix + "/items?category=consumable&pageSize=15"))!;
        var item = catalog.Items.First();
        using var savedRole = await host.PostAsync(Prefix + "/character", csrf,
            new SaveInitializationRequest<CharacterInitialization>(0, new(20, 24, [new(item.ItemId, 20)])));
        Assert.True(savedRole.IsSuccessStatusCode, await savedRole.Content.ReadAsStringAsync());
        var bus = host.Services.GetRequiredService<ICommandBus>();
        Assert.Equal(AuthenticationStatus.Success, (await TestCredentials.RegisterAsync(host.Services, "initial-user-01", "test-password-123")).Status);
        Assert.Equal(AuthenticationStatus.AccountExists, (await TestCredentials.RegisterAsync(host.Services, "initial-user-01", "test-password-123")).Status);
        Assert.Equal(new[] { "100:20:5" }, await host.ReadAsync("SELECT cera||':'||token_cera||':'||happy_token_cera FROM accounts"));
        Assert.Equal(new[] { "8" }, await host.ReadAsync("SELECT selection_key FROM account_cargo_state"));
        Assert.Equal(new[] { "1" }, await host.ReadAsync("SELECT COUNT(*) FROM account_premiums WHERE premium_type=97 AND end_time>strftime('%s','now')"));
        var result = await bus.SendAsync(new CreateCharacterCommand(new(1), 0, Encoding.UTF8.GetBytes("配置角色")));
        Assert.Equal(CharacterCreationStatus.Created, result.Status);
        Assert.Equal(new[] { "20" }, await host.ReadAsync("SELECT level FROM characters"));
        Assert.Equal(new[] { "24" }, await host.ReadAsync("SELECT list_param16 FROM character_container_state WHERE list_type=2"));
        Assert.Equal(new[] { "20" }, await host.ReadAsync($"SELECT SUM(count) FROM item_slots WHERE id={item.ItemId} AND list_type=0"));
        // Reading current configuration or the roster must not re-grant assets.
        await host.Client.GetAsync(Prefix);
        await bus.SendAsync(new Dfo.Server.Protocol.Game.AccountRoster.GetAccountCharacterRosterCommand(new(1)));
        Assert.Equal(new[] { "100" }, await host.ReadAsync("SELECT cera FROM accounts"));
        Assert.Equal(new[] { "20" }, await host.ReadAsync($"SELECT SUM(count) FROM item_slots WHERE id={item.ItemId} AND list_type=0"));
    }

    [PvfFact]
    public async Task Initialization_failure_rolls_back_the_new_identity_and_every_granted_asset()
    {
        var fault = new InitializationFault();
        await using var host = await GmTestHost.StartAsync(realContent: true, configureServices: services =>
        {
            var registration = services.Last(d => d.ServiceType == typeof(IInitializationDomainService));
            services.Remove(registration);
            services.AddSingleton<IInitializationDomainService>(p => new FailingInitialization(
                (IInitializationDomainService)registration.ImplementationFactory!(p), fault));
        });
        var csrf = await GmCeraHttpTests.Login(host);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix + "/account", csrf,
            new SaveInitializationRequest<AccountInitialization>(0, Account()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.PostAsync(Prefix + "/character", csrf,
            new SaveInitializationRequest<CharacterInitialization>(0, new(20, 24, [])))).StatusCode);
        var bus = host.Services.GetRequiredService<ICommandBus>();
        fault.Account = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await TestCredentials.RegisterAsync(host.Services, "rollback-user", "test-password-123"));
        Assert.Empty(await host.ReadAsync("SELECT account_id FROM accounts"));
        Assert.Empty(await host.ReadAsync("SELECT account_id FROM account_premiums"));
        fault.Account = false;
        await TestCredentials.RegisterAsync(host.Services, "rollback-user", "test-password-123");
        fault.Character = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(new CreateCharacterCommand(new(1), 0, Encoding.UTF8.GetBytes("回滚角色"))));
        Assert.Empty(await host.ReadAsync("SELECT character_id FROM characters"));
        Assert.Empty(await host.ReadAsync("SELECT character_id FROM character_skills"));
        Assert.Empty(await host.ReadAsync("SELECT owner_id FROM item_slots"));
        Assert.Equal(new[] { "100" }, await host.ReadAsync("SELECT cera FROM accounts"));
    }

    private sealed class InitializationFault { public bool Account { get; set; } public bool Character { get; set; } }
    private sealed class FailingInitialization(IInitializationDomainService inner, InitializationFault fault) : IInitializationDomainService
    {
        public ValueTask<InitializationDocument<AccountInitialization>> AccountAsync(CancellationToken ct) => inner.AccountAsync(ct);
        public ValueTask<InitializationDocument<CharacterInitialization>> CharacterAsync(CancellationToken ct) => inner.CharacterAsync(ct);
        public ValueTask<InitializationOptions> OptionsAsync(CancellationToken ct) => inner.OptionsAsync(ct);
        public ValueTask<InitializationDocument<AccountInitialization>> SaveAsync(SaveInitializationRequest<AccountInitialization> r, string a, CancellationToken ct) => inner.SaveAsync(r, a, ct);
        public ValueTask<InitializationDocument<CharacterInitialization>> SaveAsync(SaveInitializationRequest<CharacterInitialization> r, string a, CancellationToken ct) => inner.SaveAsync(r, a, ct);
        public async ValueTask ApplyAccountAsync(Dfo.Server.Data.Accounts.AccountId a, CancellationToken ct)
        { await inner.ApplyAccountAsync(a, ct); if (fault.Account) throw new InvalidOperationException("Injected initialization failure."); }
        public async ValueTask ApplyCharacterAsync(CharacterId c, CancellationToken ct)
        { await inner.ApplyCharacterAsync(c, ct); if (fault.Character) throw new InvalidOperationException("Injected initialization failure."); }
    }
}
