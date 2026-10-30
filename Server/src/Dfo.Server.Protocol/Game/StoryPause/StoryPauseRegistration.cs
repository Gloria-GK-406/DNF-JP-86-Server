using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.StoryPause;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.StoryPause;

/// <summary>What the StoryPause slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class StoryPauseRegistration
{
    internal static IServiceCollection AddStoryPauseSlice(this IServiceCollection services)
    {
        services.AddGameNotiMessageEncoder<StoryPauseNotification, StoryPauseEncoder>();
        services.AddGameCmdMessageDecoder<StoryPauseDecoder>(ClientMessageType.GameStoryPause);
        services.AddProtocolCommandHandler<GameStoryPauseCommand, bool, GameStoryPauseCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameStoryPause, typeof(StoryPauseRequest)));
        services.RegisterProtocolMessageHandler<StoryPauseHandler, StoryPauseRequest, GameStoryPauseCommand>(ClientMessageType.GameStoryPause);
        return services;
    }
}
