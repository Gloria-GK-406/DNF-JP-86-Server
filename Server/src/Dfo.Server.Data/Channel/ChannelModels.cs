namespace Dfo.Server.Data.Channel;

public sealed record ChannelServerInfo(
    int ChannelId,
    string Name,
    int MaximumUsers,
    int CurrentUsers,
    string Host,
    int Port);

/// <summary>
/// The process-local Game listeners in the same order as the advertised channel catalogue.
/// </summary>
/// <remarks>
/// Listener ports and advertised ports are deliberately separate deployment values. Keeping the
/// former in this value prevents entry policy from treating a public/NAT port as a local bind.
/// </remarks>
public sealed record ChannelListenerOptions(
    IReadOnlyList<int> GamePorts,
    bool AllowAdvertisedPortFallback);

/// <summary>What a channel is, as far as entry policy is concerned.</summary>
public enum ChannelEntryKind
{
    /// <summary>An ordinary channel, open to any character.</summary>
    General,

    /// <summary>A channel that admits only characters at or above a minimum level.</summary>
    LevelRestricted,

    /// <summary>The duelling channel, which carries its own skill profile.</summary>
    FreeDuel,
}

/// <summary>
/// The channel a character is entering on, resolved from the listener that accepted the
/// connection.
/// </summary>
/// <remarks>
/// Entry policy asks what kind of channel this is rather than which port it is, so the
/// listener numbering stays a configuration detail.
/// </remarks>
public sealed record ChannelEntryIdentity(int ChannelId, int ListenerPort, ChannelEntryKind Kind)
{
    /// <summary>True when the duelling skill profile applies on this channel.</summary>
    public bool IsFreeDuel => Kind is ChannelEntryKind.FreeDuel;

    /// <summary>True when a character's position on this channel is not persisted.</summary>
    /// <remarks>
    /// The level-restricted channel places characters transiently: they return to wherever
    /// they were before entering it, so writing the transient position back would strand
    /// them there.
    /// </remarks>
    public bool IsTransient => Kind is ChannelEntryKind.LevelRestricted;
}

/// <summary>Why a character was refused entry to a channel.</summary>
/// <param name="ErrorCode">The code the legacy server reports to the client.</param>
/// <param name="Message">The operator-facing explanation the legacy server sends.</param>
public sealed record ChannelAdmissionRefusal(byte ErrorCode, string Message);

/// <summary>
/// Raised when a character may not enter the channel they connected to.
/// </summary>
/// <remarks>
/// Refusal is raised rather than returned so that no partially prepared entry can be
/// published: the selection never reaches publication once this leaves the policy.
/// </remarks>
public sealed class ChannelAdmissionRefusedException(
    ChannelEntryIdentity channel,
    ChannelAdmissionRefusal refusal)
    : InvalidOperationException(refusal.Message)
{
    public ChannelEntryIdentity Channel { get; } = channel;

    public ChannelAdmissionRefusal Refusal { get; } = refusal;
}
