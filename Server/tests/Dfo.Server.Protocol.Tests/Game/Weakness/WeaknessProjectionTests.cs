using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Weakness;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.Weakness;

public sealed class WeaknessProjectionTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(32)]
    [InlineData(100)]
    public void Notification_encodes_one_percentage_byte(byte value)
    {
        var frame = Assert.Single(new WeaknessProjectionEncoder().Encode(new WeaknessProjectionNotification(value)));
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x21, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1, 2)));
        Assert.Equal(new byte[] { value }, frame[15..]);
    }

    [Fact]
    public async Task Projection_uses_current_value_and_refuses_replaced_session()
    {
        await TestGameplay.RunAsync(async () =>
        {
            var session = SelectedSessionIdentity.Create(new(1), new(1), new("old"), 1, 10011);
            var sessions = new Sessions(session);
            var state = new State { Value = 10 };
            var sender = new Sender();
            var delivery = new WeaknessProjectionDelivery(state, sessions, sender);
            await delivery.SendCurrentAsync(session, default);
            state.Value = 19;
            await delivery.SendCurrentAsync(session, default);
            sessions.Current = session with { ConnectionId = new("new"), ConnectionGeneration = 2 };
            await delivery.SendCurrentAsync(session, default);
            state.Value = 28;
            await delivery.SendCurrentAsync(sessions.Current, default);
            Assert.Equal(new byte[] { 10, 19, 28 }, sender.Sent.Select(item => item.Value));
        });
    }

    private sealed class State : IWeaknessDomainService, IWeakness
    {
        public byte Value { get; set; }
        public ValueTask<int> RecoveryGoldCost(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IWeakness For(CharacterId id) => this;
        public TimeSpan RecoveryInterval => TimeSpan.FromMinutes(1);
        public IReadOnlyList<CharacterId> AdvanceRecovery() => [];
        public ValueTask SetWeakness() => throw new NotSupportedException();
        public void Recover() => Value = 100;
        public void PauseRecovery() { }
        public void ResumeRecovery() { }
    }

    private sealed class Sessions(SelectedSessionIdentity current) : ISelectedSessionDomainService
    {
        // Test double: the current selection counts as being in the world, and
        // remembers which sessions were fenced.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<
            Dfo.Server.Data.World.SelectedSessionIdentity, bool> testDoubleFences = new();
        void Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.EnterWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) { }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.Fence(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
            && testDoubleFences.TryAdd(session, true) | true;
        Dfo.Server.Data.World.SelectedSessionStanding? Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.StandingOf(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).Find(session.AccountId) == session
                ? new(Dfo.Server.Data.World.SelectedSessionPhase.InWorld, testDoubleFences.ContainsKey(session), false)
                : null;
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsInWorld(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session)
                is { Phase: Dfo.Server.Data.World.SelectedSessionPhase.InWorld };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.IsSelected(
            Dfo.Server.Data.World.SelectedSessionIdentity session) =>
            ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).StandingOf(session) is { ConnectionRevoked: false };
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Characters.CharacterId characterId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByCharacter(characterId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            ushort wireUserId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByWireUserId(wireUserId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }
        bool Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService.TryGetInWorld(
            Dfo.Server.Data.Networking.NetworkConnectionId connectionId,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Dfo.Server.Data.World.SelectedSessionIdentity? session)
        {
            session = ((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).FindByConnection(connectionId);
            if (session is not null && !((Dfo.Server.Abstractions.Sessions.ISelectedSessionDomainService)this).IsInWorld(session)) session = null;
            return session is not null;
        }

        public SelectedSessionIdentity Current { get; set; } = current;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId id) => Current.ConnectionId == id ? Current : null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId id) => Current.CharacterId == id ? Current : null;
        public SelectedSessionIdentity? Find(AccountId id) => Current.AccountId == id ? Current : null;
        public SelectedSessionIdentity? FindByWireUserId(ushort id) => Current.WireUserId == id ? Current : null;
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
    }

    private sealed class Sender : IGameMessageSender
    {
        public List<WeaknessProjectionNotification> Sent { get; } = [];
        public ValueTask SendNotiAsync(NetworkConnectionId id, IGameNotiMessage message, CancellationToken token)
        { Sent.Add(Assert.IsType<WeaknessProjectionNotification>(message)); return ValueTask.CompletedTask; }
        public ValueTask SendCmdAsync(NetworkConnectionId id, IGameCmdMessage message, CancellationToken token) => throw new NotSupportedException();
        public ValueTask SendExplicitAsync(NetworkConnectionId id, GameExplicitMessageDelivery delivery, CancellationToken token) => throw new NotSupportedException();
    }
}
