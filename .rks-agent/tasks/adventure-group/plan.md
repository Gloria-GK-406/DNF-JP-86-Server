---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: 冒险团按账号派生、通关经验加成与升级推送

## Planning Choice

direct。所有工作共用一个领域服务和一个 schema 版本，依赖是线性的：规则 → 领域服务 → 消费方（经验、发包、推送）→ 删列。一份 Plan 可以维护全部细节，不需要单独交接或单独恢复的单元。

## Approach and Shared Decisions

路径以 `Server/` 为根，除非另注。

- **DP-1 规则与内容分层**（RQ-001）：
  - 内容源：`ICharacterManageContentSource`（Abstractions/Content）与 `PvfCharacterManageContentSource`（Infrastructure/Content）。读 `etc/linksystem/charactermanage.etc`，用 `PvfDocument` 取各段，`Lazy` 加载，实现 `IContentWarmup`，数据不合法时抛 `InvalidDataException`。
  - 各段的读法：
    - `[point bonus]`：(min, max, point) 三元组；
    - `[manage level point]`：阈值列表；
    - `[manage level max]`：单个数；
    - `[exp bonus]`：(level, percent) 对；
    - `[gold bonus]`：(level, percent) 对，只解析不用。
  - 校验：阈值单调递增，max 不超过阈值个数。
  - 规则服务：`IAdventureGroupRuleService`（Abstractions）与 `AdventureGroupRuleService`（Application），只接收角色等级列表，给出 (G, S, Lmax) 和 `ClearExperienceRate(standing, characterLevel)`。
- **DP-2 领域服务**（RQ-002）：`IAdventureGroupDomainService`（Abstractions）与 `AdventureGroupDomainService`（Application）。账号角色集统一用 `ICharacterTable.ReadByAccountAsync(account)`，即 `delete_flag=0` 的角色及其等级。对外方法：
  - `ReadAsync(AccountId) → AdventureGroupStanding(byte Level, int Point)`；
  - `ClearExperienceRateAsync(AccountId, CharacterId) → double`；
  - `LevelRiseAsync(CharacterLevelChanged) → AdventureGroupLevelRise?(AccountId, From, To)`：
    - 账号由 `FindOwnerAsync` 取得，角色已删除时返回 null；
    - 把本角色的等级分别替换为 `Before`、`After`，其他角色取当前等级，算出 a 与 b；
    - 只有 b > a 时返回结果。
  - `AdventureGroupStanding` 放在 `Dfo.Server.Data`，供 Protocol 使用。
- **DP-3 推送挂点与不重复**（RQ-007）：
  - 订阅现有的 `CharacterLevelChanged` 事件，策略用 **LiveOnly**，不设合并键。LiveOnly 先提交消费决定再调用，从不重放，跨进程或超过 30 秒都会跳过，所以每次等级变更最多推一次，不需要持久化任何冒险团状态。
  - 订阅者放在 Protocol 的 `Game/AdventureGroup/` 切片，写法参照 `RentalSlice` 的 LuckyStar 订阅：
    1. 调 `LevelRiseAsync`；
    2. 用 `sessions.Find(account)` 找到会话，并要求 `IsInWorld`；
    3. `ProjectAsync(TheseSessions([owner]), AdventureGroupLevelUp(owner, count))`。
  - 通知处理器再核对一次接收者与 `IsInWorld`，然后组出 count 帧空 body 的 `0x01A0`。
- **DP-4 帧顺序与重复加级**（RQ-007 后半）：
  - 事件作业排在发布它的命令之后，`0x01A0` 总在该命令自己的帧之后发出。
  - 现有帧中，经验获得升级（击杀、通关、任务、经验道具、祭坛、锦标赛）不在升级当刻发 USERINFO1，`0x01BA` 也只在入场和客户端询问时发，所以这些路径不会叠加。
  - 唯一在同一命令内发出带新 G 的 USERINFO1 的是升级券（`LevelUpTicketSlice`）。客户端会不会在会话中途把 USERINFO1 的冒险团等级当绝对值写入，现有证据判断不了。
  - 本 Plan 不为未证实的假设改动升级券路径，在用户测试中单列升级券升级一步。若实测多加一级，按契约「实机证伪假设」交回用户裁定。
  - 这是 RQ-007 唯一依赖实机的部分，在 verification 中如实标注。
- **DP-5 发包取值**（RQ-006）：
  - `CharacterEntryExtrasState.ManageLevel` 换成 `AdventureGroupStanding AdventureGroup`，由 `CharacterEntryStateDomainService.LoadAsync(accountId, …)` 向领域服务取值填入。这里已有黑钻、名牌等账号级事实的先例。
  - 填入后，USERINFO1 尾部（u8 G、i32 S）和 subtype3 +3（u16 G，取目标账号）自动正确，因为 subtype3 的 `LoadAsync` 用的就是目标会话的账号。
  - 入场批次的 `MercenarySupportStateMessage` 改为携带选中角色 Extras 里的冒险团值。
  - `0x01BA` 命令按 `FindByConnection` 找到会话后查询领域服务；没有会话时回 0。
  - 选人列表头：`CharacterStateProjection` 第 74 行改为写 0。选人界面那条路径本来就写 0。
- **DP-6 经验**（RQ-003–005）：
  - 新增 `ExperienceBonusKind.AdventureGroup`、`ExperienceBreakdown.AdventureGroup`，并把它加进 `Total` 和 `Add` 分支。
  - `ExperienceBonusDomainService.CollectAsync` 只在 `DungeonClear` 时加入冒险团加成项，加成率取自领域服务，为 0 时不加。
  - 领主之塔通关用 `Event = DungeonClear` 收集，因此自动纳入现有的封顶逻辑。冒险团项排在已有加成项之后，触顶时先被截短。
  - 死亡之塔、迷妄之塔结算不收集加成，不受影响；击杀经验不加。
  - `0x0023` 第 7 格写 `experience?.AdventureGroup`。
  - `0x0025` 的经验分行：未映射的项按现状并入基础行，总数保持一致。
- **DP-7 删列**（RQ-008）：
  - schema `Version = 5`，DDL 去掉该列；
  - 读取链整条移除：SQL、`CharacterEntryRow`、`CharacterProfileStorageRecord`、`RequireProfileInRange` 的参数；
  - DbMigrate 新增第 4 步（v4→v5）：先用 `Shape.HasColumn` 检查，再 `ALTER TABLE characters DROP COLUMN manage_level`；
  - 补 v4→v5 测试；现有的结构一致性测试会自动覆盖新结构。

## Work and Dependencies

| ID | Work | RQ | 主要位置 | 检查 |
|---|---|---|---|---|
| T-001 | 内容源与规则服务；DI 注册；把新内容源加入 `ContentWarmupTests` | RQ-001 | Abstractions/Content、Infrastructure/Content、Application/AdventureGroup、`InfrastructureModule`、注册类 | Infrastructure 测试：真实 PVF（可跳过）+ `PvfArchiveBuilder` 合成档，保留两处格式缺陷；Application 规则测试覆盖 AC-001 的全部边界值 |
| T-002 | `AdventureGroupStanding`、领域服务与注册 | RQ-002 | Data、Abstractions、Application | 领域服务测试，用假 `ICharacterTable` 或 SQLite 夹具：删角、新建低等级角色、改等级、R 的三种情况、`LevelRiseAsync` 跨一级、跨多级、不跨级、下降、已删除 |
| T-003 | 经验：新 kind、breakdown 字段、CollectAsync、`0x0023` 第 7 格；检查 `MonsterDeathSlice` 枚举数组 | RQ-003–005 | Abstractions/World、Application/World、Protocol DungeonClear | `ExperienceCalculationDomainServiceTests`：普通通关、领主之塔未触顶、触顶、最高等级，以及击杀和死亡之塔不变；`ExperienceBonusDomainServiceTests`；`DungeonClearCodecTests` 断言 body+45 |
| T-004 | 删列与 Extras 换型：schema v5、读取链、Extras 填值、subtype2 写 0、`0x01BA` 两条路径、`GameplayStateCensusTests` 分类；DbMigrate 第 4 步；更新受影响的测试夹具 | RQ-006, RQ-008 | Persistence、Application/Characters、Data、Protocol StateSynchronization、MercenaryInfo、SelectCharacter、`Tool/DbMigrate` | 协议测试断言 USERINFO1 尾部、subtype3 +3、`0x01BA` 应答与入场、subtype2 +5/+6；DbMigrate v4→v5 测试 + 结构一致性测试；`manage_level` 搜索 |
| T-005 | `0x01A0` 消息与编码器、订阅者（LiveOnly）、通知处理器、`ProtocolModule` 接线、opcode 常量 | RQ-007 | Data/Networking、Protocol/Game/AdventureGroup、GameProtocol | 订阅者与处理器测试：帧数 = b−a；不在世界中为 0；下降为 0；处理器换了接收者为 0。注册测试确认策略是 LiveOnly、没有合并键。重复投递由 LiveOnly 机制保证，引用已有的分发器测试，或补一条注册断言 |
| T-006 | 文档：`Document/DatabaseSchemaMigration.md` 的 v4→v5 段落；四个协议页人工区与 `catalog.md` | RQ-008, RQ-009 | `Document/`、`protocol/` | 人工复读；证据标签正确 |
| T-007 | 全量构建与测试 | RQ-011 | 解决方案 | `dotnet build Server/86JP.Server.slnx` 无警告；`dotnet test` 全部通过 |
| T-008 | 用户测试准备：<br>• 从运行进程或配置核实主干库的实际路径，用 SQLite backup API 取一致备份到 `artifacts/adventure-group/db/`；<br>• 对副本运行 DbMigrate；<br>• 用 41xxx 端口段启动本分支测试服，先确认未被其他会话占用，被占用时换端口段；<br>• 写 `Document/tasks/adventure-group/用户测试流程.md`：前置条件、逐步操作、每步预期值（按副本中实际角色等级算出）、升级券一步、双客户端查看一步、正常退出重登、回归检查；<br>• 用提问工具请用户按流程测试 | RQ-010 | `artifacts/`（不提交）、`Document/tasks/adventure-group/` | 副本迁移输出；测试服就绪：status 200、端口监听 |

顺序：T-001 → T-002 → T-003、T-004、T-005（三者都只依赖 T-002，按此顺序串行做）→ T-006 → T-007 → 实现提交 → review → T-008 与 verification。

T-004 改动面最大：Extras 换型会波及所有构造 `CharacterEntryExtrasState` 的测试。先编译，按编译错误逐一修正夹具，不改变它们的断言含义。

## Verification

- 每个 T 完成后运行相应测试项目；T-007 做一次全量构建和测试，作为实现提交的前提。
- review 阶段由独立审查者对照契约、Spec 和本 Plan 审完整 diff。
- verification 阶段做以下几件事：
  - 重新跑构建和测试；
  - 搜索 `manage_level`；
  - 汇总 T-008 的副本迁移证据；
  - 收集用户对编号流程逐项的反馈：AC-003、AC-005、AC-006 的实机部分。
- 用户明确反馈通过之前，不声称实机验收通过。
