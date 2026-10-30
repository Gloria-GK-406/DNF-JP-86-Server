using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestGiveUp;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.QuestGiveUp;

/// <summary>What the QuestGiveUp slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class QuestGiveUpRegistration
{
    internal static IServiceCollection AddQuestGiveUpSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GiveUpQuestMessageDecoder>(ClientMessageType.GameGiveUpQuest);
        services.AddGameCmdMessageEncoder<GiveUpQuestAcknowledgement, GiveUpQuestAcknowledgementEncoder>();
        services.AddProtocolCommandHandler<GameGiveUpQuestCommand, GiveUpQuestOutcome, GameGiveUpQuestCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGiveUpQuest, typeof(GiveUpQuestRequest)));
        services.RegisterProtocolMessageHandler<GiveUpQuestHandler, GiveUpQuestRequest, GameGiveUpQuestCommand>(ClientMessageType.GameGiveUpQuest);
        return services;
    }
}
