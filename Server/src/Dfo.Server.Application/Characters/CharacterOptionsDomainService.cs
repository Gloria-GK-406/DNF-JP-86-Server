using System.Buffers.Binary;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Characters;

internal sealed class CharacterOptionsDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService users, ICharacterTable characters, ICharacterSettingsTable settings,
    IDomainTransactionAccessor transactions) : ICharacterOptionsDomainService
{
    public async ValueTask<bool> SaveAsync(SelectedSessionIdentity session, byte[] body, CancellationToken ct)
    {
        if (transactions.Current is null) throw new InvalidOperationException("Saving character options requires a transaction.");
        if (body.Length != 516 || BinaryPrimitives.ReadUInt32LittleEndian(body) != 512) return false;
        if (sessions.Find(session.AccountId) != session
            || !users.TryGetByConnection(session.ConnectionId,out var user)
            || user is not { State: OnlineUserState.CharacterActive }
            || user.AccountId != session.AccountId || user.CurrentCharacterId != session.CharacterId
            || await characters.FindOwnerAsync(session.CharacterId,ct) != session.AccountId) return false;
        // Transitional opaque storage, including the prefix consumed by NOTI 0187. See GH #129.
        await settings.SaveOptionAsync(session.CharacterId,body,ct);
        return true;
    }
}
