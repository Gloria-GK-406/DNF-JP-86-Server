using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Sessions;

/// <summary>Maps transport ownership to the area's opaque membership generation.</summary>
public static class TownAreaSessionBridge
{
    public static TownAreaMembershipLease TownAreaLease(this SelectedSessionIdentity session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return new TownAreaMembershipLease(session.ConnectionGeneration);
    }

    public static bool Owns(this SelectedSessionIdentity session, TownAreaMember member)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(member);
        return session.CharacterId == member.CharacterId
            && session.ConnectionGeneration == member.Lease.Value;
    }
}
