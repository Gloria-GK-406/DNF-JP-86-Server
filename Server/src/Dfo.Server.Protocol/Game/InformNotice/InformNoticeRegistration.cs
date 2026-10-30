using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.InformNotice;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.InformNotice;

/// <summary>What the InformNotice slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class InformNoticeRegistration
{
    internal static IServiceCollection AddInformNoticeSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<InformNoticeSecondDecoder>(ClientMessageType.GameInformNoticeSecond);
        services.AddGameNotiMessageEncoder<InformNoticeNotice, InformNoticeNoticeEncoder>();
        services.RegisterProtocolMessageHandler<NoticeSeenHandler, INoticeSeenReport, GameNoticeSeenCommand>(ClientMessageType.GameInformNoticeSecond, typeof(InformNoticeSecondReport));
        services.RegisterProtocolMessageHandler<NoticeSeenHandler, INoticeSeenReport, GameNoticeSeenCommand>(ClientMessageType.GameNoticeSeen, typeof(NoticeSeenRequest));
        services.AddGameCmdMessageDecoder<NoticeSeenDecoder>(ClientMessageType.GameNoticeSeen);
        services.AddProtocolCommandHandler<GameNoticeSeenCommand, bool, GameNoticeSeenCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameNoticeSeen, typeof(NoticeSeenRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameInformNoticeSecond, typeof(InformNoticeSecondReport)));
        return services;
    }
}
