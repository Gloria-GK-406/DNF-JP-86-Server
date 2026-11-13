using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Relations;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Logging;

namespace Dfo.Server.Protocol.Game.Blacklist;

public sealed record GameRegisterBlacklistCommand(NetworkDispatchContext Dispatch, RegisterBlacklistRequest Input) : IProtocolCommand<RegisterBlacklistResult>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Blacklists the named character; whoever it is, online or not, is told nothing.</summary>
/// <remarks>
/// Every request is answered: the client holds the blacklist dialog until it is. Which refusals
/// there are, and in which order, is the relation service's rule; this only resolves the name.
/// </remarks>
internal sealed class GameRegisterBlacklistCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterIdentityQueryDomainService identities,
    ICharacterRelationDomainService relations,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameRegisterBlacklistCommandHandler> logger) : ICommandHandler<GameRegisterBlacklistCommand, RegisterBlacklistResult>
{
    public async ValueTask<RegisterBlacklistResult> HandleAsync(CommandExecutionContext execution, GameRegisterBlacklistCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var response = await DecideAsync(execution, command, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(connection.ConnectionId, response, ct).ConfigureAwait(false);
        return response.Result;
    }

    private async ValueTask<RegisterBlacklistResponse> DecideAsync(CommandExecutionContext execution, GameRegisterBlacklistCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
            return Refused(connection.ConnectionId, null, "NotInWorld");
        if (command.Input.Name is not { } name)
            return Refused(connection.ConnectionId, requester, "Malformed");

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var lookup = await units.ReadOnlyAsync(execution, new("BlacklistTarget", [recipient]),
            async token => new BlacklistTarget(await identities.FindByNameAsync(name, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (lookup.Identity is not { } target)
            return Answer(requester, null, RegisterBlacklistResult.NoSuchCharacter, []);

        var outcome = await units.WriteAsync(execution, new("RegisterBlacklist", [recipient]),
            (_, token) => relations.AddAsync(requester.CharacterId, target.Character, CharacterRelationKind.Blacklist, token), ct).ConfigureAwait(false);
        var result = BlacklistPayloadCodec.ResultOf(outcome);
        return Answer(requester, target, result, result == RegisterBlacklistResult.Registered ? target.Name : []);
    }

    private RegisterBlacklistResponse Answer(SelectedSessionIdentity requester, CharacterPublicIdentity? target, RegisterBlacklistResult result, byte[] name)
    {
        logger.LogInformation("Blacklist register character={Character} target={Target}: {Result}",
            requester.CharacterId.Value, target?.Character.Value, result);
        return new(result, name);
    }

    private RegisterBlacklistResponse Refused(NetworkConnectionId connection, SelectedSessionIdentity? requester, string reason)
    {
        logger.LogInformation("Blacklist register refused connection={Connection} character={Character}: {Reason}",
            connection, requester?.CharacterId.Value, reason);
        return new(RegisterBlacklistResult.Refused, []);
    }
}

public sealed record GameDeleteBlacklistCommand(NetworkDispatchContext Dispatch, DeleteBlacklistRequest Input) : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Lifts a blacklisting by name. Every request is answered; the client has one refusal, 75.</summary>
internal sealed class GameDeleteBlacklistCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterIdentityQueryDomainService identities,
    ICharacterRelationDomainService relations,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    ILogger<GameDeleteBlacklistCommandHandler> logger) : ICommandHandler<GameDeleteBlacklistCommand, bool>
{
    public async ValueTask<bool> HandleAsync(CommandExecutionContext execution, GameDeleteBlacklistCommand command, CancellationToken ct)
    {
        var response = await DecideAsync(execution, command, ct).ConfigureAwait(false);
        await outbox.RegisterCmdAsync(command.Dispatch.Connection.ConnectionId, response, ct).ConfigureAwait(false);
        return response.Name is not null;
    }

    private async ValueTask<DeleteBlacklistResponse> DecideAsync(CommandExecutionContext execution, GameDeleteBlacklistCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        if (sessions.FindByConnection(connection.ConnectionId) is not { } requester || !sessions.IsInWorld(requester))
            return NotRemoved(connection.ConnectionId, null, "NotInWorld");
        if (command.Input.Name is not { } name)
            return NotRemoved(connection.ConnectionId, requester, "Malformed");

        var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
        var lookup = await units.ReadOnlyAsync(execution, new("BlacklistTarget", [recipient]),
            async token => new BlacklistTarget(await identities.FindByNameAsync(name, token).ConfigureAwait(false)), ct).ConfigureAwait(false);
        if (lookup.Identity is not { } target)
            return NotRemoved(connection.ConnectionId, requester, "NoSuchCharacter");

        var outcome = await units.WriteAsync(execution, new("DeleteBlacklist", [recipient]),
            (_, token) => relations.RemoveAsync(requester.CharacterId, target.Character, CharacterRelationKind.Blacklist, token), ct).ConfigureAwait(false);
        if (outcome != CharacterRelationRemoveOutcome.Removed)
            return NotRemoved(connection.ConnectionId, requester, "NotListed");

        // Only a database from before the 18-byte creation limit can hold such a name. The row is gone
        // either way; 75 tells the player the list is refreshed on reconnect, which is then true, where
        // a success the client cannot read would desynchronise every frame after it.
        if (target.Name.Length > DeleteBlacklistResponse.NameLimit)
        {
            logger.LogWarning("Blacklist removed character={Character} target={Target}, but its {NameBytes}-byte name does not fit the reply; answered 75",
                requester.CharacterId.Value, target.Character.Value, target.Name.Length);
            return new(null);
        }

        logger.LogInformation("Blacklist removed character={Character} target={Target}", requester.CharacterId.Value, target.Character.Value);
        return new(target.Name);
    }

    private DeleteBlacklistResponse NotRemoved(NetworkConnectionId connection, SelectedSessionIdentity? requester, string reason)
    {
        logger.LogInformation("Blacklist delete refused connection={Connection} character={Character}: {Reason}",
            connection, requester?.CharacterId.Value, reason);
        return new(null);
    }
}

public sealed record GameBlacklistListCommand(NetworkDispatchContext Dispatch, BlacklistListRequest Input) : IProtocolCommand<int>
{
    public IClientNetworkMessage Message => Input;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Input.Metadata);
}

/// <summary>Answers the whole blacklist, dated in the server's local zone.</summary>
/// <remarks>
/// A requester not in the world gets an empty list rather than nothing: the client asks on its way
/// back into town, and an empty list changes nothing on a client that only ever merges what it is sent.
/// </remarks>
internal sealed class GameBlacklistListCommandHandler(
    ISelectedSessionDomainService sessions,
    ICharacterRelationQueryDomainService relations,
    ICharacterIdentityQueryDomainService identities,
    ICommandWorkUnits units,
    IGameCommandOutbox outbox,
    TimeProvider clock,
    ILogger<GameBlacklistListCommandHandler> logger) : ICommandHandler<GameBlacklistListCommand, int>
{
    public async ValueTask<int> HandleAsync(CommandExecutionContext execution, GameBlacklistListCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var response = new BlacklistListResponse([]);
        if (sessions.FindByConnection(connection.ConnectionId) is { } requester && sessions.IsInWorld(requester))
        {
            var recipient = new SelectedSessionRecipient(requester, connection, command.Input.Metadata);
            var listing = await units.ReadOnlyAsync(execution, new("BlacklistList", [recipient]),
                token => BlacklistListBuilder.BuildAsync(requester.CharacterId, relations, identities, clock.LocalTimeZone, token), ct).ConfigureAwait(false);
            foreach (var skipped in listing.Skipped)
                logger.LogWarning("Blacklist entry left out character={Character} target={Target}: its {NameBytes}-byte name does not fit CMD 0x0078",
                    requester.CharacterId.Value, skipped.Character.Value, skipped.Name.Length);
            response = listing.Response;
        }
        await outbox.RegisterCmdAsync(connection.ConnectionId, response, ct).ConfigureAwait(false);
        return response.Entries.Count;
    }
}

/// <summary>The character a request named, or none; a work unit's result cannot itself be null.</summary>
internal sealed record BlacklistTarget(CharacterPublicIdentity? Identity);

public sealed class RegisterBlacklistHandler(ICommandBus commands) : ClientMessageHandler<RegisterBlacklistRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameRegisterBlacklistCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, RegisterBlacklistRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameRegisterBlacklistCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class DeleteBlacklistHandler(ICommandBus commands) : ClientMessageHandler<DeleteBlacklistRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameDeleteBlacklistCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, DeleteBlacklistRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameDeleteBlacklistCommand(context, message), ct).ConfigureAwait(false);
}

public sealed class BlacklistListHandler(ICommandBus commands) : ClientMessageHandler<BlacklistListRequest>,
    Dfo.Server.Application.Dispatching.IProtocolCommandMessageHandler<GameBlacklistListCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, BlacklistListRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameBlacklistListCommand(context, message), ct).ConfigureAwait(false);
}
