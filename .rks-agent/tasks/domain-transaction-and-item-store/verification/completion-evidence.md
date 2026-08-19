# Completion Evidence — 领域事务与物品存储抽象

- Stage: verification
- 证据取自一次**清空 `obj/` 与 `bin/` 之后的全新构建**：`0 个警告 0 个错误`，全套一次跑绿，无负载假红。

## Success Conditions

| ID | 判定 | 新鲜证据 |
|---|---|---|
| SC-1 | **通过** | 三个道具命令处理器源码对 `IDomainTransaction\|DomainTransaction\|BeginTransaction\|CommitAsync\|RollbackAsync\|TransactionScope\|Enlist\|IItemStore` 逐文件零命中。同一段业务逻辑在两种载体上的对照：`ItemContainerCarrierEquivalenceTests`（2 条）拿真 SQLite 容器与内存容器跑同一串操作并断言观察序列相等——现在它们跑的是**同一段代码**，五种操作只实现了一次 |
| SC-2 | **通过** | `DomainTransactionTests` 覆盖准备失败与持久提交失败两个方向；`UnitOfWorkBehaviorTests` 经真实 `CommandBus` 覆盖同两个方向。真载体的直接证据是本轮新增的 `TwoCarriersInOneTransactionTests`：一件道具从数据库搬进内存存储，成功时两侧都可见，失败时**两侧都不可见** |
| SC-3 | **通过** | `Enlist_RejectsASecondParticipantWhoseCommitCanFail`、`TransactionalCommand_FailsWhenASecondParticipantWhoseCommitCanFailJoins`，以及新增的 `ASecondStoreThatKeepsThingsIsRefused`（两个真 `SqliteItemStore`）。拒绝发生在改动任何已登记状态之前 |
| SC-4 | **通过** | `ItemContainerOutsideTransactionTests`（4 条）：持久容器在边界外 `PutAsync` / `ClearAsync` 均抛出，仅存内存的不抛，读在两者上都允许 |
| SC-5 | **通过** | 存储实现对 `ToOffset\|yyyy-MM-dd\|IsClaimed\|NextMainSlot\|StackLimit` 零命中，且不再回调内容源；四类判定由 `RefillRuleServiceTests`（26 条）在**不碰数据库**的情况下检验。既有行为覆盖仍通过：9 条每日刷新用例断言未改写，3 条教学奖励用例断言未改写 |
| SC-6 | **通过** | `IItemContainer.cs`、`IItemUseEffects.cs`、`ItemContainers.cs` 对 `nextEntryId\|StorageEntryId` 零命中；`TransientContainerNeedsNoIdentifierTests` 以反射断言 `ContainerSlotItem` 上不存在任何 `Entry` / `Uid` / `RowId` 成员——要求被钉成「想携带也无从携带」 |
| SC-7 | **通过** | 帧字节：两个切片的编解码类与基线**逐字节相同**，两份 diff 最早的内容 hunk 都在编解码区之后；13 条移动/使用编解码用例、161 条全部协议编解码用例全绿。全套：`944 通过 / 2 跳过 / 0 失败`（基线 848 / 2 / 0）。实机走查已由用户走通，见下节 |

## 约束

| 约束 | 结果 |
|---|---|
| `0x0013` / `0x002C` 帧字节与时序不变 | 编解码路径逐字节未动。**一处例外已记录**：两条移动真并发时，从前一方得静默失败帧、现在两方都得成功帧。这是 RQ-011 明文授权的那一处变化（`SlotsChanged` 被证明不可达后移除），TASK-07 有完整记述 |
| 既有测试语义不得修改 | 除申报的三处外未改动任何断言：TASK-03 的载体等价脚本减去三条容器级拒绝观察（逐条说明去向）、`PvfEntryLifecycleContentSourceTests` 两条断言改指迁出后的规则（期望值不变）、`ItemRefusalCodeTests` 删去点名已移除成员的那一行 |
| 最多一个可失败参与者，第二个以失败暴露 | SC-3 |
| 事务外改动持久物品状态以失败暴露 | SC-4 |
| 提交失败不向客户端回提示 | `NetworkPlugins.OnTcpReceived` 记录并重抛而不写帧，提交失败到客户端即是静默 |
| 新增存储实现不得自建任何表 | 三个新文件对 `CREATE TABLE\|CREATE INDEX\|ALTER TABLE` 零命中；`Store_DefinesNoTableOfItsOwn` 在运行期比对一次完整读—写—提交前后的 `sqlite_master` 表名集合 |
| 不得改动 `Tool/` 与 `.worktrees/legacy-reference/` | `git status` 无命中 |
| 不得手工编辑客户端可执行文件与 DLL | 改动集内无二进制 |
| `protocol/` 内不得出现凭据与 PII | `protocol/` 未被改动 |

## 消失清单

全库零命中：`nextEntryId`、`ToStorageKind`、`MainInventoryMovePlan` / `MoveKind` / `SlotPair` / `Occupant`、`ContainerMovePlan`、`ContainerConsumption`、`DisplacedEntryId`、`ParkedSlotIndex`、`ApplyMainMoveAsync`、`GrantItemAsync`、`ConsumeMainItemAsync`、`NextMainSlotAsync`、`IsClaimedAsync`、`CalculateGrant`、`ItemMoveStatus.SlotsChanged`。

其中三处值得单独点出：

- **「槽位 -1 暂泊」**：交换在行寻址下无法两端直写，旧实现把一行暂泊到 `slot_index = -1` 熬过事务。槽寻址下每个槽直接被告知它现在装什么，这个构造随实现一并消失。
- **伪造主键**：永不落库的内存容器曾必须自行发号一个假的数据库行号，只为迎合上层的行寻址词汇。词汇没了，发号也没了。
- **两份移动实现**：移动、交换、合并、拆堆曾在 SQL 里一份、在内存容器里一份，只在有人不断核对时才一致。现在只有 `ItemContainerOperations` 一份描述，两种载体跑同一段代码。

## CK-028 实机走查：已由用户执行

2026-08-19 由用户在本改动集的构建产物上走通，服务端为 worktree 构建 + 主分支数据库的副本（原库未被改动）。角色 5，账号 1。

### 服务端日志

管道日志确认三条道具命令**在生产路径上确实以事务型执行**（`CommandLoggingBehavior`，共 14 次）：`MoveItemSpaceCommandHandler` 5 次、`UseStackableCommandHandler` 1 次、`GrantTutorialRewardCommandHandler` 8 次，全部 `as Transactional`。

```
Move item space character=5: item 8474  5->4  count=1
Move item space character=5: item 8474 74->5  count=1
Move item space character=5: item 8474 65->74 count=1
Tagged id report id=38 flag=1 declared=1 granted=1
Move item space character=5: item 8474  4->65 count=2      ← 合并
Use stackable  character=5: used item 8474 from slot 4, 1 left
Move item space character=5: item 8474  7->4  count=1      ← 反向请求
```

整份日志**零异常、零 `refused as`**；唯一一条 warn 是 `Unhandled Game frame type=0x01E4`，与本次改动无关。

最后一条移动值得单记：日志里的两端是客户端原样发来的 `7->4`，而落库结果是道具在槽 7——这正是处理器文档所述的「反向请求」（源格为空时客户端在指名它想填哪一格），该路径在实机上走通。

`Tagged id report id=38 declared=1 granted=1` 是教学奖励经新的事务型命令、经容器写入后**在真实游戏中发出一件道具**，此前只有自动化证据。

### 落库状态与核心完整性

| 槽 | 道具 | 数量 | 行号 |
|---|---|---|---|
| 7 | 8474 | 1 | **33**（新行号） |
| 177 | 2680738 | 2 | 21 |
| 178 | 2749110 | 3 | 22 |
| 179 | 10002520 | 3 | 23 |
| 180 | 10003235 | 1 | 24 |
| 181 | 690001556 | 2 | 25 |

六行核心均为 82 字节，`marker@21 = -1`、`byte66 = 0xFF` 逐行正确——clear + write 的往返没有丢字节。被移动过的那件道具行号由种子的 21–25 段变为 33，即移动重建了行；**用户重新登入后道具仍在槽 7**，这正是「行号变动不影响入场快照」的实机确认，也是本次改动中我最担心的那一步。

### 四步判据

| 步骤 | 结果 |
|---|---|
| 拖拽落位与改动前一致 | **通过**（用户确认；日志见上） |
| 使用后回血且数量减一 | **通过**（用户确认；`used item 8474 from slot 4, 1 left`） |
| 退出再入场，背包内容一致 | **通过**（用户确认「重上后挪动的 item 也被持久化了」） |
| 冷却拒绝仍然生效 | **从原版客户端不可构造**，见下 |

### 关于冷却拒绝：一条来自用户的领域事实

用户指出：**客户端自己有冷却检测，冷却中直接本地拒绝，根本不会把帧发出来。** 因此服务端的 `OnCooldown` 拒绝在正常游戏中几乎不可触达——可触达的窗口只有网络延迟那么宽，或者客户端与服务端的冷却时长不一致时。

这改变了这一条的记法：它不是「用户漏测」，而是**该情形在原版客户端上不可构造**。服务端那条判定因此是一道后备（面对改装客户端、或两端时长漂移），而不是常规路径。

其自动化覆盖仍在且通过：`ItemUsabilityDomainServiceTests` 两处 `ItemUsability.OnCooldown`、`ItemRefusalCodeTests` 锁住其提示码，`--filter ~Cooldown|~Usability` 22 条全绿。本次改动没有触碰该判定本身，只是把消耗改为经容器写入。

**据此，SC-7 记为通过**：三条可构造的判据全部走通，第四条的不可构造性有明确原因并有自动化覆盖兜底。

### 与 `SlotsChanged` 的同构之处

值得并排记一笔：`SlotsChanged`（TASK-07 移除）与 `OnCooldown`（保留）是同一形状的两个例子——服务端拒绝路径的可达性，取决于客户端会不会把那一帧发出来。前者是服务端自己拆掉了检测，后者是客户端根本不发。两者都提醒：**「有没有测到」和「能不能构造」是两个问题**，记录时不能混为一谈。

## 结论

TaskContract 的全部成功条件（SC-1 至 SC-7）均以新鲜证据满足。变更评审判 **approved-with-findings**，无 blocking 项，RQ-001 至 RQ-011 逐条判定为满足。

本次改动集尚未提交——按计划，接受前的完整改动集是唯一回退单位。
