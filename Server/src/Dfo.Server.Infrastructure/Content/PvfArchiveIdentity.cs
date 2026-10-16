using System.Security.Cryptography;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>Hashes the archive file that <see cref="PvfArchiveProvider"/> would open, once per process.</summary>
/// <remarks>Only a successful hash is cached: an archive that is absent now may be placed before the next ask.</remarks>
internal sealed class PvfArchiveIdentity(PvfContentOptions options) : IContentArchiveIdentity
{
    private readonly Lock _gate = new();
    private Task<string>? _hashing;

    public async ValueTask<string?> Sha256Async(CancellationToken cancellationToken)
    {
        Task<string> hashing;
        lock (_gate)
        {
            if (_hashing is null || _hashing.IsFaulted || _hashing.IsCanceled)
            {
                var path = options.ResolveArchivePath();
                if (path is null) return null;
                _hashing = Task.Run(() => HashAsync(path));
            }
            hashing = _hashing;
        }
        try { return await hashing.WaitAsync(cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
