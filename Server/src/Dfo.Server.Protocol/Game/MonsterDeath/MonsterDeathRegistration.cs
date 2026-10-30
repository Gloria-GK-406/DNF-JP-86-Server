using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MonsterDeath;

/// <summary>What the MonsterDeath slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MonsterDeathRegistration
{
    internal static IServiceCollection AddMonsterDeathSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<ExperienceNotification, ExperienceEncoder>();
        services.AddGameNotiMessageEncoder<MonsterDeathNotification, MonsterDeathEncoder>();
        services.AddGameCmdMessageDecoder<MonsterDeathDecoder>(ClientMessageType.GameMonsterDeath);
        services.AddProtocolCommandHandler<GameMonsterDeathCommand, bool, GameMonsterDeathCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameMonsterDeath, typeof(MonsterDeathRequest)));
        services.RegisterProtocolMessageHandler<MonsterDeathHandler, MonsterDeathRequest, GameMonsterDeathCommand>(ClientMessageType.GameMonsterDeath);
        return services;
    }
}
