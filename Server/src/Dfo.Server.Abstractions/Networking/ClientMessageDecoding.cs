using Dfo.Server.Data.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Abstractions.Networking;

/// <summary>
/// Decodes the body of one Game endpoint wire type into a typed client message.
/// </summary>
/// <remarks>
/// The two endpoints do not share a frame header -- Game carries a selector and a type, Channel a
/// classification and a message number -- so each gets its own seam. A merged seam would hand every
/// implementer fields that are meaningless for its own endpoint.
///
/// Returning null means "this body is not this message". The caller drops such a frame silently, so
/// a decoder signals a body it does not recognise by returning null and never by throwing. Throwing
/// remains reserved for a body that is recognised but malformed, which is the existing behaviour of
/// the payload codecs this seam fronts.
/// </remarks>
public interface IGameMessageDecoder
{
    IClientNetworkMessage? Decode(GameMessageMetadata metadata, ReadOnlySpan<byte> body);
}

/// <summary>
/// Decodes one Channel endpoint message number into a typed client message. See
/// <see cref="IGameMessageDecoder"/> for why the two endpoints have separate seams and for the
/// null-versus-throw rule, which is identical here.
/// </summary>
public interface IChannelMessageDecoder
{
    IClientNetworkMessage? Decode(ChannelMessageMetadata metadata, ReadOnlySpan<byte> body);
}

/// <summary>Binds one Game wire key to the decoder that owns it.</summary>
public sealed record GameMessageDecoderRegistration(byte Selector, ushort Type, Type DecoderType);

/// <summary>Binds one Channel message number to the decoder that owns it.</summary>
public sealed record ChannelMessageDecoderRegistration(byte MessageNumber, Type DecoderType);

/// <summary>
/// Declares that a decoder can produce this message type.
/// </summary>
/// <remarks>
/// The wire key space and the message type space are not the same space: one wire key may produce
/// different message types depending on the body, and a produced type may have no wire number of
/// its own. This flat declaration is therefore the only thing a coverage check needs, and it is
/// deliberately free of any endpoint shape so that the check does not have to know about either.
/// </remarks>
public sealed record ClientMessageProductionRegistration(ClientMessageType MessageType, Type DecoderType);

public static class ClientMessageDecodingExtensions
{
    /// <summary>
    /// Registers a decoder for one Game wire key together with every message type it can produce.
    /// </summary>
    public static IServiceCollection AddGameMessageDecoder<TDecoder>(
        this IServiceCollection services,
        byte selector,
        ushort type,
        params ClientMessageType[] produces)
        where TDecoder : class, IGameMessageDecoder
    {
        ArgumentNullException.ThrowIfNull(services);
        RequireDeclaredProductions<TDecoder>(produces);
        services.AddSingleton<TDecoder>();
        services.AddSingleton(new GameMessageDecoderRegistration(selector, type, typeof(TDecoder)));
        return AddProductions<TDecoder>(services, produces);
    }

    /// <summary>
    /// Registers a decoder for one Channel message number together with every message type it can
    /// produce.
    /// </summary>
    public static IServiceCollection AddChannelMessageDecoder<TDecoder>(
        this IServiceCollection services,
        byte messageNumber,
        params ClientMessageType[] produces)
        where TDecoder : class, IChannelMessageDecoder
    {
        ArgumentNullException.ThrowIfNull(services);
        RequireDeclaredProductions<TDecoder>(produces);
        services.AddSingleton<TDecoder>();
        services.AddSingleton(new ChannelMessageDecoderRegistration(messageNumber, typeof(TDecoder)));
        return AddProductions<TDecoder>(services, produces);
    }

    /// <summary>
    /// Validated before anything is added, so a rejected registration leaves the service collection
    /// exactly as it found it.
    /// </summary>
    private static void RequireDeclaredProductions<TDecoder>(ClientMessageType[] produces)
    {
        ArgumentNullException.ThrowIfNull(produces);
        if (produces.Length == 0)
        {
            // A decoder that declares nothing would decode messages no coverage check can see,
            // which is the exact gap this registration exists to close.
            throw new ArgumentException(
                $"Decoder '{typeof(TDecoder).Name}' must declare at least one produced message type.",
                nameof(produces));
        }
    }

    private static IServiceCollection AddProductions<TDecoder>(
        IServiceCollection services,
        ClientMessageType[] produces)
    {
        foreach (var messageType in produces)
        {
            services.AddSingleton(new ClientMessageProductionRegistration(messageType, typeof(TDecoder)));
        }

        return services;
    }
}
