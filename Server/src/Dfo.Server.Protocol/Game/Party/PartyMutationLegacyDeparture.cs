using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Protocol.Game.Party;

internal abstract class PartyMutationLegacyDeparture : PartyMutationProtocolCommandHandler
{
    private PartyMutationLegacyDeparture() : base(null!, null, null, null!, null!, null, null!) { }
    internal static ValueTask<PartyOperationResult> LeaveAsync(IPartyService parties, IDungeonRunDomainService? runs,
        CharacterId character, CancellationToken token) => MutatePartyStateAsync(parties, runs, PartyChange.Leave, character, null, token);
}
