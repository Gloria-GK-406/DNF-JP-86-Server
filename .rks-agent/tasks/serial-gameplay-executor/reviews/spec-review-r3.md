---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/spec.md
round: 3
contract_revision: 2
verdict: approved
reviewer: independent subagent (fresh context; reviewing-work Spec reference + sdd-governed Adversarial Review Transitions; bounded re-review scope)
---

# Spec 复审 r3：serial-gameplay-executor（contract revision 2）

## 范围与依据

- 目标：`spec.md` 当前文本（`state: ready`，`contract.revision: 2`），worktree HEAD `a02bce46`。代码路径相对 `Server/src/`。
- 这是该目标的第三轮复审，也是第二次 bounded 复审。只核对：
  1. B-2 是否消除；
  2. 本次修改有没有直接回归，或与 RQ-031(0)(c)、RQ-023(c)、DR-4、DR-5～DR-8、附录 G、RQ-036 矛盾；
  3. R-2～R-4 是否写清楚。
- 不重做全面审查。此前证据沿用 `reviews/spec-review.md`、`reviews/spec-review-r2.md`，挑战集沿用 `reviews/spec-challenges.md`。
- 准则：`reviewing-work` 的 Spec 参考；`sdd-governed`「Adversarial Review Transitions」。

## 1. B-2：已消除

先复核今天的行为：

- `Application/Notifications/NotificationBus.cs:189-254`（`AttemptAsync`）：`ComposeAsync` 抛出的异常被捕获，记一条 `Warning`，返回 `Failed`，不关闭连接。只有注册的重试上限大于 0 时才重新入队（`:177-178`）。
- `Protocol/Game/Altar/AltarProjection.cs:94-108`：任一 attempt 为 `Failed`，就抛出，再转成 `TransactionOutcomeUncertainException(PostCommitPublicationFailure)`，对每个接收者调用 `BlockAsync`，然后重新抛出。

再对照新写法：

| 核对项 | 新 RQ-031(d) | 判断 |
|---|---|---|
| 普通组装失败 | 丢弃该收件人的条目、记 `Warning`、不关连接、CMD 不失败。与今天的 `Failed` 一致 | ✓ 不再是可见变化，无需在附录 G 补条目 |
| 祭坛 | CMD 能看到逐收件人的组装结果；依据 `Failed` 做决定的 CMD 照原样处理，祭坛封锁并关闭全部参与者 | ✓ 与 RQ-023(c)「非传输失败保持今天的处理方式」、DR-4「非传输失败（组帧……）仍按 RQ-023(c) 封锁全部会话」一致 |
| 可判定性 | AC-011 新增两条用例：普通组装失败不关连接；祭坛组装失败封锁并关闭全体。两条都能失败 | ✓ |
| 与 (0) 的衔接 | (0) 规定组装在提交时完成（单元内登记），或在登记时完成（单元外登记）。两种情况下，控制权回到 CMD 之前组装都已完成，所以「CMD 能看到组装结果」成立。今天祭坛的投影在 `AltarActionCommand` 提交之后、事务之外，属于后一种 | ✓（(d) 只写了单元内那种情况，但 (0) 已经覆盖另一种，不影响判定） |

所以 B-2 指出的两条后果都已不存在：

- 对实施者不再有互相矛盾的要求；
- 也没有未申报的可见变化。

## 2. 本次修改与其余条款的一致性

| 关系 | 核对结果 |
|---|---|
| (d) 与 (c) | 一致。祭坛的 CMD 抛出后：单元外登记的祭坛消息照常刷出，`BlockAsync` 的关连接请求一律执行，来源连接（若有）最后关闭。这与今天「其他收件人先收到消息，再被封锁」的顺序相同。定时器经 `FailAsync` 触发时没有来源连接，(c) 的「若有」已覆盖 |
| (d) 与 RQ-036 | 不冲突。RQ-036 只管传输层交付失败、超时、队列超限；(d) 不再关连接，两者没有交集 |
| (d) 与 VC-4 | 一致。祭坛今天的 `Failed` 同时包含组装失败和传输失败。改造后：传输失败发生在 CMD 之后，按 VC-4 只关失败的连接；组装失败按 (d) 回到 CMD，封锁全体 |
| (d) 与 DR-5、DR-6 | 一致。组装失败的条目被丢弃、不刷出，所以按「刷出时推进或记录」，水位线不推进、配额不记已送。这与今天「只认 Delivered」相同（`PartyTownProjectionDelivery.cs:275-286`，以及 `*QuotaDailyRefreshService.cs:32`、`UsageQuotaProjection.cs:114`） |
| (d) 与 DR-7、DR-8、VC-6 | 有措辞上的张力，但不构成实质，见残余 R-5 |
| (d) 与附录 G | (d) 不再引入可见变化。钱包变化、PvP 参与者两类通知在组装失败时不再重试，由 VC-5 的「不再重试一次」覆盖。这两类是仅有的重试上限大于 0 的注册：`ProtocolModule.cs:452`、`:616-617`。措辞问题见 R-6 |
| AC-009 改写与 RQ-025 | 一致，见第 3 节 |

本次修改没有引入直接回归。

## 3. R-2～R-4：已写清楚

- **R-2（AC-009）**：已改为「效果结算请求的协议 CMD 缺少 RQ-025 状态 (2)/(3) 时抛出程序错误；非效果消息没有账本条目时照常执行」，与 RQ-025 的三种状态逐字对应。已关闭。
- **R-3（RQ-090 方案 ②）**：已写明 WireSmoke 缺少的场景（例如进本）由测量工具按线级集成测试的驱动方式补齐，并标注「合成负载」的局限。已关闭。
- **R-4（端到端口径）**：已注明两点：多段 CMD 的首条回包按 VC-1 推迟；没有回包的 CMD 不计入。已关闭。

## 非阻断残余风险

- **R-1（沿用 r2，未处理）**：关连接请求在 (b) 与 (c) 下的措辞不对称。(b) 说回滚单元内登记的条目会被丢弃，(c) 说关连接请求一律执行。今天没有调用方会在回滚单元内登记关连接，所以没有实际后果。Plan 或实现时，在发件箱的单元测试里定一种语义即可。
- **R-5：(d) 的兜底句「依据 Failed 做业务决定的 CMD 照原样处理」，与几条专门为交付失败写的条款，在组装失败时字面上可能冲突。**
  - GM Cera 提醒：今天组装失败会得到 `unconfirmed`（`GmCeraDomainService.cs:24`）。按 (d) 照原样，仍是 `unconfirmed`；而 VC-6 写的是「不再有未确认」。
  - 房间机制：今天组装失败会抛 `IOException`，等可靠事件重试（`DungeonRoomMechanismNotificationSubscriber.cs:18-19`）。按 (d) 照原样，仍会重试；而 DR-8 的承接只写了「失败连接关闭、重连后下发」。
  - 队伍：今天在 CMD 内对 `Failed` 最多尝试 2 次（`PartyTownProjectionDelivery.cs:249`、`:269-286`）。按 (d) 照原样，仍会重新组装。
  - 这几条都只在 composer 抛异常这条缺陷路径上出现，没有测试钉住，对客户端的可见影响至多是 GM 结果字符串不同。所以不构成实质。
  - 建议 Plan 写一句明确：DR-7、DR-8、VC-6 说的是传输层交付失败；组装失败按 (d)。如果用户希望 VC-6 对组装失败也成立（不再有 `unconfirmed`），要在附录 G 调整。
- **R-6：(d) 标题「与今天一致」不完全准确。** 钱包变化、PvP 参与者两类通知今天组装失败后会重试 1 次，改造后不再重试，这由 VC-5 覆盖。但 VC-5 的「失败时断开该连接」只适用于传输失败；组装失败按 (d) 不断开。实施时以 (d) 为准即可。

## 结论

`approved`。

- B-2 已消除：RQ-031(d) 与今天 `NotificationBus` 的逐收件人 `Failed` 语义一致，祭坛「任一 Failed 即封锁全体」由「CMD 可见组装结果、照原样处理」保留，与 RQ-023(c)、DR-4、VC-4 一致。AC-011 两条新用例可判定。
- 本次修改没有直接回归。R-2～R-4 已写清楚。
- 剩下的 R-1、R-5、R-6 都是措辞层面的残余，只涉及缺陷路径或理论情形，Plan 阶段一句澄清或一条单元测试即可消化，不需要再走一轮复审。
- 本审查只覆盖上述范围，不代表整体完成。
