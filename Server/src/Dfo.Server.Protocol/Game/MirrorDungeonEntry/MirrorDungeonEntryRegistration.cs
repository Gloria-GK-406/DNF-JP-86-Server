using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.MirrorDungeonEntry;

/// <summary>What the MirrorDungeonEntry slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class MirrorDungeonEntryRegistration
{
    internal static IServiceCollection AddMirrorDungeonEntrySlice(this IServiceCollection services)
    {

        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonEntryDecoder>(0x02B6, ClientMessageType.GameMirrorDungeonEntry);
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonMissionNotification, Dfo.Server.Protocol.Game.MirrorDungeonEntry.MirrorDungeonMissionEncoder>();
        return services;
    }
}
