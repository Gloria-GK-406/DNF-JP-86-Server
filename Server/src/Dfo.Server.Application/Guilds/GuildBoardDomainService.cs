using System.Text;
using Dfo.Server.Abstractions.Guilds;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Guilds;

internal sealed class GuildBoardDomainService(IGuildDomainStore store, TimeProvider clock) : IGuildBoardDomainService
{
    private const int Capacity = byte.MaxValue;
    private const int MaximumTextWidth = 120;

    public async ValueTask<GuildBoardSnapshot?> ReadAsync(CharacterId viewer, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(viewer, ct).ConfigureAwait(false) is not { Guild.Id: var guild })
            return null;
        return await ReadSnapshotAsync(guild, viewer, ct).ConfigureAwait(false);
    }

    public async ValueTask<GuildBoardWriteResult> WriteAsync(CharacterId author, string text, CancellationToken ct)
    {
        if (await store.FindMembershipAsync(author, ct).ConfigureAwait(false) is not { Guild.Id: var guild })
            return new(false, null);
        var written = IsValidText(text) && await store.TryWriteBoardPostAsync(
            guild, author, text, clock.GetUtcNow(), Capacity, ct).ConfigureAwait(false);
        return new(written, await ReadSnapshotAsync(guild, author, ct).ConfigureAwait(false));
    }

    public async ValueTask<bool> DeleteAsync(CharacterId actor, uint postId, CancellationToken ct)
    {
        if (postId == 0 || await store.FindMembershipAsync(actor, ct).ConfigureAwait(false) is not { } membership)
            return false;
        return await store.DeleteBoardPostAsync(membership.Guild.Id, postId, membership.Member.IsLeader ? null : actor, ct)
            .ConfigureAwait(false);
    }

    private async ValueTask<GuildBoardSnapshot> ReadSnapshotAsync(GuildId guild, CharacterId viewer, CancellationToken ct) =>
        new(viewer, clock.GetUtcNow(), await store.ReadBoardPostsAsync(guild, ct).ConfigureAwait(false));

    private static bool IsValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += rune.IsAscii ? 1 : 2;
            if (Rune.IsControl(rune) || width > MaximumTextWidth)
                return false;
        }
        return true;
    }
}
