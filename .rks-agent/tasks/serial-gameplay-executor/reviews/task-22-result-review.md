---
kind: rks-review
target: tasks/TASK-22.md
verdict: approved
---

# TASK-22 结果快速审查（T-005b，独立审查者）

- 审查对象：HEAD `09e4983c` 加未提交差异（`Server/src` 99 个文件，＋285 −1021；tests 删 4、改 43、新建 2）；Result 第 1 轮，合同 Revision 1，主控方裁定 0～3。评审者只读，本文件由主控方按评审回报落盘。
- 适用标准：reviewing-work 的 Task Unit 与 Code 准则。
- 评审者自查：逐个读 45／50／55／57 号行为、`SelectedSessionSafety`、`DomainEventDispatcher`、`ScheduledTaskHostedService`、`EffectOperationLedger`、`JoustMaintenanceService`、4 个事务准入门使用者与婚礼文件，对照基准；`ProtocolModule.cs` 归一化比对，只删访问方式实参，118 处 `CommandExecutionKind` 不变；过滤运行 Application.Tests 63 条全部通过。

## 发现（均为低严重度，不阻断）

- **L1**：判据 J3 名单漏 `EventWaitHandle`、`WaitHandle`、`SpinWait` 与 `System.Threading.Lock` 的显式 `Enter`，类注释「no other waiting primitive is used at all」说过了头；J2 只按文件不按处数判断。
- **L2**：调度器合并语义并非处处「与改前一致」：基准默认 4 个槽几乎不争用，已入执行器队列未开始的 RunOnce 任务再次到期时会补跑一次；现在合并为一次、按最新时刻执行。符合裁定第 2 条意图且实际触发不到，不算缺陷，措辞应改为「保留并扩大到执行器排队期间」。
- **L3**：`t005-closure.md` §7「每项都有用例守着」与 F-93 矛盾（`BlockAsync` 的 `DiscardExact` 无用例；H1～H3 未变异）。
- **L4**：`GuestList.AccountsAsync`（`:59-60`）已无调用方，原注释说明它就是婚礼推进取的租约；`GuestList` 只经 DI 自动解析，删除只需连带删构造参数 `ICharacterTable owners`。F-94 的保留理由不准确。
- **L5（信息）**：并发排空时，失败若发生在 `TryBeginAsync` 之前且两个排空者持有同一条旧读，后一个作业会再调一次 `FailAsync`（`attempt_count` 多加 1，`next_attempt_at` 重置）。订阅者不会重复调用，「每条只消费一次、退避中不提前重试」仍成立；生产每组只有一个排空者，触发不到。

## 45 号行为与 `BlockAsync` 的非锁职责（附录 H 第 45 行）

只删了 `using`、构造参数 `lifecycle`、租约列表、借用循环与 `finally` 释放；其余逐字未改、次序未变；两次 `DemandRecipient` 与 Capture 内的 `DemandCurrent` 都在，符合 R9。

| 项 | 改后位置 | 守着它的用例 |
|---|---|---|
| H1 `BeginMain` | `:12` | 未变异（基准阅读） |
| H2 `DemandRecipient` ×2 | `:13`、`:25` | 未变异 |
| H3 `Capture` | `:15` | 未变异 |
| H4 账号与会话一致性 | `:16-17` | `A_command_whose_account_differs…`（S6 只红它） |
| H5 参与者集合含报告会话 | `:18-20` | `A_participant_set_without_the_reporting_session…`（S4 只红它） |
| H6 逐个 `DemandCurrent` | `:23-24` | `A_participant_that_is_no_longer_current…`（S7 只红它） |
| H7 不确定时逐个 `BlockAsync` | `:29-33` | `An_uncertain_outcome_fences_every_participant…`（S5 只红它） |
| H8 `scope.Enter` | `:26` | `The_handler_runs_with_the_captured_recipient…`（S8 红它及另 3 条） |

`SelectedSessionSafety.BlockAsync` 只删借用一行与构造参数；`DiscardExact` 无用例，已登记 F-93。

## 50／55／57 号行为删除

| 被删内容 | 判定 |
|---|---|
| 50 `OnlineUserExecutionBehavior` | 只有访问方式相关的接口校验，随访问方式规格消除 |
| 55 `AccountExecutionBehavior` | 接口校验与空账号列表校验，无日志、上下文、隔离标记或补偿；账号与会话一致性在 45 号保留 |
| 57 `TransactionalExecutionBehavior` | 只取门；事务开启在 60 号，60 号只改注释 |
| `CommandExecutionContext` 两个字段 | 无其他读者 |
| 保留的 4 处 `AccountIds` | `BuyItemCommand`、`HousingCommand`、`CapsuleCommand`、`WeddingLovePointCommand` 的处理器都传给领域服务 |
| 删掉的 14 处 `AccountIds` | 无生产读者（`GrantGmCeraCommand` 读的是 `Input.AccountIds`，仍在） |
| `OnlineUserId` 的 3 个计算属性 | 无读者 |

## 已核实无问题

- **事件消费**：`TryBeginAsync` 条件 `state=0 AND next_attempt_at<=@now` 加执行器串行保证单次领取；新并发用例两段分别区分两项条件（读代码结论，该变异未实跑）；N5 由 `RunAsync` 驱动，断言保留，S12 使其变红。
- **调度器**：入队前的只读 `Requested` 读取只服务于失败日志、无副作用；`Global_limit…` 断言不变，S11 只红它；去掉 `Task.Run` 后异常、取消、`completed.TryWrite`、`WhenAll` 的路径与改前一致（差异见 L2）。
- **事务准入门**：4 个使用者保留「已在事务内直接执行，否则 Begin／执行／Commit」；分发器 3 处的事务次序不变。
- **婚礼接口**：`WeddingAccountsAsync` 与 `AdvanceMarriageWeddingCommand.AccountIds` 无残留读者。
- **判据**：29 条名称规则覆盖全部 `Server/src`，词边界；C8、S1～S3 有区分力；信号量名单 7 项都有理由。
- **测试处置**：D=45 与少掉的方法逐一对应；断言多重集合有变化的 11 个文件都对到处置表；N-V3 改写理由成立；编译迫使文件断言多重集合不变。
- **收口**：Plan T-005 行与附录 F「删除」栏逐项有证据；AC-017 记部分满足；投递三项归 T-006；VC-7 注明 `ExactSessions` 例外；「TASK-22 提交后 T-006 进入条件成立」站得住。
- **数字**：8192 ＝ 8170 ＋ 2 ＋ 20；取证 39 条三种加法都对；S9、S13 登记 F-93 足够。

## 主控方处置

见 `tasks/TASK-22.md`「主控方处置（评审后，提交前）」：L1、L4 就地改代码与判据并补反例，L2、L3 改措辞，L5 登记 F 项。
