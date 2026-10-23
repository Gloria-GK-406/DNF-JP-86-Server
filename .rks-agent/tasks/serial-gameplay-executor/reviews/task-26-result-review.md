---
kind: rks-review
target: tasks/TASK-26.md
verdict: approved
---

> 第 1 轮结论为 `changes_required`（发现 1：F-115）；第 2 轮定点复核后更新为 `approved`，见 `## Round 2`。评审者只读，本文件由主控方按评审回报落盘。

# TASK-26 结果快速审查（T-006d 收口，独立审查者）

- 审查对象：HEAD `a861c4de` 加未提交差异与未跟踪文件；合同 Revision 1，执行前裁定 1～4，Result 第 1 轮。
- 适用标准：reviewing-work 的 Task Unit、Code、Deliverable 准则。

## Round 1 — `changes_required`

1. **（中，阻断）F-115 是 T-006 带进来的可利用回归，被写成 AC-013 的「附条件」，没有裁定授权。** 标题长度为 `i32`，解码器只拒绝负数（`PartyCommandSlice.cs:234-243`）；入站帧上限 1 MiB，标题最多约 1,048,546 B；出站名单帧 80 ＋ T，T ≤ 1,048,496 时可编码入队；`OutboundSender.cs:373` 以 `bytes + item.Length > QueueByteLimit` 判定 `OverLimit` 并关闭连接。失败情景：改包客户端建单人队、设约 1 MiB 标题、站在热门区域，之后每个走进该区域的玩家在 `inbound:GameSetUserArea` 中先登记 52 B 再登记约 1 MiB 的队伍名单帧，受害者自己的连接被关闭；两个约 0.5 MiB 标题的队伍也能做到。基准 `a4422480` 上直发有 2 s 期限，正常客户端几秒内收完，不会被断开。风险等级中。攻击者能任意打破的条件不能支撑「满足，附条件」。应在 T-006 内处理，但要改生产代码，超出本单元边界，需主控方裁定。
2. **（低）** `HostOutputWiringSourceTests` 的触发记号 `new ProcessStartInfo(` 太窄，漏掉对象初始化器写法、`ProcessStartInfo info = new(…)` 与 `Process.Start("dotnet", args)`。

第 1 轮已核实无问题：选角时城镇在场快照走不到（`EntryPlacementRuleService.cs:131`、`:156-157`）；每人 670／521 B 与 N_max 推导及实测自洽；逐项对照抽查的用例名都能检索到且支撑结论，附条件项如实记载；§10 Spec 修订汇总与 `a4422480..a861c4de` 的 4 处差异一一对上；§8 移交清单与 115 条 follow-ups 交叉核对无遗漏；F-14、F-21、F-61 关闭证据成立；测试改动理由与反例区分力；数字 8238 ＝ 8218 ＋ 2 ＋ 18、取证 40 条；边界与行尾。

主控方处置见 `tasks/TASK-26.md`「主控方处置（第 1 轮评审后）」：F-115 不接受风险，另立 TASK-27 修复（含同类字段排查），M-2 进入条件改为 TASK-26 与 TASK-27 都提交。

## Round 2 — `approved`

- 评审者自跑 `HostOutputWiringSourceTests` 4 条全部通过。
- F-115 改写到位（与基准对比、风险等级中、归属 TASK-27、修复前 T-006 不算完成）；AC-013、RQ-036 两行删去标题条件；§8 单列 TASK-27；§9 写明 T-006 尚未完成、M-2 今天不成立；§11 末行拆为两行；§7 的 8239 条说明与日志一致；通读无残留的附条件措辞。
- 扫描判据改为 `\bProcessStartInfo\b|\bProcess\.Start\(`，新增对象初始化器反例先红后绿；MF2、MF3 在新规则上重跑通过并逐字节还原；识别范围与 `Process.Start("kill", …)` 的情况已写明。
- 边界：`Server/src`、`spec.md`、`plan.md`、`AGENTS.md`、`Docs/` 无差异；`cr-at-eol` 口径 `diff --check` 无输出；行尾 CR 为 0。
- 不阻断的小问题 4 条（第 1 轮 Remaining 未标注已取代；§9 一句可补半句；F-113 每人上界按 T＝0 计，交 TASK-27 重算；F-115「基准上没有字节队列上限」宜改为「投递路径上」）由主控方在提交前处理，见 `tasks/TASK-26.md`「主控方处置（第 2 轮复核后，提交前）」。
