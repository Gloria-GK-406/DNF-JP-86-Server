# 随机精英怪（#355）

## 问题

普通地下城里一只精英怪都不出（用户实机确认，2026-11-16）。服务端只认地图里写死的 `[champion]` 怪，
不读 `.dgn` 的 `[champion]` 出现率，也不做随机晋升。所以「精灵守护者」（任务 4，消灭 5 只精英级怪物，
`hunt monster -1 -1 -2 5`）这类以精英为目标的任务完不成，精英的经验也拿不到。

## 证据

### 70Reference（`Research/1995chen-dnf/other/等级补丁/70/df_game_r`，SHA-256 `CF45F4E8…2EB7`）

全部【反汇编确认】，另注明的除外：

- `ImportDungeonScript` @ 0x088E7BD8 把 `.dgn` 的 `[champion]` 五个整数读到 STDungeonScript+0xB4。
  `CDungeon::set_dungeon` @ 0x0834ADFA 把它们复制下来，`SelectDungeon` @ 0x082FF6E0 开局时按难度 0..4
  取一个写到 CBattle_Field+0x70。
- `ImportMapScript` 读地图顶层的 `[champion] N`（@ 0x089E096F，落到 CMap+0x80，是房间的固定精英数），
  以及 `[fix champion]`（@ 0x089E399D，落到 CMap+0x12C，无参标记）。
- `CBattle_Field::getAddChampionMobCnt` @ 0x083010EE 算精英数：
  - v = rate[难度]，乘难度倍数 {100,150,250,500,100}% 后截断；
  - 房间没有 `[fix champion]` 时，p = v·100/(迷宫宽×高)，`p > get_rand_int(100)` 就额外加 1；
  - 返回 = 固定数 N + 0 或 1。
  - 不涉及队伍人数、活动或道具。
- `add_monsters_current_map` @ 0x0830199F..0x083019F4 建立候选：地图 `[monster]` 里的 `[normal]` 行，排除 named
  （`isNamedMonster`，ID 50000–50999、56000–60000），排除竞技地下城；`[half]`/`[ratio]` 按人数展开后逐个实例算。
  然后用 `rand()%size` 不放回抽 cnt 次，把类型写成 1。宝箱出的怪占名额但不晋升
  （`add_monster_additional_champion` @ 0x083022A8）。
- 时机：`ConsistMap` 只在首次进入房间时执行（`MoveMap` @ 0x08308EF7 遇到已访问的房间直接返回）。
- 类型 2（super champion）只来自地图脚本，随机晋升只写 1。
- 进房包（`CBattle_Field::MakePacket` 0x1D 分支 @ 0x08309104）对每只怪只发类型，另发一个房间 seed，
  **不发精英特征**。70 在服务端用这个 seed 选特征（`makeChampionFeatureList` @ 0x088BA0FD 等），
  并加强怪物数值（`upgradeAbilityByChampionFeature` @ 0x088B7C29，HP ×1.5 为推测）。

中间产物（反汇编、符号表）在一次会话的临时目录里，没有入库。

### 上游 ServerS4A21

`GameWorld/Dungeon.cs` 的 `GetChampionCount`/`PromoteChampions` 是 70 的简化版：随机部分的公式与 70 相同，
但没有地图固定数，第 5 档难度没有倍数分支（结果相同），候选多了"可见、阻挡"条件，还排除了安图恩团本
（210–215、218–224）。

### A21 PVF（content 0.3.3-dev.1）

- 1051 个地下城中有 1041 个声明了 `[champion]`；按 `PvfDungeonDefinitionSource` 读得到迷宫的有 768 个。
  例：格拉卡 `3 33 50 70 80`，熔岩穴 `6 33 50 70 80`，安图恩觉醒 243–247 首档是 100。
- 地图顶层 `[champion] N`：N=2 的 21 张、N=1 的 6 张、N=0 的 23 张（例：`MirkwoodFrost/h1401.map` 为 2）。
  `[fix champion]` 34 张，都是无参标记，多数与 `[champion] 0` 同时出现。

## 实现

| 位置 | 内容 |
|---|---|
| `PvfDungeonDefinitionSource` | 读 `[champion]`，最多 5 个非负整数，存到 `DungeonDefinition.ChampionRates` |
| `PvfMapActorDeclarationParser` | 读顶层 `[champion] N` 与 `[fix champion]`，存到 `DungeonMapActorDeclarations.Champions` |
| `DungeonStructureRuleService.ResolveTopologyAsync` | 进入迷宫地下城时把出现率固定进 `DungeonLayout.ChampionRates`；没经过这里建的布局（竞技、守护者祭坛等）保持 null，不晋升 |
| `ChampionPromotionRules` + `DungeonRoomRuleService.DescribeAsync` | 房间生成时按 70 规则晋升，放在地狱派对模板行加入之前 |

晋升改的是怪物的类型字节，下游原有链路照常读它：进房帧的类型字段、击杀等级（任务 `-2` 精英目标、精英经验、
装备成长），都不需要另外改。

与 70 的差异（有意保留）：

- 随机数用 `DropRandom`，种子是 run seed、地图和格子。同一房间重新描述时结果不变，分布与 70 相同；
  不复刻 glibc `rand()` 的序列。
- 候选还要求敌对（`IsHostile`），因此 A21 的 dummy 行和友方怪不会入选。70 没有 dummy 修饰词。
- `[half]`/`[ratio]` 在本仓本来就是一行一个 actor，没有可展开的。宝箱怪在本仓属于对象模板，不进候选，
  因此也不占名额。
- 不在服务端选特征，也不加强数值：本仓不模拟怪物数值，客户端按房间 seed 显示特征（推测，以实机为准）。
- 没有上游的安图恩团本特判，以 PVF 数据为准（70 没有安图恩）。

## 验证

- 自动测试：
  - `ChampionPromotionRulesTests`：概率公式、候选过滤、固定数与 `[fix champion]`、布局没有出现率时不晋升、
    确定性、各候选都可能被选中。
  - `DungeonChampionRatesTests`：真实 PVF 的出现率；全部地下城普查，没有一个因 `[champion]` 解析失败。
  - `PvfMapActorDeclarationParserTests`：合成与真实地图的固定数和标记。
- 全量测试：除上面这些外，还有 48 条用真实 PVF 或录制报文的用例失败。在主干同一提交
  （`355dde103`）、同一份 dev PVF 上重跑，这 48 条同样失败，是既有问题，与本改动无关。
- 实机：2026-11-16 用户按下节步骤用 Client-08 测试，反馈全部通过：精英会出现，「精灵守护者」可以完成，
  回归正常。逐步的观察记录没有单独留存。

## 用户测试步骤

测试服：worktree `DNF-JP-86-Server-champion`，端口 41080，库为 `E:/Project/DNF/.task-evidence/355-elf-guardian/dfo-server.db`
（主干玩家库副本，v10）；客户端 `Client-08`。

1. 用 16 级以上角色进格拉卡（或任意普通地下城）普通难度，打完几个房间。预期：部分房间（约三分之一）
   出现带精英光环、名字变色的怪。
2. 同一地下城换冒险或更高难度。预期：几乎每个房间都有 1 只精英。
3. 进地图写了 `[champion] 2` 的房间（MirkwoodFrost h1401 所在地下城）。预期：该房间至少 2 只精英。
4. 接「精灵守护者」（任务 4），击杀 5 只精英。预期：进度随精英击杀增加，满 5 后可以交任务并拿到称号。
5. 击杀精英与普通怪，比较经验。预期：精英给的经验更高。
6. 正常退出后重新登录。预期：任务进度保留。
7. 回归：Boss、名怪、APC 的类型不变；地狱派对、镜像、绝望之塔等能正常进入和通关。
