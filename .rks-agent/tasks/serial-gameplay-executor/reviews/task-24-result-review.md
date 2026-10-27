---
kind: rks-review
target: tasks/TASK-24.md
verdict: approved
---

# TASK-24 结果快速审查（T-006b，独立审查者）

- 审查对象：HEAD `50d7bce4` 之上的未提交差异与未跟踪文件（生产 15 个文件，测试 41 个已跟踪改动与 8 个新建）；合同 Revision 1，Result 第 1 轮。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。
- 评审者自跑：Protocol.Tests 中 GmCeraReminder、TitleProjectionOrder、AltarProjectionFailure、PartyNotification、PartyTownProjectionDelivery 共 50 条，全部通过。

## 发现（均不阻断）

1. **（低到中）F-107 与 Result Why 2 漏写 gm-admin 对 HTTP 500 的反应。** `Web/gm-admin/src/pending.tsx` 的 `submit` 在 500 且无 `outcome` 时判为 `unknown`，保留待定记录与原编号，提示勿新建编号重发；原编号查询能找到已提交回执，重放不会二次入账，所以页面不会诱发重复发放。但提醒持续抛出时，原编号重放每次仍是 500（`GmCeraHttpTests` V6），GM 只能经「查询」消掉待定记录，看到的始终是「未确认」而实际已到账。修法：补写 F-107；可选加固为提醒失败时返回带 JSON 体的 `Error(500, "点券已到账，提醒未能发出；勿新建编号重发。", "unknown")`，不违背裁定 2。
2. **（低）** `GmCeraDomainService.cs:31` 中「有会话但 `Gone`／`Declined` → `offline`」分支没有用例，M13 只改了 `sessions.Find` 为空那条路。
3. **（信息）** D6、D7（`ReturnToSelectionHandler.cs:52`、`SelectCharacterHandler.cs:247-252`）删吞咽后，关闭若抛出会取代随后的 `failure.Throw()`；能走到这里的只有执行器外的程序错误，可接受。
4. **（信息）** T15 用占位作业并直接调用 `AchievementEventSubscriber.HandleAsync`，能证明作业串行、作业结束刷出与按完成顺序交付（有区分力），不证明真实选角序列中称号事件一定在选角作业之后入队。

## 逐项核对

| 项 | 结论 |
|---|---|
| D 类 10 处 | 成立。`OutboxConnectionCloser` 在调用点只会因参数为空、取消（10 处都传 `None`）、执行器外、已刷出发件箱抛出；唯一的作业结束钩子 `Flush` 不回头登记，「已刷出」只有泄漏 flow 碰得到，属程序错误。`close_failed` 日志分类无消费者 |
| KE／KB 保留 | 成立。抽查 12 处以上可执行语句未动；R 类证据经 `FaultingEncoder` 或真实总线注入，M3～M19 都让指定用例变红 |
| 队伍两把门 | 成立。水位线、`CurrentPartyId`、`CurrentMembers`、`FullStateMembers`、墓碑逐行保留；删掉的 `PlanParticipants` 等都是只喂取门的纯读取 |
| 取证源迁移 | 成立。源名 `Dfo.Server.Gameplay.Assertions` 与事件名 `Violation` 不变；三处断言都改调 `GameplayAssertionDiagnostics.Report`；正向对照七个程序集各一条 `write-off-executor` |
| GM Cera | `Declined` 记 `offline` 合理：`AccountWalletChangedHandler` 只在会话不匹配、被替换、不在世界或钱包为空时弃权，都是「没有发出」；组装失败等其余提醒完再抛，语义正确；HTTP 500 见发现 1 |
| `ClosedAsync` | 只是测试读取时机问题。`ConnectionQueue.End` 先在锁内置 `ended`、取消在途发送、报告结果，再由 `FailConnection` 分离关闭；失败后再入队条目被丢弃，不存在失败后关闭前仍写帧，次序符合 RQ-036 |
| 行尾 | 无问题：41 个改动的已跟踪文件在基准与工作区都无 CRLF，BOM 与末尾换行一致 |
| 附录 E 证据表 | 成立。DR-1～DR-17、VC-3、VC-4、VC-6 每行都有经真实执行器、登记适配器、发件箱与发送端的用例；F-108 降级符合裁定 3 条件 |
| Spec | 只改 RQ-031(d) 末句与 AC-011 第 4 条，措辞与代码一致 |
| 数字 | 8208 ＋ 3 ＋ 18 ＝ 8229 ＝ 8191 ＋ 45 － 7；Host 那条失败为满载假红形状；取证 40 ＝ 2 ＋ 1 ＋ 37；AC-017 检索第一条只命中 8 行同名无关 `.Gate`，第二条 0；过渡期 3 ＋ 18 ＋ 7 ＋ 1 ＝ 29 |

## 主控方处置

见 `tasks/TASK-24.md`「主控方处置（评审后，提交前）」：发现 1 采用加固写法并补写 F-107，发现 2 补用例与变异，发现 3、4 写入 Remaining。
