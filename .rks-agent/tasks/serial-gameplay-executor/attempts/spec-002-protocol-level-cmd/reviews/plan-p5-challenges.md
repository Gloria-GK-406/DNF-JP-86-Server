# Plan P5 有界对抗性挑战

## 目标、来源与范围

- Generator：fresh independent `b_plan_p5_challenges`，与 Plan producer、Spec producer／reviewer及后继 Plan reviewer 分离；应用 `generating-adversarial-challenges`。本记录是供独立评审裁定的攻击结果，不是 Plan review verdict。
- 唯一工作目录 `/Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021`，HEAD `f42efb55bedad71dc1196100e39255bc483ed72c`，原任务分支未变。
- 精确目标：[Plan P5 ready](../plan.md)，71863B，SHA `88920242fc6f685de9431a96a65112db5d8b3b2d0ab0273d41cc3d51b122aff0`。
- 匹配 [P5 amendment](../verification/plan-p5-amendment.md)，12843B，SHA `5d2a9c1aa0fff1877b9b75b6f904293f75f5bdf40b8e6dfe404c2af98c4553ba`；[原P4快照](../verification/evidence/plan-p5-amendment/plan-p4-before.md) 61551B，SHA `db6da8763f55366a4d44358f4537d2106f6f55d1fab0e55b6d08e357ff5d8ad7`。
- Governing sources：根 Contract r4 Goal、保护／可见变化约束及 SC-8B；子 Contract r1；已批准 [Spec B4 ready R2](../spec.md) 42632B，SHA `9163ce4ce16d6debf4c0898094af1468cc1f7cd28b8d3207e7aa5cb8933d3b60`，及其 [fresh review](spec-b4-review.md)；TASK-28 已提交旧行为报告和七个正式 JSON。B4 的精确买家保护是本轮既定 WHAT，不重新选择或暗改。
- Attack scope：仅 Cera 单协议的普通写／NonRequest、窄动作映射、真实确认回滚结果、默认失败与精确参与者、真实 scope／已登记条目、旧强测试迁移和证据影响范围。没有扩大成全 Spec／Plan 审查，不要求高层 Plan 预写局部 API、物理白名单或未来 Task。

## 保留挑战

**high-risk 0；low-risk 0。** 未发现需保留的独立 material HOW 缺陷，未为数量或格式要求制造挑战。此结果不代替后继独立 Plan review，也不证明未来产品或测试已经通过。

## 实际攻击路径与不保留原因

1. **把所有回滚或动作异常恢复成 ACK。** 实读 `CommandWorkUnits.cs:95–128` 的 BusinessRollback 与 CommitAsync 窄 span：现有 CommitRolledBack 要求 KnownStorage 来源、原 cause 同对象、ConfirmedRollback、无 guard／request failure、非取消／非typed，并在真实 finally 清理后完成 tracked operation。Plan §4 的 P5段明确复用这些条件，不新增恢复 API；动作只在原 clock／domain窄 try 保存异常，以 RequireRollback(cause)保因，mapper在单元完成之后。`ICommandWorkUnits.cs` 的 RequireRollback(cause)会拒绝取消／typed并保 sticky。C2的第二 rollback错误因此不能靠先捕到普通 SQL 就正常返回；P5直接要求真实双cause和原资产事实。没有保留该攻击。
2. **无足迹／空参与者绕过，或普通成功被一律围栏。** 实读 Cera领域入口及写前的 DemandEffects、现有 NonRequest一精确actor判定、`ExplicitNonRequestFootprint`、FailAsync与精确Block。Plan §4/P5及§5具名消费限制明确一次Ordinary write、一个含原physical context及显式metadata的买家，无Main／legacy桥。Committed／Uncertain只有在默认失败收口时才执行保护；健康成功／拒绝／确认回滚不Fence。B4先前CH-1的WHAT纠正已被落实，没有新的HOW冲突。
3. **scope故障被 body throw 或零帧假证据代替。** 实读 CommandBus 的 drain→request结束→真实scope cleanup→最终FailAsync，以及 GameplayOutbox.Complete 对已提交／单元外条目的保留、登记顺序和失败来源关闭置后。P5明确旧fine scope须对应新whole真实DI scope，记录故障前已登记消息及最终实际发送／关闭；禁止撤条目、提前flush或追加StorageFailureACK，也不承诺零帧。精确保护对替换身份／其他连接的断言要求实际状态或受控真实替换，不只靠Exact名称。该路径没有被遗漏。
4. **删fine后强测试变弱，或伪造wire字段。** 实读原Cera handler／fine／domain／成功通知和 Host 的 `RealShopPurchasesCompeteWithGrantsWithoutLostSpendingOrRoleMultiplication`：其三次真购买与HTTP grants、逐receipt及97810／三operation是实际旧意图。P5保原日志／错误码／通知受众和顺序，明确原direct-fine用例按可表达范围转真实whole／decoder或真实domain＋原显式事务test seam，不造入站字段、不留legacy转发、不删NoSession／回滚断言。原session／ownership拒绝不能因actor装配被无说明改成异常。具体fixture／结果形状可由临近Task以原强断言低成本验证，不是必须现在新增机制的高层缺口。
5. **把旧受控观察冒新native／物理验收，或混全量轮次。** 已读TASK-28正式七例Summary和cause图：A/B分别为action1811与实际Commit787确认回滚；C1/C2为Uncertain、C3为Committed scope故障，旧正常返回且无close。P5按这些阶段要求同provider真decoder的新对照、原原因引用、全部列含NULL/BLOB及事件、真实sender／受控内存transport的实际bytes与CloseAsync；控制故障及观察提前enlist的局限保留。源freeze、实际影响full、逐case变化、原DFO expected/control及混轮次原bytes／provenance条件均明确；若实际触core/API须返主控并扩大八freshfull。它没有把旧9014或未来T-012的证据冒称当前新通过。

## 交接与限制

本次仅静态源码和已提交观察读取，没有 native、新测试、数据库操作或重新生成证据。没有保留针对未来尚未执行检查的“缺证明”问题；普通实现错误仍由临近Task readiness、真实前后oracle、原强测试和required结果快审发现，不能因本记录0挑战跳过这些控制。

复核当前Plan／amendment／Spec身份与HEAD、index空；只新建本文件。目标、Spec、原P4快照、旧Task和其它证据均未改。本记录落稿后保持immutable，交另一fresh独立reviewer对当前P5给出正式判断；没有自批、stage／commit／remote、版本／tag／部署／游戏或Task29。
