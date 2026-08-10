using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Data.OnlineUsers;

public readonly record struct OnlineUserId(Guid Value)
{
    public static OnlineUserId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

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
    OnlineUserState State,
    long Generation);
