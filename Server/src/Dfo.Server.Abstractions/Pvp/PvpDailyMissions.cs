using Dfo.Server.Data.Channel;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Pvp;

public sealed record PvpDailyMission(ushort Id, int Progress, int Goal);

public interface IPvpDailyMissionQueryDomainService
{
    ValueTask<IReadOnlyList<PvpDailyMission>> CurrentAsync(SelectedSessionIdentity session, ChannelEntryIdentity channel,
        CancellationToken cancellationToken);
}

public interface IPvpDailyMissionStore
{
    ValueTask<int> ProgressAsync(CharacterId character, DateOnly day, ushort mission, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<ushort, int>> ReadDayAsync(CharacterId character, DateOnly day, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<int, int>> ReadMetricsAsync(CharacterId character, DateOnly day, ushort mission,
        CancellationToken cancellationToken);
    ValueTask<int> RecordMetricAsync(CharacterId character, DateOnly day, ushort mission, int metric, bool increment,
        CancellationToken cancellationToken);

    /// <summary>Advances an already deduplicated match. Returns true only for the increment that earns the reward.</summary>
    ValueTask<bool> AdvanceAsync(CharacterId character, DateOnly day, ushort mission, int goal, PvpMatchId match,
        CancellationToken cancellationToken, int amount = 1);
}
