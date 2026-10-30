using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.CollectionBox;

/// <summary>What the CollectionBox slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class CollectionBoxRegistration
{
    internal static IServiceCollection AddCollectionBoxSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxSelectDecoder>(0x03E5, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxPutDecoder>(0x03E6, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxWithdrawDecoder>(0x03E7, ClientMessageType.GameCollectionBox);
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxExtendDecoder>(0x03E8, ClientMessageType.GameCollectionBox);
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxHandler, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest, Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommand>(ClientMessageType.GameCollectionBox);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommand, bool, Dfo.Server.Protocol.Game.CollectionBox.GameCollectionBoxCommandHandler>(
            new[] { CommandRegistrationExit.CommittedReplayCurrentStateBatch, CommandRegistrationExit.SettledInitialBatch },
            new ProtocolCommandMessageBinding(ClientMessageType.GameCollectionBox, typeof(Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxRequest)));
        services.AddGameCmdMessageEncoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxAck, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxAckEncoder>();
        services.AddGameNotiMessageEncoder<Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxContentsMessage, Dfo.Server.Protocol.Game.CollectionBox.CollectionBoxContentsEncoder>();
        return services;
    }
}
