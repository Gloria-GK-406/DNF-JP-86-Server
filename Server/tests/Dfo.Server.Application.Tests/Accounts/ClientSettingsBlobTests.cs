using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Application.Tests.World;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Dfo.Server.Protocol;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.SaveGameOption;
using Dfo.Server.Protocol.Game.SaveHotkeys;
using Dfo.Server.Protocol.Game.SaveQuickChat;
using Dfo.Server.Application.Commands;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class ClientSettingsBlobTests
{
    [Fact]
    public async Task Registered_event_subscription_projects_latest_blob_only_after_commit_and_skips_offline_owner()
    {
        var bus = new VisibilityBus();
        await using var f = new LearningFixture(configure: services =>
        {
            new ProtocolModule().ConfigureServices(services, new ConfigurationBuilder().Build());
            services.AddSingleton<INotificationBus>(bus);
        });
        await f.SeedAsync();
        f.Sql("INSERT INTO character_settings(character_id,user_state_bits) VALUES(71,3)");
        var peer = f.EnterWorld(2, 72, "visibility-peer");
        var sessions = f.Services.GetRequiredService<Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService>();
        var towns = f.Services.GetRequiredService<Dfo.Server.Abstractions.World.ITownAreaDomainService>();
        TestGameplay.Run(f.Services, () =>
        {
            towns.Enter(new(peer.CharacterId, peer.TownAreaLease(), new(1, 0, 0), new(0, 0, 0, 0, 0), true));
        });
        var service = f.Services.GetRequiredService<IClientSettingsDomainService>(); var origin = Origin(f);
        var on = new byte[150]; on[2] = on[110] = on[148] = 1;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await f.Services.GetRequiredService<IClientSettingsTable>().SaveMainAsync(new(1), on, default);
                await tx.CommitAsync(default);
            }
        });
        bus.Compose = async (recipient, notice) =>
        {
            Assert.Null(f.Services.GetRequiredService<Dfo.Server.Abstractions.Transactions.IDomainTransactionAccessor>().Current);
            using var scope = f.Services.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<NotificationHandlerRegistry>();
            var registration = registry.GetRegistration(notice.GetType());
            return await ((INotificationComposer)scope.ServiceProvider.GetRequiredService(registration.ComposerType))
                .ComposeAsync(recipient, notice, default);
        };
        var dispatcher = f.Services.GetRequiredService<DomainEventDispatcher>();
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await service.SaveMainAsync(origin, new byte[150], default);
                await service.SaveMainAsync(origin, on, default);
                await service.SaveMainAsync(origin, new byte[150], default);
                Assert.Empty(bus.Messages);
                await tx.CommitAsync(default);
            }
        });
        Assert.Empty(bus.Messages);
        Assert.Equal(3, await dispatcher.DrainOnceAsync());
        Assert.Equal(2, bus.Messages.Count); // Self and the visible town peer see the owner's flags.
        Assert.All(bus.Messages, message =>
        {
            Assert.Equal("UserVisibilityMessage", message.GetType().Name);
            Assert.Equal((byte)25, message.GetType().GetProperty("UserStateBits")!.GetValue(message));
            Assert.Equal((ushort)71, message.GetType().GetProperty("UserId")!.GetValue(message));
        });
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            towns.Enter(new(peer.CharacterId, peer.TownAreaLease(), new(1, 0, 1), new(0, 1, 0, 0, 0), true));
            await using (var tx = f.Scope.Begin())
            {
                await service.SaveMainAsync(origin, on, default);
                await tx.CommitAsync(default);
            }
        });
        await dispatcher.DrainOnceAsync();
        Assert.Equal(3, bus.Messages.Count); // The peer in another area receives no refresh.
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await using (var tx = f.Scope.Begin())
            {
                await service.SaveMainAsync(origin, new byte[150], default);
                await tx.CommitAsync(default);
            }
            sessions.ReleaseExact(f.Session);
        });
        await dispatcher.DrainOnceAsync();
        Assert.Equal(3, bus.Messages.Count);
    }

    [SkippableFact]
    public async Task Full_character_views_derive_visibility_from_blob_without_updating_the_legacy_row()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            await using var f = new LearningFixture(useArchive: true, configure: services => services.AddSingleton(
                new Dfo.Server.Infrastructure.Content.PvfContentOptions { ArchivePath = ContentArchive.Path }));
            await f.SeedAsync();
            f.Sql("INSERT INTO character_settings(character_id,user_state_bits) VALUES(71,3); INSERT INTO character_stats(character_id) VALUES(71); INSERT INTO character_condition(character_id,pc_room_state) VALUES(71,0); INSERT INTO character_pvp(character_id) VALUES(71); INSERT INTO character_expert_job(character_id) VALUES(71)");
            var table = f.Services.GetRequiredService<IClientSettingsTable>();
            await using (var tx = f.Scope.Begin()) { await table.SaveMainAsync(new(1), new byte[150], default); await tx.CommitAsync(default); }
            var entry = await f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterEntryStateDomainService>().LoadAsync(new(1), new(71), default);
            Assert.Equal(25, entry.Public.VisibilityBits); Assert.Equal(25, entry.Public.Settings.UserStateBits);
            Assert.Equal(25, entry.Settings.VisibilityBits);
            var roster = await f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterRepository>()
                .ListByAccountWithAppearanceAsync(new(1), default);
            var display = Assert.Single(roster).Display!;
            Assert.False(display.ShowGrowEffect); Assert.False(display.ShowAwakeningAvatar);
            Assert.False(display.ShowFullAvatar); Assert.Equal(25, display.VisibilityBits);
            Assert.Equal(3, f.Scalar("SELECT user_state_bits FROM character_settings WHERE character_id=71"));
        });
    }

    private sealed class VisibilityBus : INotificationBus
    {
        public Func<NotificationRecipient, object, Task<IReadOnlyList<IServerNetworkMessage>>> Compose = null!;
        public List<IServerNetworkMessage> Messages = [];
        public async ValueTask<NotificationDelivery> PublishAsync<T>(NotificationAudience audience, T notice, CancellationToken ct)
            where T : class, INotification<T>
        {
            var results = new List<NotificationAttempt>();
            foreach (var session in Assert.IsType<NotificationAudience.TheseSessions>(audience).Sessions)
            {
                Messages.AddRange(await Compose(new(session.CharacterId, session.ConnectionId, session), notice));
                results.Add(new(session.CharacterId, NotificationOutcome.Registered));
            }
            return new(results);
        }
    }

    [Fact]
    public async Task Appearance_keeps_blob_as_the_only_write_and_emits_only_changed_selected_options_after_commit()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            f.Sql("INSERT INTO character_settings(character_id,user_state_bits) VALUES(71,3)");
            var service = f.Services.GetRequiredService<IClientSettingsDomainService>(); var origin = Origin(f);
            var on = new byte[150]; on[2] = on[110] = on[148] = 1;
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await service.SaveMainAsync(origin, on, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='settings.appearance-changed'"));
            await using (var tx = f.Scope.Begin())
                Assert.True(await service.SaveMainAsync(origin, new byte[150], default));
            Assert.Equal(3, (await service.ReadAppearanceAsync(new(1), 3, default)).VisibilityBits);
            Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='settings.appearance-changed'"));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await service.SaveMainAsync(origin, new byte[150], default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(25, (await service.ReadAppearanceAsync(new(1), 3, default)).VisibilityBits);
            Assert.Equal(3, f.Scalar("SELECT user_state_bits FROM character_settings WHERE character_id=71"));
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='settings.appearance-changed'"));
            var unrelated = new byte[150]; unrelated[20] = 7;
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await service.SaveMainAsync(origin, unrelated, default));
                Assert.True(await service.SaveMainAsync(origin, unrelated, default));
                await tx.CommitAsync(default);
            }
            Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM domain_events WHERE event_type='settings.appearance-changed'"));
            await using var reload = new LearningFixture(existing: f.Path);
            Assert.Equal(25, (await reload.Services.GetRequiredService<IClientSettingsDomainService>()
                .ReadAppearanceAsync(new(1), 3, default)).VisibilityBits);
            Assert.Equal(3, reload.Scalar("SELECT user_state_bits FROM character_settings WHERE character_id=71"));
        });
    }

    private static ClientSettingsOrigin Origin(LearningFixture f)
    {
        return TestGameplay.Run<ClientSettingsOrigin>(f.Services, () =>
        {
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(f.Session.ConnectionId, out var user));
            return new(user!.SessionIdentity, f.Session.AccountId, user.Generation, f.Session);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blobs_commit_or_rollback_without_changing_other_columns_or_profiles(bool commit)
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            f.Sql("INSERT INTO account_settings(account_id,hotkey_key_type,hotkey_slots) VALUES(1,7,X'3412');");
            var service = f.Services.GetRequiredService<IClientSettingsDomainService>();
            var origin = Origin(f);
            var main = Enumerable.Range(0,150).Select(i => (byte)i).ToArray();
            var bank0 = Enumerable.Repeat((byte)123,300).ToArray();
            var bank1 = Enumerable.Repeat((byte)234,300).ToArray();
            byte[] text0 = [65, 9, 49, 10, 0];
            byte[] text1 = [66, 9, 50, 10, 255, 0, 127];
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await service.SaveMainAsync(origin, main, default));
                Assert.True(await service.SaveQuickChatAsync(origin, 1, bank1, default));
                Assert.True(await service.SaveQuickChatAsync(origin, 0, bank0, default));
                Assert.True(service.ActivateHotkeyProfile(origin, 0));
                Assert.True(await service.SaveHotkeysAsync(service.CaptureHotkeyUpload(origin)!, text0, default));
                Assert.True(service.ActivateHotkeyProfile(origin, 1));
                Assert.True(await service.SaveHotkeysAsync(service.CaptureHotkeyUpload(origin)!, text1, default));
                if (commit) await tx.CommitAsync(default);
            }
            await using var reload = new LearningFixture(f.Path);
            var row = (await reload.Services.GetRequiredService<IAccountSettingsTable>().FindAsync(new(1), default))!;
            Assert.Equal(7, row.HotkeyKeyType); Assert.Equal([0x34,0x12], row.AccountHotkeySlotPrefix);
            var profiles = await reload.Services.GetRequiredService<IClientSettingsDomainService>().ReadHotkeysAsync(new(1), default);
            if (commit)
            {
                Assert.Equal(main,row.MainGameOptions); Assert.Equal(bank0,row.QuickChatBank0); Assert.Equal(bank1,row.QuickChatBank1);
                Assert.Equal(text0, profiles.Single(p => p.KeyType == 0).Payload);
                Assert.Equal(text1, profiles.Single(p => p.KeyType == 1).Payload);
            }
            else { Assert.Null(row.MainGameOptions); Assert.Empty(row.QuickChatBank0); Assert.Empty(row.QuickChatBank1); Assert.Empty(profiles); }
            Assert.Null(await reload.Services.GetRequiredService<IAccountSettingsTable>().FindAsync(new(2), default));
        });
    }

    [Fact]
    public async Task Profile_changes_and_stale_or_disconnected_origins_cannot_overwrite_saved_values()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var service = f.Services.GetRequiredService<IClientSettingsDomainService>(); var origin = Origin(f);
            Assert.Null(service.CaptureHotkeyUpload(origin));
            Assert.True(service.ActivateHotkeyProfile(origin,0));
            var old = service.CaptureHotkeyUpload(origin)!;
            Assert.True(service.ActivateHotkeyProfile(origin,1));
            await using (var tx = f.Scope.Begin())
            {
                Assert.False(await service.SaveHotkeysAsync(old,[1,2,3],default));
                Assert.False(await service.SaveMainAsync(origin with { Generation = origin.Generation + 1 },new byte[150],default));
                Assert.False(await service.SaveMainAsync(origin with { AccountId = new(2) },new byte[150],default));
                Assert.False(await service.SaveQuickChatAsync(origin,2,new byte[300],default));
                Assert.False(await service.SaveQuickChatAsync(origin,0,new byte[299],default));
                Assert.False(await service.SaveMainAsync(origin,new byte[149],default));
                Assert.True(await service.SaveHotkeysAsync(service.CaptureHotkeyUpload(origin)!,[],default));
                await tx.CommitAsync(default);
            }
            Assert.Empty(Assert.Single(await service.ReadHotkeysAsync(new(1),default)).Payload);
            Assert.Null(await f.Services.GetRequiredService<IAccountSettingsTable>().FindAsync(new(1),default));
            f.Services.GetRequiredService<IOnlineUserDomainService>().TryDisconnectConnection(origin.Connection.ConnectionId);
            Assert.Null(service.CaptureHotkeyUpload(origin));
            await using var last = f.Scope.Begin();
            Assert.False(await service.SaveMainAsync(origin,new byte[150],default));
        });
    }

    [Fact]
    public async Task Writes_require_a_transaction_and_separate_accounts_keep_distinct_text()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var service = f.Services.GetRequiredService<IClientSettingsDomainService>(); var first = Origin(f);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveMainAsync(first,new byte[150],default).AsTask());
            var registry = f.Services.GetRequiredService<OnlineStateRegistry>();
            var connection = new NetworkConnectionContext(new("settings-other"), NetworkEndpointKind.Game,10011);
            var id = registry.RegisterConnection(connection).OnlineUserId; registry.Authenticate(id,new(2));
            var user = registry.GetRequiredUser(id); var second = new ClientSettingsOrigin(user.SessionIdentity,new(2),user.Generation,null);
            Assert.True(service.ActivateHotkeyProfile(first,0)); Assert.True(service.ActivateHotkeyProfile(second,0));
            await using (var tx = f.Scope.Begin())
            {
                Assert.True(await service.SaveHotkeysAsync(service.CaptureHotkeyUpload(first)!,[1,2,3],default));
                Assert.True(await service.SaveHotkeysAsync(service.CaptureHotkeyUpload(second)!,[4,5,6],default));
                await tx.CommitAsync(default);
            }
            Assert.Equal([1,2,3],Assert.Single(await service.ReadHotkeysAsync(new(1),default)).Payload);
            Assert.Equal([4,5,6],Assert.Single(await service.ReadHotkeysAsync(new(2),default)).Payload);
            service.ForgetConnection(first.Connection.ConnectionId); Assert.Null(service.CaptureHotkeyUpload(first));
            Assert.NotNull(service.CaptureHotkeyUpload(second));
        });
    }

    [Fact]
    public async Task Production_handlers_commit_before_return_and_entry_reads_the_same_full_blobs()
    {
        await using var f = new LearningFixture(configure: s => new ProtocolModule().ConfigureServices(s,new ConfigurationBuilder().Build()));
        await f.SeedAsync(); var origin = Origin(f);
        var commands = f.Services.GetRequiredService<ICommandBus>();
        Assert.True(await TestGameplay.RunAsync(() => Task.FromResult(f.Services.GetRequiredService<IClientSettingsDomainService>().ActivateHotkeyProfile(origin,0))));
        Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(origin.Connection.ConnectionId, out var current));
        var dispatch = new NetworkDispatchContext(current!.Connection);
        var main=Enumerable.Range(0,150).Select(i=>(byte)i).ToArray();
        byte[] keys=[65,9,49,10,0]; var chat=Enumerable.Repeat((byte)99,300).ToArray();
        var dispatcher = f.Services.GetRequiredService<Dfo.Server.Abstractions.Networking.INetworkMessageDispatcher>();
        await dispatcher.DispatchAsync(dispatch,new SaveGameOption1Request(GameMessageMetadata.Empty,main),default);
        await dispatcher.DispatchAsync(dispatch,new SaveQuickChatRequest(GameMessageMetadata.Empty,1,chat),default);
        await dispatcher.DispatchAsync(dispatch,new SaveHotkeysRequest(GameMessageMetadata.Empty,keys),default);
        var state=await f.Services.GetRequiredService<IAccountEntryStateDomainService>().LoadSettingsAsync(new(1),default);
        Assert.Equal(main,state.MainGameOptions); Assert.Equal(chat,state.QuickChatBank1);
        Assert.Equal(keys,Assert.Single(state.HotkeyProfiles).Payload);
        Assert.Equal(0,Assert.Single(state.HotkeyProfiles).KeyType);
    }

    [Fact]
    public async Task Late_cleanup_of_an_old_identity_cannot_remove_a_replacement_profile()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var f = new LearningFixture(); await f.SeedAsync();
            var service=f.Services.GetRequiredService<IClientSettingsDomainService>();
            var registry=f.Services.GetRequiredService<OnlineStateRegistry>();
            var users=f.Services.GetRequiredService<IOnlineUserDomainService>();
            var connection=new NetworkConnectionContext(new("reused-settings-connection"),NetworkEndpointKind.Game,10011);
            ClientSettingsOrigin Authenticate()
            {
                var id=registry.RegisterConnection(connection).OnlineUserId;registry.Authenticate(id,new(1));
                var snapshot=registry.GetRequiredUser(id);return new(snapshot.SessionIdentity,new(1),snapshot.Generation,null);
            }
            var old=Authenticate();Assert.True(service.ActivateHotkeyProfile(old,0));
            service.ForgetConnection(connection.ConnectionId);
            // An activation that had already acquired the online gate may finish after early cleanup.
            Assert.True(service.ActivateHotkeyProfile(old,0));
            Assert.True(users.TryDisconnectConnection(connection.ConnectionId));
            service.ForgetConnection(old.Connection);
            var replacement=Authenticate();Assert.True(service.ActivateHotkeyProfile(replacement,1));
            service.ForgetConnection(old.Connection);
            Assert.Equal(1,service.CaptureHotkeyUpload(replacement)!.KeyType);
            service.ForgetConnection(replacement.Connection);Assert.Null(service.CaptureHotkeyUpload(replacement));
        });
    }
}
