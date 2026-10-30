using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.HiddenCharacterInfo;

/// <summary>What the HiddenCharacterInfo slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class HiddenCharacterInfoRegistration
{
    internal static IServiceCollection AddHiddenCharacterInfoSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<HiddenCharacterInfoMessageDecoder>(ClientMessageType.GameHiddenCharacterInfo);
        services.AddGameCmdMessageEncoder<HiddenCharacterInfoResponse, HiddenCharacterInfoResponseEncoder>();
        services.RegisterTelemetryMessageHandler<HiddenCharacterInfoHandler>(ClientMessageType.GameHiddenCharacterInfo);
        return services;
    }
}
