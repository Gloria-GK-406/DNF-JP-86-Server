using Dfo.Server.Data.Characters;

namespace Dfo.Server.Data.Guilds;

public sealed record GuildBoardPost(uint Id, CharacterId Author, byte[] AuthorName, byte AuthorJob, bool AuthorIsLeader,
    string Text, DateTimeOffset PostedAt);

public sealed record GuildBoardSnapshot(CharacterId Viewer, DateTimeOffset ReadAt, IReadOnlyList<GuildBoardPost> Posts);

public sealed record GuildBoardWriteResult(bool Written, GuildBoardSnapshot? Snapshot);
