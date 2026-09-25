# ServerS4A21 的 `[enemy reward item]` 参照实现

取证范围：只读 `E:/Project/DNF/ServerS4A21`，HEAD `4c6c442c607fdf1803a3f0a4e7502bb53165a7d5`。以下仅说明该现代参照服的**当前代码行为**，不证明 A21/70 级台服原版客户端或服务端语义。主仓对照 HEAD `4970b6911cca221496ba11dd8ad5a675807fde91`。

## 定义与类型

- `Tool/PvfLib/Models/QuestFile.cs:61-64,156-163,213-290`：`[monster reward item]` 是七整数一组；`[enemy reward item]` 是八整数一组，次序为 `enemyCode, enemyType, dungeonId, difficulty, itemId, count, dropRate, maxStack`。两节分别保存、分别解析；重复出现同一节时赋值覆盖前一节。解析对不足整组或非整数整组静默跳过，没有语义域校验。
- `Server/DfoServer/GameWorld/QuestDropProvider.cs:19-21`：**任务 enemy type** 为 `1=monster, 2=AI character/APC, 3=passive object`。这是本 PVF 节的判别值；主仓另一个独立掉落表中的 `0=Monster/1=APC` 是另一命名空间，不能直接复用或平移推断。
- `Server/DfoServer/Network/Handlers/Dungeon/DungeonCombatHandler.cs:252-255` 与 `DungeonActorQuestSync.cs:119-140`：运行时 actor type `0..3 → 任务 type 1`，`5..8 → type 2`，`9 → type 3`。`QuestTargetIndex.cs:789-825` 也把 hunt enemy 的 `1/2` 精确目标归服务端计数、`3` 归客户端计数；这是任务进度规则，不能直接当作掉落发放规则。

## 候选、概率和上限

- `QuestDropProvider.CheckMonsterDrop`（`30-109`）对每个有效任务先枚举七字段 `MonsterRewardItems`，再枚举八字段且 `EnemyType == 1` 的 `EnemyRewardItems`，**两者可同时产出候选**，没有互斥或按物品去重。两边都要求 code 精确相等，`dungeonId=-1` 和 `difficulty<0` 可通配（`339-346`）。
- `QuestDropProvider.CheckEnemyDrop`（`262-312`）仅枚举八字段，code、type、地城和难度均匹配，用于 APC/type 2 和物件/type 3。候选保留 `Count/DropRate/MaxStack`，并从当前任务 `seeking` 所需物品求 `SeekingRequiredCount` 与优先任务背包标记（`314-337`）。
- `RollDrop`（`355-390`）：每个候选做 `max(1, Count)` 次独立的 `Next(100) < DropRate`，成功次数最多 999；有效持有上限为 `min(MaxStack, seeking 需求数)`，缺失上限按无限处理。这里的“次数”是同一事件中的概率试验次数，不是跨事件可领次数。`QuestDropService.BuildGrantRequests`（`603-653`）对同物品逐候选累加预期持有量，避免本批跨任务/跨两节越过候选上限；从在线主背包计已持有量。
- 源码注释在 `QuestDropProvider.cs:348-353` 声称 IDA 中“随机选一个候选”；**实际实现却遍历所有匹配候选并逐个 roll**（`QuestDropService.cs:611-652`）。因此多候选时概率/产量不能据这段注释等同原版。服务中还可能由任务助手给基础成功数加 50% 期望量（`QuestDropService.cs:656-669`、`QuestAssistantDropPolicy.cs:9-37`）。

## 事件时机与发放

- 普通怪物/APC：`DungeonKillApplicationService.cs:424-435,471-507` 在死亡回包后，按 actor type 选 `CheckAiCharacterDrop` 或 `CheckMonsterDrop`，然后同步击杀任务进度；普通怪物在 Tower 路径被跳过。物件：`DungeonCombatHandler.cs:111-162` 先接受物件死亡并同步任务、处理机制/通关意图，再调用 `CheckPassiveObjectDrop`（非 Tower）。两者顺序并不相同。
- `QuestDropService.cs:254-305,329-426`：上述入口共用 `CheckDrop`，先校验当前 run/房间、奖励策略、进入地城时冻结的任务激活快照与当前活动任务，再找候选。`CommitAutomaticDrops`（`429-601`）在在线背包锁和 SQLite 事务下重新核对任务激活，规划/应用直发道具、以 `SourceEventId` 更新寻物进度并处理重复事件，持久化背包后提交；失败尝试回滚在线背包变更。`QuestDropNotificationBatcher.cs:13,56-92,135-156` 在成功后约 100 ms 合并背包槽位刷新通知。因此这条路径是**直接入背包**，不是地面掉落再拾取。另有捕获怪物道具的地面注册路径（`QuestDropService.cs:90-252`），不应混同八字段 enemy reward。

## 明显风险与主仓接入影响

1. **参照实现和其逆向注释冲突**：多个匹配候选全量 roll，尤其七字段与八字段同物品共存时可能增加产量。必须先查 70 级台服 ELF 是否按候选池选一项、逐项 roll，或按类型路由；不能照搬本实现。
2. **输入校验不足**：`QuestFile.cs:260-290` 只验证可解析整数；`QuestDropProvider.cs:361-366` 把 `Count<=0` 当作一次尝试，`DropRate<0/>100` 分别恒败/恒中；`CheckMonsterDrop` 和 `CheckEnemyDrop` 不过滤非正物品 ID/数量。主仓若接入应明确非法 PVF 的处理与诊断，不沿用静默跳过。
3. **类型域与任务进度分开**：`QuestTargetIndex.cs:444-465` 为未完成副本目标收集 enemy code 时不检查 enemy type，且只按 code 去重；这是目标生成辅助索引，不足以证明掉落类型映射。主仓应给八字段单独定义和类型转换，不复用独立掉落表的 `0/1` 枚举。
4. **主仓现状**：`Server/src/Dfo.Server.Infrastructure/Content/PvfQuestDefinitionSource.cs:259-260,474-510,1040` 当前只解析七字段节；`Server/src/Dfo.Server.Abstractions/Content/QuestContent.cs:196-207,237-254` 只承载七字段定义；`Server/src/Dfo.Server.Application/World/QuestMonsterDropRules.cs:72-75` 的 `DrawEnemyRewards` 仍返回空。接入范围至少覆盖 PVF 投影/契约、类型化事件、规则计算与既有直发提交链；在消费链未确认前不宜把八字段合并进七字段 DTO。

## 待与 70 级台服 ELF 消费链逐项对照

1. `[enemy reward item]` 是否真按八整数步长读取；字段顺序与数值单位、非法/重复分组处理。
2. enemy type `1/2/3` 与运行时 monster/APC/可破坏物的实际映射；死亡、捕获、物件破坏各自调用点与前置条件。
3. 七字段和八字段同时存在时，是两套候选合并、互斥、覆盖，还是选池后只取一项；同物品跨任务如何处理。
4. `Count` 是 Bernoulli 试验数还是固定发放数量；`DropRate` 分母、加成次序、随机抽取方式。
5. `MaxStack` 限制的是主背包持有、主/任务背包合计、地上待拾取，还是任务生命周期累计次数；寻物需求数是否另行封顶。
6. 奖励何时产生：死亡确认前后、死亡通知中、地面掉落/拾取时或直接入背包；与任务进度及客户端通知的顺序和重复事件保护。
