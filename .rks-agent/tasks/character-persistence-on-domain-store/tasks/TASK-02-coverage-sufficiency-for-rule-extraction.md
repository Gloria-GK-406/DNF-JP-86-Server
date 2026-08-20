# TASK-02 — 判定并补齐待外迁行为的覆盖

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 判定三处待外迁规则的现有覆盖是否钉住了当前行为，对不足处补齐用例。本单元**不改任何生产代码**。
- Referenced spec requirements: RQ-008
- Referenced plan sections: `plan.md` 的 `### T-002`；`## Risks and Rollback` 第四条
- Owned boundary: BD-012
- Permitted files or interfaces:
  - `Server/tests/`
- Dependencies: TASK-01
- Required dependency outputs: TASK-01 的载体协议不被本单元消费；依赖仅用于保持线性次序与全量基线的连续性。
- Acceptance conditions:
  1. 三处规则各有明确结论：过期专家契约的技能上限回收、地下城持久效果的恢复与经验发放、金币上限等级的解析。
  2. 判定依据是「若把该规则改错，是否有用例会红」，且判定为充分者须指名那条用例。
  3. 判定为不足者已补齐用例，断言**当前**行为。
  4. 本单元的差异只含测试文件，生产代码零改动。
  5. 全量测试通过数不低于 TASK-01 结束时。
- Task-specific authorization: 属根 TaskContract `task` 授权范围内。本单元禁止修改 `Server/src/` 下任何文件。
- Required methods: 表征测试（characterization test）。补齐的用例断言现状而非期望，不得顺手修正任何可疑行为。
- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-004 | Non-command: 逐处记录「改错该规则会红的用例」，三处均有指名 | 三处结论齐备，不足处已补齐 |
| CK-005 | `dotnet test Server/86JP.Server.slnx -m:1` | 全量绿，通过数不低于 TASK-01 结束时 |

- Required evidence: 三处的覆盖充分性结论与依据；补齐用例的清单；证明生产代码零改动的差异统计。

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
## Execution Result 1

### 判定方法

本单元的验收条件 2 要求判定依据是「若把该规则改错，是否有用例会红」。这条不靠读代码判断，而是**逐处注入错误再跑**——目视判断在本单元里两次给出了错误结论，见下。

每次注入只改一个语义点，跑完即从副本恢复，并核对恢复。全部注入均未进入任何最终产物。

### 三处规则的逐处结论

| 规则 | 注入的错误 | 结果 | 结论 |
|---|---|---|---|
| 金币上限等级：取两个上限中较小者，再落入五档 | `Math.Min(carry, auction)` → `carry` | **2 条转红** | 补齐后充分 |
| 专家契约过期回收：免费基线可抬高上限 | `free > maximum` → `free < maximum` | **1 条转红** | 原有覆盖已充分 |
| 地下城死信校验：版本、账号、范围、目标、种类 | `record.Scope != 3` → `!= 33` | **4 条转红** | 原有覆盖已充分 |
| 地下城幸运星上限 999 | `current < 999` → `current < 1000` | **1 条转红** | 原有覆盖已充分 |
| 地下城经验/荣誉拆分的 86 级边界 | `settlementLevel >= 86` → `> 86` | **全绿** | 原有覆盖**不足**，已补齐 |

指名的用例：

- 专家契约 → `SqliteEntryLifecycleRecoveryTests.ExpertContractReconciliation_ActiveNoopsExpiredCapsAndWrongOwnerRollsBack`
- 死信校验 → `DungeonRecovery_WrongPlayerScopeOrTargetDeadLettersWithoutApplyingWalletMutation` 等 4 条
- 幸运星上限 → `DungeonLuckyStarRecovery_AtCapCommitsGrantedFalseWithoutChangingWallet`
- 金币上限 → 本单元新增（见下）
- 荣誉拆分 → 本单元新增（见下）

### 两次被目视判断漏掉的缺口

启动本单元时，我依据「地下城恢复有 6 条用例」目测判定其覆盖充分。证伪推翻了这个判断两次：

1. **荣誉拆分的 86 级边界无人守**。既有的经验用例只走**跨入**最后一级的那一侧（85 级升 86），从未有过角色本就在 86 级的情形，于是 `>= 86` 与 `> 86` 在既有用例下完全等价。这是三处规则里最复杂的一处，也正是 TASK-03 要搬的那部分。
2. **`state IN (0,3)` 的重取路径无人守**（`state IN (0,3)` → `state IN (0)` 全绿）。该谓词属于认领与租约的 SQL，按 plan `### T-003` 的措辞「持久侧只留读、写与状态推进」它**不在**本次外迁范围内，故不构成 TASK-03 的前置阻碍。记录在案，留给 change review 判断是否值得单独补。

### 新增覆盖

| 文件 | 新增 | 钉住的行为 |
|---|---|---|
| `SqliteCharacterEntryStateRepositoryTests.cs` | `CharacterEntryState_GoldUpgradeLevelFollowsTheLowerOfTheTwoLimits`（9 组 InlineData） | 五档全部、每档边界两侧、以及两个上限各自作为较小者的情形。原有覆盖只有 `(6亿, 6亿) → 2` 一个点：只钉住一档，且两值相等使 `Math.Min` 完全不可见 |
| 同上 | `CharacterEntryState_GoldUpgradeLevelIsLowestWhenNoLimitsAreRecorded` | 无上限行时读作最低档而非失败。规则搬走时，「它被喂什么」也一并搬，故一并钉住 |
| `SqliteEntryLifecycleRecoveryTests.cs` | `DungeonExperienceRecovery_AtTheLastLevelTurnsTheWholeGainIntoHonour` | 角色已在 86 级时全额转荣誉；含 `result_json` 逐字断言 |
| 同上 | `DungeonExperienceRecovery_BelowTheLastLevelProducesNoHonourAndLeavesTheAccountAlone` | 未触及上限时不产生荣誉，账号一行不动 |

两条地下城用例共用新的 `SeedExperienceOutboxAsync` 夹具，与既有的 `SeedLuckyStarOutboxAsync` 同形。

新增用例的预期值全部**先手算再落笔**，两条地下城用例首跑即通过，与手算逐值吻合（荣誉 25、总荣誉 30、胶囊 27、`newExp` 85000、`normalizedMaxLevelExp` true）。这一点重要：表征测试若照抄运行结果，就只是把当前输出复述一遍；先算后验才说明规则确被读懂。

### 补齐后的复证伪

`settlementLevel >= 86` → `> 86` 重跑：

```
Dfo.Server.Infrastructure.Tests.Characters.SqliteEntryLifecycleRecoveryTests.DungeonExperienceRecovery_AtTheLastLevelTurnsTheWholeGainIntoHonour [FAIL]
失败!  - 失败:     1，通过:     9，总计:    10
```

缺口已闭合。恢复后复跑 `10 通过 / 0 失败`。

### 检查

| ID | 结果 |
|---|---|
| CK-004 | 三处规则均有指名用例；两处判定为不足的缺口中，属本次外迁范围的一处已补齐，另一处（`state IN (0,3)`）不属外迁范围，已记录 |
| CK-005 | 全量 `960 通过 / 2 跳过 / 0 失败` = 基线 944 + TASK-01 的 4 + 本单元的 12。不低于 TASK-01 结束时的 948 |

### 生产代码零改动

`git diff --stat -- Server/src/` 只显示 TASK-01 拥有的两个文件，未跟踪的新增亦只有 TASK-01 的两处。本单元的差异全部落在 `Server/tests/`，符合 Task-specific authorization「禁止修改 `Server/src/` 下任何文件」。

## Executor Self-check 1

| 验收条件 | 证据 |
|---|---|
| 1 三处规则各有明确结论 | 上表五行（地下城一处拆为三条规则分别证伪） |
| 2 判定依据是「改错会红」，充分者指名用例 | 全部结论由注入证伪得出，非目视；四处指名了具体用例 |
| 3 不足者已补齐，断言当前行为 | 金币上限与荣誉拆分两处共补 12 条，预期值先手算后验证 |
| 4 差异只含测试文件 | `git diff --stat -- Server/src/` 只含 TASK-01 的改动 |
| 5 全量不低于 TASK-01 结束时 | 960 ≥ 948 |

### 自认的薄弱处

- 我最初对地下城覆盖的目视判定是错的，两次。这直接印证了本单元存在的理由，但也说明**其余判定为「充分」的三处同样是靠单点注入得出的**：单点注入证明该点被守住，不证明整条规则被守住。若 TASK-03 外迁时改动的是未被注入过的分支，仍可能静默变形。TASK-03 的做法应是外迁前后跑同一组用例，而不是依赖本单元的充分性结论。
- `state IN (0,3)` 的缺口按 plan 措辞判为不在外迁范围内。这一判断依赖对「状态推进」四字的读法；若 change review 认为认领谓词也属外迁范围，本结论需重议。
## Independent Quick Review 1 — QR-T02-R1

- 判定：`passed`
- 评审的契约修订：Contract revision 1（无 Amendment）
- 被评审的 owned diff：`git diff -- Server/tests/` 中属本单元的两个文件——`Characters/SqliteCharacterEntryStateRepositoryTests.cs`（+78/-0）与 `Characters/SqliteEntryLifecycleRecoveryTests.cs`（+79/-0）。同一 diff 中的 `Inventory/SqliteItemStoreTests.cs`（+8/-3）属 TASK-01 的既有基线，不属本单元。
- 检视的证据：`git status --porcelain --untracked-files=all`、`git diff --stat -- Server/src/`、`git diff -- Server/tests/`、两个受改测试文件全文、`SqliteCharacterEntryStateRepository.cs:312-322,866-874`、`SqliteDungeonPersistentEffectRecoveryRepository.cs:1-240`、`SqliteExpertSkillContractReconciliationRepository.cs:60`、plan 的 `### T-002` 与 `### T-003`、TASK-01 的 Contract Amendment A1、`Server` 下 `.bak/.orig/*~` 搜索、行尾与 BOM 抽检。按指示未跑任何测试。

### 零生产改动与注入恢复（通过，独立核对）

`Server/src/` 的差异恰为 TASK-01 的六件：受版本控制的 `IItemStore.cs`、`SqliteItemStore.cs`，新增的 `Transactions/DomainStores.cs`、`Persistence/SqliteDatabaseParticipant.cs`（后者由 TASK-01 的 Contract Amendment A1 授权），测试侧 `OneDatabaseOneParticipantTests.cs`、`SqliteItemStoreTests.cs`。本单元未触碰 `Server/src/` 任何文件，Task-specific authorization 成立。

六个注入点全部原样，且证明力强于目视——承载它们的三个文件根本不出现在 `git status` 中，与 HEAD 逐字节相同：`SqliteCharacterEntryStateRepository.cs:868` 为 `Math.Min(carry, auction)`；`SqliteExpertSkillContractReconciliationRepository.cs:60` 为 `free > maximum`；`SqliteDungeonPersistentEffectRecoveryRepository.cs` 的 `:42`、`:68` 为 `state IN (0,3)`/`state IN(0,3)`，`:79` 为 `record.Scope != 3`，`:104` 为 `current < 999`，`:168,173,174` 为 `settlementLevel >= 86`。工作树中亦无 `.bak`/`.orig`/`*~` 残留，注入用的副本已清干净。两个受改测试文件均 CRLF、无 BOM。全程无任务级提交。

### 新增用例确为表征测试（通过，逐值复算）

未采信执行者的手算，逐条对现行实现重算：

- 9 组金币 InlineData 全部与 `ResolveGoldUpgradeLevel`（`:866-874`）一致，包括 `(900_000_000, 499_999_999) → 0` 与 `(900_000_000, 700_000_000) → 3` 这两条使 `Math.Min` 首次可见的行。
- `GoldUpgradeLevelIsLowestWhenNoLimitsAreRecorded` 走 `ReadPairOrDefaultAsync` 的 `firstDefault/secondDefault = 0,0`（`:319-321`），落 0 档，与断言一致。
- `AtTheLastLevelTurnsTheWholeGainIntoHonour`：`ProgressionPolicy` 的 `LevelThresholds[84]=85000`、rate=100。honor=25（`settlementLevel>=86` 分支）、normal=0、`newExp` 被 `:173` 归一为 85000、`normalizedMaxLevelExp` 因 84000≠85000 为 true、`newLevel` 停在 86、totalHonor=5+25=30、capsuleGain=25 故 totalCapsule=27、`persisted` 由 `normalizedMaximum` 单独撑起为 true。`result_json` 的十二个字段名与次序与 `:213-221` 的匿名类型逐字吻合。
- `BelowTheLastLevelProducesNoHonourAndLeavesTheAccountAlone`：saturated=80025 ≤ maxEntry=85000 故 honor=0，`:181` 与 `:200` 两个 `honor > 0` 均不进入，账号确实一行不动；`newExp`=80025 不触发 `:177-178` 的升级循环。

无一条断言执行者认为「应该」的行为；两条地下城用例断言的都是现状，含 `result_json` 逐字。既有用例语义未被改动（本单元的 diff 全是新增行）。

### 判定方法的自洽性（通过）

四处「注入→转红条数」与我读到的用例体逐条对得上，非事后编造：

- `record.Scope != 3` → `!= 33` 会把所有合法记录打成死信，恰好 4 条断言提交结果的用例转红（`CommitsMutationAndOutboxAtomically`、`AtCapCommitsGrantedFalse`、`ReclaimsExpiredExperience...`、`UsesDurableSettlementBaseline...`）；而 `WrongPlayerScopeOrTarget...` 与 `OverlappingMalformedRecord...` 本就期望死信，确实不转红。与「4 条」吻合。
- `current < 999` → `< 1000`：钱包值仍是 999（`Math.Min(999, 1000)`），唯一转红的是 `AtCap...` 的 `result_json` 逐字断言。与「1 条」吻合，且说明该用例的钉法本身选对了。
- `free > maximum` → `free < maximum`：`ExpertContractReconciliation_...` 中 skill 103 的 `OrdinaryMaximumLevel: 0` 配 free 基线 1，翻转后该技能被删，`:195` 断言转红。与「1 条」吻合。
- `settlementLevel >= 86` → `> 86` 补齐后 `失败:1，通过:9，总计:10`：`SqliteEntryLifecycleRecoveryTests` 恰有 10 条（8 既有 + 2 新增），转红的只可能是新增的 `AtTheLastLevel...`（既有的 `UsesDurableSettlementBaseline...` 的 previousLevel=85，两种写法下都不进该分支）。数字与指名自洽，缺口确已闭合。
- `Math.Min(carry, auction)` → `carry` 后「2 条转红」：9 行 InlineData 中恰有 2 行的 carry 不是较小者，与之吻合。

指名的三条既有用例均存在于 `SqliteEntryLifecycleRecoveryTests.cs:81`、`:123`、`:160`，且确实覆盖所称分支。

### 两处缺口的处置

荣誉拆分 86 级边界：判为不足并补齐成立，见上节。

`state IN (0,3)` 的重取路径：**独立判断认为执行者的读法成立**，且理由可以更硬。plan `### T-003` 步骤 2 要外迁的是「可提交判定与经验发放」——「可提交判定」在实现中对应 `:78-80` 那段版本/账号/范围/目标/种类的校验（决定死信还是施用），而 `:42` 是读、`:64-69` 的认领 UPDATE 是把行从 0/3/租约过期推到 1 并附租约，正属「持久侧只留读、写与状态推进」中的读与状态推进；它是 outbox 自己的并发控制，脱离 SQL 无法表达，搬进业务层反而要把 SQL 一起搬走。另有一条执行者未提到的独立支撑：`state=3` 在 `Server/src` 全域**没有任何写入方**（`dungeon_persistent_effect_outbox` 仅由该文件访问，写入的状态只有 1/2/4），故未被守住的那一半谓词在当前生产路径上不可达；`state=1 AND lease_expires_at<=@now` 那一支则已由 `ReclaimsExpiredExperienceAndDeadLettersBadRecordIndependently` 覆盖。不阻断本单元，转下述非阻断第 2 条。

### 自认薄弱处的处置

执行者的自认诚实且完整，但并不削弱验收条件 1、2：条件 2 的判据措辞就是单点的「若把该规则改错，是否有用例会红」，单点注入正是它要求的东西，不是它的打折。因此不据此要求本单元补更多覆盖。执行者自己给出的推论（TASK-03 应外迁前后跑同一组用例，而非依赖本单元的充分性结论）是正确的处置，请 TASK-03 落实。

### 阻断发现

无。

### 非阻断发现

| ID | 文件与行 | 说明 |
|---|---|---|
| QR-1 | `Server/tests/Dfo.Server.Infrastructure.Tests/Characters/SqliteCharacterEntryStateRepositoryTests.cs:136-138` 对 `:140-148` | 注释与 Execution Result 均称「每档边界两侧」，实际只有 5 亿与 6 亿两档做到了两侧（499_999_999/500_000_000、599_999_999/600_000_000）；7 亿与 8 亿两档只有「恰在门槛」一侧，缺 699_999_999 与 799_999_999。故 `>= 700_000_000` 被下调（如改成 `>= 650_000_000`）不会转红。补两行 InlineData 即闭合；措辞亦应与实际覆盖对齐。 |
| QR-2 | `Server/src/Dfo.Server.Infrastructure/Characters/SqliteDungeonPersistentEffectRecoveryRepository.cs:42,68` | 本轮认可其不属 T-003 外迁范围（理由见上节），但缺口本身仍在：`state=3` 一支无用例。当前无写入方使其不可达，一旦后续单元（如 T-006 的 schema 归口，或任何引入失败重试写回 3 的改动）让它可达，该谓词就是无人守的。建议在其可达的那一刻补一条，或明确记为「3 号状态目前无生产写入方」。 |
| QR-3 | 本文件 108 行 | CK-005 的数字自洽（960 = 944 + 4 + 12，新增 12 = 9 InlineData + 1 Fact + 2 Fact），按指示未复跑，采信执行者与控制器的一致记录。若最终评审要做新鲜验证，此处是唯一未由本轮独立复核的数字。 |

### 结论

契约、边界、方法、证据四项均未见可局部证明的缺陷：差异只落在两个测试文件，生产代码逐字节未动，注入全部恢复，新增 12 条经逐值复算确为断言现状的表征测试，五处「注入→转红」的条数与用例体互证。判定 `passed`。这只表示本单元可进入执行者的接受流程，不构成最终质量批准；QR-1 建议在 TASK-03 动手前顺手补上，QR-2 请随 T-003/T-006 的边界结论一并复看。

## Remediation 1 (post-verdict)

复评判 `passed`，无阻断。QR-1 属实且修正代价极小，予以闭合；另两条记录在案。

| findings | 处置 |
|---|---|
| QR-1 注释与记录称「每档边界两侧」名不副实：只有 5 亿、6 亿两档做到两侧，7 亿与 8 亿缺下侧，故这两档的门槛被下调不会转红 | 补 `[InlineData(699_999_999, 900_000_000, 2)]` 与 `[InlineData(799_999_999, 900_000_000, 3)]`。该类由 20 条增至 22 条。复证伪：把 `if (value >= 700_000_000) return 3;` 改为 `>= 690_000_000`，补之前全绿，补之后 `失败: 1，通过: 21`，缺口确已闭合。恢复后复跑 22 通过 |
| QR-2 `state=3` 一支仍无用例，当前靠「全域无写入方」而不可达 | 保持不修。复评独立给出了比原记述更硬的支撑：该表仅由一个文件访问，写入状态只有 1/2/4，故 `state=3` 今日不可达；另一支 `state=1 AND lease_expires_at<=@now` 已有覆盖。若后续单元让它可达，须补覆盖。已交由 change review |
| QR-3 CK-005 未由复评独立复跑 | 属实，是我在派发时明示「不要跑测试」所致。该数字自洽（960 = 944 + 4 + 12），并将在 TASK-07 的新鲜验证中重新取得 |

复验：`dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~CharacterEntryStateRepositoryTests` → `已通过! - 失败: 0，通过: 22`。全量待 TASK-03 一并复跑。
