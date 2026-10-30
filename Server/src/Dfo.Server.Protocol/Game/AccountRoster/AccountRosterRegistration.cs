using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Application.Notifications;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.AccountRoster;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.AccountRoster;

/// <summary>What the AccountRoster slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class AccountRosterRegistration
{
    internal static IServiceCollection AddAccountRosterSlice(this IServiceCollection services)
    {
        services.AddNotificationHandler<
            OtherCharacterDetailRequested,
            OtherCharacterDetailRequestedHandler>();
        services.AddGameCmdMessageDecoder<GetUserInfoMessageDecoder>(
            GameProtocol.GetUserInfoType,
            ClientMessageType.GameGetAccountCharacterRoster,
            ClientMessageType.GameGetOtherCharacterDetail);
        services.AddGameNotiMessageEncoder<AccountCharacterRosterResponse, AccountCharacterRosterResponseEncoder>();
        services.AddGameNotiMessageEncoder<
            OtherCharacterDetailResponse,
            OtherCharacterDetailResponseEncoder>();
        // These complete protocol commands own their original explicit write segments; no account gate remains.
        services.AddProtocolCommandHandler<GameAccountRosterCommand, AccountCharacterRoster, GameAccountRosterCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetAccountCharacterRoster, typeof(GetAccountCharacterRosterRequest)));
        services.AddProtocolCommandHandler<GameOtherCharacterDetailCommand, GameOtherCharacterDetailCompleted, GameOtherCharacterDetailCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGetOtherCharacterDetail, typeof(GetOtherCharacterDetailRequest)));
        services.RegisterProtocolMessageHandler<GetAccountCharacterRosterHandler, GetAccountCharacterRosterRequest, GameAccountRosterCommand>(ClientMessageType.GameGetAccountCharacterRoster);
        services.RegisterProtocolMessageHandler<GetOtherCharacterDetailHandler, GetOtherCharacterDetailRequest, GameOtherCharacterDetailCommand>(
            ClientMessageType.GameGetOtherCharacterDetail);
        return services;
    }
}
