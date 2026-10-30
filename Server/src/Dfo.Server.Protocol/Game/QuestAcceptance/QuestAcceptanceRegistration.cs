using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestAcceptance;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.QuestAcceptance;

/// <summary>What the QuestAcceptance slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class QuestAcceptanceRegistration
{
    internal static IServiceCollection AddQuestAcceptanceSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<AcceptQuestMessageDecoder>(ClientMessageType.GameAcceptQuest);
        services.AddGameCmdMessageEncoder<AcceptQuestAcknowledgement, AcceptQuestAcknowledgementEncoder>();
        services.AddProtocolCommandHandler<GameAcceptQuestCommand, QuestAcceptOutcome, GameAcceptQuestCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameAcceptQuest, typeof(AcceptQuestRequest)));
        services.RegisterProtocolMessageHandler<AcceptQuestHandler, AcceptQuestRequest, GameAcceptQuestCommand>(ClientMessageType.GameAcceptQuest);
        return services;
    }
}
