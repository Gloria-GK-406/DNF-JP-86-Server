using Dfo.Server.Abstractions.Administration;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game;

public sealed class AccountWalletChangedHandler(IAccountWalletDomainService wallets, ISelectedSessionDomainService sessions)
    : INotificationHandler<AccountWalletChanged>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, AccountWalletChanged notice, CancellationToken token)
    {
        var current = sessions.Find(notice.Account);
        if (current is null || current.CharacterId != recipient.CharacterId || current.ConnectionId != recipient.ConnectionId
            || recipient.ExpectedSession != current) return [];
        var balances = await wallets.ReadAsync(notice.Account, token);
        if (balances is null || sessions.Find(notice.Account) != current) return [];
        return [GameInstanceDelivery.Wallet(balances)];
    }
}
