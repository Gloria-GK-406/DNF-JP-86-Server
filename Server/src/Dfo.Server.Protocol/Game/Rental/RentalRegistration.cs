using Dfo.Server.Protocol.Game.Rental;
using Dfo.Server.Abstractions.Rental;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Rental;

/// <summary>What the Rental slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class RentalRegistration
{
    internal static IServiceCollection AddRentalSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<RentEquipmentDecoder>(ClientMessageType.GameRentEquipment);
        services.AddGameCmdMessageDecoder<ChargeRentPointDecoder>(ClientMessageType.GameChargeRentPoint);
        services.RegisterProtocolMessageHandler<RentEquipmentHandler,RentEquipmentRequest,GameRentEquipmentCommand>(ClientMessageType.GameRentEquipment);
        services.RegisterProtocolMessageHandler<ChargeRentPointHandler,ChargeRentPointRequest,GameChargeRentPointCommand>(ClientMessageType.GameChargeRentPoint);
        services.AddGameCmdMessageEncoder<RentEquipmentResponse, RentEquipmentEncoder>();
        services.AddGameCmdMessageEncoder<ChargeRentPointResponse, ChargeRentPointEncoder>();
        services.AddProtocolCommandHandler<GameRentEquipmentCommand,bool,GameRentEquipmentCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameRentEquipment,typeof(RentEquipmentRequest)));
        services.AddProtocolCommandHandler<GameChargeRentPointCommand,bool,GameChargeRentPointCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameChargeRentPoint,typeof(ChargeRentPointRequest)));
        services.AddDomainSubscriber<LuckyStarChanged, LuckyStarChangedSubscriber>("rental.lucky-star-refresh", DomainEventDeliveryPolicy.LiveOnly, x=>x.AccountId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        services.AddNotificationHandler<LuckyStarPanelRefresh, LuckyStarPanelRefreshHandler>();
        return services;
    }
}
