using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestCompletion;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.QuestCompletion;

/// <summary>What the QuestCompletion slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class QuestCompletionRegistration
{
    internal static IServiceCollection AddQuestCompletionSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<FinishQuestMessageDecoder>(
            GameProtocol.FinishQuestType, ClientMessageType.GameFinishQuest);
        services.AddGameCmdMessageEncoder<FinishQuestAcknowledgement, FinishQuestAcknowledgementEncoder>();
        services.AddProtocolCommandHandler<GameFinishQuestCommand, QuestFinishOutcome, GameFinishQuestCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameFinishQuest, typeof(FinishQuestRequest)));
        services.RegisterProtocolMessageHandler<FinishQuestHandler, FinishQuestRequest, GameFinishQuestCommand>(ClientMessageType.GameFinishQuest);
        return services;
    }
}
