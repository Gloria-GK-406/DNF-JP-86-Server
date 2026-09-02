# TASK-08 — 副本自己说了算的那几个

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

副本定义的读取扩展出牌面产出声明与付费牌开关；规则服务在产生牌面前先看副本是否
自行声明，声明了就用它的（含按评级的分布），未声明的落全局表。

- Referenced spec requirements: RQ-011
- Referenced plan sections: `plan.md` 的 T-008；边界 BD-020, BD-006, BD-021
- Owned boundary: BD-020, BD-006, BD-021
- Permitted files or interfaces:

- `Server/src/Dfo.Server.Abstractions/Content/DungeonContent.cs`（扩展）
- `Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs`（扩展）
- `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs`（择路）
- `Server/tests/`（新增判据）

- Dependencies: TASK-02
- Required dependency outputs: TASK-02 的规则服务
- Acceptance conditions:

1. 有声明的副本，牌面内容出自该副本的声明而非全局表。
2. 未声明的副本行为不变，仍落全局表。
3. 客户端上报的评级只在这一情形下使用；全局表不按评级分档。
4. 既有的副本定义判据仍全部通过。
5. 全量判据 不低于 1690 通过、0 失败、2 跳过。

- Task-specific authorization: 根契约（revision 2）的授权覆盖本任务，无收窄。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：判据先行，再让它变绿。
- 分层按 `AGENTS.md` 与 `Docs/新功能接入规范.md`：内容源只被规则服务注入；
  规则服务不持有仓储、注册表、目录、存储或领域服务；handler 只做四类动作；
  命令处理器禁止嵌套 dispatch；能触达持久写的命令一律 Transactional。
- 归档读法在 XML doc remarks 里写明段落出处；读不出来的记为未查明，不填看似合理的数。
- `System.*` using 在前，本仓不做字母序。`TreatWarningsAsErrors`。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-019 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter ResultCard` | 有声明与无声明两类副本各一条判据通过 |
| CK-020 | `Non-command:` 对一个自行声明的副本产生牌面 | 内容出自该副本的声明而非全局表 |
| CK-S | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1690 通过、0 失败、2 跳过 |

- Required evidence: CK-019 的判据输出；CK-020 的观察记录；全量判据输出。

## Blocker Report — round 1

### 一句话

**归档里声明牌面产出的那一段，是大会（tournament）副本的东西，不是普通通关翻牌的东西。**
声明它的四个副本，本服务器的副本内容源一个也读不出来（判据已钉住）；
而要让规则服务知道「这一局是哪个副本」，必须改两个不在许可清单里的文件。
两条独立的理由，任一条都足以让验收条件 1 与 3 无法诚实达成，所以停在这里报。

**不是宣布 Spec 阻塞或失败**——那是控制器的事。下面是处置建议所需的全部材料。

### 一、探针查出的形状（命令与逐字输出）

前缀一律 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet run --project Tool/PvfProbe --`。

```text
-- tags dungeon/dungeon.lst   （节选）
      5  [gold card use]
      4  [result card]
      4  [reward item rate]

-- values dungeon/dungeon.lst "[result card]"
first token after [result card] over 1047 files:
   1043  (absent)
      4  [reward item rate]

-- values dungeon/dungeon.lst "[gold card use]"
first token after [gold card use] over 1047 files:
   1042  (absent)
      5  0

-- where dungeon/dungeon.lst "[result card]" "[reward item rate]" 100
id=120  dungeon/Shonantournament/yellowdragon.dgn
id=121  dungeon/Shonantournament/bluedragon.dgn
id=122  dungeon/Shonantournament/blackdragon.dgn
id=4119 dungeon/Event/Shonantournament/Princessfight_event.dgn

-- where dungeon/dungeon.lst "[gold card use]" "0" 100
id=100  dungeon/SouthernDale/Grimseeker.dgn
id=3902 dungeon/Act3/sao_enter.dgn
id=3901 dungeon/Act3/sao_system.dgn
id=3503 dungeon/Act5/Counterattackarea.dgn
id=3700 dungeon/Tau_kingdom/Tau_kingdom.dgn
```

声明本身（`dump dungeon/Shonantournament/yellowdragon.dgn`）：

```text
[result card]
[reward item rate]
3329 60 1 3329 35 2 3329 5 3
[/reward item rate]
0 0 0 100 1 70 0 30 2 80 0 20 3 90 0 10 4 100 0 0 5 0 100 0
[/result card]
```

四个副本的差异只在物品与行数：121 用 3323、122 用 4831，权重与那六行分布与 120 逐字相同；
4119 的整段是 `[reward item rate] 0 100 0` 与直接数据 `0 0 0 100`（**只有一行**，key 只有 0）。

### 二、每个字段的含义，以及各自的证据等级

| 位置 | 读法 | 凭什么 |
|---|---|---|
| `[reward item rate]` 三元组 | `itemId weight count` | ① 参照服务端 `E:/Project/DNF/ServerS4A21/Tool/PvfLib/Models/DungeonFile.cs:65-70` 的 `TournamentRewardItemRate{ItemId,Weight,Count}`，切分见同文件 `1053-1072`（三宽）。② 自洽：60+35+5=100，4119 的单条也是 100；若倒过来读成 `(id,count,weight)`，4119 唯一一条的权重就是 0，那一段谁也抽不出来。③ 三个 id 都是堆叠物：3329「普通的曲玉」、3323「华丽的曲玉」（`stackable/material/`）、4831「黑龙之息」（`stackable/quest/`），数量 1/2/3 说得通。 |
| `[result card]` 直接数据四元组 | `key goldWeight itemWeight emptyWeight` | 参照服务端 `DungeonFile.cs:57-63` + `1031-1051`（四宽切分）；**消费方式**见 `Server/DfoServer/Game/Dungeon/Tournament/TournamentDungeonApplicationService.cs:534-560`：`roll = lcg.Next(gold+item+empty)`，`roll < gold` → 金币牌，`roll < gold+item` → 按 `[reward item rate]` 权重抽一件，否则空牌。 |
| 四元组的第一位 `key` | **未查明，且不使用** | 见下。 |
| `[gold card use]` 的值 | 五个副本一律 `0`；「0＝关闭付费牌」只是**字面推断** | 参照服务端只在 PvfLib 里把它解析成 `GoldCardUse`（`DungeonFile.cs:184` 默认 -1、`551` 解析），**整个 DfoServer 没有任何一处读这个属性**（对标识符 `GoldCardUse` 全仓搜索只有这两行）。没有第二个来源能确认 0 的动作是「关闭」。 |

**第一位 `key` 为什么记为未查明：**

- 参照服务端把它填成 `runtime.CompletedRounds`——大会的**已完成轮数**
  （`TournamentDungeonApplicationService.cs:471`，且 `472` 要求 `<= 4`），
  优胜者另外再取一次 `resultKey: 5`（同文件 `513`）。
  `TournamentDungeonDefinition.cs:322-323` 校验 key 落在 0..5，`346-351` 要求六个 key 一个不缺。
- 也就是说：0..4 是打了几轮，5 是优胜专用行。**它不是客户端上报的通关评级。**
- 本服务器没有大会模型，也没有「轮数」这个概念，所以对一次普通通关，**没有任何可辩护的 key**。
- 数目也对不上：本仓的通关评级走 `RankSystemInfo.etc` 的八档门槛
  （99/90/80/60/50/30/20/10，见 `Server/src/Dfo.Server.Abstractions/Content/DungeonRankContent.cs`
  的 remarks 与 `BandOf`），而这里是六个 key。
- 旁证：三件物品的 `[flavor text]` 明写「只有在黄龙大会上取得优异成绩的勇士才能获得」。

按硬约束 4（读不出含义的字段记为未查明并不使用），**「含按评级的分布」这一步没有归档依据可实现**；
把 `ClientRankPoint` 映射成 `key` 是发明。

### 三、阻塞点一：许可清单覆盖不了验收条件 1 与 3

规则服务今天拿到的全部事实是
`ClearRewardOccasion(Kind, CharacterLevel, Difficulty, MapMaxCount, PartyMemberCount)`
（`Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs:73`），
**里面没有副本 id，也没有评级**。`PaidFlipCost(int characterLevel)` 同样只有等级。
所以「先看副本是否自行声明」这一步在规则服务里无从下手——它不知道这是哪个副本，
而按 `AGENTS.md:387` 规则服务又不许自己去查世界状态。

要让它知道，至少得改这两个文件，**两个都不在本任务的许可清单里**：

| 必须改 | 属于 | 为什么非改不可 |
|---|---|---|
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | **BD-005**（T-002 拥有） | `ClearRewardOccasion` 要多一个副本 id；且 `IDungeonDefinitionSource.GetAsync` 是 `ValueTask`，`FaceFor` 得跟着变异步（`PvfTokenReader` 是 ref struct，同步阻塞不是选项） |
| `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs` | **BD-010**（T-003 拥有） | 它手里已经有 `run.Layout.DungeonId`（`CardRewardDomainService.cs:107` 拿它拌种子），但只有它能把这个 id 填进 occasion |

这不是我挑剔措辞：`Docs/新功能接入规范.md:203-210` 自己写的分层就是
「规则服务……接收 facts（**副本 id**、角色等级、难度、**评级**、探索度、队伍人数、显式种子）」，
而 TASK-02 的执行记录第 6 条明写「`[result card]` 副本级覆盖一行没碰，**按任务要求不预留抽象**」。
缺口是上游两个任务的契约切分留下的，不是这里能补的。

**顺带一处措辞错（不构成阻塞，但照抄清单会找不到文件）**：
许可清单与 BD-020 写的是 `Server/src/Dfo.Server.Abstractions/Content/DungeonContent.cs`，
仓里没有这个文件（`find . -name DungeonContent.cs` 无输出）；
对应的真实文件是 `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs`。
同目录另有 `DungeonExperienceContent.cs`、`DungeonRankContent.cs`、`DungeonRoomActorContent.cs`。

### 四、阻塞点二：声明牌面的四个副本，本服务器一个也到不了

**这一条比上一条更硬，因为就算把清单放开、把接线做通，那条路上也没有一个副本能走到。**

四个声明 `[result card]` 的副本都开了 `[maze info]`，但**没有一个声明 `[map specification]`**：

```text
dungeon/Shonantournament/yellowdragon.dgn                mapspec=0 mazeinfo=1
dungeon/Shonantournament/bluedragon.dgn                  mapspec=0 mazeinfo=1
dungeon/Shonantournament/blackdragon.dgn                 mapspec=0 mazeinfo=1
dungeon/Event/Shonantournament/Princessfight_event.dgn   mapspec=0 mazeinfo=1
```

`PvfDungeonDefinitionSource` 只在 `[map specification]` 处进入 `Section.Rooms`，
`Close` 又跳过 `Rooms.Count == 0` 的 draft，于是 `mazes.Count == 0` → **`GetAsync` 返回 null**，
与「归档根本没声明这个副本」同一个答案（该设计意图写在
`DungeonDefinitionContent.cs` 的 `IDungeonDefinitionSource` remarks 里）。
null 的副本拿不到 layout（`DungeonLayoutRuleService.ResolveAsync` 第一步就 return null），
也就没有 run、没有结算、没有牌面。

**这不是推理，是判据**：本轮新增的
`Server/tests/Dfo.Server.Infrastructure.Tests/Content/DungeonResultCardDeclarationTests.cs`
扫全部 dungeon.lst，断言声明集合恰好是 `{120,121,122,4119}`，且四个 `GetAsync` 全为 null，
同时以副本 144 作阴性对照（它不声明、且可达）。

再看客户端那一侧：大会的翻牌走的是**另一对协议**——
`protocol/game/client-to-server/0x01CA.md` `TOURNAMENT_REWARD_SELECT_STATE` 与
`0x01CB.md` `TOURNAMENT_REWARD_SELECT`（body 两字节 `cardType<2`、`cardIndex<2`，
即 2 型 × 2 张），而不是本 Spec 做的 `0x0045/0x0046/0x0047`。
参照服务端也是这样分的：`TournamentResultCards` / `TryGetResultCard` / `RewardItemRates`
在 `Server/DfoServer` 里只出现在 `Game/Dungeon/Tournament/` 之内，
普通通关的发牌器 `Game/Dungeon/ClearRewardGenerator.cs` 只吃 dungeonLevel / 难度 / 队伍 / 房间数，
**一次也不查副本的 `[result card]`**。

结论：RQ-011 的前提——「极少数副本在自己的 `.dgn` 里为**通关翻牌**声明了产出」——
按本档案与参照服务端都不成立。那一段是大会玩法的数据，走另一套协议、另一套发牌器，
而且它所属的四个副本在本服务器里连进都进不去。

**另一半不一样**：`[gold card use]` 的五个副本里有四个是**普通可达副本**
（100 Grimseeker、3700 Tau_kingdom、3901 sao_system、3902 sao_enter；只有 3503 同样无房间）。
所以「付费牌开关」这一半是真的落在本 Spec 的路上的——只要许可清单放开，它可以做。
这一点也被本轮判据钉住了。

### 五、本轮唯一的改动

只有一个新文件，落在许可清单的 `Server/tests/`（BD-021「新增判据」）之内，**没有动任何生产代码**：

- `Server/tests/Dfo.Server.Infrastructure.Tests/Content/DungeonResultCardDeclarationTests.cs`（新增，160 行）

加它的理由是本 Spec 的第一条规矩：上面第四节那些是对归档与仓内代码的可验证事实断言，
要么有判据钉住要么删去。删掉它们这份报告就只剩空话。
类名含 `ResultCard`，故 CK-019 的 `--filter ResultCard` 能选中它。

**判据输出（逐字）**

```text
$ PVF_ARCHIVE_PATH=.../Script.pvf dotnet test Server/86JP.Server.slnx --nologo --filter ResultCard
已通过! - 失败:     0，通过:     2，已跳过:     0，总计:     2，持续时间: 1 s - Dfo.Server.Infrastructure.Tests.dll

$ PVF_ARCHIVE_PATH=.../Script.pvf dotnet test Server/86JP.Server.slnx --nologo
Networking      0 失败 /  51 通过 / 0 跳过
Dfo.Pvf         0 失败 /  60 通过 / 0 跳过
Protocol        0 失败 / 479 通过 / 0 跳过
Persistence     0 失败 / 248 通过 / 0 跳过
Application     0 失败 / 855 通过 / 0 跳过
Host            0 失败 /   4 通过 / 0 跳过
Infrastructure  0 失败 / 206 通过 / 2 跳过
合计            0 失败 / 1903 通过 / 2 跳过
```

基线 1901/0/2 → **1903 通过 / 0 失败 / 2 跳过**，涨的 2 条就是本轮新增的两条。假红一次没遇到。

**变异检查（五个，全部见红）**

| 变异 | 改成 | 结果 |
|---|---|---|
| M1 | 期望集合去掉 4119 | 红（`Assert.Equal() Failure: Sets differ`） |
| M2 | 四个副本改断言 `NotNull` | 红 |
| M3 | 阴性对照 144 改断言 `Null` | 红 |
| M4 | 期望可达集合塞进 3503 | 红 |
| M5 | `[gold card use]` 期望值 0 改 1 | 红 |

**工具骗人的地方，抓到一次，写在这里**：契约要求「还原时重写内容并显式回填 mtime」。
我第一次把 **M1 也**按这个办法做（写完回填 mtime），结果**判据是绿的**——
不是判据不敏感，是 MSBuild 按 mtime 做增量编译，看见文件「没变」就没重编，
跑的还是变异前的程序集。把文件 `touch` 一下再跑，M1 立刻变红。
所以本轮改成：**变异时让 mtime 自然前进；还原时重写内容但不回填 mtime，改用 sha256 验还原**。
还原后 `sha256 = 1302ab90e8d77e252cb4c00d2f70a56b8711afc42ab81c5ccf7d8d30d7d866f8`，
与变异前逐字节相同，且重跑仍是 2 通过。
**「回填 mtime」这条建议原样用在本仓会造出一个从头到尾不会红的变异循环，后续任务应当知道。**

**行尾**：`crlf 0 / lf 160 / cr 0`（按 `b.count(b'\r\n')` 对 `b.count(b'\n')` 数字节，
没用 grep 数 CR）；`git diff --no-index --numstat` 与加 `--ignore-cr-at-eol` 两次都是
`160  0`，逐行一致。

### 六、逐条对照契约的 Acceptance conditions

1. **有声明的副本，牌面内容出自该副本的声明而非全局表** — **不成立，且按现清单做不到**。
   缺副本 id（阻塞点一）；即便补上，四个声明副本在本服务器全部不可达（阻塞点二，已钉判据）。
2. **未声明的副本行为不变，仍落全局表** — 成立且未被触碰：本轮没有改一行生产代码，
   全局表那条路逐字未动，全量判据 0 失败可证。
3. **客户端上报的评级只在这一情形下使用；全局表不按评级分档** — 后半成立（全局表本来就不看评级，
   `ClearRewardOccasion` 里没有评级这一项）；**前半没有归档依据可实现**：那个 key 是大会轮数，
   不是评级（第二节）。
4. **既有的副本定义判据仍全部通过** — 成立，`PvfDungeonDefinitionSourceTests` 全绿，
   既有用例的断言与期望值一个字没改。
5. **全量判据不低于 1690 通过、0 失败、2 跳过** — 成立：1903 / 0 / 2。

### 七、给控制器的处置建议（三选一，我不替你选）

- **甲：把 RQ-011 判为前提不成立，改写或撤掉。** 证据最全的一条路。
  `[result card]` 属于大会玩法（另一对协议 `0x01CA/0x01CB`、另一个发牌器、四个不可达副本），
  与本 Spec 的通关翻牌不是同一套机制。撤掉 RQ-011 不影响 RQ-001..RQ-010、RQ-012。
- **乙：把 RQ-011 收窄成只做 `[gold card use]`。** 这一半是真落在本 Spec 路上的：
  四个可达副本关闭付费牌。需要把清单放开到 `IClearRewardRuleService.cs`（`PaidFlipCost` 要知道副本）
  与 `CardRewardDomainService.cs`（把 `run.Layout.DungeonId` 填进去）；
  另需接受「0＝关闭」是字面推断、没有第二来源（参照服务端解析了但从不读它）。
- **丙：维持 RQ-011 原样，放开清单，并接受两处发明**：把 `ClientRankPoint` 映射成 result key，
  以及为四个无房间副本另开一条不经 `DungeonDefinition` 的读取口。
  **我不建议**：前者违反硬约束 4，后者做出来也没有真副本能跑到，判据只能喂假数据。

若走乙或丙，`[result card]` 那一段本身是可以在**不碰 `DungeonDefinition`** 的前提下读出来的——
给 `IDungeonDefinitionSource` 加一个与迷宫无关的第二个读取口即可（这两个文件都在本任务清单内），
这样四个无房间副本的声明也能读到，而 `GetAsync` 对它们仍然答 null、既有行为分毫不动。
这条我确认过可行，但**没有实现**：在 RQ-011 的去留定下来之前，那个记录长什么样是猜的。

### 八、拿不准的地方

1. **`[gold card use]` 的 0 到底是不是「关闭」。** 五个副本清一色是 0，没有别的取值可以对照；
   参照服务端解析它却从不读它。字面上像开关，但这是我读的，不是谁说的。
2. **`[result card]` 的 gold / item / empty 三列次序，靠的是参照服务端一个来源。**
   自洽性只能证明它们是一组和为 100 的权重，证明不了哪一列是哪个。
   本仓引用参照服务端命名的先例有（如 `DungeonClearSlice` 里 clearBase / rankBonus 的取舍），
   等级相同：可用，但要标明是它的命名。
3. **四个副本「不可达」是就今天的 `IDungeonDefinitionSource` 而言。**
   若日后另起一条大会入场路径（不经迷宫），可达性结论要重算——判据会在那时变红，这是好事。
4. **我没有实机验证过任何一条。** 全部结论来自档案探针、参照服务端源码与本仓判据。

## Controller Disposition — 阻塞报告成立，任务收窄而非作废

执行者报告 RQ-011 的前提不成立并停下，**没有自行扩界、没有猜一个用法**。做得对。
控制器独立复核了它的载荷事实，结论比报告更清楚。

### 控制器自己测出来的（`PvfProbe lst dungeon/dungeon.lst <id>`）

| 副本 | `[result card]` | `[gold card use]` | `[map specification]` | 正常进本路径可达 |
|---|---|---|---|---|
| 120 / 121 / 122 / 4119 | **有** | — | **0** | **否** |
| 100 / 3700 / 3901 / 3902 | — | **0** | 1 | **是** |
| 3503 | — | 0 | 0 | 否 |
| 144（对照） | — | — | 5 | 是 |

### 裁定一：RQ-011 的「牌面产出声明」那一半，前提确实不成立

声明 `[result card]` 的**四个副本全部没有 `[map specification]`**，
`IDungeonDefinitionSource.GetAsync` 一律答 null，本 Spec 建的这条路进不去、不会结算。
它们是大会玩法，走另一对协议；参照服务端的普通发牌器也一次都不查这一段。

**因此不实现它。** 实现一条任何通关都到不了的分支，只能靠一个无法验证的读法（列义取自参照
服务端的命名）撑着，正是本 Spec 一路在拒绝的东西。

**RQ-011 并未被违反**：它说「声明了的按声明产生，未声明的落全局表」——
前半句对每一个**可达**副本都是空真，后半句成立且已有判据。
这一条连同证据进 TASK-09 的读数对照。

### 裁定二：`[gold card use]` 是真的、可达的，本 Task 改做它

四个能进的副本（100 / 3700 / 3901 / 3902）声明 `[gold card use] 0`，
即**这些副本关闭付费翻牌**。这是一个副本级的、可达的、归档明说的控制，
`plan.md` 的 T-008 本来就把它列为输入。

**本 Task 的交付物收窄为**：读取该声明，并让声明为关闭的副本不提供付费翻牌。

Acceptance conditions 相应调整：
1. 声明关闭的副本，付费翻牌不可用；未声明的副本行为不变。
2. **未声明副本的行为不变要有判据钉住**（原红线不变），用真档案里的真副本做阴阳对照。
3. 归档里读不出含义的字段一律记为未查明并不使用。

### 裁定三：批准扩界

规则服务要按副本择路，就必须知道是哪个副本，而 `ClearRewardOccasion` 里没有副本 id。
Permitted files or interfaces 增补：
- `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs`（仅为 occasion 增副本 id）
- `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs`（仅为填上它）
- `Server/src/Dfo.Server.Protocol/Game/CardReward/`（仅为付费翻牌在关闭时的拒绝）

### 裁定四：控制器的清单写错了一个文件名

原清单写 `DungeonContent.cs`，**仓里不存在**；真实文件是 `DungeonDefinitionContent.cs`。
执行者指了出来。许可清单以真实文件名为准。

这是控制器在本 Spec 里第三次把一个没核过的名字写进正式产物
（前两次：行尾那次的 grep、TASK-07 把改名后的常量当成不存在）。

## Controller Note — 变异检查的做法被我修错了一半，须更正

TASK-06 抓到变异工具假阴性后，控制器把「**还原时重写内容并显式回填 mtime**」写进了此后每一份
派工简报。**这条只修对了一半。**

本 Task 的执行者报告：回填 mtime 的做法用在**变异**那一步时，
MSBuild 增量编译会跳过重编，于是 **M1 第一次是假绿**。
它改成「变异让 mtime 前进、还原后用 sha256 校验内容」才正常。

**更正后的做法（TASK-09 及以后照此）**：
- **施加变异时**：让文件 mtime **前进**，确保下一次构建真的重编译。
- **还原时**：重写内容，并用**内容哈希**校验还原正确，而不是靠回填 mtime。
- **仍然保留**那条底线：**至少先观察到一个变异真的变红**，才能把「其余也红」当成证据。

**为什么这条值得单独记**：控制器为修一个假阴性而下的指令，**制造了另一个方向的假阴性**，
并且被原样传播了三份简报。一个「从头到尾没红过的变异循环」与「根本没跑的循环」无法区分——
这正是那条底线存在的理由，它这次接住了错误的指令。

## Execution Result — round 2

按裁定二收窄后的交付物：**读取 `[gold card use]`，让声明为关闭的副本不提供付费翻牌**。
`[result card]` 按裁定一不实现。

### 一、改了哪些文件

生产代码六处，全部在裁定三批准后的清单内：

| 文件 | 改了什么 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs` | `DungeonDefinition` 增 `bool OffersPaidCard = true`（尾部可选参数，既有构造点不动）。读法、出处、以及未查明的部分写在它的 `<param>` remarks 里 |
| `Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs` | 增 `[gold card use]` 标签、`Section.PaidCardSwitch`、一条整数分支与构造实参；类 remarks 增一段说明该标签在文件作用域而不在迷宫块里 |
| `Server/src/Dfo.Server.Abstractions/World/IClearRewardRuleService.cs` | **新增** `ValueTask<bool> OffersPaidFlipAsync(int dungeonId, CancellationToken)`。`PaidFlipCost(int characterLevel)` **一个字符都没动** |
| `Server/src/Dfo.Server.Application/World/ClearRewardRuleService.cs` | 构造增 `IDungeonDefinitionSource`（BD-006 的 Inputs 本来就列了它）；实现新方法；类 remarks 由「两个内容源」改为「三个」并写明第三个只为这一件事而读 |
| `Server/src/Dfo.Server.Application/World/CardRewardDomainService.cs` | `SettleAsync` 改 `async`；取价前先问副本提不提供，不提供就不向价格表要价 |
| `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs` | **只改 `SelectCardStatus.NotPriced` 的 remarks**，写明它现在承载两种成因。零逻辑改动——拒绝本来就落在这条路上 |

判据七处（`Server/tests/`，BD-021）：

| 文件 | 改了什么 |
|---|---|
| `Dfo.Server.Infrastructure.Tests/Content/DungeonPaidCardSwitchTests.cs` | **新增**：档案里读出的开关，阴阳两向 |
| `Dfo.Server.Application.Tests/World/DungeonPaidCardSwitchRuleTests.cs` | **新增**：规则服务对真档案的回答，阴阳两向 + 无定义副本 + 价格不受开关影响 |
| `Dfo.Server.Application.Tests/World/PaidCardFlipCostTests.cs` | `Flip` fixture 的写死副本 144 改为可选参数（默认仍是 144，既有四条用例的行为逐字不变）；**新增**端到端判据两条 |
| `Dfo.Server.Application.Tests/World/ClearRewardRuleServiceTests.cs` | fixture 增 `StubDungeons`；`Service()` 增可选参数；注册用例补一行 stub 注册；**改了一条既有断言**（见下文偏离二） |
| `Dfo.Server.Application.Tests/World/CardRewardDomainServiceTests.cs` | 假规则服务补新成员；注册用例补一行 stub 注册 |
| `Dfo.Server.Application.Tests/World/ClearRewardArchiveTests.cs` | 两处构造补上真档案的 `IDungeonDefinitionSource` |
| `Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs` | 假规则服务补新成员（照该假件原样，被问就抛） |

round 1 的 `DungeonResultCardDeclarationTests.cs` 按裁定保留，本轮一个字没动。

**没有动**：`ApplicationModule.cs`（DI 按构造签名自动解析新依赖，两条注册判据可证）、
`ICardRewardDomainService.cs`、`IDungeonRunDomainService.cs`、`DungeonClearSlice.cs`、
以及 CardReward 切片里除 `SelectCardCommand.cs` 之外的五个文件。

### 二、关闭是怎么实现的

一条直路，每一段都在它该在的层：

```
.dgn 的 [gold card use] 0
  → PvfDungeonDefinitionSource 读成 DungeonDefinition.OffersPaidCard = false     （内容源）
  → ClearRewardRuleService.OffersPaidFlipAsync(dungeonId) 答 false               （规则服务，唯一读取点）
  → CardRewardDomainService 结算时不向价格表要价，PaidFlipCost 落 null           （业务服务，择路）
  → 结算帧写 UnpricedPaidFlipCost（int.MaxValue，没有钱包够得着）                 （既有行为，未改）
  → SelectCardCommandHandler.ChargeAsync 在扣款之前以 NotPriced 拒绝              （既有行为，未改）
```

拒绝发生在扣款与认领之前，所以**一分钱不扣、一个牌位不认领**，判据逐项断言了这三件事。
**免费牌完全不受影响**——同一局的免费翻牌照常翻得动，这一条也有断言。

### 三、两处偏离裁定字面的做法，都是往窄里做

**偏离一：没有给 `ClearRewardOccasion` 增副本 id，而是给规则服务加了一个只问副本的新方法。**

裁定三写的是「仅为 occasion 增副本 id」。但 occasion 是喂 `FaceFor` / `ChanceOf` 的，
而收窄后的任务里**牌面内容不按副本变**（`[result card]` 不实现），
给 occasion 加一个谁都不读的字段，正是 TASK-02 被要求不要做的「预留抽象」。
裁定给出的目的是「规则服务要按副本择路，就必须知道是哪个副本」——
`OffersPaidFlipAsync(int dungeonId, …)` 满足这个目的，且改动更小。

顺带保住了一条既有断言：`PaidFlipCost` 的 remarks 明写「一个等级，别无其他……
occasion 不是这里该问的东西」，并有判据 `Assert.Single(...GetMethod("PaidFlipCost").GetParameters())`
钉住。把副本塞进 `PaidFlipCost` 会把这条断言弄红，也会让「价格按副本变」看起来成立——
而价格表只按等级。**按副本变的不是价钱，是有没有这张牌**，两个问题就分两个方法。

**偏离二：没有新增专门的「已关闭」拒绝状态，只扩写了 `NotPriced` 的说明。**

裁定三批准了 CardReward 切片「仅为关闭时的拒绝」。做下来发现拒绝**本来就已经落在这条路上**，
不需要新逻辑。要让 handler 分辨「档案没给这个等级定价」与「副本关掉了这张牌」，
就得加宽 `ClearRewardSettlementResult`（`ICardRewardDomainService.cs`）与
`ClearRewardCardSet`（`IDungeonRunDomainService.cs`）——**两个都不在批准清单里**，
而两种情形对玩家与线上字节完全同一（同一帧、同一拒绝、同样不扣钱）。
所以我把两种成因写进 `NotPriced` 的 remarks，而不是偷偷让它名不副实。

**由此产生三处文档漂移，都在我不能改的文件里，交 TASK-09**（`int?` 的 null 现在多了一种成因）：

| 文件 | 现在的措辞 | 为什么不准了 |
|---|---|---|
| `Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs` 的 `UnpricedPaidFlipCost` | 「when the archive prices no flip at this clear's level」 | 关闭了付费牌的副本也会写这个值 |
| `Server/src/Dfo.Server.Abstractions/World/ICardRewardDomainService.cs` 的 `ClearRewardSettlementResult.PaidFlipCost` | 「or null when the archive priced no such level」 | 同上 |
| `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` 的 `ClearRewardCardSet.PaidFlipCost` | 「or null when nothing priced the level it settled at」 | 措辞偏等级；严格说不算错，但漏了副本这一种 |

**改了一条既有断言，说明如下。** `ClearRewardRuleServiceTests.The_rule_service_is_handed_no_reader_of_world_state`
枚举规则服务的构造依赖，原为 `[IClearRewardRateSource, IDropItemPoolSource, ClearRewardRateOptions]`。
加内容源必然改它，且 `plan.md` 的 **BD-006 本来就把 `IDungeonDefinitionSource` 列为该服务的 Inputs**。
我只在列表里插入了这一项并写明理由；该用例真正的判据——
「构造参数里不许出现 Repository / Registry / Directory / Store / Persistence / DomainService 后缀」——
**一个字没动，仍然全绿**。除此之外没有改动任何既有断言或期望值。

### 四、归档读法与未查明

- **出处**：副本自己的 `.dgn` 的 `[gold card use]`，文件作用域，后跟一个整数。
  1047 个副本里 5 个声明（100 / 3503 / 3700 / 3901 / 3902），**五个写的都是 `0`**。
- **`0 = 关闭` 是读法不是明说**，理由写进了 `OffersPaidCard` 的 remarks：
  缺省（1042 个副本什么都不写）就是提供，价钱由 `[gold card cost table]` 按等级给全；
  若 `0` 表示「提供」，这五处声明就等于什么也没做。参照服务端把它解析成 `GoldCardUse`
  并缺省为 `-1`（`Tool/PvfLib/Models/DungeonFile.cs:184,551`），**然后整个服务端从不读它**，
  所以没有第二个来源能坐实方向。
- **未查明并不使用**：非 0 值的含义。档案里根本没有出现过非 0，所以读作「没关」而不是第三种状态。
- **一处已知的读不全，写进判据而不是藏起来**：副本 3503 声明了开关，但它的迷宫没有房间，
  `IDungeonDefinitionSource` 对它答 null，于是它的开关拿不到。它本来也进不去（无定义即无 layout
  即无 run），观察不到；`A_dungeon_with_no_definition_is_not_treated_as_switched_off` 把这件事
  连同理由一起钉住了——哪天无迷宫副本变得可读，这条判据会红。

### 五、判据（逐字）

```text
$ PVF_ARCHIVE_PATH=.../Script.pvf dotnet test Server/86JP.Server.slnx --nologo
Protocol        0 失败 / 479 通过 / 0 跳过
Networking      0 失败 /  51 通过 / 0 跳过
Dfo.Pvf         0 失败 /  60 通过 / 0 跳过
Persistence     0 失败 / 248 通过 / 0 跳过
Host            0 失败 /   4 通过 / 0 跳过
Application     0 失败 / 869 通过 / 0 跳过
Infrastructure  0 失败 / 214 通过 / 2 跳过
合计            0 失败 / 1925 通过 / 2 跳过
```

**1903 → 1925 通过，0 失败，2 跳过。** 涨的 22 条全是本轮新增：
基础设施 +8（4 阴 + 3 阳 + 1 邻域不受扰），
应用层 +14（规则服务 4 阴 + 3 阳 + 1 无定义 + 1 价格不动 = 9；端到端 4 阴 + 1 阳对照 = 5）。

分片（CK-019 的旧过滤词已选不到本轮判据，两个都记在这里）：

```text
$ dotnet test ... --filter ResultCard   → 0 失败 /  2 通过    （round 1 那两条，仍绿）
$ dotnet test ... --filter PaidCard     → 0 失败 / 25 通过    （Infrastructure 8 + Application 17）
```

**过程中真出过一次红，不是假红**：第一次跑全量时 Application 有 3 条失败，
两条是测试自己的 DI 容器没注册新内容源（`Unable to resolve service for type 'IDungeonDefinitionSource'`），
一条就是上面那条依赖枚举断言。三条都是本次改动的直接后果，已按上文处理。
契约里点名的两类假红（10 秒预算的真进程用例、两用例抢同一个 SQLite 库）本轮一次都没遇到；
新增的端到端判据每个 `Flip` 都开自己的临时库文件。

### 六、变异检查：六个，六个都红

按**更正后**的做法：施加变异时让 mtime 自然前进（不回填），还原后用 sha256 校验内容。

| 变异 | 改在哪 | 改成什么 | 结果 |
|---|---|---|---|
| N1 | `PvfDungeonDefinitionSource` | 删掉 `[gold card use]` 的分派行（当它不存在） | 红：基础设施 5 / 应用 9 |
| N2 | 同上 | 方向反过来：`!= 0` 改 `== 0` | 红：基础设施 5 / 应用 9（**两向都红**） |
| N3 | `ClearRewardRuleService` | 仍去读副本，但无视开关，恒答「提供」 | 红：应用 9 |
| N4 | 同上 | 让沉默变成拒绝（无定义的副本答「不提供」） | 红：应用 1 |
| N5 | `CardRewardDomainService` | 不再问副本，一律照等级取价 | 红：应用 4（正是四个关闭副本的端到端用例） |
| N6 | `ClearRewardRuleService` | 一律答「不提供」 | 红：应用 7（**对照用例接住了它**） |

几点说明：

- **N2 是这组里最要紧的一个**：它同时打红「关闭的副本读成关闭」与「没声明的副本读成没关」，
  证明两向都被钉住，而不是一个恒定答案碰巧过关。
- **N4 只红 1 条是设计如此**：关于「无定义副本」的断言只有那一条，它就是为这个变异写的。
- **N6 只被对照用例接住**：若判据只有「关闭的副本被拒」那一半，恒答「不提供」会全绿。
  这正是那条阳性对照存在的理由。
- N3 与 N6 的第一版写法把 `dungeons` 变成未读参数，`TreatWarningsAsErrors` 直接编译失败——
  **那不是「变红」，那是判据根本没跑**。改成仍然读副本、只换判定条件之后才是有效变异。
  这一条与 mtime 那一条是同类陷阱：**看着是红的，其实什么都没验证。**

还原核对（三个生产文件逐字节回到变异前）：

```text
P1 sha256 8befcdce421f660a03cb9eccd91afa557eeb14d57a34699054090823dd269817  matches pristine: True
P2 sha256 5bc38bedca77e1b5789ed74cc8919bdff1c054350ec5dacbd11078031e26f443  matches pristine: True
P3 sha256 0e84da1a9438c33e3fac6d7faf343c5a2ff282115222791c34ff6bba0bbeaa85  matches pristine: True
```

还原后重跑全量：0 失败 / 1925 通过 / 2 跳过，与变异前一致。

### 七、行尾

十四个被本轮碰过或新增的文件逐个按字节数核（`b.count(b'\r\n')` 对 `b.count(b'\n')`，
另数 `b.count(b'\r')`；没用 grep 数 CR）：**crlf 全为 0，cr 全为 0**。

两个受版本控制的文件另做逐行核对，两次结果一致：

```text
$ git diff --numstat
27	1	Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs
21	1	Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs
$ git diff --numstat --ignore-cr-at-eol
27	1	Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs
21	1	Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs
```

改动足迹核对：受版本控制的已改文件由 33 增至 35（正是上面这两个），
新增未跟踪文件三个（round 1 一个 + 本轮两个判据文件），没有第四个。

### 八、拿不准的地方

1. **`0 = 关闭` 仍然只是读法。** 五个副本清一色 `0`，没有反例可对照；参照服务端解析了却从不读。
   论证（缺省即提供，故 `0` 只能是关闭）我认为站得住，但它是论证不是引文。
   翻案只需改 `PvfDungeonDefinitionSource` 一行。
2. **`NotPriced` 现在一名两义。** 两种成因对玩家同一，但日志里分辨不出来。
   要分辨就得加宽两个不在清单里的接口，值不值得是控制器的事。
3. **副本 3503 的开关读不到**（无房间迷宫）。不可观察，但读得不全，已被判据钉住。
4. **关闭的副本，结算帧写的是 `int.MaxValue`。** 这是既有行为，不是我这轮定的。
   客户端见到它会怎么画**未实证**——原注释就这么写着。
   若日后确认客户端对这个值有别的反应，落点在 `DungeonClearSlice`，不在本轮改的任何文件里。
5. **我没有实机验证过任何一条。** 全部来自档案、参照服务端源码与本仓判据。

## Executor Self-check — round 2

### 对照裁定二给出的收窄后 Acceptance conditions

1. **声明关闭的副本，付费翻牌不可用；未声明的副本行为不变** — 成立，两向都有判据。
   关闭侧：`A_dungeon_that_switched_its_paid_card_off_refuses_the_flip`（Theory，真档案里的
   **四个**真副本 100 / 3700 / 3901 / 3902），断言 `Status == NotPriced`、`GoldCharged == 0`、
   `GoldPaid == 0`、`Slots` 为空、**钱包读回来一分未动**，且同一局的免费翻牌仍然 `Flipped`。
   未声明侧：`A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip`（副本 144，
   与关闭侧同样的结算路径、同样的资金额，只有副本不同），断言照原价扣款并翻出牌面。
   变异 N5 打红前者，N6 打红后者。
2. **未声明副本的行为不变要有判据钉住，真副本阴阳对照** — 成立，三层各一对：
   内容层 `DungeonPaidCardSwitchTests`（关闭 4 个 / 未声明 3 个：144、10000、10004）、
   规则层 `DungeonPaidCardSwitchRuleTests`（同样 4 对 3，另加无定义副本与「开关不动价格」）、
   端到端 `PaidCardFlipCostTests`（4 对 1）。
   另外 `PaidCardFlipCostTests` 原有的四条用例默认仍在副本 144 上跑，逐字未改，全绿——
   **它们本身就是「未声明副本行为不变」的现成证据**。
3. **读不出含义的字段记为未查明并不使用** — 成立。非 0 值的含义未查明，读作「没关」而不是
   第三种状态；`[result card]` 按裁定一整段不实现，一个字段都没用。

### 对照原契约仍然有效的两条

4. **既有的副本定义判据仍全部通过** — 成立。`PvfDungeonDefinitionSourceTests` 全绿，
   断言与期望值一字未改；另加 `Reading_the_switch_leaves_the_rest_of_the_dungeon_alone`
   断言读了开关之后副本 100 的路径、迷宫、房间、等级、基准等级、经验权重仍各自成立
   （防的是「新分支吃掉了邻居的整数」这一类）。
5. **全量判据不低于 1690 通过、0 失败、2 跳过** — 成立：**1925 / 0 / 2**（本轮基线 1903）。

### 自查里我认为评审最该盯的三处

- 偏离一（没给 occasion 加副本 id，改加了一个新方法）——理由在上文第三节。
- 偏离二连带的三处文档漂移，在我不能改的文件里，已逐条列出交 TASK-09。
- 唯一被改动的既有断言（规则服务依赖枚举），以及为什么它是 BD-006 本来就写明的输入。
## Independent Quick Review — round 2

**Verdict: `passed`**（含四条非阻塞观察，其中第一条是对执行结果里一句证据陈述的更正）

- 评审的契约版本：Contract revision 1，叠加本文件的 `Controller Disposition` 裁定一至四。
  验收以裁定二改写后的三条为准，不以原契约的第 1、3 条为准。
- 评审的 owned diff：受版本控制的两个文件（`git diff` 可见）
  `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs`（+27 −1）与
  `Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs`（+21 −1）；
  另有四个生产文件与八个判据文件属本 Spec 尚未提交的新增文件，逐一读毕。
- 我自己跑过的命令：全量判据（变异前 1 次、变异中 4 次、还原后 2 次）、
  `--filter ResultCard`、`--filter PaidCard`、四个文件的 sha256、
  十四个文件的行尾字节计数、三组符号的全仓 grep。

### 一、我自己重做的变异（四个，两个是指定复核项）

变异一律让 mtime 自然前进；还原一律用 sha256 校验内容。变异前基线经我亲自跑过：
**0 失败 / 1925 通过 / 2 跳过，退出码 0**。

| 我做的变异 | 改在哪 | 我实测的结果 |
|---|---|---|
| 复核 N2：`token.Value != 0` 改 `== 0` | `PvfDungeonDefinitionSource.cs:208` | 红：基础设施 5 / 应用 9 —— **计数与执行者一致，但失败名单全在关闭侧** |
| 复核 N3「一律答提供」：模式后接 `或 true` | `ClearRewardRuleService.cs:115` | 红：应用 9，基础设施 0 —— 与执行者一致 |
| 复核 N6「一律答不提供」：模式后接 `且 false` | `ClearRewardRuleService.cs:115` | 红：应用 7，**全部落在对照侧**（见下）—— 与执行者一致 |
| 我另加的一个（执行者没做）：默认值 `var offersPaidCard = true` 改 `false` | `PvfDungeonDefinitionSource.cs:91` | 红：基础设施 3 + 应用 6，**全部落在未声明侧** |

三个用短路常量写法的变异都真的编译通过并跑完，不是编译失败冒充变红。

还原核对（与变异前逐字节相同）：

```text
8befcdce421f660a03cb9eccd91afa557eeb14d57a34699054090823dd269817  PvfDungeonDefinitionSource.cs
5bc38bedca77e1b5789ed74cc8919bdff1c054350ec5dacbd11078031e26f443  ClearRewardRuleService.cs
0e84da1a9438c33e3fac6d7faf343c5a2ff282115222791c34ff6bba0bbeaa85  CardRewardDomainService.cs
fc2709ef756a002603915deac6afc487680912ad0c7b3a87d190a5d40142e4ae  DungeonDefinitionContent.cs
```

还原后重跑全量：**0 失败 / 1925 通过 / 2 跳过，退出码 0**。
`--filter ResultCard` 得 0 失败 / 2 通过；`--filter PaidCard` 得基础设施 8 + 应用 17 = 25 通过。

### 二、非阻塞观察一：N2 的性质被执行者说错了

执行结果第六节写「**N2 是这组里最要紧的一个**：它同时打红「关闭的副本读成关闭」与
「没声明的副本读成没关」，证明两向都被钉住」。**这句不成立。**

我复现的同一个变异下，十四条失败的完整名单是：

```text
DungeonPaidCardSwitchTests.A_dungeon_declaring_the_switch_offers_no_paid_card(100/3700/3901/3902)
DungeonPaidCardSwitchTests.Reading_the_switch_leaves_the_rest_of_the_dungeon_alone
DungeonPaidCardSwitchRuleTests.A_dungeon_that_switched_its_paid_card_off_offers_no_flip(100/3700/3901/3902)
DungeonPaidCardSwitchRuleTests.The_switch_does_not_move_the_price
PaidCardFlipCostTests.A_dungeon_that_switched_its_paid_card_off_refuses_the_flip(100/3700/3901/3902)
```

**十四条全在关闭侧，一条 `declares nothing` 的用例都没红。** 原因在代码里是明摆的：
未声明的副本根本不进 `Section.PaidCardSwitch` 分支，它拿的是
`PvfDungeonDefinitionSource.cs:91` 的默认值 `true`，方向翻转碰不到它。
（`The_switch_does_not_move_the_price` 里虽然两侧断言都有，但它先死在
`DungeonPaidCardSwitchRuleTests.cs:104` 的 `Assert.False(...100)` 上，仍是关闭侧。）

**结论没变，但支撑它的证据得换一个。** 未声明侧确实被钉住了，钉住它的是另外两个变异：
我复现的 N6（应用 7 条全在对照侧：`A_dungeon_that_declares_nothing_still_offers_a_flip` 三例、
`The_switch_does_not_move_the_price`、`A_dungeon_with_no_definition_is_not_treated_as_switched_off`、
`A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip`、
`A_clear_that_levelled_the_character_still_charges_what_it_announced`），
以及我另加的那个默认值变异（基础设施 3 条 `A_dungeon_declaring_nothing_still_offers_the_paid_card`
分别为 144 / 10000 / 10004，加应用 6 条）。

所以裁定二的红线**是达成的**，只是执行结果第六节那句说明须以本节为准。

### 三、逐条对照裁定二的三条 acceptance

**1. 声明关闭的副本，付费翻牌确实不可用 —— 成立。**
路径逐段读过，每一段都在它该在的层：
`PvfDungeonDefinitionSource.cs:203-209`（读 0 得 `OffersPaidCard=false`）
到 `ClearRewardRuleService.cs:112-115`（唯一读取点）
到 `CardRewardDomainService.cs:55-58`（不提供就不向价格表要价）
到 `SelectCardCommand.cs:279-280`（`ChargeAsync` 在 `TrySpendAsync` **之前**以 `NotPriced` 返回）。
拒绝发生在扣款与 `ClaimAsync` 之前，判据 `PaidCardFlipCostTests.cs:214-246` 对四个真副本
断言了 `NotPriced`、`GoldCharged==0`、`GoldPaid==0`、`Slots` 为空、**钱包读回来分文未动**，
且同一局的免费翻牌仍 `Flipped`。

**2. 未声明的副本行为不变，且有判据钉住 —— 成立，且我独立验证了「钉住」这一半。**
三层各有一对：内容层 `DungeonPaidCardSwitchTests.cs:62-72`（144 / 10000 / 10004）、
规则层 `DungeonPaidCardSwitchRuleTests.cs:44-50`（同三个）、
端到端 `PaidCardFlipCostTests.cs:257-279`。我用两个变异（N6、默认值翻转）确认这三层
都真的会红。另外 `PaidCardFlipCostTests` 原有的三条用例默认仍跑在副本 144 上。

**3. 读不出含义的字段记为未查明且不使用 —— 成立。**
非 0 值的含义在 `DungeonDefinitionContent.cs` 的 `OffersPaidCard` remarks 里明写为未查明，
并读作「没关」而不是第三种状态；`0 等于关闭` 明写为读法而非归档明说，并给了论证与
参照服务端「解析了却从不读」的限度。`[result card]` 在 `Server/src` 下一处实现都没有
（全仓 grep 只命中 `ClearRewardRuleService.cs:17` 的一句 remarks，说明为何不做）。

### 四、阴阳对照是不是真的成对 —— 是

`A_dungeon_that_switched_its_paid_card_off_refuses_the_flip`（`PaidCardFlipCostTests.cs:219-220`）
与对照 `A_dungeon_that_declares_no_switch_still_charges_for_a_paid_flip`（同文件 `:262-263`）
用同一个 `Flip` fixture、同一个 `settleAtLevel: Level`、同一笔 `funded: price + 500`，
**唯一的差别是 `dungeonId`**；两者都经 `SettleAsync`（含 `OffersPaidFlipAsync` 与 `PaidFlipCost`）
到 `SelectCardCommandHandler.ChargeAsync`，是同一条结算路径。
我做的 N6（一律答不提供）只打红对照那一条而打不红那四条，正说明对照真的在接东西，
不是走了另一条路的摆设。

顺带一处值得记下的、不影响判定的事实：
`A_paid_flip_costs_the_archives_price_for_that_level` 与
`An_account_a_coin_short_of_the_archives_price_is_refused_and_keeps_it` 在 N6 下**没有红**，
因为它们走 `settleAtLevel: null` 的分支，牌面与价钱是直接 `FixClearRewardCards` 摆下去的，
`SettleAsync` 一进门就在 `CardRewardDomainService.cs:40-41` 返回既有牌，
从不问 `OffersPaidFlipAsync`。这与执行者「既有用例逐字不变」的说法一致，
也说明「已定牌的局不再问副本」这一既有语义没有被这轮改动搅动。

### 五、越界与既有断言

- **符号足迹**：`OffersPaidCard`、`OffersPaidFlipAsync`、`gold card use` 三个符号在
  `Server/src` 下只出现在裁定三批准后的那 6 个文件里，没有第七个。
- **`PaidFlipCost` 的签名一字未动**：`IClearRewardRuleService.cs:148` 仍是
  `int? PaidFlipCost(int characterLevel)`，钉住它的
  `Assert.Single(...GetMethod("PaidFlipCost").GetParameters())`
  （`ClearRewardRuleServiceTests.cs:363-366`）仍在、仍绿。
- **唯一改动的既有断言正当**：`ClearRewardRuleServiceTests.cs:317-324` 的依赖枚举加入
  `IDungeonDefinitionSource`。`plan.md:47` 的 BD-006 一行本来就把它列为该服务的 Inputs，
  加内容源必然要改这个列表；同一用例真正的判据——
  `Repository / Registry / Directory / Store / Persistence / DomainService` 六个后缀的禁令
  （`ClearRewardRuleServiceTests.cs:326-332`）——一字未动。
- **DI 未被触碰**：`ApplicationModule.cs:172` 仍是
  `TryAddSingleton<IClearRewardRuleService, ClearRewardRuleService>()`，按构造签名解析，
  执行者「没动 ApplicationModule」的说法属实。

### 六、分层

- `IDungeonDefinitionSource` 在 `Server/src` 下只被两个**规则服务**注入
  （`ClearRewardRuleService.cs:44` 与既有的 `DungeonLayoutRuleService.cs:20`），
  符合 `AGENTS.md:380`「内容源只被规则服务注入」。
- 它是**内容源**不是世界状态，`AGENTS.md:387`「规则服务不许自己查世界状态」未被触碰：
  副本 id 是 facts，由业务服务 `CardRewardDomainService.cs:56` 从
  `run.Layout.DungeonId` 填入。
- handler 仍不问规则服务：`SelectCardCommandHandler` 的依赖没变，
  `ChargeAsync` 经 `cards.SettleAsync`（业务服务）取价，符合 `AGENTS.md:375`。

### 七、解析改动没有邻域回归

`[gold card use]` 在本次改动之前落在 tag dispatch 的 `else { section = Section.None; }`
（`PvfDungeonDefinitionSource.cs:129-132`），其后那个整数会掉进整数 switch 的无匹配分支被丢掉；
现在它被 `Section.PaidCardSwitch` 读走，随即 `section = Section.None`。
房间、尺寸、起点、终点四段的 `Flush`（`:239-266`）不认 `PaidCardSwitch`，落不到任何一个 case。
因此无论该标签出现在文件作用域还是迷宫块内，房间解析的结果都与改动前逐字相同。
`Reading_the_switch_leaves_the_rest_of_the_dungeon_alone`
（`DungeonPaidCardSwitchTests.cs:85-101`）另从副本 100 一侧钉住了这一点。

### 八、两处「往窄里做」的偏离 —— 我判定都正当，不是回避 acceptance

- **偏离一（新方法而非给 occasion 加副本 id）**：裁定三给出的**目的**是「规则服务要按副本择路，
  就必须知道是哪个副本」，`OffersPaidFlipAsync(int dungeonId, …)` 完全满足这个目的，
  改动更小，且避免了给 occasion 添一个 `FaceFor` 与 `ChanceOf` 都不读的字段
  （收窄后牌面内容本就不按副本变）。触碰的文件仍是裁定三点名的那一个。
- **偏离二（不新增拒绝状态，只扩写 `NotPriced` 的 remarks）**：要分辨两种成因就必须加宽
  `ICardRewardDomainService` 与 `IDungeonRunDomainService`，**两个都不在批准清单里**；
  而 acceptance 只要求「付费翻牌不可用」，判据已把「不可用」拆成
  拒绝、不扣钱、不认领、钱包不动、免费牌照翻五条来断言。没有回避。
  执行者把两种成因写进 `SelectCardCommand.cs:37-61` 的 remarks 而不是让枚举名不副实，
  处理方式恰当。

### 九、其余非阻塞观察

1. **`CardRewardDomainServiceTests` 的假件里有死脚手架。**
   `CardRewardDomainServiceTests.cs:111` 的 `AskedOffered` 与 `:114` 的 `ClosedDungeonId`
   只被 `:119-120` 写入，**没有任何用例读它们**（全文件 grep 只有这四处）。
   不违反 BD-021，但要么补一条业务服务层「关闭则不取价」的组合判据来用它，要么删掉。
2. **CK-019 的过滤词已陈旧，我实测确认。** `--filter ResultCard` 只选中 round 1 的 2 条
   （关于 `[result card]` 声明的事实断言）；收窄后的实质判据要 `--filter PaidCard`（25 条）。
   执行者已披露。建议控制器在交 TASK-09 时把 CK-019 的过滤词更正为 `PaidCard`，
   否则照抄 CK-019 的人会以为那两条判据就是本 Task 的全部证据。
3. **结算路径多了一次归档读。** `CardRewardDomainService.cs:55-58` 对每一次「尚未定牌」的
   结算都会先问一次副本，未声明副本的**行为**不变（答 true，取价照旧），但多了一次
   `IDungeonDefinitionSource.GetAsync`。只是开销，不是行为差异。
4. **`0 等于关闭` 仍只是读法。** 执行者在 `OffersPaidCard` 的 remarks 里已把这一点、
   它的论证（缺省即提供，故 0 只能是关闭）、以及「参照服务端解析了却从不读」的限度
   都写明了，符合硬约束 4。我无从提供第二来源，也不认为本 Task 有义务提供；
   这条应随读数对照进 TASK-09。

### 十、行尾

本轮碰过或新增的十四个文件逐个按字节数核（分别数 CRLF、CR、LF）：
**crlf 全为 0，cr 全为 0**。

### 十一、留给验收者的一句话

裁定二的三条 acceptance 都成立，且「未声明的副本行为不变」这条红线我用两个独立变异
亲自确认了它真的会红。唯一需要落笔更正的是执行结果第六节对 N2 性质的那句话——
它不影响结论，但影响后续任务对「哪个变异钉住了什么」的引用，故记在这里。

## Controller Disposition — round 2 验收通过，四条观察分流

独立评审 `passed`。控制器独立复核后接受：Task 文件前六节行号未动（3/51/300/356/374/577），
评审轮次落在 610；受控两文件 `git diff --numstat` 回到 27/1 与 21/1，
全量判据由控制器亲自重跑为 **1925 通过 / 0 失败 / 2 跳过，退出码 0**。
执行结果、自检、评审轮次与 owned diff 指向同一轮。**TASK-08 置 `done`。**

### 观察一：N2 的证据被执行者说强了 —— 更正，但红线不受影响

执行者称 N2（把 `token.Value != 0` 的方向反过来）「两向都红」，并以此论证
「不是恒定答案碰巧过关」。评审复现了同一变异，**失败计数完全一致，但十四条失败全在关闭侧**。

原因是结构性的：未声明的副本压根不进 `Section.PaidCardSwitch`，
拿的是解析开头第 91 行的默认值 `true`，**方向翻转碰不到它们**。
所以 N2 只证明了关闭侧在被读，没有证明未声明侧在被钉。

**红线本身仍然成立**，钉住它的是 N6（一律答不提供，只被阳性对照接住）
与评审自己另加的默认值变异（`true` 改 `false`，红在未声明侧）。
换言之：结论对，论据错，而且是评审补上了真正的论据。

**这是本 Spec 第三次由变异证据出错。** 前两次是假阴性与假绿（见本文件
`Controller Note`），这次是真红但**打错了地方却被当成打对了**。
三次形状不同、方向不同，共同点只有一个：**变异证据是本流程里最不可靠的一类陈述**，
因为它天然没有第二个观察者——除非有。这次有，所以抓到了。
交 TASK-09 记入读数对照的「记录内部的分歧」。

### 观察二：判据里的死脚手架 —— 交 TASK-09 清理

`CardRewardDomainServiceTests.cs:111,114` 的 `AskedOffered` / `ClosedDungeonId` 只写不读。
不影响正确性，但它**看起来像在检查什么而其实没有**，正是本 Spec 一路在清的形状。
不在本 Task 改：`passed` 覆盖的是已评审的那份 diff，此刻动它就得重评。交 TASK-09。

### 观察三：CK-019 的过滤器已陈旧 —— 交 TASK-09 更正

`--filter ResultCard` 实测只选中 round 1 遗留的 2 条；收窄后的实质判据是
`--filter PaidCard`（25 条）。计划里的检查项与它实际检查的东西已经对不上。

### 观察四：每次未定牌结算多一次归档读 —— 记录，不改

`CardRewardDomainService.cs:55-58`。行为不变，只是开销，且内容源本身有缓存。
本 Spec 不是性能任务，不为此改动已评审的实现。记在此处备查。
