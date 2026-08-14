using Dfo.Server.Data.Networking;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

public sealed class ClientHardwareDetailPayloadCodecTests
{
    // Real captured body (protocol record E-001, 2026-08-14 Windows client):
    // vendor bucket 0 (Intel), 12 logical processors, 2611 MHz, 16107 MB physical memory,
    // GPU PCI 0x8086:0xA7A8, 4093 MB video memory, OS 14 (Windows 10/11), bitness 2 (64-bit).
    private static readonly byte[] ObservedBody =
    [
        0x00,
        0x0C,
        0x33, 0x0A, 0x00, 0x00,
        0xEB, 0x3E,
        0x86, 0x80,
        0xA8, 0xA7,
        0xFD, 0x0F,
        0x0E,
        0x02,
    ];

    [Fact]
    public void Decode_InterpretsTheObservedBodyLosslessly()
    {
        var metadata = new GameMessageMetadata(0x01, 0x01DD, 0xA9CFE592, 2);

        var request = ClientHardwareDetailPayloadCodec.Decode(metadata, ObservedBody);

        Assert.Equal(ClientMessageType.GameClientHardwareDetail, request.MessageType);
        Assert.Equal(metadata, request.Metadata);
        Assert.Equal(0, request.CpuVendorBucket);
        Assert.Equal(12, request.CpuLogicalCores);
        Assert.Equal(2611u, request.CpuClockMhz);
        Assert.Equal(16107, request.TotalMemoryMegabytes);
        Assert.Equal(0x8086, request.GpuPciVendorId);
        Assert.Equal(0xA7A8, request.GpuPciDeviceId);
        Assert.Equal(4093, request.VideoMemoryMegabytes);
        Assert.Equal(0x0E, request.OperatingSystemKind);
        Assert.Equal(2, request.OperatingSystemBitness);
    }

    [Fact]
    public void Decode_ReadsMultiByteFieldsAsLittleEndian()
    {
        // Distinct byte patterns per field so a swapped pair or a shifted offset fails loudly.
        byte[] body =
        [
            0x02,
            0xFF,
            0x78, 0x56, 0x34, 0x12,
            0x34, 0x12,
            0x02, 0x10,
            0x04, 0x30,
            0x06, 0x50,
            0x07,
            0x01,
        ];

        var request = ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(2, request.CpuVendorBucket);
        Assert.Equal(255, request.CpuLogicalCores);
        Assert.Equal(0x12345678u, request.CpuClockMhz);
        Assert.Equal(0x1234, request.TotalMemoryMegabytes);
        Assert.Equal(0x1002, request.GpuPciVendorId);
        Assert.Equal(0x3004, request.GpuPciDeviceId);
        Assert.Equal(0x5006, request.VideoMemoryMegabytes);
        Assert.Equal(0x07, request.OperatingSystemKind);
        Assert.Equal(1, request.OperatingSystemBitness);
    }

    [Fact]
    public void Decode_RejectsBodiesThatAreNotSixteenBytes()
    {
        Assert.Throws<InvalidDataException>(
            () => ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, ObservedBody.AsSpan(0, 15)));
        Assert.Throws<InvalidDataException>(
            () => ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, [.. ObservedBody, 0x00]));
        Assert.Throws<InvalidDataException>(
            () => ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, []));
    }

    [Fact]
    public void Decode_PreservesValuesOutsideTheSampledDomains()
    {
        // The body has no count, tag or checksum, so there is no structural invariant beyond
        // length: unknown vendor buckets, OS enums and bitness values must survive decoding
        // rather than being rejected, because they are exactly the hardware not yet sampled.
        byte[] body = [.. Enumerable.Repeat<byte>(0xFF, 16)];

        var request = ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, body);

        Assert.Equal(0xFF, request.CpuVendorBucket);
        Assert.Equal(uint.MaxValue, request.CpuClockMhz);
        Assert.Equal(ushort.MaxValue, request.TotalMemoryMegabytes);
        Assert.Equal(0xFF, request.OperatingSystemBitness);
    }

    [Fact]
    public void Decode_AcceptsTheAllZeroBodyTheClientSendsWhenProbesFail()
    {
        // The client zero-initialises the record and only overwrites fields whose probe
        // succeeded, so an all-zero body is reachable (e.g. IsWow64Process failing keeps
        // bitness at 0). It must decode, not throw.
        var request = ClientHardwareDetailPayloadCodec.Decode(GameMessageMetadata.Empty, new byte[16]);

        Assert.Equal(0, request.OperatingSystemBitness);
        Assert.Equal(0u, request.CpuClockMhz);
    }
}
