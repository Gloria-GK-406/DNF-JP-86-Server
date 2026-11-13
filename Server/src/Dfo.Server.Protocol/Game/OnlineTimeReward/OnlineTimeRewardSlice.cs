using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Notifications;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.OnlineTimeReward;

/// <summary>What one online-time payment shows the stay that earned it: the new wallet, then an optional notice.</summary>
public sealed record OnlineTimeRewardPaid(SelectedSessionIdentity Session, int Cera, int TokenCera, bool Notice)
    : INotification<OnlineTimeRewardPaid>;

/// <summary>Shows a committed online-time payment to the session that earned it, if it is still in the world.</summary>
/// <remarks>Live only: a character who left before the delivery sees the new balance at the next login.</remarks>
internal sealed class OnlineTimeRewardGrantedSubscriber(ISelectedSessionDomainService sessions, IEventService notifications)
    : IDomainEventSubscriber<OnlineTimeRewardGranted>
{
    public async ValueTask HandleAsync(DomainEventEnvelope<OnlineTimeRewardGranted> message, CancellationToken ct)
    {
        var paid = message.Payload;
        var owner = sessions.Find(paid.AccountId);
        if (owner is null || owner.CharacterId != paid.CharacterId || owner.ConnectionId.Value != paid.ConnectionId
            || owner.ConnectionGeneration != paid.ConnectionGeneration || !sessions.IsInWorld(owner)) return;
        await notifications.ProjectAsync(new NotificationAudience.TheseSessions([owner]),
            new OnlineTimeRewardPaid(owner, paid.Cera, paid.TokenCera, paid.Notice), ct).ConfigureAwait(false);
    }
}

public sealed class OnlineTimeRewardPaidHandler(IAccountWalletDomainService wallets, ISelectedSessionDomainService sessions)
    : INotificationHandler<OnlineTimeRewardPaid>
{
    public async ValueTask<IReadOnlyList<IServerNetworkMessage>> ComposeAsync(NotificationRecipient recipient, OnlineTimeRewardPaid notice, CancellationToken token)
    {
        var session = notice.Session;
        if (recipient.CharacterId != session.CharacterId || recipient.ConnectionId != session.ConnectionId || !sessions.IsInWorld(session)) return [];
        var balances = await wallets.ReadAsync(session.AccountId, token).ConfigureAwait(false);
        if (balances is null) return [];
        return notice.Notice
            ? [GameInstanceDelivery.Wallet(balances), new GameServerNoticeMessage(Text(notice.Cera, notice.TokenCera))]
            : [GameInstanceDelivery.Wallet(balances)];
    }

    internal static string Text(int cera, int tokenCera)
    {
        var parts = new List<string>(2);
        if (cera > 0) parts.Add($"{cera} 点券");
        if (tokenCera > 0) parts.Add($"{tokenCera} 代币");
        return "在线奖励：获得 " + string.Join("、", parts) + "。";
    }
}

internal static class OnlineTimeRewardSliceRegistration
{
    internal static IServiceCollection AddOnlineTimeRewardSlice(this IServiceCollection services)
    {
        services.AddDomainSubscriber<OnlineTimeRewardGranted, OnlineTimeRewardGrantedSubscriber>(
            "activity.online-time-reward.notifications", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<OnlineTimeRewardPaid, OnlineTimeRewardPaidHandler>();
        return services;
    }
}
