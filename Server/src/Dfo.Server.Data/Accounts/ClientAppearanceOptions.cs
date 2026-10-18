using System.Buffers.Binary;

namespace Dfo.Server.Data.Accounts;

/// <summary>Read-only interpretation of the three verified A21 main-option slots.</summary>
public sealed class ClientAppearanceOptions
{
    private readonly byte[]? blob;
    private readonly byte fallback;

    public ClientAppearanceOptions(byte[]? blob, byte fallbackVisibility)
    {
        if (blob is not null && blob.Length != 150) throw new ArgumentException("A21 main options require 150 bytes.", nameof(blob));
        this.blob = blob?.ToArray();
        fallback = fallbackVisibility;
    }

    public bool ShowGrowEffect => Read(2, (fallback & 2) != 0);
    public bool ShowFullAvatar => Read(110, (fallback & 8) == 0);
    public bool ShowAwakeningAvatar => Read(148, (fallback & 16) == 0);

    /// <summary>Transient wire projection; unrelated legacy bits remain unchanged.</summary>
    public byte VisibilityBits => (byte)((fallback & ~26)
        | (ShowGrowEffect ? 2 : 0) | (ShowFullAvatar ? 0 : 8) | (ShowAwakeningAvatar ? 0 : 16));

    private bool Read(int offset, bool missing) => blob is null
        ? missing : BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(offset, 2)) != 0;
}
