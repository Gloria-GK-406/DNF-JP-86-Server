namespace Dfo.Server.Data.OnlineUsers;

public readonly record struct SessionIpv4Address(byte A, byte B, byte C, byte D)
{
    public bool IsServerObservedUnicast => A is > 0 and < 224;

    public bool IsUsableUnicast => A is > 0 and < 224 and not 127;
}

/// <summary>The latest UDP endpoint observed for one exact game-connection session.</summary>
public sealed record SessionEndpoint(
    byte NatType,
    SessionIpv4Address InnerAddress,
    SessionIpv4Address OuterAddress,
    ushort Port,
    uint Mtu)
{
    public const uint MinimumMtu = 576;
    public const uint MaximumMtu = ushort.MaxValue;

    public bool IsValid =>
        InnerAddress.IsServerObservedUnicast
        && OuterAddress.IsServerObservedUnicast
        && Port != 0
        && Mtu is >= MinimumMtu and <= MaximumMtu;

    public bool IsValidClientReport =>
        InnerAddress.IsUsableUnicast
        && OuterAddress.IsUsableUnicast
        && Port != 0
        && Mtu is >= MinimumMtu and <= MaximumMtu;
}
