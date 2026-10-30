using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.TaggedIdReport;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

/// <summary>What the TaggedIdReport slice registers; called once from <see cref="Dfo.Server.Protocol.ProtocolModule"/>.</summary>
internal static class TaggedIdReportRegistration
{
    internal static IServiceCollection AddTaggedIdReportSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<TaggedIdReportMessageDecoder>(ClientMessageType.GameTaggedIdReport);
        services.AddGameCmdMessageEncoder<TutorialFlagAcknowledgement, TutorialFlagAcknowledgementEncoder>();
        services.AddProtocolCommandHandler<GameTaggedIdReportCommand, bool, GameTaggedIdReportCommandHandler>(new ProtocolCommandMessageBinding(ClientMessageType.GameTaggedIdReport, typeof(TaggedIdReportRequest)));
        services.RegisterProtocolMessageHandler<TaggedIdReportHandler, TaggedIdReportRequest, GameTaggedIdReportCommand>(ClientMessageType.GameTaggedIdReport);
        return services;
    }
}
