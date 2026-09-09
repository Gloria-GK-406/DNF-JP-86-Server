namespace Dfo.Server.Data.Characters;

/// <summary>Unpacked career identity. Bit shape and archive support are separate questions.</summary>
public readonly record struct CharacterCareer
{
    public CharacterCareer(int job, int growType = 0, int awakening = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(job);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(job, byte.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegative(growType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(growType, 15);
        ArgumentOutOfRangeException.ThrowIfNegative(awakening);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(awakening, 15);
        Job = (byte)job;
        GrowType = (byte)growType;
        Awakening = (byte)awakening;
    }

    public byte Job { get; }
    public byte GrowType { get; }
    public byte Awakening { get; }
    public byte PackedGrowType => (byte)(GrowType | (Awakening << 4));

    public static CharacterCareer FromPacked(byte job, int packedGrowType)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(packedGrowType);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(packedGrowType, byte.MaxValue);
        return new(job, packedGrowType & 0x0F, packedGrowType >> 4);
    }
}

