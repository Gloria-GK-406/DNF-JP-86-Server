using System.Globalization;
using System.Text.RegularExpressions;
using Dfo.Server.Testing;
using Microsoft.Data.Sqlite;
using static Dfo.Server.Networking.Tests.Characterization.CharacterizationSteps;

namespace Dfo.Server.Networking.Tests.Characterization;

/// <summary>RQ-080 (e): a party of two enters the Tower of Death (11000) through SELECT_DUNGEON.</summary>
[Collection(CharacterizationCollection.Name)]
public sealed class PartyTowerCharacterizationTests
{
    private const int TowerOfDeath = 11000;
    private const byte TowerLevel = 50;

    [PvfFact]
    public async Task A_party_entering_the_tower_of_death_receives_the_recorded_transcripts()
    {
        using var timeout = new CancellationTokenSource(CharacterizationCollection.Budget);
        await using var server = await CharacterizationHost.StartAsync(null, timeout.Token);
        // The tower admits level 50 and above and takes one item 4183 from each member. Nothing on
        // the wire raises a new character past level 2 (TUTORIAL_LEVEL_UP) or hands it 4183, so both
        // are written to SQLite after creation and before selection -- the one preparation the
        // game protocol cannot perform. The level column follows experience through the schema's
        // projection trigger.
        var (leader, member) = await TwoPlayersInTownAsync(
            server, timeout.Token, () => PrepareEveryCharacterAsync(server, timeout.Token));
        using var _ = leader;
        using var __ = member;
        await FormPartyAsync(leader, member, timeout.Token);
        await OpenDungeonListAsync(leader, member, timeout.Token);
        // Tower entry has no Appendix D order requirement; only each connection's transcript is pinned.
        await EnterDungeonAsync(leader, member, TowerOfDeath, TowerMap, timeout.Token);

        await CharacterizationTranscript.AssertMatchesAsync("e-party-tower-entry", leader, timeout.Token, TowerMasks);
        await CharacterizationTranscript.AssertMatchesAsync("e-party-tower-entry", member, timeout.Token, TowerMasks);
    }

    /// <summary>NOTI 0x008F, the tower map, ends each member's tower entry.</summary>
    private static bool TowerMap(WireFrame frame) => frame.Is(0x00, 0x008F);

    /// <remarks>
    /// The run seed is drawn by IllusionTowerProjectionDelivery itself (Random.Shared.Next(), passed
    /// into EnterIllusionTowerCommand), before any injectable service sees it. Pinning it would mean
    /// decorating the whole tower domain service, which is the code this refactor reshapes; the
    /// floor-one room it seeds has fixed actors, so only the seed itself shows on the wire.
    /// </remarks>
    internal static readonly IReadOnlyList<FrameMask> TowerMasks =
    [
        new(0x00, 0x008F, WireFrame.HeaderLength + 2, 4,
            "ILLUSION_TOWER_MAP, body 2..5: room.Seed, derived from the run seed IllusionTowerProjectionDelivery " +
            "draws from Random.Shared (IllusionTowerMapPayloadCodec.Encode)."),
    ];

    private static async Task PrepareEveryCharacterAsync(CharacterizationHost server, CancellationToken cancellationToken)
    {
        var experience = await ExperienceForLevelAsync(server, TowerLevel, cancellationToken);
        await server.ExecuteSqlAsync(
            $"UPDATE characters SET exp = {experience.ToString(CultureInfo.InvariantCulture)} WHERE level < {TowerLevel};",
            cancellationToken);
        // One 4183 (kind 4, a quest-space pile in the archive) at Main/177, the first quest slot,
        // in the same 82-byte core layout CREATE_CHARACTER writes for its own pile: kind, template,
        // value = count, marker -1 at 21, "no changed random option" 0xFF at 66.
        var core = new byte[82];
        core[0] = 4;
        BitConverter.TryWriteBytes(core.AsSpan(1), TowerTicket);
        BitConverter.TryWriteBytes(core.AsSpan(5), 1);
        BitConverter.TryWriteBytes(core.AsSpan(21), -1);
        core[66] = 0xFF;
        await server.ExecuteSqlAsync(
            $"""
            INSERT INTO items(item_core)
            SELECT X'{Convert.ToHexString(core)}' FROM characters c
            WHERE NOT EXISTS (SELECT 1 FROM item_slots s WHERE s.owner_scope='character' AND s.owner_id=c.character_id AND s.id={TowerTicket})
            ORDER BY c.character_id;
            INSERT INTO item_slots(owner_scope, owner_id, list_type, slot_index, kind, id, core_uid, count)
            SELECT 'character', c.character_id, 0, 177, 'template', {TowerTicket},
                   (SELECT MAX(item_uid) FROM items), 1
            FROM characters c
            WHERE NOT EXISTS (SELECT 1 FROM item_slots s WHERE s.owner_scope='character' AND s.owner_id=c.character_id AND s.id={TowerTicket});
            """,
            cancellationToken);
    }

    private const int TowerTicket = 4183;

    /// <summary>
    /// The least experience that projects to <paramref name="level"/>, read off the projection trigger
    /// the server installed from its own experience table (CharacterExperienceStoragePolicy).
    /// </summary>
    private static async Task<long> ExperienceForLevelAsync(
        CharacterizationHost server, byte level, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection($"Data Source={server.DatabasePath};Pooling=False");
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='trigger' AND name='character_level_update_projection';";
        var sql = (string)(await command.ExecuteScalarAsync(cancellationToken))!;
        var match = Regex.Match(sql, $@"WHEN NEW\.exp < (\d+) THEN {level - 1} ");
        Assert.True(match.Success, "The level projection trigger has no threshold for level " + level);
        return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }
}
