# TASK-06 — 业务规则迁出持久化实现

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 把日界、领取幂等、叠加或新开格、按段查找空位四类判定迁至可脱离数据库检验的规则服务，持久化实现只保留写行；退役物品相关的旧存储成员与并行类型。
- Referenced spec requirements: RQ-006
- Referenced plan sections: `../plan.md` 的 `### T-006` 全节；BD-011、BD-012、BD-013、BD-014、BD-015、BD-016
- Owned boundary: BD-011, BD-012, BD-013, BD-014, BD-015, BD-016
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Application/Inventory/`（新增规则服务）
  - `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteEntryInventoryMutationRepository.cs`
  - `Server/src/Dfo.Server.Abstractions/Inventory/IInventoryEntryServices.cs`
  - `Server/src/Dfo.Server.Protocol/Game/TaggedIdReport/TaggedIdReportHandler.cs`
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`、`Server/src/Dfo.Server.Protocol/ProtocolModule.cs`
  - `Server/tests/`（新增规则用例）
- Dependencies: TASK-05
- Required dependency outputs: TASK-05 的覆盖结论与补齐后的用例——它们是本任务的等价性证据，其断言不得修改。
- Acceptance conditions:
  1. 四类判定在不涉及数据库的情况下被检验。
  2. 持久化实现中检索日界、幂等、叠加决策与按段查找为零命中；不再回调内容源。
  3. TASK-05 的用例在归属改变后原样通过，断言未被改写。
  4. 教学奖励改用迁出后的规则与存储抽象，其对外可观察结果不变。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。收窄：不得修改 TASK-05 用例的任何断言。
- Required methods: `test-driven-development`：规则服务的四类判定先有会红的脱库用例再实现。
- Required checks: CK-019、CK-020、CK-021、CK-022（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-019 的 RED 与 GREEN 证据；CK-020 的断言未改核对与通过结果；CK-021 的检索结果；CK-022 的全套结果。

## Execution Result 1

### 四类判定迁出

新增 `IItemArrivalRuleService` 与 `IDailyRefillRuleService`（契约在 `Abstractions/Inventory/IItemArrivalRules.cs`，实现在 `Application/Inventory/ItemArrivalRuleService.cs`，与既有的 `IItemSlotRuleService` 同一形状）。四类判定各自成为一个不碰任何存储的函数：

| 判定 | 迁到哪里 |
|---|---|
| 日界 | `DailyRefillRuleService.PeriodOf(DateTimeOffset)` |
| 领取幂等 | `DailyRefillRuleService.IsDue(claimedPeriod, period)` |
| 叠加或新开格 | `ItemArrivalRuleService.ForDailyRefill` / `.ForGrant` |
| 按段查找空位 | 同上（`ItemArrival` 三种结果之一即 `NoRoom`） |

发放数量 `CalculateGrant` 也一并迁入 `DailyRefillRuleService.GrantFor`，并从 `PvfEntryLifecycleContentSource` 删除——这满足「不再回调内容源」，同时消掉一份会漂移的重复。

持久化实现改为**读、问、写**三步：读出已记录的期与该道具的各堆、读出已占用的槽，把它们交给规则，然后照着规则给的 `ItemArrival` 写行。原来的 `IsClaimedAsync`（它自己判断「算不算领过」）换成 `ClaimedPeriodAsync`（只答记录的是哪一天）；`NextMainSlotAsync` 整个删除。

### 退役的旧存储成员与并行类型（BD-011、BD-012）

自 TASK-03 起已是生产死代码，此处退役：

- 成员：`ReadMainSlotAsync`、`ReadMainSlotsAsync`、`ApplyMainMoveAsync`、`ConsumeMainItemAsync`
- 类型：`MainInventoryOccupant`、`MainInventorySlotPair`、`ConsumedInventoryItem`、`MainInventoryMovePlan`、`MainInventoryMoveKind`
- 实现侧的 `MoveSlotAsync`、`WriteEntryCountAsync`、`DeleteEntryAsync`、`ReadOccupantAsync` 等只为它们存在的私有辅助，以及 `ParkedSlotIndex` 常量——**「槽位 -1 暂泊」这个构造至此从代码库中消失**
- 用例：`MainInventoryMoveTests.cs` 删除（它测的是已删除的方法）

`SqliteEntryInventoryMutationRepository` 现在只剩 `ApplyDailyRefillAsync`、`GrantItemAsync`、`ReadMainItemsAsync` 三个公开成员。

### 教学奖励改用存储抽象（BD-014 全部完成）

初版把这一半留白了，理由写的是「`TaggedIdReportHandler` 没有任何行为用例，改写无覆盖的入场路径风险太大」。**这个理由是错的**，评审指出得对：`ExitFlushHandlerTests` 里有三条，其中 `TaggedIdReport_AcknowledgesTheSlotAnItemActuallyReached` 端到端跑 `HandleAsync`，断言的正是改写会威胁到的那几件事——回执里的槽位、道具号、数量，以及发放确实落到了存储。更难堪的是，本任务**自己编辑过那个文件**（删掉了退役成员的桩），覆盖就在眼前。

决定性的顾虑既然不成立，就把它做完，而不是只改措辞：

- `IItemContainer` 新增 `ReadAllAsync`——放置决策要知道哪些槽被占、这件道具已有哪几堆，两者都不是「一个槽」的问题。它走存储，因此在用例内能看见本用例已写下的内容；`SnapshotAsync`（持久侧走入场加载器）不能用于这个目的。
- 新增事务型命令 `GrantTutorialRewardCommand` 及其处理器：逐件问 `IItemArrivalRuleService.ForGrant`，经容器写入。一步教学奖励的多件道具**一起落定**，而不是一件一个事务；也因此后一件的放置能看见前一件。
- `TaggedIdReportHandler` 改为分派该命令，不再持有 `IEntryInventoryMutationRepository` 与 `IItemPlacementDefinitionSource`。
- `GrantItemAsync` 与 `GrantedInventoryItem` 至此无生产调用方，一并退役（BD-011/BD-012）。`SqliteEntryInventoryMutationRepository` 现在只剩 `ApplyDailyRefillAsync` 与 `ReadMainItemsAsync` 两个公开成员。

三条既有用例只作依赖调整，断言一字未改：`RecordingInventory` 由假仓储改为「角色 1 的主容器 + 内存存储」，`Granted` 仍答 `(itemId, count)` 列表；测试正文未动。

`StackableItemCore` 由 internal 改为 public——命令处理器要为新开的一格造出与既有实现逐字节相同的堆核心，另抄一份才是会漂移的做法。

## Executor Self-check 1

| Acceptance condition | 结果 | 证据 |
|---|---|---|
| 1. 四类判定在不涉及数据库的情况下被检验 | 通过 | CK-019：26 条用例，无一使用临时数据库文件 |
| 2. 持久化实现中检索四类判定为零命中；不再回调内容源 | 通过 | CK-021 |
| 3. TASK-05 的用例在归属改变后原样通过，断言未被改写 | 通过 | CK-020：9 条全绿；`git diff` 对该文件只有构造参数一处机械调整 |
| 4. 教学奖励改用迁出后的规则与存储抽象 | 通过 | 见上节。三条既有用例断言未改且全绿 |

### Validation

| ID | 命令 | 结果 |
|---|---|---|
| CK-019 | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~RefillRule` | RED：26 条全部止于 `NotImplementedException`（构建通过，行为缺失）。GREEN：`已通过! - 失败: 0，通过: 26` |
| CK-020 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~DailyRefill` | `已通过! - 失败: 0，通过: 9`，断言未被改写 |
| CK-021 | 在持久化实现中检索日界、幂等、叠加决策、按段查找、内容源回调 | 五类**均零命中**。唯二提到 `SlotStart`/`SlotEnd` 的两行是把段**传给**规则服务、以及段满时错误消息里引用它，不构成查找 |
| CK-022 | `dotnet test Server/86JP.Server.slnx -m:1` | `939 通过 / 2 跳过 / 0 失败`（新增 26 条规则用例、删除 9 条已死代码用例、其余不变） |

## Independent Quick Review 1

评审（覆盖 TASK-05、TASK-06、TASK-07）判 **failed**，唯一 blocking 项是本任务写作中的一处事实错误：我声称教学奖励处理器没有行为覆盖，实际有三条。评审的判词写得准确——这不是代码缺陷，是**待决评审所依赖的证据出了错**，会让变更评审在一个不成立的前提上做决定。

已按上文处置：既然决定性顾虑不成立，就把 BD-014 做完，而不是只订正措辞。

评审同时独立复核并确认（摘要）：

- **每日刷新与发放的等价性逐项成立**。领取判定的两种写法归约为同一个命题（PK 上存在一行且 `period == 今天` 且 `value > 0`），在缺行、`value = 0`、NULL 三种情形下答案一致；`items[0]` 与 `MinBy(SlotIndex)` 因槽位唯一而同为一行；段满时异常类型、消息文本、事务回滚时机三者与基线相同；`ForGrant` 逐条复现基线，含 `StackLimit == 1` 与 `== 0` 两个边界；`GrantFor` 与被删除的 `CalculateGrant` 函数体逐字节相同。
- **退役是干净的**。九个退役标识符加 `ParkedSlotIndex`、`NextMainSlotAsync`、`IsClaimedAsync`、`CalculateGrant` 全库零命中；被删除的每一段都被逐一点名核对，保留的九个成员未被改动。
- **TASK-05 的四处变异全部复现**，红的用例集合与记录逐条相符；「既有那条只是假覆盖」经分析与实测两路证实——幂等被改坏时它保持绿，只有新用例变红。
- **TASK-07 的移除是外科式的**。`ErrorCodeFor` 除删去的那一支外与基线逐字节相同，八条拒绝原因提示码不变，遍历枚举的用例仍在，无成员会悄悄落入默认。

### Remediation 1（非阻断项）

| findings | 处置 |
|---|---|
| `FirstFreeSlot` 对非法段抛出，而基线在发放路径上捕获它并答 null | 保留并记录。今天不可达：`PvfItemPlacementDefinitionSource` 把 `SlotEnd < SlotStart` 映射为 null，其余段皆为硬编码合法常量；每日刷新一侧唯一的非法段伴随 `StackLimit == 0`，而 `GrantFor` 在任何查找之前就短路为 0 |
| 非法段的异常文案由「Daily refill slot range」改为「Item slot range」 | 保留。规则现在同时服务刷新与发放，文案不该只提刷新；异常类型不变，路径不可达 |
| `PeriodOf` 加了 `CultureInfo.InvariantCulture`，基线用当前区域性 | 保留。仅在宿主默认非公历时有别，而那种情形下基线写出的期字符串本就是错的。评审说得对，这是未在文中说明的行为改变，现予记录 |
| `ClaimedPeriodAsync` 的 SQL 仍含 `value>0` | 保留。真正的判定（比较哪一天）在规则里，CK-021 实质成立；`value>0` 是「这一行还算数」的行存在性条件，与基线逐字等价 |
| `SqliteEntryInventoryMutationRepository` 中 `using Dfo.Server.Infrastructure.Content;` 已死 | 已删除 |
| 构造尝试用例是两个裸 `Task.Run`，无强制交错的屏障，单凭它证明不了不可达 | 记录于 TASK-07。评审说得对：承重的证据是结构性的（组合操作没有失败出路、移动路径上不存在第二次读），用例是佐证而非证明。TASK-07 的记述已同时写明结构性理由 |
| `ItemRefusalCodeTests` 仍为不可产出的 `ItemUseStatus.SlotChanged` 锁着提示码 | 记录，留待变更评审。TASK-07 的收窄条款只授权移除移动一侧那一个 |
| `IItemArrivalRules.cs` 与 `PvfEntryLifecycleContentSource.cs` 两条路径不在本任务 Permitted files 字面之内 | 记录。两者在文中均已申报；Abstractions 的落位沿用既有 `IItemSlotRuleService.cs` 的先例，是清单写窄了而非落位错了 |
| `ExitFlushHandlerTests` 删桩后留下三处空行 | 已随本次改写一并消除 |

### 复验

| ID | 结果 |
|---|---|
| CK-019 | `已通过! - 失败: 0，通过: 26` |
| CK-020 | `已通过! - 失败: 0，通过: 9`，断言未改写 |
| CK-021 | 五类判定在持久化实现中均零命中 |
| CK-022 | `dotnet test Server/86JP.Server.slnx -m:1` → `940 通过 / 2 跳过 / 0 失败` |
| 教学奖励 | `--filter FullyQualifiedName~TaggedIdReport` → `已通过! - 失败: 0，通过: 3`，三条断言原样 |
