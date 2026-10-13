using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Tests.Characters;
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

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class ClientSettingsBlobTests
{
    private static ClientSettingsOrigin Origin(LearningFixture f)
    {
        var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
        Assert.True(users.TryGetByConnection(f.Session.ConnectionId, out var user));
        return new(user!.SessionIdentity, f.Session.AccountId, user.Generation, f.Session);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Blobs_commit_or_rollback_without_changing_other_columns_or_profiles(bool commit)
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
    }

    [Fact]
    public async Task Profile_changes_and_stale_or_disconnected_origins_cannot_overwrite_saved_values()
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
    }

    [Fact]
    public async Task Writes_require_a_transaction_and_separate_accounts_keep_distinct_text()
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
    }

    [Fact]
    public async Task Production_handlers_commit_before_return_and_entry_reads_the_same_full_blobs()
    {
        await using var f = new LearningFixture(configure: s => new ProtocolModule().ConfigureServices(s,new ConfigurationBuilder().Build()));
        await f.SeedAsync(); var origin = Origin(f);
        var commands = f.Services.GetRequiredService<ICommandBus>();
        Assert.True(await commands.SendAsync(new ActivateClientHotkeyProfileCommand(origin,0),default));
        var dispatch = new NetworkDispatchContext(
            new NetworkConnectionContext(origin.Connection.ConnectionId, NetworkEndpointKind.Game,10011));
        var main=Enumerable.Range(0,150).Select(i=>(byte)i).ToArray();
        byte[] keys=[65,9,49,10,0]; var chat=Enumerable.Repeat((byte)99,300).ToArray();
        await f.Services.GetRequiredService<SaveGameOption1Handler>().HandleAsync(dispatch,new SaveGameOption1Request(GameMessageMetadata.Empty,main),default);
        await f.Services.GetRequiredService<SaveQuickChatHandler>().HandleAsync(dispatch,new SaveQuickChatRequest(GameMessageMetadata.Empty,1,chat),default);
        await f.Services.GetRequiredService<SaveHotkeysHandler>().HandleAsync(dispatch,new SaveHotkeysRequest(GameMessageMetadata.Empty,keys),default);
        var state=await f.Services.GetRequiredService<IAccountEntryStateDomainService>().LoadSettingsAsync(new(1),default);
        Assert.Equal(main,state.MainGameOptions); Assert.Equal(chat,state.QuickChatBank1);
        Assert.Equal(keys,Assert.Single(state.HotkeyProfiles).Payload);
        Assert.Equal(0,Assert.Single(state.HotkeyProfiles).KeyType);
    }

    [Fact]
    public async Task Late_cleanup_of_an_old_identity_cannot_remove_a_replacement_profile()
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
    }
}
