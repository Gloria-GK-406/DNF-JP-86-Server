using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Dispatching;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Protocol.Game.GuildCreation;

public sealed record GuildCreationRequest(
    ClientMessageType MessageType, GameMessageMetadata Metadata, bool Malformed, string? Text = null) : IClientNetworkMessage;

internal sealed record GuildCreationReply(ServerMessageType MessageType, byte? Error = null) : IGameCmdMessage;
internal sealed record GuildCreationPermit : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildCreationPermit;
}

internal sealed record GuildCreated(GuildId Guild, string Name) : IGameNotiMessage
{
    public ServerMessageType MessageType => ServerMessageType.GameGuildCreated;
}

internal static class GuildCreationPayloadCodec
{
    // ClientHack 0020 changes A21's code page to UTF-8. The reference server supplies the
    // length-prefixed layout, while the JP86 client's configured code page supplies the encoding.
    private static readonly UTF8Encoding TextEncoding = new(false, true);

    internal static byte[] EncodeGuildText(string text) => TextEncoding.GetBytes(text);

    internal static string? DecodeText(ReadOnlySpan<byte> body, int minimumBytes = 0, int maximumBytes = int.MaxValue)
    {
        if (body.Length < sizeof(int))
            return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (length < minimumBytes || length > maximumBytes || length != body.Length - sizeof(int))
            return null;
        try
        {
            return TextEncoding.GetString(body[sizeof(int)..]);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    internal static byte[] EncodeCreated(GuildId guild, string name)
    {
        var text = EncodeGuildText(name);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(guild.Value);
        writer.Write(text.Length);
        writer.Write(text);
        return stream.ToArray();
    }
}

internal abstract class GuildTextDecoder(ClientMessageType type, int minimum, int maximum) : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body)
    {
        var text = GuildCreationPayloadCodec.DecodeText(body, minimum, maximum);
        return new GuildCreationRequest(type, metadata, text is null, text);
    }
}

internal sealed class GuildCheckNameDecoder() : GuildTextDecoder(ClientMessageType.GameGuildCheckName, 2, 24);
internal sealed class GuildCheckPromotionDecoder() : GuildTextDecoder(ClientMessageType.GameGuildCheckPromotion, 0, 80);
internal sealed class GuildPermitRequestDecoder() : GuildTextDecoder(ClientMessageType.GameGuildRequestCreationPermit, 0, 64);

internal sealed class GuildFinalizeCreationDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildCreationRequest(ClientMessageType.GameGuildFinalizeCreation, metadata, body is not [1]);
}

internal sealed class GuildCancelCreationDecoder : IGameMessageDecoder
{
    public IClientNetworkMessage Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body) =>
        new GuildCreationRequest(ClientMessageType.GameGuildCancelCreation, metadata, !body.IsEmpty);
}

internal sealed class GuildCreationReplyEncoder : GameCmdMessageEncoder<GuildCreationReply>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildCreationReply message) =>
        OnePayload(message, message.Error is { } error ? GameCmdEnvelope.Refuse(error) : GameCmdEnvelope.Accept([]));
}

internal sealed class GuildCreationPermitEncoder : GameNotiMessageEncoder<GuildCreationPermit>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildCreationPermit message) =>
        OnePayload(message, new byte[] { 1 });
}

internal sealed class GuildCreatedEncoder : GameNotiMessageEncoder<GuildCreated>
{
    protected override IReadOnlyList<GameMessagePayload> EncodePayloads(GuildCreated message) =>
        OnePayload(message, GuildCreationPayloadCodec.EncodeCreated(message.Guild, message.Name));
}

public sealed class GuildCreationRequestHandler(ICommandBus commands)
    : ClientMessageHandler<GuildCreationRequest>, IProtocolCommandMessageHandler<GameGuildCreationCommand>
{
    protected override async ValueTask HandleMessageAsync(NetworkDispatchContext context, GuildCreationRequest message, CancellationToken ct) =>
        _ = await commands.SendAsync(new GameGuildCreationCommand(context, message), ct).ConfigureAwait(false);
}

internal static class GuildCreationRegistration
{
    internal static void AddGuildCreationSlice(this IServiceCollection services)
    {
        services.AddGameCmdMessageDecoder<GuildCheckNameDecoder>(ClientMessageType.GameGuildCheckName);
        services.AddGameCmdMessageDecoder<GuildCheckPromotionDecoder>(ClientMessageType.GameGuildCheckPromotion);
        services.AddGameCmdMessageDecoder<GuildPermitRequestDecoder>(ClientMessageType.GameGuildRequestCreationPermit);
        services.AddGameCmdMessageDecoder<GuildFinalizeCreationDecoder>(ClientMessageType.GameGuildFinalizeCreation);
        services.AddGameCmdMessageDecoder<GuildCancelCreationDecoder>(ClientMessageType.GameGuildCancelCreation);
        services.AddGameCmdMessageEncoder<GuildCreationReply, GuildCreationReplyEncoder>();
        services.AddGameNotiMessageEncoder<GuildCreationPermit, GuildCreationPermitEncoder>();
        services.AddGameNotiMessageEncoder<GuildCreated, GuildCreatedEncoder>();
        ClientMessageType[] types =
        [
            ClientMessageType.GameGuildCheckName,
            ClientMessageType.GameGuildCheckPromotion,
            ClientMessageType.GameGuildRequestCreationPermit,
            ClientMessageType.GameGuildFinalizeCreation,
            ClientMessageType.GameGuildCancelCreation,
        ];
        services.AddProtocolCommandHandler<GameGuildCreationCommand, bool, GameGuildCreationCommandHandler>(
            [.. types.Select(type => new ProtocolCommandMessageBinding(type, typeof(GuildCreationRequest)))]);
        foreach (var type in types)
            services.RegisterProtocolMessageHandler<GuildCreationRequestHandler, GuildCreationRequest, GameGuildCreationCommand>(type);
    }
}
