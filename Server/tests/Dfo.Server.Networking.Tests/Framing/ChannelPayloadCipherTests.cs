using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Dfo.Server.Networking.Framing;

namespace Dfo.Server.Networking.Tests.Framing;

[Trait("Category", "Unit")]
public sealed class ChannelPayloadCipherTests
{
    [Fact]
    public void UncompressedEncryption_MatchesTheLegacyAesVector()
    {
        var plaintext = new byte[20];
        "66"u8.CopyTo(plaintext.AsSpan(4));

        var encrypted = ChannelPayloadCipher.Encrypt(
            plaintext,
            "20260810000006",
            compress: false);

        Assert.Equal(
            Convert.FromHexString("0EE2E44B83907B0A7C7CDFC60E4547C7306401405749FB5325EE8EA058F3F253"),
            encrypted);
    }

    [Fact]
    public void CompressedEncryption_ProducesLegacyCompatibleZlibAndZeroPadding()
    {
        var encrypted = ChannelPayloadCipher.Encrypt(
            new byte[] { 0x10, 0x20, 0x30 },
            "20260810000006",
            compress: true);

        using var compressed = new MemoryStream(encrypted);
        using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
        using var ciphertext = new MemoryStream();
        zlib.CopyTo(ciphertext);
        using var aes = Aes.Create();
        aes.Key = Encoding.ASCII.GetBytes("20260810000006\0\0");
        var plaintext = aes.DecryptEcb(ciphertext.ToArray(), PaddingMode.None);

        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, plaintext[..3]);
        Assert.All(plaintext[3..], value => Assert.Equal(0, value));
    }
}
