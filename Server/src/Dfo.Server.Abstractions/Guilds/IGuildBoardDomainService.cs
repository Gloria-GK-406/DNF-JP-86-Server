using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildBoardDomainService
{
    ValueTask<GuildBoardSnapshot?> ReadAsync(CharacterId viewer, CancellationToken ct);
    ValueTask<GuildBoardWriteResult> WriteAsync(CharacterId author, string text, CancellationToken ct);
    ValueTask<bool> DeleteAsync(CharacterId actor, uint postId, CancellationToken ct);
}
