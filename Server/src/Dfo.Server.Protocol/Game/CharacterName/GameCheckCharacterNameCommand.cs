using System.Text;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.CharacterName;

public sealed record GameCheckCharacterNameCommand(NetworkDispatchContext Dispatch, CheckCharacterNameRequest Request)
    : IProtocolCommand<CharacterNameStatus>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

public sealed class GameCheckCharacterNameCommandHandler(
    IOnlineUserQueryDomainService onlineUsers,
    ICharacterRosterDomainService characters,
    IGameCommandOutbox outbox,
    ILogger<GameCheckCharacterNameCommandHandler> logger) : ICommandHandler<GameCheckCharacterNameCommand, CharacterNameStatus>
{
    public async ValueTask<CharacterNameStatus> HandleAsync(CommandExecutionContext context,
        GameCheckCharacterNameCommand command, CancellationToken cancellationToken)
    {
        if (!onlineUsers.TryGetByConnection(command.Dispatch.Connection.ConnectionId, out var onlineUser)
            || onlineUser?.AccountId is not { } accountId)
        {
            throw new InvalidOperationException(
                $"Game connection '{command.Dispatch.Connection.ConnectionId}' is not authenticated.");
        }

        // A body too short to hold a name never reaches the domain: there is nothing to ask about.
        // It still gets an answer, because the client is holding a naming dialog open.
        var status = command.Request.RawName is null
            ? CharacterNameStatus.Malformed
            : await characters.CheckNameAsync(command.Request.RawName, cancellationToken).ConfigureAwait(false);

        // The name is logged, not just its length. A job the client refuses to create produces a
        // name check and then nothing at all, so this line is the only record that an attempt
        // happened -- and without the name there is no way to tell which attempt it was.
        logger.LogInformation(
            "Check character name {ConnectionId} account={AccountId}: {Status} name='{Name}' ({NameBytes}B)",
            command.Dispatch.Connection.ConnectionId,
            accountId,
            status,
            DescribeName(command.Request.RawName),
            command.Request.RawName?.Length ?? 0);

        await outbox.RegisterCmdAsync(
            command.Dispatch.Connection.ConnectionId,
            new CheckCharacterNameResponse(status),
            cancellationToken).ConfigureAwait(false);
        return status;
    }

    /// <summary>Renders a proposed name for the log, falling back to hex when it is not text.</summary>
    /// <remarks>
    /// Undecodable bytes become hex rather than replacement characters, so a malformed name stays
    /// distinguishable from a name that genuinely contains U+FFFD.
    /// </remarks>
    private static string DescribeName(byte[]? rawName)
    {
        if (rawName is null || rawName.Length == 0)
        {
            return "<none>";
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(rawName);
        }
        catch (DecoderFallbackException)
        {
            return Convert.ToHexString(rawName);
        }
    }
}
