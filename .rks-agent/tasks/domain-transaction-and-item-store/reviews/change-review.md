# Change Review — 领域事务与物品存储抽象

- Stage: review
- Reviewer: independent (未参与实现)
- Scope: 整份改动集（分支 `domain-transaction-and-item-store`，无提交，工作区即改动集）

## Verdict

**approved-with-findings。无 blocking 项。RQ-001 至 RQ-011 逐条判定为满足。**

评审专门去找了路由决策所担心的那种失效——「某条保证被删掉而全套测试照样全绿」——没有找到。它用三处变异证明关键守卫确实承重：

| 改坏的守卫 | 变红 |
|---|---|
| 单一可失败参与者的拒绝 | 2 条 |
| 持久容器在边界外拒绝改动 | 1 条 |
| 协调器的回滚 | 2 条 |

## Requirement Coverage（评审判定）

| RQ | 判定 | 评审核实的证据 |
|---|---|---|
| RQ-001 | 满足 | 三段提交 + `await using` 在每一条非正常出路上回滚；变异证明回滚承重 |
| RQ-002 | 满足 | 两个命令处理器对事务词汇零命中；边界来自 `ProtocolModule` 的三处 `Transactional` 注册 |
| RQ-003 | 满足 | 五种操作只实现一次；载体等价用例拿真 SQLite 容器与内存容器跑同一脚本并断言观察序列相等 |
| RQ-004 | 满足 | `Enlist` 在改动任何已登记状态之前拒绝第二个可失败参与者；变异证明承重 |
| RQ-005 | 满足 | 持久容器抛、仅存内存的自开自结；变异证明承重 |
| RQ-006 | 满足 | 四类判定迁出，持久化实现只剩两个公开成员且对四类判定零命中；26 条规则用例不碰数据库 |
| RQ-007 | 满足 | 全库对伪造标识与行寻址词汇零命中；反射断言 `ContainerSlotItem` 上不存在此类成员 |
| RQ-008 | 满足 | 存储词汇只有读/写/清；`ContainerMoveKind` 只存在于协议切片内，存储不引用 |
| RQ-009 | 满足（实机部分待用户） | 两个切片的编解码区与基线逐字节相同——两份 diff 最早的内容 hunk 都在基线第 189 行之后；`SlotsChanged` 是枚举末位成员，移除不改变任何编号 |
| RQ-010 | 满足 | 五种操作两侧各自表达；评审独立复核了化妆装细节以 core 内 UID 为键这一承重前提 |
| RQ-011 | 满足 | 移除干净，其余八条拒绝原因与提示码不变；TASK-07 据实记录了「不可达是因为检查被拆掉，不是因为竞态被解决」 |

## Findings 与处置

### 已修

| findings | 处置 |
|---|---|
| 教学奖励多件道具的情形无覆盖——「后一件看得见前一件」与「一步之内一起落定」这两条正是改写的理由，却没有用例 | 新增 `TaggedIdReport_SettlesEveryItemOfOneStepTogether`：一步给两件同种道具，断言最终是**一堆二**而不是两堆一，且两条回执指向同一格 |
| 无任何用例把真 `SqliteItemStore` 与真 `MemoryItemStore` 放进同一次事务再让它失败；EV-001 是靠组合成立而非直接成立 | 新增 `TwoCarriersInOneTransactionTests`（3 条）：一件道具从「留得住的」搬进「留不住的」，成功时两侧都可见、失败时两侧都不可见，外加第二个持久存储被拒 |
| 移动的 `switch` 有个 `default:` 分支，为一个不可达的 kind 编造了面向玩家的提示码（23「无法移动该物品」），与同文件二十行之后自陈的纪律相反 | 改为抛出。那是编码错误，不是要读给玩家听的句子 |
| `SqliteEntryInventoryMutationRepository` 里 `using Dfo.Server.Infrastructure.Content;` 已死 | 已删 |

### 记录不修

| findings | 判断 |
|---|---|
| **冷却不是事务参与者**。提交失败时道具没被消耗，冷却却已经起了；基线是先消耗（且已提交）再起冷却，两者不会这样打架 | **不扩大范围**。RQ-001 写的是「对**物品**的多处改动」，冷却不是物品改动；Spec 的 Non-Goals 也明列「物品之外的其他持久化路径」。评审的实际意见是「没有任何地方记下这个取舍」——现予记录：这正是「仅存内存的参与者」概念将来要覆盖的形状，接冷却只需让 `ICharacterItemCooldownDomainService` 成为一个不可失败的参与者，但那要动本 Spec 范围之外的接口。**留作 Docs 条目** |
| 移动/拆堆/合并会重排 `item_uid`，入场租约若变脏会因此失败 | 已在 TASK-03 记录并带入 TASK-06。仍然休眠：`TryMarkDirtyExact` 无生产调用方，`IsDirty` 只有那一个写入者。评审补充了一点值得留下的判断：这不是回归——基线在同样情形下会**静默回退**那次移动，而现在会响亮地报错。CK-028 第 5 步（退出再入场）正是为它设的 |
| 教学奖励现在依赖角色**已绑定容器**，基线不依赖；会话已找到但注册表已清的窗口内会静默不发放 | 记录。窗口真实但极窄（清注册表严格晚于移除会话），且失败是空回执而非错误帧。**留作 Docs 条目** |
| 仅存内存的存储在两次取锁之间重建再整体替换，并发写者可能丢更新 | 已在 TASK-03 记录。休眠：`CreateTransient` 无生产调用方 |
| 读谓词带 `character_id`、删谓词不带 | 已在 TASK-02 记录。评审核了三处插入点与入场加载器，确认今天不存在「读不见却会被删」的活行 |
| `ItemUseStatus.SlotChanged` 不可产出却保留，`ItemRefusalCodeTests` 仍为它锁着提示码 | 保留是契约正确的选择——RQ-011 只授权移除移动一侧。评审的意见是「该被标注的是那条已死的断言，而不是悄悄删掉」。**留作 Docs 条目** |
| 每日刷新里 `Math.Max(0, ReadCount(...))` 是残留的算术钳位 | 基线原样继承；CK-021 点名的四类判定确为零命中 |
| `Dfo.Server.Application.csproj` 的 `InternalsVisibleTo` 与 `PvfEntryLifecycleContentSourceTests` 两处改动未在任何 Amendment 中申报 | 属申报遗漏。两者都无实质后果（一条程序集可见性；两条断言从被删除的 `CalculateGrant` 改指 `DailyRefillRuleService.GrantFor`，期望值不变），现补记于此 |

## Non-Goals

评审逐条核实未越界：槽位映射与分段定义未动；未做结构定义集中化、未建新表；`PersistentItemContainer.SnapshotAsync` 仍走入场加载器，入场快照未改由容器供给；第二个持久参与者被拒绝而非被迁就。

## 消失清单

评审全库检索，以下构造零命中：`nextEntryId`、`ToStorageKind`、`MainInventoryMovePlan` / `MoveKind` / `SlotPair` / `Occupant`、`ContainerMovePlan`、`DisplacedEntryId`、`ParkedSlotIndex`、`ApplyMainMoveAsync`、`GrantItemAsync`、`ConsumeMainItemAsync`、`NextMainSlotAsync`、`IsClaimedAsync`、`CalculateGrant`。`SlotsChanged` 唯一残留是记录构造尝试的那条用例注释。

## CK-028

评审确认其未完成被如实记录、范围划定正确：TASK-08 明说走查没做、给出五步流程、并且**没有**把验收条件 3 记为满足；其替代物（组合根冒烟）是一项真实检查，也没有被冒充成走查。EV-007 因此仍然开着——那是完成证据的事实，不是代码缺陷。

## 复验

`dotnet test Server/86JP.Server.slnx -m:1` → **`944 通过 / 2 跳过 / 0 失败`**（评审复核时为 940，其后本轮补了 4 条覆盖）。
