using Dfo.Server.Abstractions.Channel;
using Dfo.Server.Data.Channel;

namespace Dfo.Server.Application.Tests.Support;

/// <summary>
/// Stands in for channel policy in tests whose subject is something else.
/// </summary>
/// <remarks>
/// Admits everyone by default. A test that wants a refusal supplies one, which is how the
/// refusal path is exercised without also depending on the level rule's current threshold.
/// </remarks>
internal sealed class StubChannelEntryPolicy(ChannelAdmissionRefusal? refusal = null)
    : IChannelEntryDomainService
{
        public ValueTask<Dfo.Server.Data.Channel.ChannelLoginEntry> ResolveLoginAsync(int listenerPort,CancellationToken ct) =>
            ValueTask.FromException<Dfo.Server.Data.Channel.ChannelLoginEntry>(new NotSupportedException("This channel-identity fixture does not supply login deployment facts."));

    /// <summary>An ordinary channel, which is what most tests are entering on.</summary>
    public static ChannelEntryIdentity GeneralChannel { get; } =
        new(ChannelId: 11, ListenerPort: 10011, ChannelEntryKind.General);

    /// <summary>The channel that turns low-level characters away.</summary>
    public static ChannelEntryIdentity RestrictedChannel { get; } =
        new(ChannelId: 100, ListenerPort: 10161, ChannelEntryKind.SaintHorn);

    /// <summary>The duelling channel.</summary>
    public static ChannelEntryIdentity FreeDuelChannel { get; } =
        new(ChannelId: 68, ListenerPort: 10068, ChannelEntryKind.FreeDuel);

    public ValueTask<ChannelEntryIdentity> ResolveAsync(
        int listenerPort,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(GeneralChannel with { ListenerPort = listenerPort });

    public ChannelAdmissionRefusal? Refuse(ChannelEntryIdentity channel, int characterLevel) => refusal;
}
