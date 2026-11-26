using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildCreation;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildDiscovery;

public sealed record GuildDiscoveryRequest(
    ClientMessageType MessageType, GameMessageMetadata Metadata, bool Valid, string? Name = null) : IClientNetworkMessage;

internal sealed class GuildSearchDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var name = GuildCreationPayloadCodec.DecodeText(body, 1, 24);
        return new GuildDiscoveryRequest(ClientMessageType.GameGuildSearch, metadata, name is not null, name);
    }
}

internal abstract class EmptyGuildDiscoveryDecoder(ClientMessageType type) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildDiscoveryRequest(type, metadata, body.IsEmpty);
}

internal sealed class GuildAccountMembershipsDecoder() : EmptyGuildDiscoveryDecoder(ClientMessageType.GameGuildAccountMemberships);
internal sealed class GuildRecommendationsDecoder() : EmptyGuildDiscoveryDecoder(ClientMessageType.GameGuildRecommendations);

internal sealed record GuildDiscoveryReply(
    ServerMessageType MessageType, GuildDiscoveryDetails? Found = null,
    IReadOnlyList<GuildAccountEntry>? AccountGuilds = null,
    IReadOnlyList<GuildRecommendation>? Recommendations = null) : IGameCmdMessage;

internal sealed class GuildDiscoveryReplyEncoder : GameCmdMessageEncoder<GuildDiscoveryReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildDiscoveryReply message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        if (message.Found is { } guild)
        {
            writer.Write(guild.Guild.Value);
            WriteGuildText(writer, guild.Name);
            writer.Write(checked((ushort)guild.MemberCount));
            WriteGuildText(writer, guild.Promotion);
            writer.Write(0u);
            writer.Write((byte)0);
        }
        else if (message.AccountGuilds is { } memberships)
        {
            writer.Write(checked((byte)memberships.Count));
            foreach (var membership in memberships)
            {
                ChatDString.Write(writer, membership.CharacterName);
                WriteGuildText(writer, membership.GuildName);
            }
        }
        else if (message.Recommendations is { } recommendations)
        {
            writer.Write(checked((byte)recommendations.Count));
            foreach (var recommendation in recommendations)
            {
                writer.Write(recommendation.Guild.Value);
                WriteGuildText(writer, recommendation.Name);
                ChatDString.Write(writer, recommendation.LeaderName);
            }
        }
        else
        {
            return OnePayload(message, GameCmdEnvelope.Refuse(0x22));
        }

        return OnePayload(message, GameCmdEnvelope.Accept(stream.ToArray()));
    }

    private static void WriteGuildText(BinaryWriter writer, string text) =>
        ChatDString.Write(writer, GuildCreationPayloadCodec.EncodeGuildText(text));
}

public sealed class GuildDiscoveryRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildDiscoveryRequest>, IProtocolCommandMessageHandler<GameGuildDiscoveryCommand>
{
    protected override async ValueTask HandleMessageAsync(
        NetworkDispatchContext context, GuildDiscoveryRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildDiscoveryCommand(context, message), ct).ConfigureAwait(false);
}

public sealed record GameGuildDiscoveryCommand(NetworkDispatchContext Dispatch, GuildDiscoveryRequest Request)
    : IProtocolCommand<bool>
{
    public IClientNetworkMessage Message => Request;
    public ProtocolFrameMetadata Frame => new ProtocolFrameMetadata.Game(Request.Metadata);
}

internal sealed class GameGuildDiscoveryCommandHandler(
    IGuildQueryDomainService guilds, ISelectedSessionDomainService sessions, ICommandWorkUnits units,
    IGameCommandOutbox outbox) : ICommandHandler<GameGuildDiscoveryCommand, bool>
{
    public async ValueTask<bool> HandleAsync(
        CommandExecutionContext context, GameGuildDiscoveryCommand command, CancellationToken ct)
    {
        var connection = command.Dispatch.Connection;
        var request = command.Request;
        var type = (ServerMessageType)(int)request.MessageType;
        var reply = new GuildDiscoveryReply(type);

        if (request.Valid && sessions.TryGetInWorld(connection.ConnectionId, out var viewer))
        {
            var recipient = new SelectedSessionRecipient(viewer, connection, request.Metadata);
            reply = await units.ReadOnlyAsync(context, new("discover-guilds", [recipient]), async token =>
                request.MessageType switch
                {
                    ClientMessageType.GameGuildSearch => new GuildDiscoveryReply(type, Found:
                        await guilds.FindGuildAsync(request.Name!, token).ConfigureAwait(false)),
                    ClientMessageType.GameGuildAccountMemberships => new GuildDiscoveryReply(type, AccountGuilds:
                        await guilds.ReadAccountGuildsAsync(viewer.AccountId, token).ConfigureAwait(false)),
                    ClientMessageType.GameGuildRecommendations => new GuildDiscoveryReply(type, Recommendations:
                        await guilds.ReadRecommendationsAsync(token).ConfigureAwait(false)),
                    _ => throw new InvalidOperationException($"Unsupported guild discovery request {request.MessageType}."),
                }, ct).ConfigureAwait(false);
        }

        await outbox.RegisterCmdAsync(connection.ConnectionId, reply, ct).ConfigureAwait(false);
        return reply.Found is not null || reply.AccountGuilds is not null || reply.Recommendations is not null;
    }
}

internal static class GuildDiscoveryRegistration
{
    internal static void AddGuildDiscoverySlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildSearchDecoder>(ClientMessageType.GameGuildSearch);
        services.AddGameCmdMessageDecoder<GuildAccountMembershipsDecoder>(ClientMessageType.GameGuildAccountMemberships);
        services.AddGameCmdMessageDecoder<GuildRecommendationsDecoder>(ClientMessageType.GameGuildRecommendations);
        services.AddGameCmdMessageEncoder<GuildDiscoveryReply, GuildDiscoveryReplyEncoder>();
        services.AddProtocolCommandHandler<GameGuildDiscoveryCommand, bool, GameGuildDiscoveryCommandHandler>(
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildSearch, typeof(GuildDiscoveryRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildAccountMemberships, typeof(GuildDiscoveryRequest)),
            new ProtocolCommandMessageBinding(ClientMessageType.GameGuildRecommendations, typeof(GuildDiscoveryRequest)));
        services.RegisterProtocolMessageHandler<GuildDiscoveryRequestHandler, GuildDiscoveryRequest, GameGuildDiscoveryCommand>(
            ClientMessageType.GameGuildSearch);
        services.RegisterProtocolMessageHandler<GuildDiscoveryRequestHandler, GuildDiscoveryRequest, GameGuildDiscoveryCommand>(
            ClientMessageType.GameGuildAccountMemberships);
        services.RegisterProtocolMessageHandler<GuildDiscoveryRequestHandler, GuildDiscoveryRequest, GameGuildDiscoveryCommand>(
            ClientMessageType.GameGuildRecommendations);
    }
}
