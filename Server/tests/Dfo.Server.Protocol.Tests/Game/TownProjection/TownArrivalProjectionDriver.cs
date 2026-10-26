using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.Party;
using Dfo.Server.Protocol.Game.TownProjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests.Game.TownProjection;

// These component observations begin at town admission, excluding the separate weakness stage.
// The real public delivery always includes weakness; CMD coverage exercises that full boundary.
internal sealed class TownArrivalProjectionDriver(TownAreaProjectionDelivery delivery)
{
    public ValueTask<int> EnterAsync(IReadOnlyList<TownAreaEntry> entries, CancellationToken token) =>
        TownArrivalProjection.EnterAsync(
            ProtocolAlgorithmFixture.Field<ISelectedSessionDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<ITownMovementDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<IMarriageHousingQueryDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<ITownAreaDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<ISelectedCharacterCurrentViewDomainService>(delivery),
            ProtocolAlgorithmFixture.Field<IGameCommandOutbox>(delivery),
            ProtocolAlgorithmFixture.Field<IWeaknessDomainService>(delivery), false,
            new LegacyCommandProjections(ProtocolAlgorithmFixture.Field<IEventService>(delivery)),
            ProtocolAlgorithmFixture.Field<IPartyTownProjectionDelivery>(delivery), entries, token);
}
