using System.Diagnostics.CodeAnalysis;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Users;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Sessions;

/// <summary>The selected-session responsibility of the online identity.</summary>
/// <remarks>
/// Holds nothing of its own: a selection lives in <see cref="OnlineStateRegistry"/> beside the
/// online user it belongs to, so the account, the connection and the stage of the session are
/// one record and are never read from two places.
/// </remarks>
internal sealed class SelectedSessionDomainService(OnlineStateRegistry state)
    : ISelectedSessionDomainService, ISelectedSessionSnapshotQuery
{
    public IReadOnlyList<SelectedSessionIdentity> Snapshot() => state.SnapshotSelections();

    public SelectedSessionIdentity? Select(SelectedSessionIdentity session) => state.Select(session);

    public void EnterWorld(SelectedSessionIdentity session) => state.EnterWorld(session);

    public bool Fence(SelectedSessionIdentity session) => state.Fence(session);

    public bool ReleaseExact(SelectedSessionIdentity session) => state.ReleaseExact(session);

    public SelectedSessionIdentity? Find(AccountId accountId) => state.Find(accountId);

    public SelectedSessionIdentity? FindByCharacter(CharacterId characterId) =>
        state.FindByCharacter(characterId);

    public SelectedSessionIdentity? FindByWireUserId(ushort wireUserId) =>
        state.FindByWireUserId(wireUserId);

    public SelectedSessionIdentity? FindByConnection(NetworkConnectionId connectionId) =>
        state.FindByConnection(connectionId);

    public SelectedSessionStanding? StandingOf(SelectedSessionIdentity session) =>
        state.StandingOf(session);

    public bool IsSelected(SelectedSessionIdentity session) => state.IsSelected(session);

    public bool IsInWorld(SelectedSessionIdentity session) => state.IsInWorld(session);

    public bool TryGetInWorld(CharacterId characterId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
        state.TryGetInWorld(characterId, out session);

    public bool TryGetInWorld(ushort wireUserId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
        state.TryGetInWorld(wireUserId, out session);

    public bool TryGetInWorld(NetworkConnectionId connectionId, [NotNullWhen(true)] out SelectedSessionIdentity? session) =>
        state.TryGetInWorld(connectionId, out session);
}
