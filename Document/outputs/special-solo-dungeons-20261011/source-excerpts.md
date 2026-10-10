# 当前代码与上游参考摘录

代码仅证明各自实现，不是实机证据。版本身份见 inputs.json。

## current: Server/src/Dfo.Server.Application/World/DungeonEntryDecisionDomainService.cs

```csharp
1:using Dfo.Server.Abstractions.World;
2:
3:namespace Dfo.Server.Application.World;
4:
5:/// <summary>Random entry is deliberately disabled until its eligibility/probability rules exist.</summary>
6:public sealed class DungeonEntryDecisionDomainService : IDungeonEntryDecisionDomainService
7:{
8:    public DungeonEntryKind ResolveEntryKind(DungeonEntryContext context)
9:    {
10:        ArgumentNullException.ThrowIfNull(context);
11:        return context.RequestMode == 1 ? DungeonEntryKind.ManualHell : DungeonEntryKind.Normal;
12:    }
13:}
14:
```

## current: Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs

```csharp
70:    DungeonPathDirection Connections,
71:    IReadOnlyList<int> MapCandidates,
72:    IReadOnlyList<int> BossMapCandidates)
73:{
74:    /// <summary>False when content omits the door pattern; All then carries no pattern restriction.</summary>
75:    public bool HasDeclaredConnections { get; init; } = true;
76:
77:    /// <summary>An explicitly fixed resource with unrestricted connection metadata.</summary>
78:    public static DungeonCellDefinition Fixed(int mapId) =>
79:        new(DungeonPathDirection.All, [mapId], []) { HasDeclaredConnections = false };
80:}
81:
82:public enum DungeonEntryMode : byte { Maze, Tournament, VillageRevenge }
83:
84:/// <summary>
85:/// A dungeon as the archive declares it.
86:/// </summary>
87:/// <param name="Mazes">
88:/// Every maze declared, in archive order. Most dungeons declare one; the ones that declare
89:/// several expect the server to choose, which is what makes maze choice a rule rather than a
90:/// lookup.
```

## current: Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs

```csharp
338:}
339:
340:internal sealed record BossTowerBaselineMessage : GameStateSynchronizationMessage, IGameNotiMessage
341:{
342:    internal override ushort Type => 0x021F;
343:    internal override byte[] EncodeBody() => new byte[4];
344:}
345:
346:internal sealed record RentalStateMessage(
```

## upstream: Game/Dungeon/TowerOfDespairProgressService.cs

```csharp
1:using System;
2:using DungeonData = DfoServer.GameWorld.Dungeon;
3:
4:namespace DfoServer.Game.Dungeon
5:{
6:    public sealed class TowerOfDespairProgressService
7:    {
8:        private readonly TowerOfDespairProgressRepository _repository;
9:
10:        public TowerOfDespairProgressService(TowerOfDespairProgressRepository repository)
11:        {
12:            _repository = repository ?? throw new System.ArgumentNullException(nameof(repository));
13:        }
14:
15:        public int ResolveEntryDungeonId(int characterId, int requestedDungeonId)
16:        {
17:            if (!DungeonData.TryGetTowerOfDespairFloor(requestedDungeonId, out _))
18:                return requestedDungeonId;
19:
20:            if (!TryGetNextFloor(characterId, out var nextFloor, out _))
21:                return requestedDungeonId;
22:
23:            return DungeonData.TryGetTowerOfDespairDungeonId(nextFloor, out var dungeonId)
24:                ? dungeonId
25:                : requestedDungeonId;
26:        }
27:
28:        internal bool TryGetNextFloor(
29:            int characterId,
30:            out int nextFloor,
31:            out Exception error)
32:        {
33:            nextFloor = 1;
34:            error = null;
35:            try
36:            {
37:                nextFloor = _repository.GetNextFloor(characterId);
38:                return true;
39:            }
40:            catch (Exception ex)
41:            {
42:                error = ex;
43:                return false;
44:            }
45:        }
46:
47:        internal bool TryRecordClear(
48:            int characterId,
49:            int clearedDungeonId,
50:            out int nextFloor,
51:            out Exception error)
52:        {
53:            nextFloor = 0;
54:            error = null;
55:            if (!DungeonData.TryGetTowerOfDespairFloor(clearedDungeonId, out var clearedFloor))
56:                return false;
57:
58:            try
59:            {
60:                nextFloor = _repository.RecordClear(characterId, clearedFloor);
61:                return true;
62:            }
63:            catch (Exception ex)
64:            {
65:                error = ex;
66:                return false;
67:            }
68:        }
69:    }
70:}
```

## upstream: Game/Dungeon/TowerOfDespairProgressRepository.cs

```csharp
20:                .ConnectionString;
21:        }
22:
23:        public int GetNextFloor(int characterId)
24:        {
25:            if (characterId <= 0)
26:                return 1;
27:
28:            using (var connection = new SqliteConnection(_connectionString))
29:            {
30:                connection.Open();
31:                using (var command = connection.CreateCommand())
32:                {
33:                    command.CommandText = @"
34:SELECT highest_cleared_floor
35:FROM character_tower_of_despair_progress
36:WHERE character_id = @characterId;";
37:                    command.Parameters.AddWithValue("@characterId", characterId);
38:                    var value = command.ExecuteScalar();
39:                    if (value == null || value == DBNull.Value)
40:                        return 1;
41:
42:                    var highestClearedFloor = Math.Max(0, Math.Min(MaximumFloor, Convert.ToInt32(value)));
43:                    return Math.Min(MaximumFloor, highestClearedFloor + 1);
44:                }
45:            }
46:        }
47:
48:        public int RecordClear(int characterId, int clearedFloor)
49:        {
50:            if (characterId <= 0)
51:                throw new ArgumentOutOfRangeException(nameof(characterId));
52:            if (clearedFloor < 1 || clearedFloor > MaximumFloor)
53:                throw new ArgumentOutOfRangeException(nameof(clearedFloor));
54:
55:            using (var connection = new SqliteConnection(_connectionString))
```

## upstream: Game/Dungeon/TowerOfDespairProgressRepository.cs

```csharp
75:            int clearedFloor)
76:        {
77:            if (connection == null)
78:                throw new ArgumentNullException(nameof(connection));
79:            if (transaction == null)
80:                throw new ArgumentNullException(nameof(transaction));
81:            if (characterId <= 0)
82:                throw new ArgumentOutOfRangeException(nameof(characterId));
83:            if (clearedFloor < 1 || clearedFloor > MaximumFloor)
84:                throw new ArgumentOutOfRangeException(nameof(clearedFloor));
85:
86:            using (var command = connection.CreateCommand())
87:            {
88:                command.Transaction = transaction;
89:                command.CommandText = @"
90:INSERT INTO character_tower_of_despair_progress(
91:    character_id,
92:    highest_cleared_floor,
93:    updated_at)
94:VALUES(@characterId, @clearedFloor, CURRENT_TIMESTAMP)
95:ON CONFLICT(character_id) DO UPDATE SET
96:    highest_cleared_floor = MAX(
97:        character_tower_of_despair_progress.highest_cleared_floor,
98:        excluded.highest_cleared_floor),
99:    updated_at = CASE
100:        WHEN excluded.highest_cleared_floor
101:            > character_tower_of_despair_progress.highest_cleared_floor
102:        THEN CURRENT_TIMESTAMP
103:        ELSE character_tower_of_despair_progress.updated_at
104:    END;";
105:                command.Parameters.AddWithValue("@characterId", characterId);
106:                command.Parameters.AddWithValue("@clearedFloor", clearedFloor);
107:                command.ExecuteNonQuery();
108:            }
```

## upstream: Game/Dungeon/Tournament/TournamentDungeonRuntime.cs

```csharp
400:            }
401:
402:            var requiredActors = 15 * partyCount;
403:            if (candidates.Count < requiredActors)
404:            {
405:                failureReason =
406:                    $"candidate pool has {candidates.Count}, requires {requiredActors}";
407:                return false;
408:            }
409:
410:            Shuffle(candidates, next);
411:            var npcTeams = new List<TournamentTeamSnapshot>(15);
412:            for (var teamIndex = 0; teamIndex < 15; teamIndex++)
413:            {
414:                var members = new TournamentActorSnapshot[partyCount];
415:                for (var memberIndex = 0; memberIndex < partyCount; memberIndex++)
416:                {
417:                    members[memberIndex] =
418:                        candidates[teamIndex * partyCount + memberIndex];
419:                }
420:                npcTeams.Add(new TournamentTeamSnapshot(
421:                    position: 0,
422:                    members,
423:                    isPlayer: false));
424:            }
425:            npcTeams.Sort((left, right) =>
426:                left.TotalStrength.CompareTo(right.TotalStrength));
427:
428:            var playerMembers = new TournamentActorSnapshot[partyCount];
429:            for (var index = 0; index < playerMembers.Length; index++)
430:            {
431:                playerMembers[index] = new TournamentActorSnapshot(
432:                    code: 0,
433:                    strength: 0,
434:                    name: string.Empty,
435:                    level: 0,
436:                    actorType: 0);
437:            }
438:
439:            var playerPosition = Next(next, 16);
440:            var opponentPosition = playerPosition ^ 1;
441:            var firstRound = new TournamentTeamSnapshot[16];
442:            firstRound[playerPosition] = new TournamentTeamSnapshot(
443:                (byte)playerPosition,
444:                playerMembers,
445:                isPlayer: true);
446:            firstRound[opponentPosition] = WithPosition(
447:                npcTeams[0],
448:                opponentPosition);
449:
450:            var remainingTeams = npcTeams.GetRange(1, npcTeams.Count - 1);
451:            Shuffle(remainingTeams, next);
452:            var remainingIndex = 0;
453:            for (var position = 0; position < firstRound.Length; position++)
454:            {
455:                if (firstRound[position] == null)
456:                {
457:                    firstRound[position] = WithPosition(
458:                        remainingTeams[remainingIndex++],
459:                        position);
460:                }
461:            }
462:
463:            var rounds = new List<TournamentRoundSnapshot>(4)
464:            {
465:                new TournamentRoundSnapshot(1, firstRound),
466:            };
467:            IReadOnlyList<TournamentTeamSnapshot> previous = firstRound;
468:            for (byte round = 2; round <= 4; round++)
469:            {
470:                var nextRound = new TournamentTeamSnapshot[previous.Count / 2];
471:                for (var match = 0; match < nextRound.Length; match++)
472:                {
473:                    var left = previous[match * 2];
474:                    var right = previous[match * 2 + 1];
475:                    var winner = left.IsPlayer
476:                        ? left
477:                        : right.IsPlayer
478:                            ? right
479:                            : PickNpcWinner(left, right, next);
480:                    nextRound[match] = new TournamentTeamSnapshot(
481:                        (byte)match,
482:                        winner.Members,
483:                        winner.IsPlayer);
484:                }
485:
486:                rounds.Add(new TournamentRoundSnapshot(round, nextRound));
```

## upstream: Game/Dungeon/Tournament/TournamentDungeonRuntime.cs

```csharp
532:        private static TournamentTeamSnapshot PickNpcWinner(
533:            TournamentTeamSnapshot left,
534:            TournamentTeamSnapshot right,
535:            Func<int, int> next)
536:        {
537:            if (left.TotalStrength == right.TotalStrength)
538:                return Next(next, 2) == 0 ? left : right;
539:            return left.TotalStrength > right.TotalStrength ? left : right;
540:        }
```

## upstream: GameWorld/BloodAltarDungeonDefinition.cs

```csharp
373:        private static bool TryParsePhases(
374:            BloodAltarDungeonKind kind,
375:            string value,
376:            int monsterCount,
377:            out IReadOnlyList<BloodAltarPhaseDefinition> definitions,
378:            out string failureReason)
379:        {
380:            definitions = Array.Empty<BloodAltarPhaseDefinition>();
381:            failureReason = string.Empty;
382:            var width = kind == BloodAltarDungeonKind.Ultimate
383:                ? UltimatePhaseFieldCount
384:                : EndlessPhaseFieldCount;
385:            var tokens = ScriptValueTokenizer.Tokenize(value);
386:            if (tokens.Count == 0 || tokens.Count % width != 0)
387:            {
388:                failureReason =
389:                    $"blood altar phase rows have {tokens.Count} fields for width {width}";
390:                return false;
391:            }
392:
393:            var parsed = new List<BloodAltarPhaseDefinition>(tokens.Count / width);
394:            for (var offset = 0; offset < tokens.Count; offset += width)
395:            {
396:                if (!TryParseInt(tokens[offset], out var round)
397:                    || !TryParseInt(tokens[offset + 1], out var templateIndex)
398:                    || !TryParseInt(tokens[offset + 2], out var delay)
399:                    || !float.TryParse(
400:                        tokens[offset + 3],
401:                        NumberStyles.Float,
402:                        CultureInfo.InvariantCulture,
403:                        out var scale)
404:                    || !TryParseInt(tokens[offset + 4], out var flag)
405:                    || !TryParseInt(tokens[offset + 5], out var concurrent))
406:                {
407:                    failureReason =
408:                        $"blood altar phase row {offset / width} is malformed";
409:                    return false;
410:                }
411:
412:                var difficulty = (byte)0;
413:                if (kind == BloodAltarDungeonKind.Ultimate
414:                    && !TryParseDifficulty(tokens[offset + 6], out difficulty))
415:                {
416:                    failureReason =
417:                        $"blood altar phase row {offset / width} has invalid difficulty";
418:                    return false;
419:                }
420:                if (round < 0
421:                    || templateIndex < 0
422:                    || templateIndex >= monsterCount
423:                    || delay < 0
424:                    || scale <= 0
425:                    || float.IsNaN(scale)
426:                    || float.IsInfinity(scale)
427:                    || concurrent < 0
428:                    || concurrent > 10)
429:                {
430:                    failureReason =
431:                        $"blood altar phase row {offset / width} contains invalid values";
432:                    return false;
433:                }
434:
435:                parsed.Add(new BloodAltarPhaseDefinition(
436:                    round,
437:                    templateIndex,
438:                    delay,
439:                    scale,
440:                    flag,
441:                    concurrent,
442:                    difficulty));
443:            }
444:
445:            definitions = new ReadOnlyCollection<BloodAltarPhaseDefinition>(parsed);
```

## upstream: Game/Dungeon/DungeonEntryLimitService.cs

```csharp
34:    internal static class SpecialDungeonEntryLimitDefaults
35:    {
36:        internal static readonly SpecialDungeonEntryLimitDefault[] Entries =
37:        {
38:            new SpecialDungeonEntryLimitDefault(11006, 3, 0),
39:            new SpecialDungeonEntryLimitDefault(11007, 3, 1),
40:            new SpecialDungeonEntryLimitDefault(3054, 3, 2),
41:            new SpecialDungeonEntryLimitDefault(3056, 3, 3),
42:            new SpecialDungeonEntryLimitDefault(3057, 1, 4),
43:            new SpecialDungeonEntryLimitDefault(122, 9, 5),
44:            new SpecialDungeonEntryLimitDefault(4000, 1, 6),
45:            new SpecialDungeonEntryLimitDefault(3706, 3, 7),
46:            new SpecialDungeonEntryLimitDefault(4108, 1, 8),
47:            new SpecialDungeonEntryLimitDefault(4109, 1, 9),
48:            new SpecialDungeonEntryLimitDefault(4110, 1, 10),
49:            new SpecialDungeonEntryLimitDefault(4111, 1, 11),
50:            new SpecialDungeonEntryLimitDefault(4103, 3, 12),
51:            new SpecialDungeonEntryLimitDefault(4114, 3, 13),
52:            new SpecialDungeonEntryLimitDefault(4115, 3, 14),
53:            new SpecialDungeonEntryLimitDefault(4116, 3, 15),
54:            new SpecialDungeonEntryLimitDefault(4117, 3, 16),
55:            new SpecialDungeonEntryLimitDefault(4118, 3, 17),
56:            new SpecialDungeonEntryLimitDefault(4130, 3, 18),
57:            new SpecialDungeonEntryLimitDefault(3900, 3, 19),
58:            new SpecialDungeonEntryLimitDefault(4124, 1, 20),
59:            new SpecialDungeonEntryLimitDefault(4125, 1, 21),
60:            new SpecialDungeonEntryLimitDefault(4126, 1, 22),
61:            new SpecialDungeonEntryLimitDefault(4127, 1, 23),
62:            new SpecialDungeonEntryLimitDefault(4128, 1, 24),
63:            new SpecialDungeonEntryLimitDefault(4123, 3, 25),
64:        };
```

## upstream: Network/Core/PacketTypesA21.cs

```csharp
208:        POWER_WAR_PROCESS_INFO = 0x00C9, // 201： ENUM_NOTIPACKET_POWER_WAR_PROCESS_INFO
209:        VILLAGE_OBJECT_LIST = 0x00CA, // 202： ENUM_NOTIPACKET_VILLAGE_OBJECT_LIST
210:        REQUEST_DISJOINT_ITEM = 0x00CB, // 203： ENUM_NOTIPACKET_REQUEST_DISJOINT_ITEM
211:        NEXON_CASH_EVENT = 0x00CC, // 204： ENUM_NOTIPACKET_NEXON_CASH_EVENT
212:        EXPERT_JOB_INFO = 0x00CD, // 205： ENUM_NOTIPACKET_EXPERT_JOB_INFO
213:        CREATE_DISJOINT_STORE = 0x00CE, // 206： ENUM_NOTIPACKET_CREATE_DISJOINT_STORE
214:        HELLPARTY_VALUE = 0x00CF, // 207： ENUM_NOTIPACKET_HELLPARTY_VALUE
215:        BLOOD_DUNGEON_STATE_RANKING = 0x00D0, // 208： ENUM_NOTIPACKET_BLOOD_DUNGEON_STATE_RANKING
216:        BLOOD_DUNGEON_STATE_REWARD = 0x00D1, // 209： ENUM_NOTIPACKET_BLOOD_DUNGEON_STATE_REWARD
217:        BLOOD_MONSTER_SPAWN = 0x00D2, // 210： ENUM_NOTIPACKET_BLOOD_MONSTER_SPAWN
218:        START_BLOOD_MAP = 0x00D3, // 211： ENUM_NOTIPACKET_START_BLOOD_MAP
219:        BLOOD_INFO = 0x00D4, // 212： ENUM_NOTIPACKET_BLOOD_INFO
220:        BLOOD_CRAZY_MONSTER_SPAWN = 0x00D5, // 213： ENUM_NOTIPACKET_BLOOD_CRAZY_MONSTER_SPAWN
221:        BLOOD_ROUND_MAX_TIME = 0x00D6, // 214： ENUM_NOTIPACKET_BLOOD_ROUND_MAX_TIME
222:        BLOOD_ROUND_INTERVAL_TIME = 0x00D7, // 215： ENUM_NOTIPACKET_BLOOD_ROUND_INTERVAL_TIME
```

## upstream: Network/Core/PacketTypesA21.cs

```csharp
1481:        LODING_TIME_REPORT = 0x00FA, // 250： ENUM_CMDPACKET_LODING_TIME_REPORT
1482:        USE_SHARED_EFFECT_ITEM = 0x00FB, // 251： ENUM_CMDPACKET_USE_SHARED_EFFECT_ITEM
1483:        BUY_CERASHOP_LIMIT_ITEM = 0x00FC, // 252： ENUM_CMDPACKET_BUY_CERASHOP_LIMIT_ITEM
1484:        ADD_HACKTYPE_CNT = 0x00FD, // 253： ENUM_CMDPACKET_ADD_HACKTYPE_CNT
1485:        CHANGE_EMOTION = 0x00FE, // 254： ENUM_CMDPACKET_CHANGE_EMOTION
1486:        DIE_BLOOD_MONSTER = 0x00FF, // 255： ENUM_CMDPACKET_DIE_BLOOD_MONSTER
1487:        COMPOUND_EMBLEM = 0x0100, // 256： ENUM_CMDPACKET_COMPOUND_EMBLEM
1488:        MOTION_RESULT = 0x0101, // 257： ENUM_CMDPACKET_MOTION_RESULT
1489:        BLOOD_ROUND_UI_PREPARE_FINISH_ = 0x0102, // 258： ENUM_CMDPACKET_BLOOD_ROUND_UI_PREPARE_FINISH_
1490:        REQUEST_CONDITION_EVENT_REWARD = 0x0103, // 259： ENUM_CMDPACKET_REQUEST_CONDITION_EVENT_REWARD
1491:        CHANGE_ANOTHER_SKILL_TREE = 0x0104, // 260： ENUM_CMDPACKET_CHANGE_ANOTHER_SKILL_TREE
1492:        GUILD_CARGO = 0x0105, // 261： ENUM_CMDPACKET_GUILD_CARGO
1493:        GUILD_CARGO_HISTORY = 0x0106, // 262： ENUM_CMDPACKET_GUILD_CARGO_HISTORY
```

## upstream: Network/Builders/Init/BossTowerBodyBuilder.cs

```csharp
6:    public sealed class BossTowerBodyBuilder : IInitPacketBuilder
7:    {
8:        public ushort NotiType => 0x021F;
9:
10:        public bool TryBuild(SelectCharacterDataSnapshot snapshot, int occurrenceIndex, out byte[] body)
11:        {
12:            body = BitConverter.GetBytes(0); // boss_tower_placeholder: seed=0
13:            return true;
14:        }
```

## A21 处理注册身份摘录

```tsv
CMD	0x013D	0x00D060F0	0x00D0C385	batch	SELECT_ULTIMATE_DIFFICULTY
CMD	0x01CA	0x00D04F70	0x00D0C398	batch	TOURNAMENT_REWARD_SELECT_STATE
CMD	0x01CB	0x00D04FB0	0x00D0C3AB	batch	TOURNAMENT_REWARD_SELECT
CMD	0x0392	0x00D08400	0x00D0C404	batch	ADVANCE_ALTAR_START_GAME
CMD	0x0393	0x00D053A0	0x00D0C417	batch	ADVANCE_ALTAR_BUY_ITEM
CMD	0x0394	0x00D054B0	0x00D0C42A	batch	ADVANCE_ALTAR_SET_SLOT
CMD	0x0395	0x00D05610	0x00D0C43D	batch	ADVANCE_ALTAR_UPGRADE_GAGE
CMD	0x0396	0x00D06F00	0x00D0C450	batch	ADVANCE_ALTAR_SUMMON_UNIT
CMD	0x0397	0x00D05710	0x00D0C463	batch	ADVANCE_ALTAR_EXCHANGE_SLOT
CMD	0x0398	0x00D06160	0x00D0C476	batch	ADVANCE_ALTAR_PAUSE
CMD	0x0399	0x00D05880	0x00D0C489	batch	ADVANCE_ALTAR_GET_ACHIEVEMENT_REWARD
CMD	0x039A	0x00D05960	0x00D0C49C	batch	ADVANCE_ALTAR_RESET_STAR
NOTI	0x00D0	0x00D47070	0x00D5F93C	batch	BLOOD_DUNGEON_STATE_RANKING
NOTI	0x00D1	0x00D48A60	0x00D5F94F	batch	BLOOD_DUNGEON_STATE_REWARD
NOTI	0x00D2	0x00D47150	0x00D5F962	batch	BLOOD_MONSTER_SPAWN
NOTI	0x00D3	0x00D59ED0	0x00D5F975	batch	START_BLOOD_MAP
NOTI	0x00D4	0x00D74AD0	0x00D88BA4	batch	BLOOD_INFO
NOTI	0x00D5	0x00D472E0	0x00D5F988	batch	BLOOD_CRAZY_MONSTER_SPAWN
NOTI	0x00D6	0x00D47330	0x00D5F99B	batch	BLOOD_ROUND_MAX_TIME
NOTI	0x00D7	0x00D473C0	0x00D5F9AE	batch	BLOOD_ROUND_INTERVAL_TIME
NOTI	0x00EB	0x00D475D0	0x00D5FA46	batch	BLOOD_DUNGEON_CLEAR_COUNT
NOTI	0x0129	0x00D8C1F0	0x00D9AE0C	batch	ULTIMATE_DIFFICULTY_UI
NOTI	0x015C	0x00A15BA0	0x00A15FDA	forwarder	TOD_CLEAR_REWARD
NOTI	0x0183	0x00D56690	0x00D5FB89	batch	TOURNAMENT_INFO
NOTI	0x0184	0x00D5AB20	0x00D5FB9C	batch	TOURNAMENT_MAP_INFO
NOTI	0x0185	0x00D49030	0x00D5FBAF	batch	TOURNAMENT_CLEAR_REWARD
NOTI	0x0333	0x00D4E590	0x00D5FC21	batch	BOSS_TOWER_INFO
NOTI	0x0334	0x00D5BA20	0x00D5FC34	batch	START_BOSS_TOWER_MAP
NOTI	0x0335	0x00D47910	0x00D5FC47	batch	BOSS_TOWER_STATE_REWARD
NOTI	0x0336	0x00D47C00	0x00D5FC5A	batch	BOSS_TOWER_STATE_EPLP
NOTI	0x035B	0x00D4BE30	0x00D5FC93	batch	ADVANCE_ALTAR_STAGE_INFO
NOTI	0x035C	0x00D5C180	0x00D5FC6D	batch	ADVANCE_ALTAR_START_MAP
NOTI	0x035D	0x00D4BCC0	0x00D5FC80	batch	ADVANCE_ALTAR_SPAWN_UNIT
NOTI	0x035E	0x01E31720	0x00D5FCA6	batch	ADVANCE_ALTAR_UPDATE_STAGE_CLEAR
NOTI	0x035F	0x00D47CC0	0x00D5FCB9	batch	ADVANCE_ALTAR_TEST_CHECK_GAGE
NOTI	0x0360	0x00D47D10	0x00D5FCCC	batch	ADVANCE_ALTAR_EPLP_STATE
NOTI	0x0361	0x00D50820	0x00D5FCDF	batch	ADVANCE_ALTAR_CLEAR_STAGE
NOTI	0x0362	0x00D56CA0	0x00D5FCF2	batch	ADVANCE_ALTAR_UPDATE_RIDABLE
NOTI	0x036F	0x00CAA870	0x00CAB15D	forwarder	PVP_TOURNAMENT_INFO
```
