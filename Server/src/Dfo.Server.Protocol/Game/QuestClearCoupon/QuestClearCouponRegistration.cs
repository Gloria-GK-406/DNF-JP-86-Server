using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.QuestClearCoupon;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.QuestClearCoupon;

/// <summary>What the QuestClearCoupon slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class QuestClearCouponRegistration
{
    internal static IServiceCollection AddQuestClearCouponSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<QuestClearCouponDecoder>(
            GameProtocol.QuestClearCouponType, ClientMessageType.GameQuestClearCoupon);
        services.AddGameCmdMessageEncoder<QuestClearCouponResponse, QuestClearCouponEncoder>();
        services.AddProtocolCommandHandler<GameQuestClearCouponCommand, bool, GameQuestClearCouponCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameQuestClearCoupon, typeof(QuestClearCouponRequest)));
        services.RegisterProtocolMessageHandler<QuestClearCouponHandler, QuestClearCouponRequest, GameQuestClearCouponCommand>(ClientMessageType.GameQuestClearCoupon);
        return services;
    }
}
