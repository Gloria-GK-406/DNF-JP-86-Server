using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestProgress;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.QuestProgress;

/// <summary>What the QuestProgress slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class QuestProgressRegistration
{
    internal static IServiceCollection AddQuestProgressSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<Dfo.Server.Abstractions.Quests.MirrorQuestViewChanged, Dfo.Server.Protocol.Game.QuestProgress.MirrorQuestViewChangedHandler>();
        services.AddNotificationHandler<Dfo.Server.Abstractions.Quests.QuestProgressViewChanged, Dfo.Server.Protocol.Game.QuestProgress.QuestProgressViewChangedHandler>();
        services.AddGameCmdMessageDecoder<SetQuestTriggerMessageDecoder>(
            GameProtocol.SetQuestTriggerType, ClientMessageType.GameSetQuestTrigger);
        services.AddGameCmdMessageEncoder<SetQuestTriggerAcknowledgement, SetQuestTriggerAcknowledgementEncoder>();
        services.AddProtocolCommandHandler<GameSetQuestTriggerCommand, QuestTriggerOutcome, GameSetQuestTriggerCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSetQuestTrigger, typeof(SetQuestTriggerRequest)));
        services.RegisterProtocolMessageHandler<SetQuestTriggerHandler, SetQuestTriggerRequest, GameSetQuestTriggerCommand>(ClientMessageType.GameSetQuestTrigger);
        return services;
    }
}
