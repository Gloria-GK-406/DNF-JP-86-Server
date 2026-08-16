using Dfo.Server.Abstractions.Networking;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Networking.Runtime;

/// <summary>
/// Maps a Game wire key to the decoder that owns it.
/// </summary>
/// <remarks>
/// Unlike the message handler registry there is no completeness check to run here: the wire key
/// space is open, because the client picks the bytes. An unregistered key is an ordinary fact
/// about traffic, not a configuration error, so lookup reports a miss and the caller logs the
/// frame and drops it. Duplicate registration is the only thing that can be wrong at startup,
/// and it is.
/// </remarks>
internal sealed class GameMessageDecoderRegistry
{
    private readonly IReadOnlyDictionary<(byte Selector, ushort Type), IGameMessageDecoder> decoders;

    public GameMessageDecoderRegistry(
        IEnumerable<GameMessageDecoderRegistration> registrations,
        IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(provider);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => (item.Selector, item.Type))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Game wire key 0x{duplicate.Key.Selector:X2}/0x{duplicate.Key.Type:X4} " +
                "was registered more than once.");
        }

        decoders = registrationList.ToDictionary(
            item => (item.Selector, item.Type),
            item => (IGameMessageDecoder)provider.GetRequiredService(item.DecoderType));
    }

    public IGameMessageDecoder? Find(byte selector, ushort type) =>
        decoders.TryGetValue((selector, type), out var decoder) ? decoder : null;
}

/// <summary>
/// Maps a Channel message number to the decoder that owns it. The open-key-space reasoning on
/// <see cref="GameMessageDecoderRegistry"/> applies here unchanged.
/// </summary>
internal sealed class ChannelMessageDecoderRegistry
{
    private readonly IReadOnlyDictionary<byte, IChannelMessageDecoder> decoders;

    public ChannelMessageDecoderRegistry(
        IEnumerable<ChannelMessageDecoderRegistration> registrations,
        IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(provider);
        var registrationList = registrations.ToArray();
        var duplicate = registrationList
            .GroupBy(item => item.MessageNumber)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"Channel message number 0x{duplicate.Key:X2} was registered more than once.");
        }

        decoders = registrationList.ToDictionary(
            item => item.MessageNumber,
            item => (IChannelMessageDecoder)provider.GetRequiredService(item.DecoderType));
    }

    public IChannelMessageDecoder? Find(byte messageNumber) =>
        decoders.TryGetValue(messageNumber, out var decoder) ? decoder : null;
}
