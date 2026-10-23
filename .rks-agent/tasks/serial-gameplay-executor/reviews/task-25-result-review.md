---
kind: rks-review
target: tasks/TASK-25.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（发现 1）；第 2 轮定点复核后更新为 `approved`，见 `## Round 2`。评审者只读，本文件由主控方按评审回报落盘。

# TASK-25 结果快速审查（T-006c，独立审查者）

- 审查对象：`tasks/TASK-25.md` Result 第 2 轮（合同 Revision 1，执行前裁定 1～7，Blocker 之后的处置 1～4）；HEAD `1ee1c91e` 上的未提交差异：生产只有 `NotificationBus.cs`，测试 80 个文件与 `tests/Support/` 下 3 个新文件，处置记录与日志。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。

## Round 1 — `changes_required`

1. **（阻断）** `NpcShopPurchaseTests.cs:240`、`CeraShopPurchaseTests.cs:366`（§6.1 #20、#22）的 `Attempts == 1` 改义后失去区分力：基准上这两条组装 3 条消息，注入失效时 `Attempts` 为 3；F-102 之后恒为 1，M7 下保持绿。「基准上也一样」只对 `ItemSaleTests` 成立。修法：补「该收件人在注入点之后什么都没登记」的断言，局部重跑 M7。
2. **（不阻断）** §6.1 #10、#17、#18 的 `DoesNotContain(Batches, IsStateReplay)` 比原 `Assert.Empty` 窄，且 `IsStateReplay` 按类型名字符串判定，改名时会悄悄变成恒真。
3. **（不阻断）** C2 包装各自持有独立发件箱，这些用例证明不了「事务回滚但消息已发」不会发生，未登记后续事项。
4. **（不阻断）** `GameLoginTests.RecordingSender.SendExplicitAsync` 的拒绝守卫被删，与同类文件不一致。
5. **（不阻断）** M5 下没有当场跑 `OutboxWorkUnitTests`。

第 1 轮已核实无问题：F-102 生产改动（生产 DI 总会注册 `INetworkConnectionContexts`，与适配器查同一张表；`NotificationOutcome` 各值语义不变）；N1、N2 区分力；抽查 17 处改义；替身改写（真实适配器外包计数包装，失败一律在 `FaultingEncoder` 注入，只读门面吞不掉失败）；`CallbackCloser` 守卫；diffcheck 18 个文件逐一对上；F-104 屏障只是测试侧同步；删除 1 条的承接者；数字 8232 ＝ 8212 ＋ 2 ＋ 18、取证 40 条、表征 10 过 0 跳过、替身 100 ＝ 16 ＋ 54 ＋ 3 ＋ 27、行尾无变化。

主控方处置见 `tasks/TASK-25.md`「主控方处置（第 1 轮评审后）」。

## Round 2 — `approved`

- 评审者自跑 Application 小过滤 49 条全部通过（三条 F 类与 `EquipmentEnchantHandlerTests`、`EquipmentReinforcementHandlerTests`、`GameLoginTests`）。
- 发现 1：三处 `Assert.Empty(…Sender.Messages)` 在请求完成、发件箱刷出之后读取，钉住「注入点之后什么都没登记」；round 4 的 M7r 三条全红，M7 合计 18 条全红；§6.1 #20～#22、变异日志表头与 Result Remaining 3 已改正。
- 发现 2：三处改为请求前后比较 `Batches.Count`；`IsStateReplay` 用 `GetType(…, throwOnError: true)` 加 `IsInstanceOfType`，改名时静态初始化即抛出。
- 发现 3：F-112 已登记，§6.4 与 §11 引用。
- 发现 4：`GameLoginTests.cs:357` 补上 `RefuseExplicit`，注入清单 79 ＝ KE 18 ＋ G 61。
- 发现 5：M5r 下 `OutboxWorkUnitTests` 4 条中 2 条变红。
- 生产边界仍只有 `NotificationBus.cs`；`tests/Support/` 未改；diffcheck 18 → 20 个文件，多出的只是新加的两行；断言行 4365 → 4368；行尾无变化。

非阻断残余：F-112 正文写「RQ-031(b) 的证据是 `OutboxWorkUnitTests` 与 C1 用例」，但 C1 用例在 M5 两轮中都没有变红，不是证据。主控方已在提交前删去「与 C1 用例」。
