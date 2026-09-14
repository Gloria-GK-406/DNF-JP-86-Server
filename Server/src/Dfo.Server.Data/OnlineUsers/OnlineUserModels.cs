using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Data.OnlineUsers;

public readonly record struct OnlineUserId(Guid Value)
{
    public static OnlineUserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

/// <summary>The immutable identity of one accepted game connection.</summary>
/// <remarks>
/// This identity deliberately survives authentication and character selection transitions. A
/// character and a UDP endpoint are both attached to this session; neither is attached to the
/// other.
/// </remarks>
public readonly record struct GameSessionIdentity(
    OnlineUserId OnlineUserId,
    NetworkConnectionId ConnectionId);

public enum OnlineUserState
{
    Connected = 0,
    Authenticated = 1,
    CharacterActive = 2,
}

public sealed record OnlineUserSnapshot(
    OnlineUserId OnlineUserId,
    NetworkConnectionContext Connection,
    AccountId? AccountId,
    CharacterId? CurrentCharacterId,
    OnlineUserState State,
    long Generation)
{
    public GameSessionIdentity SessionIdentity =>
        new(OnlineUserId, Connection.ConnectionId);
}
