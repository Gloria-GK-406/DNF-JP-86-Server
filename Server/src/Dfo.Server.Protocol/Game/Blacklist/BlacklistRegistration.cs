using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.Blacklist;

/// <summary>What the Blacklist slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class BlacklistRegistration
{
    internal static IServiceCollection AddBlacklistSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<RegisterBlacklistDecoder>(ClientMessageType.GameRegisterBlacklist);
        services.AddGameCmdMessageDecoder<DeleteBlacklistDecoder>(ClientMessageType.GameDeleteBlacklist);
        services.AddGameCmdMessageDecoder<BlacklistListDecoder>(ClientMessageType.GameRequestBlacklist);
        services.AddGameCmdMessageEncoder<RegisterBlacklistResponse, RegisterBlacklistResponseEncoder>();
        services.AddGameCmdMessageEncoder<DeleteBlacklistResponse, DeleteBlacklistResponseEncoder>();
        // The list reply lives at the Game layer because friend-list pushes send it too; its one
        // encoder registration is here, and serves every sender.
        services.AddGameCmdMessageEncoder<BlacklistListResponse, BlacklistListResponseEncoder>();
        services.AddProtocolCommandHandler<GameRegisterBlacklistCommand, RegisterBlacklistResult, GameRegisterBlacklistCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRegisterBlacklist, typeof(RegisterBlacklistRequest)));
        services.AddProtocolCommandHandler<GameDeleteBlacklistCommand, bool, GameDeleteBlacklistCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameDeleteBlacklist, typeof(DeleteBlacklistRequest)));
        services.AddProtocolCommandHandler<GameBlacklistListCommand, int, GameBlacklistListCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameRequestBlacklist, typeof(BlacklistListRequest)));
        services.RegisterProtocolMessageHandler<RegisterBlacklistHandler, RegisterBlacklistRequest, GameRegisterBlacklistCommand>(ClientMessageType.GameRegisterBlacklist);
        services.RegisterProtocolMessageHandler<DeleteBlacklistHandler, DeleteBlacklistRequest, GameDeleteBlacklistCommand>(ClientMessageType.GameDeleteBlacklist);
        services.RegisterProtocolMessageHandler<BlacklistListHandler, BlacklistListRequest, GameBlacklistListCommand>(ClientMessageType.GameRequestBlacklist);
        return services;
    }
}
