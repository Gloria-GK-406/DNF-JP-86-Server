using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Abstractions.Guilds;

public interface IGuildDungeonContentSource
{
    GuildDungeonCatalog? Read();
}

public interface IGuildDungeonStore
{
    ValueTask<GuildDungeonPeriod> ResolvePeriodAsync(GuildId guild, GuildDungeonPeriod calendar, CancellationToken ct);
    ValueTask AddExtensionAsync(GuildId guild, GuildDungeonPeriod period, CancellationToken ct);
    ValueTask<IReadOnlyDictionary<int, int>> ReadProgressAsync(GuildDungeonAdmission admission, CancellationToken ct);
    ValueTask<bool> TryRecordClearAsync(Guid run, CharacterId character, GuildDungeonAdmission admission, int dungeon,
        int contribution, CancellationToken ct);
    ValueTask<int> ReadRepeatBonusAsync(CharacterId character, CancellationToken ct);
    ValueTask<int> AdvanceRepeatBonusAsync(CharacterId character, int gain, CancellationToken ct);
}

public interface IGuildDungeonDomainService
{
    ValueTask<bool> TryExtendAsync(CharacterId character, int guildPoints, DateTimeOffset now, CancellationToken ct);
    bool Supports(int dungeon);
    ValueTask<int?> ReadMedalShopAsync(GuildId guild, DateTimeOffset now, CancellationToken ct);
    ValueTask<GuildDungeonView?> ReadAsync(CharacterId character, CancellationToken ct);
    ValueTask<int?> ReadRepeatBonusAsync(CharacterId character, CancellationToken ct);
    ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext entry, CancellationToken ct);
    ValueTask<bool> SettleAsync(AccountId account, CharacterId character, DungeonRunState run, DateTimeOffset now, CancellationToken ct);
}
