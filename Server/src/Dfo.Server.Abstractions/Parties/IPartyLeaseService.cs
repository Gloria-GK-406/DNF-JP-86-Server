using Dfo.Server.Data.Parties;

namespace Dfo.Server.Abstractions.Parties;

/// <summary>Grants an activity exclusive write access to real party containers.</summary>
public interface IPartyLeaseService
{
    /// <summary>Rents one container per squad, ordered by the 1-based squad number.</summary>
    PartyLease Rent(Guid owner, int count);

    /// <summary>Atomically replaces the entire arrangement, treating omitted squads as empty.</summary>
    /// <returns>The party changes, or null when the credential or arrangement is invalid.</returns>
    PartyLeaseMutation? Replace(PartyLease lease, IReadOnlyList<PartyLeaseSquad> squads);

    /// <summary>Returns the containers when all of them are empty.</summary>
    bool Return(PartyLease lease);
}
