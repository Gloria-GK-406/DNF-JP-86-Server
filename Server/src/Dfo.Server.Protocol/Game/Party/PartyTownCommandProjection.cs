using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Execution;
using System.Collections.Immutable;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Protocol.Game.Party;

internal sealed class PartyTownCommandProjection(IPartyService parties, ITownAreaDomainService townAreas,
    ISelectedSessionDomainService selectedSessions, ICommandProjections notifications, Func<long, IPartyDeliveryStream> streamOf)
    : PartyMutationProtocolCommandHandler(selectedSessions, parties, townAreas, notifications, streamOf, null,
        Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance), IPartyTownProjectionDelivery
{
    public ValueTask RefreshMovedPartyAsync(SelectedSessionIdentity mover, PartyDirectoryScope? previousScope,
        CancellationToken token) => PartyTownRefreshMovedPartyAsync(mover, previousScope, token);
    public ValueTask RefreshObservedPartiesAsync(IReadOnlyList<SelectedSessionIdentity> recipients, CancellationToken token) =>
        PartyTownRefreshObservedPartiesAsync(recipients, token);
}
