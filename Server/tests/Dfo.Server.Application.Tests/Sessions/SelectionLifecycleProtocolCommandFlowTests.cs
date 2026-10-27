using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.ReturnToSelection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

public sealed class SelectionLifecycleProtocolCommandFlowTests
{
    [Fact]
    public async Task Real_return_command_releases_runtime_persists_position_and_keeps_the_authenticated_connection()
    {
        using var f = TeleportConsumableTests.Fixture(); var selected = f.Session;
        await Return(f);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(f.Services.GetRequiredService<ISelectedSessionDomainService>().Find(selected.AccountId));
            Assert.Null(f.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().PositionOf(selected));
            var online = f.Services.GetRequiredService<IOnlineUserQueryDomainService>().GetRequired(f.OnlineUser);
            Assert.Equal(selected.AccountId, online.AccountId); Assert.Null(online.CurrentCharacterId);
        });
        Assert.Equal(12, f.Scalar("SELECT pos_x FROM characters WHERE character_id=1"));
        Assert.Equal(34, f.Scalar("SELECT pos_y FROM characters WHERE character_id=1"));
        Assert.Single(f.Sender.Messages.OfType<ReturnToSelectionResponse>());
        Assert.Equal(new[] { Dfo.Server.Data.Networking.ServerMessageType.GameStateSynchronization,
            Dfo.Server.Data.Networking.ServerMessageType.GameReturnToSelection,
            Dfo.Server.Data.Networking.ServerMessageType.GameAccountCharacterRoster,
            Dfo.Server.Data.Networking.ServerMessageType.GameHonorLevelInfo }, f.Sender.Messages.Select(m => m.MessageType));
        Assert.Empty(f.Sender.Outbound.Closes);
    }

    [Fact]
    public async Task Real_channel_refusal_preserves_the_selected_world_and_registers_the_native_refusal_before_exact_close()
    {
        using var f = TeleportConsumableTests.Fixture(services => services.AddSingleton<IChannelEntryDomainService, RefusedChannel>());
        await Assert.ThrowsAsync<ChannelAdmissionRefusedException>(() => Select(f).AsTask());
        TestGameplay.Run(f.Services, () => Assert.True(f.Services.GetRequiredService<ISelectedSessionDomainService>().IsInWorld(f.Session)));
        var first = f.Sender.Outbound.Frames[0]; Assert.Equal(new byte[] { 0, 0xAC }, first[15..]);
        Assert.Equal(2, f.Sender.Outbound.Frames.Count);
        Assert.Same(f.Connection, Assert.Single(f.Sender.Outbound.Entries, e => e.IsClose).Connection);
        Assert.True(f.Sender.Outbound.Entries.Last().IsClose);
    }

    [Fact]
    public async Task Real_prepare_failure_compensates_the_new_selection_without_restoring_a_dead_session()
    {
        var cause = new IOException("selection preparation");
        using var f = TeleportConsumableTests.Fixture(services =>
        {
            var original = services.Last(d => d.ServiceType == typeof(ISelectCharacterPreparationDomainService));
            services.AddSingleton<ISelectCharacterPreparationDomainService>(p => new FailingPreparation(
                (ISelectCharacterPreparationDomainService)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(p)
                    ?? ActivatorUtilities.CreateInstance(p, original.ImplementationType!)), cause));
        });
        await Return(f); f.Sender.Messages.Clear();
        var failure = await Record.ExceptionAsync(() => Select(f).AsTask());
        Assert.NotNull(failure); Assert.True(Contains(failure, cause));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(f.Services.GetRequiredService<ISelectedSessionDomainService>().Find(f.Session.AccountId));
            Assert.Null(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().GetRequired(f.OnlineUser).CurrentCharacterId);
        });
        Assert.Equal(100000, f.Gold); Assert.Same(f.Connection, f.Sender.Outbound.Entries.Last(e => e.IsClose).Connection);
    }
    private static bool Contains(Exception failure, Exception cause) => ReferenceEquals(failure, cause)
        || failure.InnerException is { } inner && Contains(inner, cause)
        || failure is AggregateException aggregate && aggregate.InnerExceptions.Any(e => Contains(e, cause));
    private static ValueTask Return(ShopFixture f) => EffectRequestTransport.SendRaw(f, new(1, 0x0007, 0, 7), []);
    private static ValueTask Select(ShopFixture f) => EffectRequestTransport.SendRaw(f, new(1, 0x0004, 0, 8), [0, 0]);
    private sealed class RefusedChannel : IChannelEntryDomainService
    {
        public ValueTask<ChannelEntryIdentity> ResolveAsync(int port, CancellationToken ct) => ValueTask.FromResult(new ChannelEntryIdentity(1, port, ChannelEntryKind.General));
        public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int level) => new(0xAC, "level gate");
        public ValueTask<ChannelLoginEntry> ResolveLoginAsync(int port, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class FailingPreparation(ISelectCharacterPreparationDomainService actual, Exception cause) : ISelectCharacterPreparationDomainService
    {
        public ValueTask<ActiveCharacterState> PreflightAsync(AccountId account, ushort index, ChannelEntryIdentity channel, CancellationToken ct) => actual.PreflightAsync(account, index, channel, ct);
        public ValueTask<SelectCharacterResult> PrepareAsync(ActiveCharacterState selected, AccountId account, ChannelEntryIdentity channel, CancellationToken ct) => ValueTask.FromException<SelectCharacterResult>(cause);
        public ValueTask<SelectCharacterResult> PlaceAtSpawnAsync(SelectedSessionIdentity session, SelectCharacterResult result, CancellationToken ct) => actual.PlaceAtSpawnAsync(session, result, ct);
    }
}
