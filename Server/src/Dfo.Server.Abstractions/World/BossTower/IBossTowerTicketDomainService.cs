using System.Collections.Immutable;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.World.BossTower;

public interface IBossTowerTicketDomainService
{
    ValueTask<bool> RefillOnlineTicketAsync(SelectedSessionIdentity session, CancellationToken ct);
}
public sealed record BossTowerTicketRefilled(CharacterId CharacterId, ImmutableArray<BusinessSlot> Slots) : IDomainEvent;
