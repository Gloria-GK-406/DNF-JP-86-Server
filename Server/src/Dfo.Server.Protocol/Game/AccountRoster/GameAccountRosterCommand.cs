using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.HonorLevel;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GameAccountRosterCommand(NetworkDispatchContext Dispatch, GetAccountCharacterRosterRequest Request)
    : IProtocolCommand<AccountCharacterRoster>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameAccountRosterCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ICharacterDomainService characters,
    IGameCommandOutbox outbox) : ICommandHandler<GameAccountRosterCommand, AccountCharacterRoster>
{
    public async ValueTask<AccountCharacterRoster> HandleAsync(CommandExecutionContext context,
        GameAccountRosterCommand command, CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{command.Dispatch.Connection.ConnectionId}' is not authenticated.");
        }

        var roster = await characters.GetAccountRosterWithAppearanceAsync(accountId, cancellationToken).ConfigureAwait(false);
        await outbox.RegisterNotiAsync(
            command.Dispatch.Connection.ConnectionId,
            new AccountCharacterRosterResponse(roster, AnswersRosterQuery: true),
            cancellationToken).ConfigureAwait(false);

        // Pushed, not awaited. The client does send its own 0x0286 request and this server answers
        // that too, but the reference server does not wait to be asked: it puts the reply straight
        // after the roster, and the client reaches the roster screen having already been told.
        // Answering only on request delivers the same bytes at a later point in the sequence, and
        // this batch has twice turned out to be something the client treats as a unit.
        await outbox.RegisterCmdAsync(
            command.Dispatch.Connection.ConnectionId,
            new HiddenCharacterInfoResponse(),
            cancellationToken).ConfigureAwait(false);

        // The roster screen expects honour standing alongside the roster itself; the reference
        // server sends both from this one request. There is no honour subsystem yet, so a fresh
        // account's standing is reported -- see HonorLevelInfoNotice for why sending it is safe
        // even though the client's handling of it is uncorroborated.
        await outbox.RegisterNotiAsync(
            command.Dispatch.Connection.ConnectionId,
            new HonorLevelInfoNotice(AccountHonorLevel.Initial),
            cancellationToken).ConfigureAwait(false);
        return roster;
    }
}
