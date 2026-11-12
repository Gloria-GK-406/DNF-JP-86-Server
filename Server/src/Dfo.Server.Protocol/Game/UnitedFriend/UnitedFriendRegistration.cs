using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.UnitedFriend;

/// <summary>What the UnitedFriend slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class UnitedFriendRegistration
{
    internal static IServiceCollection AddUnitedFriendSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<AddFriendDecoder>(ClientMessageType.GameAddFriend);
        services.AddGameCmdMessageDecoder<DeleteFriendDecoder>(ClientMessageType.GameDeleteFriend);
        services.AddGameCmdMessageEncoder<AddFriendResponse, AddFriendResponseEncoder>();
        services.AddGameCmdMessageEncoder<DeleteFriendResponse, DeleteFriendResponseEncoder>();
        // The friend frames live at the Game layer because selection and the observer pushes send
        // them too; their one encoder registration each is here, and serves every sender.
        services.AddGameNotiMessageEncoder<FriendListMessage, FriendListEncoder>();
        services.AddGameNotiMessageEncoder<FriendRemovedMessage, FriendRemovedEncoder>();
        services.AddGameNotiMessageEncoder<FriendPresenceMessage, FriendPresenceEncoder>();
        // The pushes to a character's observers (Spec RQ-050 – RQ-053); they are raised from character
        // selection, the three cleanup deliveries, character deletion and the subscriptions below.
        services.AddNotificationHandler<FriendCameOnline, FriendCameOnlineHandler>();
        services.AddNotificationHandler<FriendWentOffline, FriendWentOfflineHandler>();
        services.AddNotificationHandler<FriendListChanged, FriendListChangedHandler>();
        services.AddNotificationHandler<FriendRemoved, FriendRemovedHandler>();
        services.AddDomainSubscriber<CharacterLevelChanged, FriendObserverRefreshSubscriber>("friend.level-refresh",
            DomainEventDeliveryPolicy.LiveOnly, change => FriendObserverRefreshSubscriber.KeyOf(change.CharacterId));
        services.AddDomainSubscriber<CharacterGrowTypeChanged, FriendObserverRefreshSubscriber>("friend.grow-type-refresh",
            DomainEventDeliveryPolicy.LiveOnly, change => FriendObserverRefreshSubscriber.KeyOf(change.CharacterId));
        services.AddProtocolCommandHandler<GameAddFriendCommand, AddFriendResult, GameAddFriendCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAddFriend, typeof(AddFriendRequest)));
        services.AddProtocolCommandHandler<GameDeleteFriendCommand, bool, GameDeleteFriendCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteFriend, typeof(DeleteFriendRequest)));
        services.RegisterProtocolMessageHandler<AddFriendHandler, AddFriendRequest, GameAddFriendCommand>(ClientMessageType.GameAddFriend);
        services.RegisterProtocolMessageHandler<DeleteFriendHandler, DeleteFriendRequest, GameDeleteFriendCommand>(ClientMessageType.GameDeleteFriend);
        return services;
    }
}
