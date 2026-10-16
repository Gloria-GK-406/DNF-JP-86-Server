using System.Security.Cryptography;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfArchiveIdentityTests
{
    [Fact]
    public async Task HashesTheConfiguredArchiveOnceAndReportsLowercaseHex()
    {
        var path = Path.Combine(Path.GetTempPath(), "dfo-pvf-identity-" + Guid.NewGuid().ToString("N") + ".bin");
        byte[] bytes = [1, 2, 3, 4, 5]; await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var identity = new PvfArchiveIdentity(new PvfContentOptions { ArchivePath = path });
            var expected = Convert.ToHexStringLower(SHA256.HashData(bytes));
            Assert.Equal(expected, await identity.Sha256Async(default));
            await File.WriteAllBytesAsync(path, [9, 9]);
            Assert.Equal(expected, await identity.Sha256Async(default));
        }
        finally { File.Delete(path); }
    }
}
