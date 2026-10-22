---
kind: rks-review
target: tasks/TASK-21.md
verdict: approved
---

# TASK-21 结果快速审查（T-005a，独立审查者）

- 审查对象：HEAD `b1959f3e` 之上的未提交差异：Protocol 下 64 个生产文件（＋1663 −2837）、22 个已有测试文件、新增 `LockRemovalSourceTests.cs`、`spec.md` 附录 G 的 VC-7、`follow-ups.md`（F-84～F-91）、`t005-test-dispositions.md`、`logs/task-21-*.txt`。合同 Revision 1，执行前裁定 1～9，Result 第 1 轮。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。
- 评审者自跑：`LockRemovalSourceTests` 9／9；`*TowerProjectionConcurrencyTests` 34／34（含新增 6 条）。

## 发现（均不阻断）

- **L1**：VC-7 写「总会回包」，但两塔协调器保留的 `ExactSessions` 为假的确定性拒绝（检查调用方、全体队员与塔局成员）在放弃副本首领塔分支不回包、离队与换队长只记日志、首领塔分支踢人不回拒绝包。这与基准完全一致（基准空转 8 次后得到同一结果），不是回归，但 VC-7 措辞比实际宽。修法：收口时注明，或微调 VC-7 措辞。
- **L2**：「循环以外删除的复查 12 处」实为 13 处（幻影塔一行含 `ReprojectCompletionAsync` 与 `SweepOneAsync` 两处）。
- **L3**：`logs/task-21-sites.txt` 附录 B 只列了三处取消位置变化，实际每删一处取门都会后移首次观察到取消的位置（例如 `DungeonEntryProjectionDelivery` 现在由第一次发送观察到取消，会走 `IsolatePartialCohortAsync`）。基准中取消发生在取门之后、发送之前时同样会走到这些路径，没有新的可达结果，只是概率变化。修法：结论改为「删除取门不引入基准不可达的取消结果」。
- **L4**：`sites.py` 中 `TownAreaProjectionDelivery` 的备注写「area recheck in MoveAsync」，实际是 `ForcePositionAsync`；loops 表行号 `:234-236` 正确。

## 13 处取门后复查逐处判定

判定标准：两次读取之间只有取门和只读语句，没有本作业内的写，也没有执行器外的写入者。

| 位置（基准行） | 判定 |
|---|---|
| `ReturnToSelectionHandler.cs:35-37` | 正确，`online.Generation` 等价 |
| `MarriageWeddingSlice.cs:224` | 正确 |
| `DungeonLoadingProjectionDelivery.cs:98-103` | 正确；命令之后的接受检查保留 |
| `AltarHandlers.cs:34-35` | 正确（`delivery.Participants` 只读）；`IsInWorld`、`cohort.Contains` 保留 |
| `TournamentSlice.cs:200` | 正确 |
| `LeaveDungeonSlice.cs:211`（RunId） | 正确（`TryLoadAsync` 只读） |
| `LeaveDungeonSlice.cs:219-221`（队伍代数） | 正确；`returning.Count` 业务检查保留 |
| `DungeonSelectionSlice.cs:615-619` | 正确（`ResolveSelectionMembersAsync` 只读） |
| `TownAreaProjectionDelivery.cs:234-236`（`ForcePositionAsync`） | 正确；门前 `Owns`／区域检查保留 |
| `BossTowerProjectionDelivery.cs:590-592` | 正确 |
| `BossTowerProjectionDelivery.cs:618-620` | 正确（`ExactMembers` 只读） |
| `IllusionTowerProjectionDelivery.cs:518-520` | 正确 |
| `IllusionTowerProjectionDelivery.cs:540-542` | 正确；`ReturnDueAt` 门前已等价检查 |

保留未删的几处（`AltarScheduler.AdvanceAsync`、`TournamentRewardSweepService.SweepAsync`、两塔离场投影的 `CurrentRun`、`ObserveCurrentCohort`）两次读取之间确有写，保留正确。

## 已核实无问题

- **7 个重试循环**：L1～L4 的 reroute 条件与 `HasInvolvedTower` 参数相同、中间无写，恒不成立；L7 三个条件只隔取门；L5、L6 基准中 8 次输入相同，用尽后返回 `Rejected(null, PartyFailureReason.CallerNotCurrent)`，与新写法逐字相同（N10、S5 证明区分力）。
- **非锁职责**：抽查 30 多个文件（队伍、婚礼 4 个切片、两塔、选角、副本进出／加载／选择／死亡／复活／全灭、祭坛、比武、断线观察者、回选角、称号、虚弱、城镇投影）。当前性检查、收件人捕获与参与者集合保留；`SelectCharacterHandler` 的 `pendingCloses`、回选角的关连接、两塔 `CloseExactAsync`、`CloseProjectionFailuresAsync` 的次序不变；婚礼 10 处内联调用都在方法末尾；删除的私有成员只服务于门或被删的复查。
- **门的删留**：`PartyNotificationSlice.cs` 无改动，队伍投递门在；`GameplayGateAssertion` 未动；称号门、虚弱门已删。
- **判据**：两支各有反例且有区分力（C3 范围、C4 豁免只认 `ProtocolModule.cs`、C7 整词）；名单理由成立，`PasswordComputationLimiter` 依据附录 B RQ-007 行，F-85 已登记。已知边界（Application 派生新名门类型、非 `SemaphoreSlim` 原语）属名称判据设计边界。
- **测试处置**：删除 11 例都有承接者或规格消除理由；G2 删除「共享租约升级被拒」一段成立（`task-21-upgrade.txt` 生产可达点 0／8）；19 个编译迫使文件断言多重集合不变；7 条空转用例理由成立。
- **VC-7**：N11～N14 装配首领塔协调器且答「不归我管」，S6、S7 下变红；F-88 登记足够。
- **数字**：8228 ＝ 8206 ＋ 2 ＋ 20；取证 61 条三种加法成立，反例类与对照以外为 0。

## 剩余风险（不阻断）

- R5 保留的当前性检查大多未逐一变异（F-88 → T-008）。
- 45 号行为现自取共享租约；正式全量中反例类外 `gate-wait`／`GameplaySelfWait` 为 0。

## 主控方处置

见 `tasks/TASK-21.md`「主控方处置（评审后，提交前）」。
