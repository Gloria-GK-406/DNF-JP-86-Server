using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Networking;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed class GetAccountCharacterRosterHandler(ICommandBus commands)
    : ClientMessageHandler<GetAccountCharacterRosterRequest>, IProtocolCommandMessageHandler<GameAccountRosterCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context,
        GetAccountCharacterRosterRequest message, CancellationToken cancellationToken) =>
        _ = await commands.SendAsync(new GameAccountRosterCommand(context, message), cancellationToken).ConfigureAwait(false);
}
