using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.SaveQuickChat;

/// <summary>What the SaveQuickChat slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class SaveQuickChatRegistration
{
    internal static IServiceCollection AddSaveQuickChatSlice(this IServiceCollection services)
    {
        services.RegisterProtocolMessageHandler<Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatHandler, Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatRequest, Dfo.Server.Protocol.Game.SaveQuickChat.GameSaveQuickChatCommand>(ClientMessageType.GameSaveQuickChat);
        services.AddProtocolCommandHandler<Dfo.Server.Protocol.Game.SaveQuickChat.GameSaveQuickChatCommand, bool, Dfo.Server.Protocol.Game.SaveQuickChat.GameSaveQuickChatCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameSaveQuickChat, typeof(Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatRequest)));
        services.AddGameCmdMessageDecoder<Dfo.Server.Protocol.Game.SaveQuickChat.SaveQuickChatDecoder>(0x0170, ClientMessageType.GameSaveQuickChat);
        return services;
    }
}
