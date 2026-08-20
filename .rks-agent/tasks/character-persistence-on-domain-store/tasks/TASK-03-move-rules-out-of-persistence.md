# TASK-03 — 三处规则迁出持久实现

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把三处业务规则从持久实现迁至业务层，使其可在不涉及数据库的情况下被检验；持久侧只留读与写。
- Referenced spec requirements: RQ-008
- Referenced plan sections: `plan.md` 的 `### T-003`
- Owned boundary: BD-006, BD-007, BD-011, BD-012
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Infrastructure/Characters/`
  - `Server/src/Dfo.Server.Application/Characters/`
  - `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`
  - `Server/tests/`
- Dependencies: TASK-02
- Required dependency outputs: TASK-02 的覆盖充分性结论。覆盖判定为不足且未补齐的规则不得在本单元外迁。
- Acceptance conditions:
  1. 过期专家契约后技能等级降到何值，由业务层决定，可在不涉及数据库的情况下检验。
  2. 地下城持久效果是否可提交、发放多少经验，由业务层决定，可在不涉及数据库的情况下检验。
  3. 金币上限等级取何值，由业务层决定，可在不涉及数据库的情况下检验。
  4. 三处的既有覆盖仍然通过，行为等价。
  5. 三处规则的调用时序不变。
- Task-specific authorization: 属根 TaskContract `task` 授权范围内。
- Required methods: `test-driven-development`。三处各补一组不触及数据库的规则用例。
- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Characters` | 三处的无数据库规则用例通过 |
| CK-007 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~EntryLifecycleRecovery` | 两处的既有基础设施覆盖仍然通过 |
| CK-008 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿 |

- Required evidence: 三处外迁前后的行为等价说明、无数据库规则用例清单、三条检查的实际输出。

### 共同约束

- 全程不产生任务级提交。工作保持未提交，直至完整评审与新鲜验证通过后由工作区控制器做唯一一次已接受结果提交。
- 对外可观察行为一字不变：帧字节与时序均不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 存储实现不得自建任何表。
- 开发阶段的策略是重建而非迁移：不得为既有数据编写回填、修复或迁移路径。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`；客户端可执行文件与 DLL 不得手工编辑。
- C# 文件为 CRLF、无 BOM UTF-8；`net10.0`，`TreatWarningsAsErrors`。每次以工具写入 `.cs` 后须做一次行尾归一化。

### 验证环境

- `dotnet test Server/86JP.Server.slnx -m:1`
- `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`
- 基线：944 通过 / 2 跳过 / 0 失败。`ChannelHostStartupTests` 在负载下会假红，复跑确认。

## Contract Amendment A1

- Amends: Permitted files or interfaces
- 时点：实施开始，动手之前。

**增列 `Server/src/Dfo.Server.Abstractions/Characters/` 为本单元的 Permitted path，仅限新增规则契约文件。**

为什么必须如此：本单元要把规则从持久实现搬到业务层，而 `Dfo.Server.Infrastructure` 只引用 `Dfo.Server.Abstractions` 与 `Dfo.Server.Data`，不引用 `Dfo.Server.Application`（见两个 csproj）。因此规则的**接口**必须落在 Abstractions，实现落在 Application，由组合根注入回仓储。

本仓库已有同形先例，且是上一个根任务搬走每日刷新与物品到达两条规则时确立的：`IItemArrivalRuleService` 与 `IDailyRefillRuleService` 声明于 `Abstractions/Inventory/IItemArrivalRules.cs`，实现于 `Application/Inventory/ItemArrivalRuleService.cs`，由 `InfrastructureModule.cs:98-102` 注入 `SqliteEntryInventoryMutationRepository`。本单元照此办理。

为什么不越出必要范围：只**新增**文件，**不触碰** `Abstractions/Characters/ICharacterEntryStateServices.cs`——该文件是 BD-005，属 TASK-04。业务动词的移除与人物会话契约仍是后续单元的事。

授权：属根 TaskContract `task` 授权范围内的边界记录，不扩大对外可观察行为，留待 change review 确认。
## Execution Result 1

### 交付

三处业务规则已离开持久实现，落在 Application，可在不涉及数据库的情况下被检验。持久侧保留读、写与状态推进。

| 文件 | 变化 |
|---|---|
| `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs` | 新增（见 Contract Amendment A1）。三个规则契约与其入出类型；另含 `DungeonEffectKinds`，使效果种类的名字不再由持久实现私有 |
| `Server/src/Dfo.Server.Application/Characters/CharacterEntryRuleServices.cs` | 新增。`GoldLimitRuleService`、`ExpertSkillContractRuleService`、`DungeonEffectRuleService` |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteCharacterEntryStateRepository.cs` | 删除 `ResolveGoldUpgradeLevel`，改注入 `IGoldLimitRuleService` |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteExpertSkillContractReconciliationRepository.cs` | 过期判定与上限计算外迁。现在读 premiums、问规则、读 skills、问规则、写 |
| `Server/src/Dfo.Server.Infrastructure/Characters/SqliteDungeonPersistentEffectRecoveryRepository.cs` | 死信校验、幸运星上限、经验/荣誉拆分三处外迁 |
| `Server/src/Dfo.Server.Application/ApplicationModule.cs` | 注册三个规则服务 |
| `Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` | 三个仓储改由 provider 取规则 |
| `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryRuleServiceTests.cs` | 新增 34 条不触及数据库的规则用例 |
| 四个既有测试文件 | 只因构造函数依赖变化做机械调整 |

### 为何是注入而非上提

`Dfo.Server.Infrastructure` 只引用 `Abstractions` 与 `Data`，不引用 `Application`（两个 csproj 可证）。因此规则接口置于 Abstractions、实现置于 Application、由组合根注入回仓储。这不是本次发明的形状：上一个根任务把每日刷新与物品到达两条规则搬出 `SqliteEntryInventoryMutationRepository` 时用的就是它（`InfrastructureModule.cs` 中 `IDailyRefillRuleService` 与 `IItemArrivalRuleService` 的注入）。本单元照此办理，层次未破。

### 经验规则为何拆成两个方法

原实现只在 `honor > 0` 时才去读账号，账号不存在也只在该情形下抛。若规则做成一个方法并同时接收账号的两个数值，调用方就必须每次都读账号，于是「账号缺失」会在今天不失败的路径上开始失败——一次静默的行为改变。故拆为 `SplitExperience`（不碰账号）与 `AccumulateHonour`（只在确有荣誉时调用），把「是否需要读账号」这个判断留在原处。

### 检查

| ID | 结果 |
|---|---|
| CK-006 | `已通过! - 失败: 0，通过: 34`，耗时 61 毫秒，全程无数据库 |
| CK-007 | `已通过! - 失败: 0，通过: 32`（`EntryLifecycleRecovery` 与 `CharacterEntryStateRepositoryTests` 合并跑，含 TASK-02 补的 12 条表征用例） |
| CK-008 | 全量 `996 通过 / 2 跳过 / 0 失败` = 944 基线 + 4（TASK-01）+ 14（TASK-02，含 QR-1 补的 2 条）+ 34（本单元） |

`dotnet build Server/86JP.Server.slnx`：0 警告 0 错误。

残留核对：`ResolveGoldUpgradeLevel`、`current < 999`、`settlementLevel >= 86`、`record.Scope != 3`、`free > maximum` 在 `Server/src/Dfo.Server.Infrastructure/` 下**零命中**。

### 一次真实的回归与其修复

首轮全量出现 `Dfo.Server.Protocol.Tests` 一条失败：`SelectCharacterPayload_Subtype1PreservesRealSqliteSpecialRewardSource` 报 `MissingMethodException`。原因是该用例经 `Activator.CreateInstance` 反射构造仓储，我批量改调用点的正则只匹配 `new Sqlite...(...)`，反射调用没被覆盖。已补上并复跑通过。

这条值得记下来：**构造函数签名变更的影响面不能只靠语法匹配去找**，反射构造不会在编译期报错，只会在运行期以 `MissingMethodException` 出现。本次是被全量测试抓住的。

### 一处自我纠正

批量补 `using` 的脚本顺手把四个测试文件的 using 段整体重排了，使协议测试文件的差异从 4 行涨到 14 行，且与该文件既有的（本就非字母序的）风格相冲突。已恢复原有次序，只插入新增的那一行。改动面回到 4 行。

## Executor Self-check 1

| 验收条件 | 证据 |
|---|---|
| 1 过期专家契约后技能等级降到何值由业务层决定，可无数据库检验 | `ExpertSkillContracts` 共 7 条用例；`SqliteExpertSkillContractReconciliationRepository` 中已无判定，只余读、问、写 |
| 2 地下城效果是否可提交、发放多少经验由业务层决定，可无数据库检验 | `DungeonEffects` 共 15 条用例；仓储中三处判定均已替换为规则调用 |
| 3 金币上限等级由业务层决定，可无数据库检验 | `GoldLimits` 共 12 条用例；`ResolveGoldUpgradeLevel` 已删除 |
| 4 三处既有覆盖仍通过，行为等价 | CK-007 的 32 条全通过，其中 12 条是 TASK-02 专为此刻钉住当前行为而补的 |
| 5 三处规则的调用时序不变 | 仓储的语句次序、事务边界、提交点均未改动；差异只是把判定换成对规则的调用 |

### 自认的薄弱处

- 「持久侧只留读、写与状态推进」中的**编排**仍在仓储里：它决定先读什么、何时问规则、何时写。这符合 plan `### T-003` 的措辞，但若 change review 认为编排也应上提，本单元的成果只到一半。我判断把编排上提应与 TASK-04 的会话化一起做，否则会做两遍。
- 三个规则服务都无状态且实现为单例，与既有的 `ItemArrivalRuleService` 一致；但 `HasRunOut` 接收 `now` 而调用方仍传 `DateTimeOffset.UtcNow`，即时钟仍在持久实现里取。把时钟也移出去需要注入 `TimeProvider`，超出本单元范围，记录在此。
- 本单元未触碰 `character_item_locks` 与 `state IN (0,3)` 两处 TASK-02 记录的既有缺口，二者均不在外迁范围内。

## Independent Quick Review 1

- Verdict: `changes_required`
- Reviewed contract revision: 1，含 Contract Amendment A1
- Reviewed artifacts: Task Unit Contract + A1 + Execution Result 1 + Executor Self-check 1；plan `### T-003`
- Owned diff identity（未提交工作树，按 Execution Result 1 的交付表界定本单元所属改动）：
  - 新增 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs`
  - 新增 `Server/src/Dfo.Server.Application/Characters/CharacterEntryRuleServices.cs`
  - 新增 `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryRuleServiceTests.cs`
  - 改 `Server/src/Dfo.Server.Infrastructure/Characters/{SqliteCharacterEntryStateRepository,SqliteDungeonPersistentEffectRecoveryRepository,SqliteExpertSkillContractReconciliationRepository}.cs`
  - 改 `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`
  - 四个既有测试文件的构造参数补齐
- Evidence inspected（未运行任何测试）：`git status --porcelain -- Server/`、`git diff -- Server/`、三个新增文件全文、`SqliteDungeonPersistentEffectRecoveryRepository.cs:160-230`、`SqliteExpertSkillContractReconciliationRepository.cs:55-78`、`Server/Directory.Build.props`、三个 csproj、`Server/src/Dfo.Server.Abstractions/Content/{DungeonExperienceContent,SkillPointContent}.cs`、`Server/src/Dfo.Server.Data/{Accounts/AccountModels,Characters/CharacterModels}.cs`、两个测试目录的用例属性计数、新增文件的行尾与 BOM。

### 行为等价性核对（逐项确认一致）

| 规则 | 原实现 | 结论 |
|---|---|---|
| `GoldLimitRuleService.UpgradeLevel` | `SqliteCharacterEntryStateRepository.ResolveGoldUpgradeLevel` | 五段阈值、`Math.Min` 取小、返回值逐行一致 |
| `ExpertSkillContractRuleService.HasRunOut` | 原 reader 循环 + `expired` 标志 | 等价。原为「边读边判、命中未过期即 return」，新为「全读入 List 再判」；`HasRunOut` 保留了同样的跳过、同样的提前 `return false`、同样的 `expired` 收尾。SELECT 无副作用且行集确定，两者结果恒等，差异只在多读了若干行 |
| `ExpertSkillContractRuleService.CapsFor` | 原 `changes` 循环 | 等价。四道判定的次序与条件、`free > maximum` 抬高上限、`maximum <= 0 ? null : maximum` 全部一致；`OrdinaryMaximumLevel` 为 `int?`、`free` 为 `byte`，提升行为不变；SELECT 无 ORDER BY，List 保序，故后续 DELETE/UPDATE 次序不变 |
| `DungeonEffectRuleService.CanCarryOut` | `if (Version != 1 \|\| Aid != ... \|\| Scope != 3 \|\| Target != ... \|\| Kind is not (...))` | 严格互补。新式为五项 `&&`，德摩根后与原式逐项对应；`recordedTarget` 为 `long`、`characterId` 为 `int`（`CharacterId.Value` 是 `int`），比较时的提升与原式相同 |
| `DungeonEffectRuleService.GrantLuckyStar` | `current < 999` / `Math.Min(999, ...)` | 一致 |
| `DungeonEffectRuleService.SplitExperience` | `ApplyExperienceAsync` 中被删的一段 | 一致。`LevelThresholds` 为 `IReadOnlyList<uint>`，`saturated`/`newExp` 仍为 `ulong`，`Math.Max((ulong)exp, maxEntry)` 与 `checked((uint)(...))` 原样保留；`LastLevel=86`、`LastLevelThresholdIndex=84` 只是给字面量命名 |
| `DungeonEffectRuleService.AccumulateHonour` | 原 `if (honor > 0)` 内的三行 | 一致。`ulong + uint`、`(ulong)honor * (uint)rate / 100UL`、`checked(oldCapsule + capsuleGain)`、`totalCapsule - Math.Min(oldCapsule, max)` 全部原样 |

补充核对：`Server/Directory.Build.props` 与 `Dfo.Server.Application.csproj`、`Dfo.Server.Infrastructure.csproj` 均未设 `CheckForOverflowUnderflow`，两个程序集的默认算术上下文同为 unchecked，故跨程序集搬迁不改变 `checked`/未 `checked` 表达式的溢出行为。

`persisted`（问题 2）：原 `normal > 0 || newLevel != settlementLevel || normalizedMaximum`，其中 `settlementLevel` 是 `payload.PreviousLevel`（`byte`）。新 `SplitExperience` 内 `newLevel != settlementLevel` 的两侧类型与提升完全相同（`int` vs `byte` → `byte` 提升为 `int`），`normal`、`normalizedMaximum` 同源。算的是同一件事。

拆两个方法的理由（问题 3）：成立。`SqliteDungeonPersistentEffectRecoveryRepository.cs:171-186` 显示账号的 `SELECT honor_exp,growth_capsule_exp` 与 `throw new InvalidDataException("Dungeon experience account is unavailable.")` 确实只在 `if (honor > 0)` 之内。合并为单一方法会迫使调用方无条件读账号，把「账号缺失」变成今天不失败的路径上的失败。

调用时序（验收条件 5）：三处的语句次序、事务边界、提交点均未动。地下城侧 `SplitExperience` 落在账号读取之前、`AccumulateHonour` 落在账号 UPDATE 之前，与原计算位置相同。

### 阻断项

- **QR1-B1｜CK-006 与 CK-007 记录的输出不可能来自契约所定义的命令。**
  - 位置：本 Task 文件 `## Execution Result 1` 的检查表（CK-006「通过: 34」、CK-007「通过: 32」），对照 `## Task Unit Contract` 的 Required checks 表。
  - CK-006 命令为 `--filter FullyQualifiedName~Characters`。`Server/tests/Dfo.Server.Application.Tests/Characters/` 下有 7 个类同处 `Dfo.Server.Application.Tests.Characters` 命名空间，FQN 均含 `Characters`：`CharacterEntryInventoryTests`（13 Fact + 1 MemberData Theory）、`CharacterEntryStateTests`（1）、`CharacterLifecycleServiceTests`（17）、`EquipmentEligibilityCharacterFactsTests`（1）、`SelectCharacterEntryLifecycleServiceTests`（6）、`SupportCreatureMarkerProjectionTests`（3 Fact + 2 InlineData）。该过滤器至少匹配 77 个用例，不可能是 34。而 34 恰好等于 `CharacterEntryRuleServiceTests.cs` 自身的 17 Fact + 17 InlineData，即实际跑的是一个更窄的过滤器。
  - CK-007 命令为 `--filter FullyQualifiedName~EntryLifecycleRecovery`，只匹配 `SqliteEntryLifecycleRecoveryTests` 的 10 个用例；记录的 32 = 10 + `SqliteCharacterEntryStateRepositoryTests` 的 22（5 Fact + 17 InlineData）。Execution Result 已自陈「合并跑」，属更宽的一次运行。
  - 影响：Required evidence 要求「三条检查的实际输出」，其中两条不是所记命令的输出。实质风险为零——CK-008 全量 996/2/0 严格覆盖二者，且 996 = 944 + 4 + 14 + 34 与「本单元新增 34 条」自洽——但按此记录复跑 CK-006 会得到不同数字，未来审计会误读为回归。
  - 要求的整改（二选一，不需改动任何生产代码或测试）：按契约原样复跑 CK-006 与 CK-007 并记录真实输出；或追加一条 Contract Amendment，把两条检查的过滤器改成实际执行的范围并说明理由。

### 非阻断发现

- **QR1-N1｜`Server/src/Dfo.Server.Infrastructure/Characters/SqliteExpertSkillContractReconciliationRepository.cs:60-61`**：`checked((byte))` 与 `checked((short))` 现在对每一行 `character_skills` 执行，原实现只对通过全部过滤、确定要下调的行执行。若存在越界的 `page_index`/`slot`，原来会被静默跳过，现在会抛 `OverflowException`。实践上不可达（该两列由 `byte`/`short` 域值写入），但确是一次容忍度收窄。
- **QR1-N2｜同文件 `:41`**：时钟由「查询前取 `now`」改为「读完后把 `DateTimeOffset.UtcNow` 传进 `HasRunOut`」。秒级比较下的亚秒位移，且原本就是竞态，无测试敏感性。执行者已在自认薄弱处记录时钟仍在持久侧。
- **QR1-N3｜同文件 `:31-38`**：premium 行不再短路，全部读入。结果恒等，仅多读若干行。
- **QR1-N4｜`Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs:39-46`**：`DungeonEffectKinds` 是常量宿主而非规则契约，A1 的措辞是「仅限新增规则契约文件」。它落在新增契约文件之内且 Execution Result 已披露，判为在 A1 精神之内；严格读 A1 时属边缘。
- **QR1-N5｜`SqliteDungeonPersistentEffectRecoveryRepository.cs:19-24`**：未对新增的 `rules` 做 `ArgumentNullException.ThrowIfNull`，而 `SqliteCharacterEntryStateRepository.cs:20` 为 `goldLimits` 加了。两处各自沿用了本文件原有风格（该文件对 `progression` 本来也没有守卫），不构成缺陷。
- **QR1-N6｜自认薄弱处「编排仍在仓储里」**：判断恰当。plan `### T-003` 步骤 2 的原文即「持久侧只留读、写与状态推进」，留在仓储里的正是读、写与状态推进的编排；本单元未因此欠 plan 的账。把编排上提确属 TASK-04 会话化的形状，现在做会做两遍。
- **QR1-N7｜TDD 先后不可验证**：共同约束禁止任务级提交，工作树只有一个未提交状态，无法从中证明规则用例先于实现写成。非缺陷，记录为本轮不可判定项。

### 已核清、无发现的项

- 边界：本单元未触碰 `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryStateServices.cs`（BD-005，属 TASK-04），该文件未出现在 `git status` 的修改列表中。工作树中另有 `Abstractions/Inventory/IItemStore.cs`、`Infrastructure/Inventory/SqliteItemStore.cs`、`Abstractions/Transactions/DomainStores.cs`、`Infrastructure/Persistence/`、`Inventory/SqliteItemStoreTests.cs`、`Transactions/OneDatabaseOneParticipantTests.cs` 的改动，均属同一未提交工作树里的前序单元；全仓检索三个规则接口与 `DungeonEffectKinds` 的引用，只出现在本单元交付表所列的 8 个文件中，未外溢。
- 既有测试的机械调整：四个文件只补构造参数与一行 `using`，未改动任何断言或语义。`SelectCharacterPayloadCodecTests.cs:557-561` 的反射构造改动为 4 行，using 段次序未被重排，与自陈一致。`SqliteCharacterEntryStateRepositoryTests.cs` 与 `SqliteEntryLifecycleRecoveryTests.cs` 中的大段新增属 TASK-02 的表征用例（其注释自陈「Pinned before the rule is moved」），本单元只是为其补上了新构造参数。
- 依赖前提：`Dfo.Server.Infrastructure.Tests.csproj` 已含对 `Dfo.Server.Application` 的 ProjectReference，`Dfo.Server.Application.csproj` 已含 `InternalsVisibleTo Dfo.Server.Infrastructure.Tests`，两者均先于本单元存在，未新增工程文件改动。
- 残留核对：`ResolveGoldUpgradeLevel`、`current < 999`、`settlementLevel`、`record.Scope != 3`、`free > maximum`、`LuckyStarKind`、`ExperienceKind` 在 `Server/src/Dfo.Server.Infrastructure/` 下零命中，与自陈一致。
- 新增规则用例确为无数据库：`CharacterEntryRuleServiceTests.cs` 全文只构造规则服务与纯数据，未出现任何连接、路径或 SQL。
- 编码：三个新增 `.cs` 均为无 BOM UTF-8、全 CRLF。
## Execution Result 2 (post-verdict)

首评判 `changes_required`：1 项阻断、7 项非阻断。处置如下。

### 阻断项

| findings | 处置 |
|---|---|
| QR1-B1 记录的 CK-006 与 CK-007 输出不可能来自契约定义的命令。CK-006 的 `~Characters` 会匹配该命名空间下全部 7 个类（≥77 条），而我记了 34；CK-007 的 `~EntryLifecycleRecovery` 只匹配 10 条，而我记了 32 | 已按契约原文的过滤器逐条复跑，记录真实输出（见下）。不追加 Amendment 改写检查定义——检查是契约的一部分，该改的是记述，不是契约 |

评审的定性完全正确：我当时跑的是更窄的过滤器（CK-006 实际用了 `~CharacterEntryRuleService`），CK-007 则把两个类合并跑了并自陈「合并跑」，但仍把合并后的 32 写在了 CK-007 那一行。危害正如评审所言不在当下而在将来——后人按记录复跑会得到不同数字，并把它读成回归。

### 已修正的行为差异

| findings | 修改 |
|---|---|
| N1 `checked((byte))` / `checked((short))` 现在对每一行 `character_skills` 执行，原实现只对确定要下调的行执行。越界的 `page_index` / `slot` 原本被静默跳过，现在会抛 `OverflowException` | `CapsFor` 的入参由 `(byte Page, short Slot, ...)` 改为 `(int Page, int Slot, ...)`，窄化转换移到确定要下调、构造 `ExpertSkillCap` 的那一刻。仓储读行时不再转换。行为回到与原实现逐字一致。契约上加了注释说明为何入参不预先窄化 |

这是评审在七处逐行核对中找到的**唯一一处真实行为差异**，且是我外迁时顺手做的「看起来更整洁」的改动引入的——把类型收窄提前到读取处，看着自然，实际上把一条静默跳过的路径变成了抛出。记在这里作为提醒：行为保持型外迁中，任何「顺手改进」都要单独证明。

### 记录在案、不予修改的六项

| findings | 判断 |
|---|---|
| N2 时钟由「查询前取」改为「读完后取」，亚秒位移 | 保持。原本就是竞态：契约是否过期取决于取时钟的那一瞬，位移不改变任何可判定的行为 |
| N3 premium 不再短路，多读若干行 | 保持。评审已独立确认结果恒等，SELECT 无副作用 |
| N4 `DungeonEffectKinds` 是常量宿主而非规则契约，严格读 A1 时属边缘 | 保持。评审判为「在精神之内」。这些名字原本是持久实现的私有常量，而 `CanCarryOut` 必须认得它们、仓储也必须据以分支，放在两者都能看见的契约文件里是唯一不重复的位置 |
| N5 未对 `rules` 加 null 守卫 | 保持。沿用该文件原有风格（`progression` 本来也没有）。统一加守卫应是单独一刀，不夹带在行为保持型外迁里 |
| N6 编排仍在仓储里 | 评审独立判定我的处理恰当，且本单元未欠 plan 的账。维持原判断：编排上提与 TASK-04 的会话化一起做 |
| N7 TDD 先后不可验证 | 属「全程不产生任务级提交」这一共同约束的必然结果，与 TASK-01 同因 |

### 复验（按契约原文的检查命令）

| ID | 命令 | 真实输出 |
|---|---|---|
| CK-006 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~Characters` | `已通过! - 失败: 0，通过: 95` |
| CK-007 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~EntryLifecycleRecovery` | `已通过! - 失败: 0，通过: 10` |
| CK-008 | `dotnet test Server/86JP.Server.slnx -m:1` | `996 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 487、Host 4、Infrastructure 199+2 跳过、Networking 46、Protocol 200） |

`dotnet build Server/86JP.Server.slnx`：0 警告 0 错误。

供参照，本单元新增的那一组无数据库规则用例单独跑（不属任何 CK，仅作说明）：
`--filter FullyQualifiedName~CharacterEntryRuleService` → `已通过! - 失败: 0，通过: 34`。

## Executor Self-check 2

| 首评发现 | 状态 | 证据 |
|---|---|---|
| QR1-B1（阻断）CK-006 / CK-007 记述名实不符 | 已闭合 | 上表三条均为契约原文命令的真实输出；34 那个数字改列为附注并注明不属任何 CK |
| N1 窄化转换的容忍度收窄 | 已修 | `CapsFor` 入参改为 `int`，窄化移至构造 `ExpertSkillCap` 处；CK-006、CK-007、CK-008 全绿 |
| N2、N3、N5、N6、N7 | 记录未改 | 见上表逐条理由 |
| N4 `DungeonEffectKinds` 的归属 | 记录未改 | 评审判为在 A1 精神之内；理由已补记 |

五条验收条件的证据与 Self-check 1 相同。N1 的修正只影响「哪些行会被窄化转换」，不影响任何验收条件所断言的行为；三条检查的通过数在修正前后一致。
## Independent Quick Review 2

- Verdict: `passed`
- Reviewed contract revision: 1，含 Contract Amendment A1（本轮未新增修订）
- Reviewed artifacts: `## Independent Quick Review 1`、`## Execution Result 2 (post-verdict)`、`## Executor Self-check 2`；对照 `## Task Unit Contract` 的 Required checks 表与共同约束
- 本轮评审范围：只判首评发现是否闭合、补救本身是否引入新问题。首评已核清的七处行为等价、边界、既有测试的机械调整未再复核（补救未动到它们，见下「补救范围」）
- Owned diff identity（工作树仍未提交）：与 Independent Quick Review 1 所列相同；本轮补救另改动其中三个文件
- Evidence inspected（未运行任何测试）：`git status --porcelain`、`git diff -- Server/`（含 `SqliteExpertSkillContractReconciliationRepository.cs` 全量 hunk）、`ICharacterEntryRules.cs` 与 `CharacterEntryRuleServices.cs` 全文、`CharacterEntryRuleServiceTests.cs` 全文、`Server/tests/Dfo.Server.Application.Tests/` 与 `Dfo.Server.Infrastructure.Tests/` 的用例静态枚举、`git log`、工作树文件 mtime、三个改动文件的行尾与 BOM

### QR1-B1（阻断项）：已闭合

- 三行记述现与契约 Required checks 表的命令逐字一致（本文件 `226-228` 对 `30-32`）。
- **CK-006 = 95 已独立静态复核并精确对上**：`Dfo.Server.Application.Tests/Characters/` 下 7 个类共 77 条（Fact 58 + InlineData 19），加 `CharacterEntryInventoryTests.FailingSources` 这条 `MemberData` 展开的 3 条 = 80；再加该项目其余目录中 FQN 含 `characters`（vstest `~` 为不区分大小写的 contains）的 15 条测试方法 —— `Accounts/AccountEntryStateTests.cs:71`（`RejectsCharacterSpecific…` 中的 "CharacterS" 命中）、`Inventory/ItemUsabilityDomainServiceTests.cs:243`、`Sessions/PvpLobbyEntryTests.cs:40,56`、`Sessions/SessionAdjacentEntryTests.cs:31,59,79,131`、`Sessions/SessionGenerationOwnershipTests.cs:202`、`Users/ActiveCharacterAccessTests.cs:38,85,97`、`World/WorldContentRuleTests.cs` 四条 —— 合计 80 + 15 = **95**，与记录一致。
- **CK-007 = 10 已独立静态复核**：`Dfo.Server.Infrastructure.Tests` 中 FQN 含 `EntryLifecycleRecovery` 的只有 `Characters/SqliteEntryLifecycleRecoveryTests`，该类 Fact + InlineData 恰为 10。
- CK-008 仍为 996 / 2 跳过 / 0 失败：本轮未增删任何用例（见「补救范围」），与 Execution Result 1 的 944 + 4 + 14 + 34 自洽。
- 34 已降格为附注并注明「不属任何 CK」（本文件 `232-233`），不再挂在 CK-006 行上。
- **对「拒绝追加 Amendment」这一选择的判断：恰当。** 两条检查本身没有缺陷，`~Characters` 是一张更宽的安全网；把过滤器改窄以迁就已经跑过的那次，是让契约去适配证据，方向相反。且 CK-006 的 Expected result 是「三处的无数据库规则用例通过」，95 条是包含那 34 条的超集，附注里的窄过滤器一跑又单独显示了 34，两者合起来完整支撑该 Expected result。

### QR1-N1 的修正：已恢复原行为，逐字等价

- 原实现（本次 diff 中被删的那行）在 `changes.Add((checked((byte)reader.GetInt32(0)), checked((short)reader.GetInt32(1)), maximum <= 0 ? null : maximum))` 处窄化，即**四道过滤全部通过、确定要下调之后**。
- 现实现 `CharacterEntryRuleServices.cs:95-96` 在 `new ExpertSkillCap(checked((byte)page), checked((short)slot), maximum <= 0 ? null : maximum)` 处窄化，同样在四道过滤之后，`checked` 形式与目标类型逐字相同。窄化位置回到原处。
- 未因元组类型改动引入提升差异：`page`/`slot` 在两版中于窄化点都是 `int`（原为 `reader.GetInt32` 的返回值，现为 `(int Page, int Slot, …)` 的元素）；`level` 两版均为 `int`；`free`（`byte`）与 `maximum`（`int`）的 `free > maximum`、`maximum = free` 两处提升与原式相同。`checked` 是表达式级的，跨程序集不受默认算术上下文影响（首评已核 `Directory.Build.props` 未设 `CheckForOverflowUnderflow`）。
- 调用方仍对得上：`ExpertSkillCap(byte Page, short Slot, int? Level)` 未改，故 `SqliteExpertSkillContractReconciliationRepository.cs:70-72` 的 `AddWithValue("@page", change.Page)` / `("@slot", change.Slot)` 绑定的 CLR 类型与原实现一致，`return changes.Count > 0`（`:77`）亦不变。全仓 `CapsFor` 只有这一个生产调用点。
- 测试无须改动亦成立：`CharacterEntryRuleServiceTests.cs:73,100,111,119` 的元组字面量是 `int` 常量，集合表达式在新旧两种签名下都能目标类型化，故不改测试即可编译 —— 与「本轮未触碰测试文件」的 mtime 证据一致。

### 补救范围：未扩大

首评判定（`12:43`）之后，整个工作树中被改动的文件只有四个，其余文件 mtime 均早于该时点：

- `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs`（`12:56:50`）：`CapsFor` 入参类型 + `:32-36` 的 `<remarks>`
- `Server/src/Dfo.Server.Application/Characters/CharacterEntryRuleServices.cs`（`12:56:50`）：窄化下移
- `Server/src/Dfo.Server.Infrastructure/Characters/SqliteExpertSkillContractReconciliationRepository.cs`（`12:56:50`）：读行时不再转换
- 本 Task 文件（`12:59:08`）

无测试文件、无其他生产文件、`Tool/` 与 `.worktrees/legacy-reference/` 未涉及。`git log` 最新提交为 `712bdbc`（`08:12`，早于本单元全部工作），暂存区为空 —— 「全程不产生任务级提交」仍然成立。三个改动文件均为无 BOM UTF-8、全 CRLF（`117/117`、`207/207`、`79/79`）。

### 六项「记录未改」的复核

均成立。逐条：

- **N2（时钟位移）保持：恰当。** 原为 `BeginTransaction` 后立即取 `now`，现为 premium 读完后取。二者都是这次判定自身的「此刻」，`endTime > seconds` 在两次取值之间过期的契约本就取决于取时钟的那一瞬，位移不改变任何可判定的行为，也无测试敏感性。（若要严格的位置等价，把 `var now = DateTimeOffset.UtcNow;` 提到查询之前是零成本的；但不做也站得住，不构成发现。）
- **N5（缺 null 守卫）保持：恰当。** 已核 `SqliteDungeonPersistentEffectRecoveryRepository.cs:16-25`，该构造函数对本来就有的 `progression` 同样没有守卫，新加的 `rules` 与之一致；`SqliteCharacterEntryStateRepository.cs:19-21` 的守卫是该文件自己的既有风格。三个仓储均由 `InfrastructureModule` 以 `GetRequiredService` 注入，null 不可达。统一加守卫确实应是单独一刀，夹带进行为保持型外迁反而会让 diff 混入非等价改动。
- N3、N4、N6、N7 的理由与首评的独立判断一致，无新增信息推翻。

### 本轮非阻断发现

- **QR2-N1｜`SqliteExpertSkillContractReconciliationRepository.cs:60-61`**：`reader.GetInt32(0)`/`GetInt32(1)` 现对每一行执行，`GetInt32(3)` 也不再等到定义过滤之后。这与 N1 同族但弱得多：只有当 `page_index`/`slot`/`level` 存的是 NULL 或非整数时才会以 `InvalidCastException` 表现，而 `character_skills`（建表见 `SqliteCharacterEntryStateRepository.cs:919`）的这些列全部由 `SqliteCharacterRepository.cs:269` 的 INSERT 以整数写入，本服务端写不出这种行。且该形状在首评时即已存在，非本轮补救引入。判为不需改动，记录供 change review 取舍。
- **QR2-N2｜恢复的容忍度未被测试钉住**：`CapsFor` 的测试元组字面量是 `int` 常量，在旧签名下同样能编译，因此没有任何用例会因把窄化重新提前到读取处而变红。若要钉住，一条「Page 越出 `byte` 范围但该行并不下调 → 返回空而非抛出」的用例即可。契约 Required methods 只要求「三处各补一组不触及数据库的规则用例」（专家契约一组共 7 条，已满足），故不构成缺陷。

### 结论

首评的 1 项阻断已闭合且证据可独立复核（95 与 10 两个数字均已由静态枚举精确对上），唯一一处真实行为差异 N1 的修正与原实现逐字等价，补救未越出三个文件，六项「记录未改」的理由成立。本轮无阻断项。
