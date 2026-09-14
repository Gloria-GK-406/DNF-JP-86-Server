using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Parties;

namespace Dfo.Server.Application.Tests.Support;

internal sealed class StubPartyMemberStatusDomainService(
    byte hpPercent = 100,
    PartyInviteDisplayState? invitation = null) : IPartyMemberStatusDomainService
{
    public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
        PartyCharacterSnapshot character,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(invitation ?? new PartyInviteDisplayState(0, 0, 0, 0));

    public byte CurrentHpPercentOf(CharacterId characterId) => hpPercent;
}
