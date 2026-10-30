using Dfo.Server.Application.Execution;
using Dfo.Server.Application.DomainEvents;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.CharacterDeath;

/// <summary>What the CharacterDeath slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CharacterDeathRegistration
{
    internal static IServiceCollection AddCharacterDeathSlice(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddTypedGameplayTimer<DungeonPartyWipeService>();
        var revivalFlag = configuration.GetValue<byte>("Dungeon:RevivalAckFlag", 1);
        if (revivalFlag > 1) throw new InvalidOperationException("RevivalAckFlag must be 0 or 1.");
        services.TryAddSingleton(new RevivalOptions(revivalFlag));
        services.AddGameNotiMessageEncoder<CharacterLifeStateNotification, CharacterLifeStateEncoder>();
        services.AddGameCmdMessageEncoder<CharacterDeathResponse, CharacterDeathResponseEncoder>();
        services.AddGameCmdMessageEncoder<UseRevivalCoinResponse, UseRevivalCoinEncoder>();
        services.AddGameCmdMessageDecoder<UseRevivalCoinDecoder>(0x0029, ClientMessageType.GameUseRevivalCoin);
        services.AddGameCmdMessageDecoder<CharacterDeathDecoder>(GameProtocol.CharacterDeathType, ClientMessageType.GameCharacterDeath);
        services.AddProtocolCommandHandler<GameUseRevivalCoinCommand, bool, GameUseRevivalCoinCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameUseRevivalCoin, typeof(UseRevivalCoinRequest)));
        services.RegisterProtocolMessageHandler<UseRevivalCoinHandler, UseRevivalCoinRequest, GameUseRevivalCoinCommand>(ClientMessageType.GameUseRevivalCoin);
        services.AddProtocolCommandHandler<GameCharacterDeathCommand, bool, GameCharacterDeathCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCharacterDeath, typeof(CharacterDeathRequest)));
        services.RegisterProtocolMessageHandler<CharacterDeathHandler, CharacterDeathRequest, GameCharacterDeathCommand>(ClientMessageType.GameCharacterDeath);
        return services;
    }
}
