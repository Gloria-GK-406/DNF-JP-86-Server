using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.UseStackable;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.UseStackable;

/// <summary>What the UseStackable slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class UseStackableRegistration
{
    internal static IServiceCollection AddUseStackableSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectDecoder>(0x00FB, ClientMessageType.GameSharedEffect);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.UseStackable.SharedEffectHandler, Dfo.Server.Protocol.Game.UseStackable.SharedEffectRequest, Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommand>(ClientMessageType.GameSharedEffect);
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectAck, Dfo.Server.Protocol.Game.UseStackable.SharedEffectAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.UseStackable.SharedEffectNotification, Dfo.Server.Protocol.Game.UseStackable.SharedEffectNotificationEncoder>();
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommand, bool, Dfo.Server.Protocol.Game.UseStackable.GameSharedEffectCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameSharedEffect, typeof(Dfo.Server.Protocol.Game.UseStackable.SharedEffectRequest)));
        services.AddGameCmdMessageDecoder<UseStackableDecoder>(
            GameProtocol.UseStackableType, ClientMessageType.GameUseStackable);
        services.AddGameCmdMessageEncoder<UseStackableResponse, UseStackableResponseEncoder>();
        services.AddProtocolCommandHandler<GameUseStackableCommand, ItemUseResult, GameUseStackableCommandHandler>(
            new[] { CommandRegistrationExit.SettledRequiredExplicit, CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameUseStackable, typeof(UseStackableRequest)));
        services.RegisterProtocolMessageHandler<UseStackableHandler, UseStackableRequest, GameUseStackableCommand>(ClientMessageType.GameUseStackable);
        return services;
    }
}
