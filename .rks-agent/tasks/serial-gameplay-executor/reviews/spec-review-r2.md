---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/spec.md
round: 2
contract_revision: 2
verdict: changes_required
remediation: bounded
remediation_target: spec
reviewer: independent subagent (fresh context; reviewing-work Spec reference + sdd-governed Adversarial Review Transitions; bounded re-review scope)
---

# Spec 复审 r2：serial-gameplay-executor（contract revision 2）

## 范围与依据

- 目标：`spec.md` 当前文本（`state: ready`，`contract.revision: 2`），worktree HEAD `a02bce46`。下文代码路径相对 `Server/src/`。
- 这是第一次 bounded 复审，只核对三件事：
  1. B-1 是否已消除；
  2. 本次修改是否引入直接回归或新矛盾；
  3. 非阻断 1、3、4 与 RQ-090 的改写是否清楚。
- 不重做全面审查。上一轮已有的证据沿用 `reviews/spec-review.md`，挑战集沿用 `reviews/spec-challenges.md`。
- 准则：`reviewing-work` 的 Spec 参考；`sdd-governed`「Adversarial Review Transitions」。

## 1. B-1：已消除

逐条对照原证据：

| 原后果 | 代码证据（复核） | 新写法如何承接 | 判断 |
|---|---|---|---|
| 怪物死亡封锁后重新抛出，其他参与者的关连接被丢弃 | `MonsterDeathSlice.cs:440-444`、`:485-497`：对每个 captured 调用 `BlockAsync`，然后 `throw`。`SelectedSessionSafety.cs:59-72`：`BlockAsync` = Fence + 丢弃生物运行时 + `RequestCloseExactAsync` | RQ-031(c)「关连接请求一律执行」；RQ-023(c)「封锁集合中的每个会话」；AC-011 线级回归第 1 条补上了「关闭」断言 | ✓ |
| 选角在 Publish 之后失败，补偿后同伴收不到清理通告 | `SelectCharacterHandler.cs:172-183`、`:207-231`：先投递清理通告，`:254-270` 再关闭，`:273` 才 `failure?.Throw()` | 补偿命令自己开边界落定，通告在它之后、单元外登记。按 (c)，它们属于「工作单元外登记的条目」，照常刷出；AC-011 线级回归第 3 条 | ✓ |
| 准入拒绝的 x08 两条消息丢失 | `SelectCharacterHandler.cs:150-169`：拒绝来自 Prepare 抛出，事务已回滚；x08 在 catch 中、事务外发送。`SelectCharacterLifecycleTests.cs:425-437` 断言 `["prepare","x08"]` 且 handler 不主动关闭，断线由抛出引起 | 按 (c)，x08 在回滚单元之外登记，刷出后再关闭来源连接；AC-011 线级回归第 2 条 | ✓ |

- VC-2 已收窄为「回滚或不确定工作单元内登记的消息不再发出」，理由改为指向 RQ-031(c)。这与今天一致：投影在提交之后才发生，`ProjectAsync` 也不能在未提交的写事务里调用。
- DR-15 指向 (c)，「异常即断线」保留。

**可判定性。** (c) 按「登记时刻相对工作单元的位置」与「工作单元的结局」二维分类：已提交、单元外、回滚、不确定。分类是确定的，三条线级回归都能失败。B-1 关闭。

## 2. 本次修改引入的问题

### B-2（阻断）：新增的 RQ-031(d) 与 RQ-023(c)/DR-4 冲突，并带来一项没有列入附录 G 的可见变化

**问题。** (d) 规定：为某个收件人组装失败时，只丢弃他的条目、关闭他的连接，「不让整条 CMD 失败」。它想对应的是今天「逐收件人记为 Failed」的语义。但今天有调用方把这个 Failed 当作提交后发布失败处理，进而封锁全部参与者：

- `Notifications/NotificationBus.cs:205-254`：`ComposeAsync` 抛出的异常在 `AttemptAsync` 中被捕获，记 Warning，返回 `NotificationOutcome.Failed`。
- `Protocol/Game/Altar/AltarProjection.cs:94-108`：只要任一 attempt 为 `Failed`，就抛出，再转成 `TransactionOutcomeUncertainException(PostCommitPublicationFailure)`，对**每个**接收者调用 `safety.BlockAsync`，然后重新抛出。

所以今天祭坛的某个收件人组装失败时，结果是封锁并关闭全体参与者，命令失败。Spec 里有两处说这条路径不变：

- RQ-023(c)：「提交之后、CMD 之内发生的非传输失败（组帧……）保持今天的处理方式」；
- DR-4：祭坛的「非传输失败……仍按 RQ-023(c) 封锁参与者集合中的全部会话，处理方式不变」。

而 (d) 的写法是「只关他一个，CMD 成功」。两边直接冲突。

此外，即使不看祭坛，(d) 本身也是一项可见变化：今天组装异常的结果是「记 Failed、按注册的重试上限重试、连接保留」，新写法改为断开该收件人。附录 G 没有对应条目：

- VC-4 只涵盖传输层交付失败；
- VC-5 只涵盖两类通知不再重试；
- VC-8 只涵盖超时与队列超限。

RQ-082 规定可见变化只允许附录 G 所列各项，契约 Constraint 也要求可见变化须列明。

**后果。**

- 实施者面对相互矛盾的两条要求，只能自行选择，而且没有测试拦着：检索 `tests/` 后没有发现钉住「祭坛组装失败 → 全体封锁」的用例。
- 无论选哪一边，结果都没有在 SC-11 的规划停点向用户申报。
- 「组装异常即断线」会放大影响面：一个城镇广播 composer 的缺陷，今天只产生 Warning；改造后会断开该区域内的全部收件人。用户应当在知情的情况下接受这一点。

**最小修改**（bounded，目标 spec，不改契约边界与风险依据）。二选一即可：

- **(A) 保留 (d)，把它申报出来。**
  1. 在附录 G 加一条：「组装失败时只关闭该收件人连接；今天是记 Failed、可重试、不断线。祭坛不再因单个收件人组装失败而封锁全部参与者」。
  2. 在 RQ-023(c) 与 DR-4 的「非传输失败」中，明确把「逐收件人组装失败」排除出去，像 VC-4 排除传输失败那样。
- **(B) 给 (d) 加一个例外。**
  1. 对于带参与者集合、把投影当作提交后发布的 CMD（今天的祭坛），组装失败交回 CMD，按 RQ-023(c) 封锁全体参与者；其余 CMD 按 (d) 隔离。
  2. 「组装失败即断开该收件人」仍须在附录 G 列一条。

不管选哪一种，都在 AC-011 或 AC-014 补一条祭坛组装失败的用例，断言所选结果。

### 其他核对：没有发现矛盾

| 关系 | 核对结果 |
|---|---|
| (c) 与 RQ-031(0) | 组装点不变；(c) 只决定刷出与丢弃，不改变组装时刻 |
| (c) 与 (b) | 一致，只有一处措辞上不对称，见残余 R-1 |
| (c) 与 RQ-023(c) / DR-4 / VC-4 | 一致。`BlockAsync` 的关闭在主结算单元结束之后登记，即使 CMD 正常完成也会执行 |
| (c) 与 DR-15 | 一致 |
| (c) / (d) 与 RQ-005「完成」 | 一致。「完成」= 发件箱交给发送端，失败路径同样如此 |
| (c) / (d) 与 RQ-008 | 一致。关闭由发送端在交付之后执行，执行器不等待 |
| (c) 与 RQ-012 | 一致。关闭在此前登记的条目交付之后执行 |
| (d) 与 DR-10 / RQ-034 | 不冲突。清理通告中所有者那一半是直接发送（`SupplementalSynchronizationMessages.cs:252-262`），不经 composer；同伴那一半本来就是逐收件人 |
| (d) 与 DR-5、DR-8、DR-13 | 与这几条的新承接方式（失败连接关闭）一致 |
| RQ-036 的新增内容与 RQ-005、RQ-033 | 不冲突 |
| 附录 G 其余各条 | 未受影响 |

## 3. 顺带核对的澄清项

- **非阻断 1（RQ-025、附录 H）：已澄清。**
  - 三种合法状态与今天对应：非效果消息没有账本条目；无选中会话的效果请求走 `EnterWithoutSelectedSession`，可以回拒绝、不可结算；有选中会话的效果请求有账本条目，序号必须前进。
  - 程序错误只限于「效果结算请求缺少 (2) 或 (3)」。
  - 附录 H 的账本行已补上序号前进与 `EnterWithoutSelectedSession`。
  - 残余：AC-009 仍写作「协议 CMD 在缺少请求级上下文时抛出程序错误」，没有跟着收窄，见 R-2。
- **非阻断 3（组装失败）：已写明，但写法引出了 B-2。**
- **非阻断 4（RQ-036）：已澄清。**
  - 上限按字节计；
  - 下限 ≥ 单条 CMD 对单连接最大发件箱的 2 倍，选角是已知最大者；
  - 由实测加判据钉住，AC-013 有对应断言，并要求选角全序列不触发超限。
- **RQ-090：基本清楚。**
  - 录制来源按优先级写明：先用用户提供的 A21 真实录制，没有时用 WireSmoke 合成负载，并标注局限；
  - 基线对照改用端到端延迟。
  - 残余见 R-3、R-4。

## 非阻断残余风险

- **R-1：关连接请求在 (b) 与 (c) 下的措辞不对称。** 按 Definitions，发件箱条目包括关连接请求。(b) 说在回滚单元内登记的条目会被丢弃，(c) 却说关连接请求一律执行。所以「回滚单元内登记、CMD 正常完成」时，关闭会被丢弃。今天没有这样的调用方：生产关闭点都在提交之后的投递代码或 `SelectedSessionSafety` 中，`ExpertStoreDomainService.CloseExact` 与 `HousingState.CloseExact` 不是关连接。建议在 (b) 也写明「关连接请求不随工作单元回滚而丢弃」，以免将来出现分歧。
- **R-2：AC-009「缺少请求级上下文时抛出程序错误」应改成与 RQ-025 一致的表述**：效果结算请求缺少状态 (2) 或 (3) 时抛出。否则可能被读成「非效果消息没有账本条目也算错」。
- **R-3：RQ-090 的方案 ② 覆盖不到「进本」。** `Tool/WireSmoke` 目前只有注册、登录、建角、选角、接任务（见其 README），没有进本场景，而 RQ-090 要求负载必须包括进本。Plan 要么扩展 WireSmoke 的场景，要么在测量记录里写明缺口。
- **R-4：端到端延迟口径的两个局限。**
  - 按 VC-1，多段 CMD 的首条回包会推迟到 CMD 完成后，选角这一类的对照会显得变慢，属于预期内的变化，解读时要说明；
  - 没有回包的 CMD，这个量没有定义。
  建议测量记录分类注明。
- 本次是该目标的**第二次 bounded 退回**（第一次是 r1）。按 Adversarial Review Transitions，如果 r3 仍要求修改，不能再自动退回，须请用户或上级控制者裁定。B-2 的修正只涉及一处取舍和一行附录 G，规模足以在一次退回内完成。

## 结论

`changes_required`，`remediation: bounded`，`remediation_target: spec`。

B-1 已消除。新的 RQ-031(c) 可判定，三条线级回归能失败，VC-2 与 DR-15 一致。

本次新增的 RQ-031(d) 引入了阻断项 B-2：

- 与 RQ-023(c)、DR-4 对祭坛「非传输失败封锁全体」的「不变」承诺直接冲突（`AltarProjection.cs:94-108`、`NotificationBus.cs:224-254`）；
- 「组装失败即断开收件人」是一项没有列入附录 G 的可见变化。

修正只需在 (A)、(B) 中择一，补一行附录 G，再补一条祭坛用例。复审只需核对 B-2 及其直接回归。
