using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Accounts;

internal sealed class CrystalContractDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, IPremiumServiceDomainService premiums,
    ICharacterConditionTable conditions) : ICrystalContractDomainService
{
    public async ValueTask<bool> SelectAsync(SelectedSessionIdentity session, byte mode, byte selection, CancellationToken ct)
    {
        // mode is the client's UI-origin flag; it is not a material ID or a consumption operation.
        if (mode > 1 || selection is > 5 and not 255
            || sessions.Find(session.AccountId) != session
            || !users.TryGetByConnection(session.ConnectionId, out var user)
            || user is not { State: OnlineUserState.CharacterActive }
            || user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId)
            return false;
        if (!await premiums.For(session.AccountId).IsActiveAsync(PremiumServiceType.CrystalContract, ct))
            return false;
        await conditions.SaveCrystalSelectionAsync(session.CharacterId, mode, selection, ct);
        return true;
    }
}
