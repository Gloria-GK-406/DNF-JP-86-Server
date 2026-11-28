using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Pvp;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Protocol.Game.HiddenCharacterInfo;
using Dfo.Server.Protocol.Game.HonorLevel;
using Dfo.Server.Protocol.Game.LuckyCharacter;
using Dfo.Server.Protocol.Game.PvpTotalMatchTeam;

namespace Dfo.Server.Protocol.Game.AccountRoster;

public sealed record GameAccountRosterCommand(NetworkDispatchContext Dispatch, GetAccountCharacterRosterRequest Request)
    : IProtocolCommand<AccountCharacterRoster>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameAccountRosterCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ICharacterRosterDomainService characters,
    IAccountHonorDomainService honor,
    IPvpTotalMatchRepresentativeSwitchDomainService representativeSwitch,
    LuckyCharacterRosterDelivery luckyCharacters,
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
        // A21 can query again while handling the return roster. A second roster makes the client
        // select the newly saved representative twice before the first selection completes.
        if (!representativeSwitch.HasPublishedRoster(command.Dispatch.Connection, accountId))
        {
            await luckyCharacters.RegisterAsync(command.Dispatch.Connection, cancellationToken).ConfigureAwait(false);
            await outbox.RegisterNotiAsync(
                command.Dispatch.Connection.ConnectionId,
                new AccountCharacterRosterResponse(roster, AnswersRosterQuery: true),
                cancellationToken).ConfigureAwait(false);
        }

        // Pushed, not awaited. The client does send its own 0x0286 request and this server answers
        // that too, but the reference server does not wait to be asked: it puts the reply straight
        // after the roster, and the client reaches the roster screen having already been told.
        // Answering only on request delivers the same bytes at a later point in the sequence, and
        // this batch has twice turned out to be something the client treats as a unit.
        await outbox.RegisterCmdAsync(
            command.Dispatch.Connection.ConnectionId,
            new HiddenCharacterInfoResponse(),
            cancellationToken).ConfigureAwait(false);

        var standing = await honor.ReadAsync(accountId, cancellationToken).ConfigureAwait(false);
        await outbox.RegisterNotiAsync(
            command.Dispatch.Connection.ConnectionId,
            new HonorLevelInfoNotice(standing),
            cancellationToken).ConfigureAwait(false);
        return roster;
    }
}
