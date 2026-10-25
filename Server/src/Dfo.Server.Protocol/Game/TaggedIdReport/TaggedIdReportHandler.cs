using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.TaggedIdReport;

public sealed class TaggedIdReportHandler(ICommandBus commands) : ClientMessageHandler<TaggedIdReportRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameTaggedIdReportCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, TaggedIdReportRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameTaggedIdReportCommand(context, message), ct).ConfigureAwait(false);
}
