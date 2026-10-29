using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Creatures;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dfo.Server.Testing;

/// <summary>Builds the settlement fence over a test container, filling in the parts a protocol test has no use for.</summary>
internal static class TestSessionSafety
{
    /// <summary>
    /// For hosts that compose the command mechanism without the online state, the selected sessions or the creature
    /// runtime: the fence is built over whichever of them the host has, and nobody online for the rest.
    /// </summary>
    /// <remarks>Added rather than tried, so it replaces the mechanism's own registration whichever is called first.</remarks>
    public static IServiceCollection AddSessionSafetyStandIns(this IServiceCollection services) =>
        services.AddSingleton(Create);

    public static SelectedSessionSafety Create(IServiceProvider services) => new(
        services.GetService<IOnlineUserQueryDomainService>() ?? new NoOnlineUsers(),
        services.GetService<ISelectedSessionDomainService>() ?? new NoSelectedSessions(),
        services.GetService<ICreatureRuntimeDomainService>() ?? new NoCreatureRuntime(),
        services.GetService<INetworkConnectionCloser>() ?? new NoCloser(),
        services.GetService<ILogger<SelectedSessionSafety>>() ?? NullLogger<SelectedSessionSafety>.Instance);

    /// <summary>Nobody is online.</summary>
    private sealed class NoOnlineUsers : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId onlineUserId) => throw new KeyNotFoundException();
        public bool TryGetByConnection(NetworkConnectionId connectionId, out OnlineUserSnapshot? onlineUser)
        { onlineUser = null; return false; }
    }

    /// <summary>No session is selected, so every session is stale and fencing one finds nothing.</summary>
    private sealed class NoSelectedSessions : ISelectedSessionDomainService
    {
        public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => throw new NotSupportedException();
        public void EnterWorld(SelectedSessionIdentity session) => throw new NotSupportedException();
        public bool Fence(SelectedSessionIdentity session) => false;
        public bool ReleaseExact(SelectedSessionIdentity session) => throw new NotSupportedException();
        public SelectedSessionIdentity? Find(AccountId accountId) => null;
        public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) => null;
        public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) => null;
        public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) => null;
        public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) => null;
        public bool IsSelected(SelectedSessionIdentity session) => false;
        public bool IsInWorld(SelectedSessionIdentity session) => false;
        public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session) { session = null; return false; }
        public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) { session = null; return false; }
        public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) { session = null; return false; }
    }

    private sealed class NoCloser : INetworkConnectionCloser
    {
        public ValueTask RequestCloseExactAsync(NetworkConnectionContext expected, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RequestCloseAsync(NetworkConnectionId connectionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}

/// <summary>A creature runtime with nothing running: discarding is a no-op and everything else is unexpected.</summary>
internal sealed class NoCreatureRuntime : ICreatureRuntimeDomainService
{
    public void DiscardExact(SelectedSessionIdentity session) { }
    public ValueTask<CreatureTransformationReceipt?> HatchAsync(SelectedSessionIdentity session, BusinessSlot eggSlot, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void Enter(SelectedSessionIdentity session) => throw new NotSupportedException();
    public void QueueRoomExperience(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount) => throw new NotSupportedException();
    public IReadOnlyList<CreaturePendingProjection> DrainProjections() => throw new NotSupportedException();
    public bool IsEquipped(SelectedSessionIdentity session, int itemUid) => throw new NotSupportedException();
    public ValueTask LeaveAsync(SelectedSessionIdentity session, CancellationToken cancellationToken) => throw new NotSupportedException();
    public IReadOnlyList<SelectedSessionIdentity> DueSessions() => throw new NotSupportedException();
    public ValueTask<IReadOnlyList<CreatureRuntimeChange>> AdvanceAsync(SelectedSessionIdentity session, CancellationToken cancellationToken, bool? combat = null) => throw new NotSupportedException();
    public ValueTask<CreatureRuntimeChange?> GainExperienceAsync(SelectedSessionIdentity session, int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
    public ValueTask<CreatureRuntimeChange?> GainRoomExperienceAsync(SelectedSessionIdentity session, DungeonRunId runId, MazeCell cell, int amount, CancellationToken cancellationToken) => throw new NotSupportedException();
    public void Invalidate(CharacterId characterId) => throw new NotSupportedException();
    public void ChangeCombat(CharacterId characterId, bool combat) => throw new NotSupportedException();
}
