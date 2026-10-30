using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.MoveItemSpace;
using Dfo.Server.Protocol.Game.TutorialLevelUp;
using Dfo.Server.Protocol.Game.UnansweredReports;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.MapEntryReports;
using Dfo.Server.Protocol.Game.SelectCharacter;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.TutorialLevelUp;

/// <summary>What the TutorialLevelUp slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TutorialLevelUpRegistration
{
    internal static IServiceCollection AddTutorialLevelUpSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<TutorialLevelUpDecoder>(ClientMessageType.GameTutorialLevelUp);
        services.AddGameCmdMessageEncoder<TutorialLevelUpResponse, TutorialLevelUpResponseEncoder>();
        // The tutorial's level: one row, written once, and the client is told what it now stands at.
        services.AddProtocolCommandHandler<GameTutorialLevelUpCommand, bool, GameTutorialLevelUpCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameTutorialLevelUp, typeof(TutorialLevelUpRequest)));
        services.RegisterProtocolMessageHandler<TutorialLevelUpHandler, TutorialLevelUpRequest, GameTutorialLevelUpCommand>(ClientMessageType.GameTutorialLevelUp);
        return services;
    }
}
