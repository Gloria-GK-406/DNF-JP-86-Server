namespace Dfo.Server.Data.Inventory;

/// <summary>The four A21 socket keys, kept with the medal's item identity.</summary>
public readonly record struct GuardianGemSockets(long Packed)
{
    public const int Count = 4;
    private const int ItemIdBase = 89999;

    public ushort KeyAt(int socket)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(socket);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(socket, Count);
        return (ushort)((ulong)Packed >> (socket * 16));
    }

    public int ItemIdAt(int socket)
    {
        var key = KeyAt(socket);
        return key == 0 ? 0 : ItemIdBase + key;
    }

    public int OccupiedLength()
    {
        for (var socket = Count - 1; socket >= 0; socket--)
            if (KeyAt(socket) != 0) return socket + 1;
        return 0;
    }

    public GuardianGemSockets Install(int socket, int itemId)
    {
        _ = KeyAt(socket);
        if (!CanEncode(itemId)) throw new ArgumentOutOfRangeException(nameof(itemId));
        var key = checked((ushort)(itemId - ItemIdBase));
        var shift = socket * 16;
        return new((Packed & ~(0xFFFFL << shift)) | ((long)key << shift));
    }

    public static bool CanEncode(int itemId) => itemId > ItemIdBase && itemId <= ItemIdBase + ushort.MaxValue;
}
