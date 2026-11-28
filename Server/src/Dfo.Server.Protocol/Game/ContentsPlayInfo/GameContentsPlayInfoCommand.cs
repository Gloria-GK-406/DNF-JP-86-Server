using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.UsageQuotas;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;
using Dfo.Server.Application.Commands;

namespace Dfo.Server.Protocol.Game.ContentsPlayInfo;

public sealed record GameContentsPlayInfoCommand(NetworkDispatchContext Dispatch, ContentsPlayInfoRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

public sealed class GameContentsPlayInfoCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ICharacterRosterDomainService characters,
    IUsageQuotaSnapshotDomainService quotas,
    IDarkElfRuinsDomainService ruins,
    IGameCommandOutbox sender,
    ILogger<ContentsPlayInfoHandler> logger) : ICommandHandler<GameContentsPlayInfoCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution,
        GameContentsPlayInfoCommand command, CancellationToken cancellationToken)
    {
        var context = command.Dispatch; var message = command.Input;
        ContentsPlayInfoState? state = null;
        if (message.RosterIndex <= sbyte.MaxValue
            && onlineUsers.TryGetByConnection(context.Connection.ConnectionId, out var user)
            && user?.AccountId is { } account)
        {
            var roster = await characters.GetAccountRosterAsync(account, cancellationToken).ConfigureAwait(false);
            if (message.RosterIndex < roster.Characters.Count)
            {
                var character = roster.Characters[message.RosterIndex].CharacterId;
                var mirror = await quotas.ReadAsync(account, character, UsageQuotaFamily.MirrorDungeon, cancellationToken).ConfigureAwait(false);
                var despair = await quotas.ReadAsync(account, character, UsageQuotaFamily.DespairTower, cancellationToken).ConfigureAwait(false);
                var dimension = await quotas.ReadAsync(account, character, UsageQuotaFamily.DimensionDungeon, cancellationToken).ConfigureAwait(false);
                state = new(
                    despair.Despair ?? throw new InvalidDataException("Despair quota snapshot is missing."),
                    dimension.Dimension ?? throw new InvalidDataException("Dimension quota snapshot is missing."),
                    mirror.Mirror ?? throw new InvalidDataException("Mirror quota snapshot is missing."),
                    await ruins.SelectionAsync(character, cancellationToken).ConfigureAwait(false));
            }
        }
        logger.LogInformation("Contents play info {ConnectionId}: rosterIndex={RosterIndex} accepted={Accepted}",
            context.Connection.ConnectionId, message.RosterIndex, state is not null);
        await sender.RegisterCmdAsync(context.Connection.ConnectionId,
            new ContentsPlayInfoResponse(message.RosterIndex, state), cancellationToken).ConfigureAwait(false);
        return state is not null;
    }
}
