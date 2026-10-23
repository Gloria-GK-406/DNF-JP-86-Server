---
kind: rks-review
target: tasks/TASK-23.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（F1）；第 2 轮定点复核后更新为 `approved`，见 `## Round 2`。评审者只读，本文件由主控方按评审回报落盘。

# TASK-23 结果快速审查（T-006a 投递翻转，独立审查者）

- 审查对象：HEAD `a4422480` 加未提交差异：生产 41 个文件、测试修改 30／删除 8／新建 8、`spec.md`、verification 文档与日志；合同 Revision 2，主控方两轮裁定，Result 第 1 轮。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。

## Round 1 — `changes_required`

- **F1（中，阻断）**：两塔进入（XO-3、XO-4）在传输失败时的可观察行为变化没有 Spec 授权。基准 `BossTowerProjectionDelivery.cs:173-182`、`IllusionTowerProjectionDelivery.cs:171-176`：成员循环中第一次发送失败即中止、对全部隔离成员 `CloseQuietly` 再抛出，请求者也断线；翻转后 XO-3 组为 BestEffort，只有失败者被关闭，其余成员与请求者照常进入。附录 G 只有 VC-3（组队进本，DR-2）与 VC-4；计划 V10 把今天的行为误写成「收集、继续」，核实命中停止条件 2，Result Why 1、Why 4 的「不影响」结论不成立。建议接受（DR-3 的新承接方式正指向现在的行为）并补附录 G。
- **F2（低）**：`NotificationBus` 逐条登记（F-102）是被替身推着做的选择，但与基准语义相同：单连接次序、有序组序号、传输层全有或全无、`NotificationOutcome` 失败语义都不受影响；唯一缺口是「同一收件人中后一条不可编码」时前面的已登记，与 RQ-031(d) 字面不符。修法交 TASK-25（Owned boundary 含 `NotificationBus.cs`）。
- **F3（低）**：`INotificationBus.cs:14` 的 `Delivered` 注释漏了「连接已不在而被丢弃」。
- **F4（低）**：`Host.Tests/GmEventsHttpTests.cs` 有无关行尾改动（129／120 → 去掉 CR 后 11／2）。
- **F5（低）**：AGENTS.md 中发送器、重试上限、投递期限、`NotificationRetryDrainService` 等段落已过期，未登记后续事项。

第 1 轮已核实通过：作业结束刷出与工作单元（每条事务出口都结算单元，无「回滚但已发」路径；失败时先刷出存活条目再关闭来源）；登记点完整编码的次序与 `SelectCharacterHandler` 前提；`OutboundSendFailureTests` 未改且 S24 会打红；F-83 静默丢弃有用例且不吞写失败；XO 组成员与交付等级、S11～S17 区分力；删除项无遗漏（顶替关闭经提交后回调、先移除在线身份再登记关闭）；b1／b2 严格形式开启，b1 对 S2 无区分力由 N6 承担；传输写入点只剩两处；冲突用例处置、`EffectOperationReplayTests` 保留理由、`GmEventsHttpTests` 屏障只是测试侧竞争（基准副本同样存在）、重置客户端用例改量保住意图；数字 8191 ＝ 8171 ＋ 2 ＋ 18、取证 46 条；计划外三个生产文件与 `LoginSessionReconnectTests` 改动必要。

主控方处置见 `tasks/TASK-23.md`「主控方处置（第 1 轮评审后）」：F1 接受新行为，`spec.md` 附录 G 的 VC-3 扩展到两塔进入（主控方授权的 Spec 修订）。

## Round 2 — `approved`

- F1：VC-3 改为覆盖组队进本与两塔进入，依据栏 DR-2、DR-3；Result Outcome、Why 1、Why 4 写明命中停止条件 2 与按处置第 1 条授权；TASK-24 提纲补发送端用例。
- F2：F-102 去向写明 TASK-25 改 `NotificationBus.cs` 并补用例。
- F3：`Delivered` 注释已补，差异只在注释。
- F4：`GmEventsHttpTests.cs` numstat 11／2，CRLF 行恢复为 118 行。
- F5：新增 F-105，交 T-009。
- `spec.md` 差异只有附录 A 第 4 行与附录 G VC-3；生产代码除 F3 注释外与第 1 轮相同（numstat 与 mtime 双重核对）；`cr-at-eol` 口径 `diff --check` 无输出。
- 非阻断残余 F-97、F-98、F-100、F-101、F-102、F-104 去向都已登记。
