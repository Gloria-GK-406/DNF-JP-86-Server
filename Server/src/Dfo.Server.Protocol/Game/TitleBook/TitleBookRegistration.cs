using Dfo.Server.Protocol.Game.TitleBook;
using Dfo.Server.Abstractions.Achievements;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StateSynchronization;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.TitleBook;

/// <summary>What the TitleBook slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TitleBookRegistration
{
    internal static IServiceCollection AddTitleBookSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<GameTitleBookMutationCommand, bool, GameTitleBookMutationCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameStoreTitle, typeof(TitleBookMutationRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameWithdrawTitle, typeof(TitleBookMutationRequest)));
        services.AddProtocolCommandHandler<GameQueryTitleBookCommand, bool, GameQueryTitleBookCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameQueryTitleBook, typeof(QueryTitleBookRequest)));
        services.AddProtocolCommandHandler<GameSetTitleAppearanceCommand, bool, GameSetTitleAppearanceCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetTitleAppearance, typeof(SetTitleAppearanceRequest)));
        services.AddDomainSubscriber<TitleUnlocked, TitleBookEventSubscriber>("title.noti-unlock", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<TitleStored, TitleBookEventSubscriber>("title.noti-store", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<TitleWithdrawn, TitleBookEventSubscriber>("title.noti-withdraw", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<ItemMirrorRemoved, TitleBookEventSubscriber>("inventory.noti-mirror-removed", DomainEventDeliveryPolicy.LiveOnly);
        services.AddNotificationHandler<TitleBookChanged, TitleBookChangedHandler>();
        services.RegisterProtocolMessageHandler<SetTitleAppearanceHandler, SetTitleAppearanceRequest, GameSetTitleAppearanceCommand>(ClientMessageType.GameSetTitleAppearance);
        services.AddGameCmdMessageDecoder<SetTitleAppearanceDecoder>(0x0239, ClientMessageType.GameSetTitleAppearance);
        services.AddGameCmdMessageEncoder<SetTitleAppearanceResponse, SetTitleAppearanceEncoder>();
        services.AddDomainSubscriber<TitleAppearanceChanged, TitleBookEventSubscriber>("title.noti-appearance", DomainEventDeliveryPolicy.LiveOnly);
        services.RegisterProtocolMessageHandler<TitleBookMutationHandler, TitleBookMutationRequest, GameTitleBookMutationCommand>(ClientMessageType.GameStoreTitle);
        services.RegisterProtocolMessageHandler<TitleBookMutationHandler, TitleBookMutationRequest, GameTitleBookMutationCommand>(ClientMessageType.GameWithdrawTitle);
        services.RegisterProtocolMessageHandler<QueryTitleBookHandler, QueryTitleBookRequest, GameQueryTitleBookCommand>(ClientMessageType.GameQueryTitleBook);
        services.AddGameCmdMessageDecoder<StoreTitleDecoder>(0x019C, ClientMessageType.GameStoreTitle);
        services.AddGameCmdMessageDecoder<WithdrawTitleDecoder>(0x019D, ClientMessageType.GameWithdrawTitle);
        services.AddGameCmdMessageDecoder<QueryTitleBookDecoder>(0x01A8, ClientMessageType.GameQueryTitleBook);
        services.AddGameCmdMessageEncoder<TitleBookMutationResponse, TitleBookMutationEncoder>();
        services.AddGameNotiMessageEncoder<TitleUnlockedMessage, GameNotiStateSynchronizationEncoder<TitleUnlockedMessage>>();
        return services;
    }
}
