# Implementation state — spec-002

实施七个任务已全部完成，全量套件全绿。这份记录留下每一步的证据，以及实施中回写裁定的那一处。

基线：`9c535a9715ebd142fd197026c80d711a3036abcc`。未产生任何任务级提交。

## 已完成

| 任务 | 结果 | 证据 |
|---|---|---|
| T-001 目标形状与约束落点的守卫先行 | 完成 | 新增 `Server/tests/Dfo.Server.Persistence.Tests/Schema/CharacterBodyShapeTests.cs`，15 条断言。列清单由裁定的逐列账本机械生成，不是手抄。先失败后转绿，失败原因逐条确认为「目标表不存在 / 列集不符 / 级联外键缺失」 |
| T-002 表征播种改到目标形状，期望值不动 | 完成 | 三份表征用例的播种改写；`EntryLoadCharacterizationTests.cs` 的 diff 是 20 增 19 删且**全部落在 INSERT 语句上**，期望输出一个字节未改；改写后按预期报 `no such table: character_stats` |
| T-003 库形状改为目标形状 | 完成 | `CharacterDatabaseSchema.cs`：九张源表与两张每日挑战表的声明换成六张目标表加合并后的 `character_daily_challenges`，`character_invisible_falgs` 更名。表数 53 → 49。DDL 由裁定的账本加源表声明机械生成。`CharacterBodyShapeTests` 15/15 绿 |

T-003 附带：可补列清单删去 122 条指向已消失表的条目，并为存续但改列的 `characters` 与 `character_expert_job` 重新生成 31 条；六条索引声明全部仍指向存在的表；`SchemaOwnershipTests` 的表数断言改为 49，并把逐列展开的那一条从 `character_gold_limits` 换到 `character_settings`。

实施中发现并已回写裁定的一处：**每日挑战的「已领取」今天由 claims 行的存在表达**，而 `claimed_at` 可空且从无人写；合并后由 `claimed_at` 是否存在表达。两者对任何写入者会产生的行都一致，只对「已存在且 `claimed_at` 为 NULL」的行不一致——擦库不迁移使这一点无损，但**将来写入领取的代码必须写 `claimed_at`**。已记入裁定的 M-5。

## 已完成（续）

| 任务 | 结果 | 证据 |
|---|---|---|
| T-004 建角色写六张表 | 完成 | `CharacterRosterService` 由写三行改为写五行（`characters` 的插入本就在前），`character_condition` 的写入显式给 `pc_room_state=0` |
| T-005 按表接口与读写路径 | 完成 | 新增 `CharacterBodyTables.cs` 两份（抽象与持久化），六张目标表一表一类；删掉十二个被取代的接口与实现；金币上限那一处物品侧读取改指 `character_stats`；DI 随之更新。全解决方案构建零错误零警告 |
| T-006 领域组装 | 完成 | `CharacterEntryAssembly` 新增两个私有组合函数把六张表拼回原来的两个存储记录；**领域记录文件的 `git diff` 为空** |
| T-007 行为保持证据 | 完成 | 见下 |

### 行为保持证据

- **全量 1047 通过 / 2 跳过 / 0 失败。** 基线 1032，多出的 15 条正是新增的形状守卫 `CharacterBodyShapeTests`——用例数变化可解释。
- **三份整记录表征用例的期望输出一字未改**且通过。它们的 diff 只落在播种语句上，这是本次改动最强的一条行为保持证据：底下九张表换成六张，而领域记录逐字段完全相同。
- **领域记录未被触碰**：`ICharacterEntryStateServices.cs` 与 `CharacterModels.cs` 的 diff 为空。
- **可证伪性**：把 `stamina` 与 `mood_value` 在条件表读取的 SELECT 里对调，表征用例立刻变红；还原后全绿。守卫真的在守。
- **一表一类**：脚本清点 `Server/src/Dfo.Server.Persistence` 下每个类 SQL 中出现的表名，零个类指名超过一张表。

### 实施中回写裁定的一处

每日挑战的「已领取」今天由 claims 行的存在表达，`claimed_at` 可空且从无人写；合并后由 `claimed_at` 是否存在表达。两者对任何写入者会产生的行一致，只对「已存在且 `claimed_at` 为 NULL」的行不一致。擦库不迁移使这一点无损，但**将来写入领取的代码必须写 `claimed_at`**。已记入裁定 M-5。

## 计划内的有界调整

T-004 与 T-005 在实施时交织：写入六张表需要先有写入接口，因此 T-004 一并动到了计划只列在 T-005 下的 BD-002。不改变任何验收。

## 当前状态

工作区 30 个已跟踪文件改动（332 增 790 删）加两个新文件，全量全绿，未产生任务级提交。评审进行中。
