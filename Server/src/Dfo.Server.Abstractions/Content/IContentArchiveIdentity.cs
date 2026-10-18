namespace Dfo.Server.Abstractions.Content;

/// <summary>Identity of the static content archive the server reads, for operators comparing deployments.</summary>
public interface IContentArchiveIdentity
{
    /// <summary>Lowercase hex SHA-256 of the archive file, or null when no archive can be found or read.</summary>
    ValueTask<string?> Sha256Async(CancellationToken cancellationToken);
}
