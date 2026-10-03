# Source excerpts at collection

Code proves implementation only, not wire or client acceptance.

Repository: `E:\Project\DNF\DNF-JP-86-Server\.worktrees\warehouse-storage`; HEAD: `bd029df3b7b64e7e1335e157cae82ffb1891863d`.

Repository: `E:\Project\DNF\ServerS4A21`; HEAD: `74a077a8cb4957282eb40210735f3a760c26b83e`.

## warehouse-storage: Server/src/Dfo.Server.Abstractions/World/IDungeonLayoutRuleService.cs

SHA-256: `bceb5f35ac099a70833c35aa500a9f76db4e4dfbb5c12c093e4e35fd66350853`.

```csharp
20:     public static DungeonEntryIntent Directed(int dungeonId) =>
21:         dungeonId > 0
22:             ? new DungeonEntryIntent(DungeonEntryIntentKind.Directed, dungeonId)
23:             : throw new ArgumentOutOfRangeException(nameof(dungeonId));
24: }
25:
26: public enum DungeonRunPurposeKind : byte
27: {
28:     Ordinary = 0,
29:     QuestLinked = 1,
30: }
```

```csharp
99: public sealed record DungeonLayout(
100:     int DungeonId,
101:     int MazeIndex,
102:     byte Width,
103:     byte Height,
104:     MazeCell StartCell,
105:     MazeCell BossCell,
106:     int StartRoomMapId,
107:     int Seed,
108:     IReadOnlyDictionary<MazeCell, int> Rooms,
109:     byte BasisLevel = 1,
110:     double ExperienceWeight = 1.0,
111:     DungeonRunPurpose Purpose = default,
112:     bool ConsumesFatigue = true)
113: {
114:
115:     /// <summary>Resolve an absolute map level or a level relative to this dungeon.</summary>
116:     /// <remarks>
117:     /// The dungeon's <c>[basis level]</c> belongs here because a map may be shared between
```

## warehouse-storage: Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs

SHA-256: `1711ab760c9bd6668026cc04be270f7f0721be1a56530648cb7ba791fb32bc6e`.

```csharp
77:
78:     /// <summary>Whether every actor this room declares has been reported dead.</summary>
79:     /// <remarks>
80:     /// AI characters are not counted. They fight beside the player rather than against them, and a
81:     /// room whose clearing waited on the tutorial's own escort would never clear at all.
82:     /// </remarks>
83:     public bool IsCleared => this.CurrentCellState.IsCleared;
84:
85:     /// <summary>What the room the character is standing in is for.</summary>
86:     public DungeonRoomKind CurrentRoomKind => Layout.KindOf(CurrentCell);
87:
88:     /// <summary>Whether this room is the one the run's boss stands in.</summary>
89:     public bool IsBossRoom => CurrentRoomKind == DungeonRoomKind.Boss;
90:
91:     /// <summary>
92:     /// Whether the character may walk out of the room they are in.
93:     /// </summary>
94:     /// <remarks>
95:     /// A live session on 2026-08-26 walked out of the boss room after clearing it and back through
96:     /// rooms it had already emptied, which the game does not allow. The rule is the room's, not the
97:     /// run's: the boss room has no way back out of it, and a dungeon that later grows a room with
98:     /// its own answer brings that answer with it rather than adding a case here.
99:     /// </remarks>
100:     public bool MayLeaveCurrentRoom => CurrentRoomKind != DungeonRoomKind.Boss;
101:
```

```csharp
195: public sealed record ClearRewardCardSet(
196:     ClearRewardCardFace Free,
197:     ClearRewardCardFace Paid,
198:     ImmutableHashSet<ClearRewardCardId> Claimed,
199:     int? PaidFlipCost)
200: {
201:     public ClearRewardCardFace? BlackDiamond { get; init; }
202:
203:     /// <summary>The face of that kind.</summary>
```

## warehouse-storage: Server/src/Dfo.Server.Abstractions/World/DungeonCellState.cs

SHA-256: `85f7bb733c1d1e325e89d70f771e6272599e44459c6a27fe882c2772565e3f4b`.

```csharp
78: public sealed record DungeonCellState(
79:     MazeCell Cell,
80:     int MapId,
81:     long Revision,
82:     ushort FirstActorSequence,
83:     IReadOnlyList<DungeonCellActorState> Actors,
84:     IReadOnlyList<DungeonGroundItem> GroundItems,
85:     bool ClearOpen = false)
86: {
87:     public IReadOnlyList<DungeonCellObjectState> Objects { get; init; } = [];
88:
89:     public bool IsCleared => this.Actors
90:         .Where(actor => actor.IsMonster)
91:         .All(actor => actor.IsDead);
92:
```

## warehouse-storage: Server/src/Dfo.Server.Abstractions/World/MonsterExperienceRules.cs

SHA-256: `f6c699db9ed9aa3d63a48ae4cf528026960c80841deffa68f5ff1429901b5094`.

```csharp
7: public enum MonsterRank : byte
8: {
9:     Ordinary = 0,
10:     Champion = 1,
11:     SuperChampion = 2,
12:     Boss = 3,
13: }
14:
15: /// <summary>Everything about one kill that decides what it is worth.</summary>
16: /// <remarks>
17: /// Nothing about who made the kill beyond their level. What a character carries is added on top of
18: /// this and belongs to a different rule; anyone of the same level killing the same monster in the
```

```csharp
94:     public static bool IsMonster(byte declaredActorType) =>
95:         declaredActorType <= (byte)MonsterRank.Boss;
```

## warehouse-storage: Server/src/Dfo.Server.Application/World/DungeonClearDomainService.cs

SHA-256: `14a31ae896782c6dfc24eec4f3505d46a849f8a330a14a5d105ea37646a92b55`.

```csharp
15:         AccountId accountId,
16:         CharacterId characterId,
17:         DungeonRunId expectedRunId,
18:         long expectedRoomVersion,
19:         CancellationToken cancellationToken)
20:     {
21:         var run = runs.Find(characterId);
22:         if (run is null || run.RunId != expectedRunId)
23:             run = runs.Find(expectedRunId);
24:         if (run is null
25:             || run.RunId != expectedRunId
26:             || run.RoomVersion != expectedRoomVersion
27:             || run.ClearOpen
28:             || !run.IsBossRoom
29:             || !await rooms.IsCurrentRoomClearedAsync(run, cancellationToken)
30:                 .ConfigureAwait(false))
31:         {
32:             return null;
33:         }
34:
35:         // The room's verified clear is the dungeon fact. Quest objectives consume that fact;
```

## warehouse-storage: Server/src/Dfo.Server.Application/World/DungeonCellFactory.cs

SHA-256: `953573eaf7213c461b3c41568ffb7c1f6088d46bf78036d20e047e8320cd85d6`.

```csharp
42:         var actors = new List<DungeonCellActorState>(contents.Actors.Count);
43:         for (var index = 0; index < contents.Actors.Count; index++)
44:         {
45:             var described = contents.Actors[index];
46:             var sequence = checked((ushort)(contents.FirstActorSequence + index));
47:             var isMonster = experienceRules.IsMonster(described.Actor.ActorType);
48:             IReadOnlyList<RolledDrop> pending = [];
49:             if (isMonster || described.IsAiCharacter)
50:             {
51:                 var dropContext = new MonsterDropContext(
52:                     context.RunId,
53:                     context.Layout.DungeonId,
54:                     context.Layout.Seed,
55:                     context.Cell,
56:                     context.MapId,
57:                     context.Difficulty,
58:                     sequence,
59:                     context.Layout.LevelOf(described.Actor.Level),
60:                     described.Actor.ActorType,
61:                     leader.CharacterId,
62:                     participants)
63:                 {
64:                     RandomDropCount = described.Actor.RandomDropCount,
65:                     SourceType = described.IsAiCharacter
66:                         ? IndependentDropSourceType.APC : IndependentDropSourceType.Monster,
67:                 };
68:                 var generated = new List<RolledDrop>(await loot.GenerateGroundAsync(
69:                     described.Actor.Code, dropContext, cancellationToken).ConfigureAwait(false));
70:                 foreach (var participant in participants)
71:                     generated.AddRange(await loot.GeneratePersonalAsync(
72:                         described.Actor.Code, participant.CharacterId, dropContext, cancellationToken)
73:                         .ConfigureAwait(false));
74:                 pending = generated;
75:             }
76:
77:             actors.Add(new DungeonCellActorState(
78:                 sequence,
79:                 described.Actor,
80:                 described.IsAiCharacter,
81:                 IsDead: false,
82:                 pending.ToArray(),
83:                 isMonster));
84:         }
85:
86:         var objects = new List<DungeonCellObjectState>(contents.Objects.Count);
87:         foreach (var declared in contents.Objects)
88:         {
89:             var request = new QuestEnemyDropRequest(QuestEnemyType.PassiveObject, declared.Code,
90:                 declared.Id.RandomIdentity, context.Layout.DungeonId, context.Difficulty,
91:                 context.Layout.Seed, context.Cell, context.MapId);
```

## ServerS4A21: Server/DfoServer/Network/Builders/Dungeon/DeathTowerPacketBuilder.cs

SHA-256: `3d70c40d7c38afcd8320b0557acf2856a8d9469af74a03f9bc9f8fda11bbed7b`.

```csharp
17:         public static byte[] BuildTowerInfo(
18:             int dungeonId,
19:             ushort endStage,
20:             byte towerInfoModeByte = NormalTowerInfoModeByte,
21:             byte randomBuffType = ObservedRandomBuffType)
22:         {
23:             var w = new GamePacketWriter();
24:             w.WriteUInt32((uint)dungeonId);
25:             w.WriteUInt16(endStage);
26:             w.WriteByte(towerInfoModeByte);
27:             w.WriteByte(randomBuffType);
28:             return w.ToArray();
```

```csharp
34:         public static byte[] BuildStageMap(
35:             DeathTowerSession tower,
36:             List<StageMonster> monsters,
37:             IReadOnlyList<StageTowerItem> items,
38:             uint randomSeed)
39:         {
40:             var w = new GamePacketWriter();
41:             var monsterCount = Math.Min(monsters?.Count ?? 0, byte.MaxValue);
42:             var itemCount = Math.Min(items?.Count ?? 0, byte.MaxValue);
43:
44:             // Header 11B — currentStage 是 1-based(客户端显示层数)
45:             w.WriteUInt16((ushort)(tower.CurrentStage + 1));
46:             w.WriteUInt32(randomSeed);
47:             w.WriteUInt32((uint)tower.GetCurrentMapId());
48:             w.WriteByte((byte)monsterCount);
49:
50:             // 怪物条目 14B/条
51:             for (var index = 0; index < monsterCount; index++)
52:             {
53:                 var m = monsters[index];
54:                 w.WriteUInt32((uint)m.ListIndex);       // ListIndex
55:                 w.WriteUInt16(m.MonsterUniqueId);       // MonsterUniqueId
56:                 w.WriteUInt32((uint)m.MonsterIndex);    // MonsterIndex (模板ID)
57:                 w.WriteByte(m.MonsterLevel);            // MonsterLevel
58:                 w.WriteByte(m.MonsterType);             // MonsterType
59:                 w.WriteByte(m.IsBoxMonster);            // isBoxMonster
60:                 w.WriteByte(m.BoxIndex);                // boxIndex
61:             }
62:
63:             // Items: 18-byte rows bound to the APC list index and stable item unique ID.
64:             w.WriteByte((byte)itemCount);
65:             for (var index = 0; index < itemCount; index++)
66:             {
67:                 var item = items[index];
68:                 w.WriteUInt32((uint)item.SourceListIndex);
69:                 w.WriteUInt16(item.ItemUniqueId);
70:                 w.WriteUInt32((uint)item.ItemId);
71:                 w.WriteUInt32((uint)item.DropRate);
72:                 w.WriteUInt32((uint)Math.Max(1, item.StackCount));
73:             }
74:
```

```csharp
109:         // NOTI 145 DEATH_TOWER_STATE_REWARD (变长, 双端闭环: summary + 4组×{count+items})
110:         // Client handler (86JP DNF.exe RVA 0x008F7230):
111:         // u32 summary + 4 * { u8 count + count * { u32 itemId + u32 stackCount } }.
112:         public static byte[] BuildReward(
113:             int rewardExp,
114:             IReadOnlyList<DeathTowerRewardCandidate> candidates)
115:         {
116:             var w = new GamePacketWriter();
117:             w.WriteInt32(Math.Max(0, rewardExp));
118:             for (var groupIndex = 0; groupIndex < 4; groupIndex++)
119:             {
120:                 var group = groupIndex == 0 ? candidates : null;
121:                 var count = Math.Min(byte.MaxValue, group?.Count ?? 0);
122:                 w.WriteByte((byte)count);
123:                 for (var itemIndex = 0; itemIndex < count; itemIndex++)
124:                 {
125:                     var item = group[itemIndex];
126:                     w.WriteInt32(item.ItemId);
127:                     w.WriteInt32(item.AddInfo);
128:                 }
129:             }
130:             return w.ToArray();
131:         }
132:
133:         // NOTI 146 DEATH_TOWER_STATE_EPLP (1B, 双端闭环)
134:         public static byte[] BuildEplp(bool allMembersHaveRequiredItem)
135:         {
136:             return new byte[] { allMembersHaveRequiredItem ? (byte)1 : (byte)0 };
137:         }
138:
139:         public static byte[] BuildEplpCommandAck(byte state, byte option)
```

## ServerS4A21: Server/DfoServer/Network/Handlers/Dungeon/DeathTowerCoordinator.cs

SHA-256: `d5e23bd41fb000dcbafc38cc70cf1dd119704483db7d5911269364670adccc4a`.

```csharp
83:         public async Task SendEntryPacketsAsync(EnhancedClientSession session, DeathTowerSession tower, byte difficulty = 0)
84:         {
85:             var dungeonId = tower.Config.DungeonId;
86:             var hasRun = session.Player.CurrentRun != null;
87:             FileLogger.Log($"[DeathTower] ENTER: cid={session.Player.CharacterId} dungeon={dungeonId} difficulty={difficulty} hasRun={hasRun} stages={tower.Config.TotalStages} basisLv={tower.Config.BasisLevel}");
88:
89:             // NOTI 142 DEATH_TOWER_INFO (8B)
90:             var infoBody = DeathTowerPacketBuilder.BuildTowerInfo(dungeonId, (ushort)tower.Config.TotalStages);
91:             await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x008E, infoBody));
92:             FileLogger.Log($"[DeathTower] SENT 0x008E TOWER_INFO: bodyLen={infoBody.Length}");
93:
94:             // NOTI 143 首层
95:             await SendStageMap(session, tower);
96:
97:             // NOTI 0x1E FINISH_LOADING
98:             await session.SendPacketAsync(GamePacketEnvelopeBuilder.Build(0x00, 0x001E, new byte[0]));
99:             FileLogger.Log($"[DeathTower] SENT 0x001E FINISH_LOADING (entry)");
100:         }
101:
102:         public async Task<bool> TryHandleMoveMap(EnhancedClientSession session)
103:         {
104:             var run = session?.Player?.CurrentRun;
105:             var tower = run?.Tower;
106:             if (run == null || tower == null)
107:                 return false;
108:             var runIdentity = run.CaptureIdentity();
109:
110:             var prevState = tower.State;
111:             if (prevState >= 1)
112:                 await SyncCurrentStageClearMapAsync(session, tower, "tower_move_map");
113:
114:             if (!IsCurrentTowerRun(session, run, runIdentity, tower))
115:                 return true;
116:
117:             if (!tower.TryAdvanceStage())
118:             {
119:                 FileLogger.Log($"[DeathTower] MOVE_MAP rejected: cid={session.Player.CharacterId} stage={tower.CurrentStage}/{tower.EndStage} state={tower.State} (need state>=1, not last stage)");
120:                 return true;
121:             }
122:
123:             if (prevState == 1)
124:                 FileLogger.Log($"[DeathTower] MOVE_MAP advance from state=1 (0x009F(2) not received, 86JP may skip it)");
125:
126:             FileLogger.Log($"[DeathTower] ADVANCE: cid={session.Player.CharacterId} stage={tower.CurrentStage}/{tower.EndStage} map={tower.GetCurrentMapId()}");
127:
128:             var stage = tower.CurrentStage;
129:             if (!tower.TryDeferStageLoadingRelease(runIdentity, stage))
130:             {
131:                 FileLogger.Log(
132:                     $"[DeathTower] MOVE_MAP release defer rejected: " +
133:                     $"cid={session.Player.CharacterId} " +
134:                     $"run={runIdentity.RunId}/{runIdentity.RunGeneration} " +
135:                     $"stage={stage}");
136:                 return true;
137:             }
138:
139:             try
140:             {
141:                 await SendStageMap(session, tower);
142:             }
143:             catch
```

```csharp
158:         internal async Task HandleTowerCharacterDeathAsync(
159:             EnhancedClientSession session,
160:             GameDungeon.DungeonRunIdentity expectedRun)
161:         {
162:             var run = session?.Player?.CurrentRun;
163:             var tower = run?.Tower;
164:             if (run == null
165:                 || tower == null
166:                 || !expectedRun.IsValid
167:                 || !IsCurrentTowerRun(session, run, expectedRun, tower))
168:             {
169:                 FileLogger.Log(
170:                     $"[DeathTower] PLAYER_DEATH ignored: " +
171:                     $"cid={session?.Player?.CharacterId ?? 0} " +
172:                     $"run={expectedRun.RunId}/{expectedRun.RunGeneration}");
173:                 return;
174:             }
175:
176:             DungeonRunLifecycle.CancelDeathRespawn(run);
177:             FileLogger.Log(
178:                 $"[DeathTower] PLAYER_DEATH -> settlement: " +
179:                 $"cid={session.Player.CharacterId} " +
180:                 $"run={expectedRun.RunId}/{expectedRun.RunGeneration} " +
181:                 $"stage={tower.CurrentStage}/{tower.EndStage}");
182:             await SendSettlement(session, tower, "player-death");
183:         }
184:
185:         public async Task HandleStageCommand(EnhancedClientSession session, GamePacketHeader header, byte[] body)
186:         {
187:             var tower = session.Player.DeathTowerState;
188:             if (tower == null)
189:             {
190:                 FileLogger.Log($"[DeathTower] STAGE_CMD ignored: cid={session.Player?.CharacterId} not in tower");
191:                 return;
192:             }
193:             if (body == null || body.Length < 1)
194:             {
195:                 FileLogger.Log($"[DeathTower] STAGE_CMD ignored: body null or empty");
196:                 return;
197:             }
198:
199:             var commandType = body[0];
200:             switch (commandType)
201:             {
202:                 case 1:
203:                     tower.SetFighting();
204:                     FileLogger.Log($"[DeathTower] STAGE_CMD(1) fight start: cid={session.Player.CharacterId} stage={tower.CurrentStage}");
205:                     break;
206:                 case 2:
207:                     tower.SetCleared();
208:                     FileLogger.Log($"[DeathTower] STAGE_CMD(2) stage clear: cid={session.Player.CharacterId} stage={tower.CurrentStage}/{tower.EndStage} isLast={tower.IsLastStage}");
209:                     await SyncCurrentStageClearMapAsync(session, tower, "tower_stage_cmd");
210:                     if (tower.IsLastStage)
211:                     {
212:                         await SendSettlement(session, tower, "final-stage");
213:                         return;
214:                     }
215:                     break;
216:                 default:
217:                     FileLogger.Log($"[DeathTower] STAGE_CMD unknown commandType={commandType}: cid={session.Player.CharacterId} bodyHex={BitConverter.ToString(body)}");
218:                     break;
219:             }
```

## ServerS4A21: Server/DfoServer/Game/DeathTower/DeathTowerSettlementService.cs

SHA-256: `e0727f3dbb76c795a0935fb72e8d952ae506c0613dd5f7772deed9e223529ff4`.

```csharp
99:             if (tower == null)
100:                 throw new ArgumentNullException(nameof(tower));
101:
102:             var rewardConfig = DeathTowerRewardConfig.Load();
103:             if (!rewardConfig.IsAvailable)
104:             {
105:                 throw new InvalidOperationException(
106:                     "Death tower reward configuration is unavailable.");
107:             }
108:
109:             var clearedFloorCount = Math.Max(
110:                 1,
111:                 Math.Min(tower.Config.TotalStages, tower.CurrentStage + 1));
112:             var rewardExp = CalculateRewardExperience(
113:                 context.Level,
114:                 clearedFloorCount,
115:                 rewardConfig.GetExpWeight(
116:                     tower.Config.RewardProfile,
117:                     clearedFloorCount));
118:             var candidateCount = Math.Min(
119:                 DeathTowerRewardConfig.MaximumRewardProgress,
120:                 Math.Min(
121:                     Math.Max(0, tower.Config.MaxClearItemCount),
122:                     rewardConfig.GetRewardCardCount(
123:                         tower.Config.RewardProfile,
124:                         clearedFloorCount)));
125:             var lcg = tower.StageLcg ?? new DnfLcg(tower.StageSeed);
126:             var candidates = new List<DeathTowerRewardCandidate>(candidateCount);
127:             for (var index = 0; index < candidateCount; index++)
128:             {
129:                 var kind = rewardConfig.ClassifyCandidate(
130:                     clearedFloorCount,
131:                     lcg.Next(rewardConfig.RewardRollScale));
132:                 candidates.Add(CreateCandidate(
133:                     kind,
134:                     context.Level,
135:                     context.Difficulty,
136:                     rewardConfig.GoldAmountWeight,
137:                     lcg));
138:             }
139:
140:             return new DeathTowerSettlementPlan(
141:                 context,
142:                 tower.Config.DungeonId,
143:                 clearedFloorCount,
144:                 clearTimeMilliseconds,
145:                 rewardExp,
```

## ServerS4A21: Server/DfoServer/Game/DeathTower/DeathTowerMapLoader.cs

SHA-256: `411a563226e4db8560283271ea05c9b97451737a06fdb8814c40f570d5dd5fa1`.

```csharp
1: using System;
2: using System.Collections.Generic;
3: using System.IO;
4: using System.Linq;
5: using System.Text.RegularExpressions;
6: using DfoServer.GameWorld;
7: using PvfLib;
8:
9: namespace DfoServer.Game.DeathTower
10: {
11:     public static class DeathTowerMapLoader
12:     {
13:         private const int ApcRandomListIndexStart = 64;
14:
15:         private static readonly Regex FirstIntegerRegex =
16:             new Regex(@"-?\d+", RegexOptions.Compiled);
17:         private static readonly Regex ApcLevelRegex =
18:             new Regex(@"`[^`]*`\s+\d+\s+\d+\s+\d+\s+\d+\s+(\d+)", RegexOptions.Compiled);
19:         private static readonly object ApcCacheLock = new object();
20:         private static readonly Dictionary<int, ApcDefinition> ApcDefinitions =
21:             new Dictionary<int, ApcDefinition>();
22:         private static readonly HashSet<int> InvalidApcCodes = new HashSet<int>();
23:
24:         private static LstFile _aiCharacterList;
25:         private static List<ApcDefinition> _randomApcCandidates;
26:
27:         public static List<StageMonster> LoadStageMonsters(DeathTowerSession tower)
28:         {
29:             var mapId = tower.GetCurrentMapId();
30:             var result = new List<StageMonster>();
31:             if (mapId <= 0)
32:             {
33:                 FileLogger.Log($"[DeathTower] LoadStageMonsters: invalid mapId={mapId} for stage={tower.CurrentStage}");
34:                 return result;
35:             }
36:
37:             try
38:             {
39:                 var mapContent = ReadMapContent(mapId);
40:                 if (mapContent == null)
41:                 {
42:                     FileLogger.Log($"[DeathTower] LoadStageMonsters: map file not found for mapId={mapId}");
43:                     return result;
44:                 }
45:
46:                 var map = MapFile.Parse(mapContent);
47:                 AppendOrdinaryMonsters(result, tower, map, mapId);
48:                 AppendFixedApcs(result, tower, map, mapId);
49:                 AppendRandomApcs(result, tower, map, mapId);
50:             }
51:             catch (Exception ex)
52:             {
53:                 FileLogger.Log($"[DeathTower] LoadStageMonsters failed mapId={mapId}: {ex.Message}");
54:             }
55:
56:             return result;
57:         }
58:
59:         public static List<StageTowerItem> LoadStageItems(
60:             DeathTowerSession tower,
61:             IReadOnlyList<StageMonster> monsters)
62:         {
63:             var result = new List<StageTowerItem>();
64:             if (tower == null || monsters == null || !tower.Config.ItemDropsEnabled)
65:                 return result;
66:
67:             foreach (var monster in monsters)
68:             {
69:                 if (monster.MonsterType < 5
70:                     || monster.MonsterType > 8
71:                     || !TryGetApcDefinition(monster.MonsterIndex, out var definition))
72:                 {
73:                     continue;
74:                 }
75:
76:                 foreach (var configuredItem in definition.DeathTowerItems)
77:                 {
78:                     if (result.Count >= byte.MaxValue)
79:                     {
80:                         FileLogger.Log($"[DeathTower] Stage item list truncated to {byte.MaxValue}: stage={tower.CurrentStage} map={tower.GetCurrentMapId()}");
81:                         return result;
82:                     }
83:                     if (configuredItem.ItemId <= 0 || configuredItem.DropRate < 0)
84:                     {
85:                         FileLogger.Log($"[DeathTower] Skip invalid APC tower item: apc={monster.MonsterIndex} item={configuredItem.ItemId} rate={configuredItem.DropRate}");
86:                         continue;
87:                     }
88:
89:                     result.Add(new StageTowerItem
90:                     {
91:                         SourceListIndex = monster.ListIndex,
92:                         SourceMonsterUniqueId = monster.MonsterUniqueId,
93:                         ItemUniqueId = tower.NextItemSeq(),
94:                         ItemId = configuredItem.ItemId,
95:                         DropRate = configuredItem.DropRate,
96:                         StackCount = 1,
97:                     });
98:                 }
99:             }
100:
101:             return result;
102:         }
103:
104:         private static void AppendOrdinaryMonsters(
105:             ICollection<StageMonster> result,
106:             DeathTowerSession tower,
107:             MapFile map,
108:             int mapId)
109:         {
110:             var listIndex = 0;
111:             foreach (var monster in map.Monsters)
112:             {
113:                 var monsterCode = monster.MonsterId.GetValueOrDefault();
114:                 if (monsterCode <= 0)
115:                 {
116:                     FileLogger.Log($"[DeathTower] Skip invalid monster code={monsterCode} map={mapId}");
117:                     continue;
118:                 }
119:
120:                 var rawLevel = monster.Lv.GetValueOrDefault() != 0
121:                     ? tower.Config.BasisLevel + monster.AutoLv.GetValueOrDefault()
122:                     : monster.AutoLv.GetValueOrDefault();
123:                 var level = rawLevel > 0 ? rawLevel : tower.Config.BasisLevel;
124:                 var type = (byte)monster.Type;
125:                 if (type > 3)
126:                 {
127:                     FileLogger.Log($"[DeathTower] Clamp monster type={type} to 0 code={monsterCode} map={mapId}");
128:                     type = 0;
129:                 }
130:
131:                 result.Add(new StageMonster
132:                 {
133:                     ListIndex = listIndex++,
134:                     MonsterIndex = monsterCode,
135:                     MonsterLevel = ClampLevel(level),
136:                     MonsterType = type,
137:                     IsBoxMonster = 0,
138:                     BoxIndex = 0,
139:                 });
140:             }
141:         }
142:
143:         private static void AppendFixedApcs(
144:             ICollection<StageMonster> result,
145:             DeathTowerSession tower,
146:             MapFile map,
147:             int mapId)
148:         {
149:             var listIndex = 0;
150:             foreach (var apc in map.AICharacters)
151:             {
152:                 if (apc.Code <= 0 || !TryGetApcDefinition(apc.Code, out var definition))
153:                 {
154:                     FileLogger.Log($"[DeathTower] Skip APC code={apc.Code} without valid AIC map={mapId}");
155:                     continue;
156:                 }
157:
158:                 var type = (byte)apc.AIType;
159:                 if (type < 5 || type > 8)
160:                 {
161:                     FileLogger.Log($"[DeathTower] Clamp APC type={type} to 5 code={apc.Code} map={mapId}");
162:                     type = 5;
163:                 }
164:
165:                 result.Add(new StageMonster
166:                 {
167:                     ListIndex = listIndex++,
168:                     MonsterIndex = apc.Code,
169:                     MonsterLevel = definition.Level,
170:                     MonsterType = type,
171:                     IsBoxMonster = 0,
172:                     BoxIndex = 0,
173:                 });
174:             }
175:         }
176:
177:         private static void AppendRandomApcs(
178:             ICollection<StageMonster> result,
179:             DeathTowerSession tower,
180:             MapFile map,
181:             int mapId)
182:         {
183:             var pointBudget = ReadNodeFirstInteger(map, "apc random point");
184:             if (pointBudget <= 0)
185:                 return;
186:
187:             var spawnSlots = ReadNodeFirstInteger(map, "monster spawn pos");
188:             if (spawnSlots <= 0)
189:             {
190:                 FileLogger.Log($"[DeathTower] Random APC budget has no spawn slots map={mapId} budget={pointBudget}");
191:                 return;
192:             }
193:
194:             var remainingPoints = pointBudget;
195:             var available = new List<ApcDefinition>(GetRandomApcCandidates());
196:             var selectedCount = 0;
197:
198:             while (selectedCount < spawnSlots)
199:             {
200:                 var eligible = available
201:                     .Where(candidate => candidate.AppearancePoint <= remainingPoints)
202:                     .ToList();
203:                 if (eligible.Count == 0)
204:                     break;
205:
206:                 var selected = eligible[Infrastructure.ServerRandom.Next(eligible.Count)];
207:                 available.Remove(selected);
208:                 remainingPoints -= selected.AppearancePoint;
209:
210:                 result.Add(new StageMonster
211:                 {
212:                     ListIndex = ApcRandomListIndexStart + selectedCount,
213:                     MonsterIndex = selected.Code,
214:                     MonsterLevel = selected.Level,
215:                     MonsterType = 5,
216:                     IsBoxMonster = 0,
217:                     BoxIndex = 0,
218:                 });
219:                 selectedCount++;
220:             }
221:
222:             FileLogger.Log($"[DeathTower] Random APC map={mapId} selected={selectedCount}/{spawnSlots} points={pointBudget - remainingPoints}/{pointBudget}");
223:         }
224:
225:         private static int ReadNodeFirstInteger(MapFile map, string tag)
226:         {
227:             var node = map.Root?.GetChild(tag);
228:             if (node == null || node.DataItems.Count == 0)
229:                 return 0;
230:
231:             var match = FirstIntegerRegex.Match(node.GetFirstDataContent(map.Content) ?? string.Empty);
232:             return match.Success && int.TryParse(match.Value, out var value) ? value : 0;
233:         }
234:
235:         private static IReadOnlyList<ApcDefinition> GetRandomApcCandidates()
236:         {
237:             lock (ApcCacheLock)
238:             {
239:                 if (_randomApcCandidates != null)
240:                     return _randomApcCandidates;
241:
242:                 EnsureAiCharacterList();
243:                 var candidates = new List<ApcDefinition>();
244:                 foreach (var entry in _aiCharacterList.Entries)
245:                 {
246:                     if (TryLoadApcDefinitionLocked(entry.Id, out var definition)
247:                         && definition.AppearancePoint > 0)
248:                     {
249:                         candidates.Add(definition);
250:                     }
```
