using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.PrivateStore;

/// <summary>What the PrivateStore slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class PrivateStoreRegistration
{
    private static (ClientMessageType Type, Type Message)[] Requests =>
    [
        (ClientMessageType.GamePrivateStoreCreate, typeof(PrivateStoreCreateRequest)),
        (ClientMessageType.GamePrivateStoreCompleteDisplay, typeof(PrivateStoreDisplayRequest)),
        (ClientMessageType.GamePrivateStoreEnter, typeof(PrivateStoreSimpleRequest)),
        (ClientMessageType.GamePrivateStoreExit, typeof(PrivateStoreSimpleRequest)),
        (ClientMessageType.GamePrivateStoreRemove, typeof(PrivateStoreSimpleRequest)),
        (ClientMessageType.GamePrivateStoreMoveToGate, typeof(PrivateStoreSimpleRequest)),
        (ClientMessageType.GamePrivateStoreBuy, typeof(PrivateStoreLineRequest)),
        (ClientMessageType.GamePrivateStoreVerify, typeof(PrivateStoreLineRequest)),
    ];

    internal static IServiceCollection AddPrivateStoreSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<PrivateStoreCreateDecoder>(ClientMessageType.GamePrivateStoreCreate);
        services.AddGameCmdMessageDecoder<PrivateStoreDisplayDecoder>(ClientMessageType.GamePrivateStoreCompleteDisplay);
        services.AddGameCmdMessageDecoder<PrivateStoreEnterDecoder>(ClientMessageType.GamePrivateStoreEnter);
        services.AddGameCmdMessageDecoder<PrivateStoreExitDecoder>(ClientMessageType.GamePrivateStoreExit);
        services.AddGameCmdMessageDecoder<PrivateStoreRemoveDecoder>(ClientMessageType.GamePrivateStoreRemove);
        services.AddGameCmdMessageDecoder<PrivateStoreMoveToGateDecoder>(ClientMessageType.GamePrivateStoreMoveToGate);
        services.AddGameCmdMessageDecoder<PrivateStoreBuyDecoder>(ClientMessageType.GamePrivateStoreBuy);
        services.AddGameCmdMessageDecoder<PrivateStoreVerifyDecoder>(ClientMessageType.GamePrivateStoreVerify);
        services.AddGameCmdMessageEncoder<PrivateStoreReply, PrivateStoreReplyEncoder>();
        services.AddGameNotiMessageEncoder<PrivateStoreNotice, PrivateStoreNoticeEncoder>();
        services.AddNotificationHandler<PrivateStoreCompleted, PrivateStoreCompletedHandler>();
        services.AddNotificationHandler<PrivateStoreBroadcast, PrivateStoreBroadcastHandler>();
        services.AddNotificationHandler<PrivateStoreArrival, PrivateStoreArrivalHandler>();
        services.AddProtocolCommandHandler<GamePrivateStoreCommand, bool, GamePrivateStoreCommandHandler>(
            [.. Requests.Select(request => new ProtocolCommandMessageBinding(request.Type, request.Message))]);
        foreach (var (type, message) in Requests)
            services.RegisterProtocolMessageHandler<PrivateStoreRequestHandler, IPrivateStoreRequest, GamePrivateStoreCommand>(type, message);
        return services;
    }
}
