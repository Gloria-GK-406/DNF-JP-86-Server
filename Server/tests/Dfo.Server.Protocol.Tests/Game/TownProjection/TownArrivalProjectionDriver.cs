using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

// These component observations begin at town admission, excluding the separate weakness stage.
// The real public delivery always includes weakness; CMD coverage exercises that full boundary.
internal sealed class TownArrivalProjectionDriver(params object?[] services)
{
    private readonly TownTransitDomainService transit = ProtocolAlgorithmFixture.Create<TownTransitDomainService>(services);
    private readonly TownAreaProjectionDelivery delivery = ProtocolAlgorithmFixture.Create<TownAreaProjectionDelivery>(services);

    public async ValueTask<int> EnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token)
    {
        var requests = TownArrivalProjection.TownReturnRequests(entries);
        var admitted = await transit.ReturnToTownAsync(requests, token).ConfigureAwait(false);
        return await TownArrivalProjection.ProjectAsync(
            ProtocolAlgorithmFixture.Field<ISelectedSessionDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<ITownAreaDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<ISelectedCharacterCurrentViewDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<IGameCommandOutbox>(delivery),
            ProtocolAlgorithmFixture.Default<IWeaknessDomainService>(), false,
            ProtocolAlgorithmFixture.Field<ICommandProjections>(delivery),
            ProtocolAlgorithmFixture.Field<IPartyTownProjectionDelivery>(delivery),
            TownArrivalProjection.TownAdmissions(entries, requests, admitted), token).ConfigureAwait(false);
    }
}
