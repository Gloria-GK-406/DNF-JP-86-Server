using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Tests.Support;

internal sealed class StubPartyMemberStatusDomainService(
    byte hpPercent = 100,
    PartyInviteDisplayState? invitation = null) : IPartyMemberStatusDomainService
{
    public ValueTask<PartyInviteDisplayState> InvitationOfAsync(
        SelectedSessionIdentity session,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(invitation ?? new PartyInviteDisplayState(0, 0, 0, 0));

    public byte CurrentHpPercentOf(SelectedSessionIdentity session) => hpPercent;
}
