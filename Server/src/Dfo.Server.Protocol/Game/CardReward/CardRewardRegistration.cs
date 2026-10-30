using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.CardReward;
using Dfo.Server.Protocol.Game.DungeonClear;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dfo.Server.Protocol.Game.CardReward;

/// <summary>What the CardReward slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CardRewardRegistration
{
    internal static IServiceCollection AddCardRewardSlice(this IServiceCollection services)
    {
        services.TryAddSingleton(DungeonCardPresentationTiming.Production);
        services.TryAddSingleton<IDungeonCardPresentationDelay,
            SystemDungeonCardPresentationDelay>();
        services.TryAddSingleton<DungeonCardPresentationCoordinator>();
        services.AddSingleton<IDungeonCardPresentationControl>(p => p.GetRequiredService<DungeonCardPresentationCoordinator>());
        services.AddSingleton<Dfo.Server.Abstractions.Execution.IGameplayProducerDrain>(provider => provider.GetRequiredService<DungeonCardPresentationCoordinator>());
        services.AddGameCmdMessageDecoder<CardSelectRightStateDecoder>(ClientMessageType.GameCardSelectRightState);
        services.AddGameCmdMessageDecoder<SelectCardDecoder>(ClientMessageType.GameSelectCard);
        services.AddGameCmdMessageEncoder<
            CardSelectRightStateResponse, CardSelectRightStateResponseEncoder>();
        services.AddGameCmdMessageEncoder<SelectCardResponse, SelectCardResponseEncoder>();
        services.AddProtocolCommandHandler<GameScoreScrollStateCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameScoreScrollState, typeof(ScoreScrollStateRequest)));
        services.AddProtocolCommandHandler<GameCardSelectRightStateCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameCardSelectRightState, typeof(CardSelectRightStateRequest)));
        services.RegisterProtocolMessageHandler<CardSelectRightStateHandler, CardSelectRightStateRequest, GameCardSelectRightStateCommand>(ClientMessageType.GameCardSelectRightState);
        services.AddProtocolCommandHandler<GameSelectCardCommand, bool, GameCardCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSelectCard, typeof(SelectCardRequest)));
        services.RegisterProtocolMessageHandler<SelectCardHandler, SelectCardRequest, GameSelectCardCommand>(ClientMessageType.GameSelectCard);
        services.AddInternalCommandHandler<DungeonCardLayoutCommand, bool, GameCardCommandHandler>();
        services.AddInternalCommandHandler<DungeonCardAutoFlipCommand, bool, GameCardCommandHandler>();
        return services;
    }
}
