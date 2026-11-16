using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.World;

/// <summary>
/// Which of a room's ordinary monsters the room promotes to champions when it is first built.
/// </summary>
/// <remarks>
/// 70Reference (<c>df_game_r</c>, SHA-256 CF45F4E8…2EB7):
/// <c>CBattle_Field::getAddChampionMobCnt</c> @ 0x083010EE gives the count and
/// <c>add_monsters_current_map</c> @ 0x0830199F..0x083019F4 with
/// <c>add_monster_additional_champion</c> @ 0x083022A8 picks the monsters. The count is the map's
/// fixed <c>[champion] N</c> plus, unless the map says <c>[fix champion]</c>, one more when a roll
/// of 0..99 falls under <c>rate[difficulty] * {100,150,250,500,100}[difficulty] / 100 * 100 /
/// (maze width * height)</c>, every step truncating. It is a per-room chance of one extra
/// champion, not a per-monster chance.
/// <para>
/// Promotion only ever writes type 1. Type 2 (super champion) comes from a map that declares it,
/// never from here. The entry frame carries the type and the room seed, not a feature list: 70
/// rolls the features server-side for its own combat numbers, which this server does not simulate,
/// so no features are chosen here.
/// </para>
/// <para>
/// Departures from 70, kept deliberately: rolls come from <see cref="DropRandom"/> keyed by the run
/// seed and the cell instead of the global <c>rand()</c>, so a room described twice answers the
/// same; and candidates must be hostile, which also excludes A21 dummy rows (a modifier 70 does not
/// have). <c>[half]</c>/<c>[ratio]</c> rows are one actor each here, so 70's per-instance
/// expansion has nothing to expand. Box-spawned monsters, which in 70 take a slot without being
/// promoted, are object templates here and are simply not candidates.
/// </para>
/// </remarks>
internal static class ChampionPromotionRules
{
    /// <summary>The difficulty multiplier, in percent. The fifth tier is not multiplied.</summary>
    private static int DifficultyPercent(int index) => index switch
    {
        0 or 4 => 100,
        1 => 150,
        2 => 250,
        3 => 500,
        _ => 0,
    };

    /// <summary>Salts the room seed away from every actor sequence (1..60000) a drop seed uses.</summary>
    private const int Salt = -0x43484D50;

    /// <summary>The percent chance (0..∞, at or above 100 always) that the room gains one random champion.</summary>
    public static int RandomChance(IReadOnlyList<int> rates, DungeonDifficulty difficulty, byte width, byte height)
    {
        ArgumentNullException.ThrowIfNull(rates);
        var index = (int)difficulty;
        var area = width * height;
        if (index >= rates.Count || area <= 0)
            return 0;
        var adjusted = checked(rates[index] * DifficultyPercent(index)) / 100;
        return checked(adjusted * 100) / area;
    }

    /// <summary>The room's actors with the chosen ordinary monsters raised to type 1.</summary>
    public static IReadOnlyList<RoomActorDescription> Promote(
        IReadOnlyList<RoomActorDescription> actors, MapChampionDeclaration map,
        DungeonCellCreationContext context)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Layout.ChampionRates is not { } rates)
            return actors;

        var random = new DropRandom(DropRandom.SeedFor(
            context.Layout.Seed, context.MapId, context.Cell.X, context.Cell.Y, Salt));
        var count = map.FixedCount;
        if (!map.SuppressesRandom && random.Hits(
                RandomChance(rates, context.Difficulty, context.Layout.Width, context.Layout.Height), 100))
            count++;
        if (count <= 0)
            return actors;

        var candidates = Enumerable.Range(0, actors.Count).Where(index => IsCandidate(actors[index])).ToList();
        if (candidates.Count == 0)
            return actors;
        var promoted = actors.ToArray();
        for (var picked = 0; picked < count && candidates.Count > 0; picked++)
        {
            var at = random.Next(candidates.Count) - 1;
            var index = candidates[at];
            candidates.RemoveAt(at);
            promoted[index] = promoted[index] with
            {
                Actor = promoted[index].Actor with { ActorType = (byte)MonsterRank.Champion },
            };
        }
        return promoted;
    }

    /// <summary>A plain <c>[normal]</c> row of the map's <c>[monster]</c> list that is not named.</summary>
    private static bool IsCandidate(RoomActorDescription actor) =>
        !actor.IsAiCharacter && !actor.IsNamedMonster && !actor.IsHellTemplate
        && actor.ObjectTemplateOwner is null
        && actor.Actor.ActorType == (byte)MonsterRank.Ordinary && actor.Actor.IsHostile
        && actor.Actor.Declaration is null or { Source: DungeonActorSpawnSource.Monster };
}
