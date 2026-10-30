using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MarriageHousing;

/// <summary>What the MarriageHousing slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MarriageHousingRegistration
{
    internal static IServiceCollection AddMarriageHousingSlice(this IServiceCollection services)
    {
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageHousing.GameHousingCommand, bool, Dfo.Server.Protocol.Game.MarriageHousing.GameHousingCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMarriageHousing, typeof(Dfo.Server.Protocol.Game.MarriageHousing.HousingNetworkRequest)));
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.MarriageHousing.GameCapsuleCommand, bool, Dfo.Server.Protocol.Game.MarriageHousing.GameCapsuleCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameMarriageCapsule, typeof(Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleMakeDecoder>(0x0404, ClientMessageType.GameMarriageCapsule);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleGiveDecoder>(0x0405, ClientMessageType.GameMarriageCapsule);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleTakeDecoder>(0x0406, ClientMessageType.GameMarriageCapsule);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleHandler, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRequest, Dfo.Server.Protocol.Game.MarriageHousing.GameCapsuleCommand>(ClientMessageType.GameMarriageCapsule);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleAck, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleStateMessage, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleStateEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyMessage, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyEncoder>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRefresh, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleRefreshHandler>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyRefresh, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergyRefreshHandler>();
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Marriage.MarriageCapsuleEnergyChanged, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleEnergySubscriber>("marriage.capsule-notification", DomainEventDeliveryPolicy.LiveOnly);
        services.AddDomainSubscriber<Dfo.Server.Abstractions.Marriage.MarriageCapsuleGiftChanged, Dfo.Server.Protocol.Game.MarriageHousing.CapsuleGiftSubscriber>("marriage.capsule-gift-notification", DomainEventDeliveryPolicy.LiveOnly);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingStorageDecoder>(0x03FD, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingMoveDecoder>(0x03FE, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingDeleteDecoder>(0x03FF, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingPlantDecoder>(0x0400, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingWaterDecoder>(0x0401, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingHarvestDecoder>(0x0402, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingClearDecoder>(0x0403, ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingRingDecoder>(0x03F4, ClientMessageType.GameMarriageHousing);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.MarriageHousing.HousingHandler, Dfo.Server.Protocol.Game.MarriageHousing.HousingNetworkRequest, Dfo.Server.Protocol.Game.MarriageHousing.GameHousingCommand>(ClientMessageType.GameMarriageHousing);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.MarriageHousing.HousingAck, Dfo.Server.Protocol.Game.MarriageHousing.HousingAckEncoder>();
        services.AddNotificationHandler<Dfo.Server.Protocol.Game.MarriageHousing.HousingChanged, Dfo.Server.Protocol.Game.MarriageHousing.HousingChangedHandler>();
        return services;
    }
}
