using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Networking.Runtime;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Tests.Characterization;

[Collection(CharacterizationCollection.Name)]
public sealed class SelectionLifecycleWholeCommandTests
{
    // USERINFO subtype/count header (3B), followed by the A21 reserved identity block (38B).
    private const int PvpUserIdOffset = 41;
    [PvfFact]
    public async Task A_real_selection_displaces_the_existing_same_account_session_and_keeps_the_replacement_live()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        using var oldClient = await server.EnterWorldAsync("old", "selection-displaced", "OldOwner", timeout.Token);
        var sessions = server.Services.GetRequiredService<ISelectedSessionDomainService>();
        var old = TestGameplay.Run(server.Services, () => sessions.Find(new AccountId(1))!);
        Assert.NotNull(old);
        using var replacement = await server.ConnectAsync("replacement", timeout.Token);
        await replacement.ReadUntilAsync(frame => frame.Is(0, 1), timeout.Token);
        // Prepare a second authenticated connection while keeping the first selected, so the
        // SELECT_CHARACTER displacement stage itself (not login supersession) must do the cleanup.
        var connection = server.Services.GetRequiredService<TouchSocketConnectionRegistry>().Snapshot()
            .Single(entry => entry.Context.ConnectionId != old.ConnectionId).Context;
        TestGameplay.Run(server.Services, () =>
        {
            var users = server.Services.GetRequiredService<OnlineStateRegistry>();
            Assert.True(users.TryGetByConnection(connection.ConnectionId, out var user));
            users.Authenticate(user!.OnlineUserId, old.AccountId);
            Assert.True(sessions.IsInWorld(old));
        });
        await replacement.SelectCharacterAsync(timeout.Token);
        await oldClient.ReadUntilClosedAsync(timeout.Token);
        await replacement.FenceAsync(timeout.Token);
        TestGameplay.Run(server.Services, () =>
        {
            var current = Assert.IsType<Dfo.Server.Data.World.SelectedSessionIdentity>(sessions.Find(old.AccountId));
            Assert.Equal(connection.ConnectionId, current.ConnectionId);
            Assert.Equal(old.CharacterId, current.CharacterId);
            Assert.True(sessions.IsInWorld(current)); Assert.False(sessions.IsInWorld(old));
            var runtime = server.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>();
            Assert.Null(runtime.PositionOf(old)); Assert.NotNull(runtime.PositionOf(current));
        });
        Assert.True(oldClient.IsClosed); Assert.False(replacement.IsClosed);
    }

    [PvfFact]
    public async Task Real_pvp_selections_publish_the_ready_lobby_roster_and_notify_the_existing_peer()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(new CharacterizationOptions
        {
            Decorate = services => services.Decorate<IChannelEntryDomainService>((_, inner) => new PvpChannel(inner)),
        }, timeout.Token);
        using var first = await server.EnterWorldAsync("first", "pvp-first", "FirstDuel", timeout.Token);
        var before = first.Frames.Count();
        using var second = await server.EnterWorldAsync("second", "pvp-second", "NextDuel", timeout.Token);
        await first.FenceAsync(timeout.Token);
        Assert.Contains(second.Frames, frame => frame.Is(0, 2) && frame.Body.Length > 3
            && frame.Body[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[1..]) == 2);
        Assert.Contains(first.Frames.Skip(before), frame => frame.Is(0, 2)
            && frame.Body.Length > PvpUserIdOffset + 1
            && frame.Body[0] == 0 && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[1..]) == 1
            && BinaryPrimitives.ReadUInt16LittleEndian(frame.Body[PvpUserIdOffset..]) == second.WireUserId);
        TestGameplay.Run(server.Services, () =>
        {
            var ready = server.Services.GetRequiredService<IPvpLobbyDomainService>().ReadyOn(server.GamePort, new CharacterId(0));
            Assert.Equal(new[] { (int)first.WireUserId, second.WireUserId }, ready.Select(member => member.CharacterId.Value));
            var sessions = server.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.All(ready, member => { Assert.NotNull(member.Session); Assert.True(sessions.IsInWorld(member.Session)); });
        });
        Assert.False(first.IsClosed); Assert.False(second.IsClosed);
    }

    private sealed class PvpChannel(IChannelEntryDomainService actual) : IChannelEntryDomainService
    {
        public async ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken token) =>
            (await actual.ResolveAsync(port, token)) with { Kind = ChannelEntryKind.Pvp };
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int level) => null;
        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int port, CancellationToken token) => actual.ResolveLoginAsync(port, token);
    }
}
