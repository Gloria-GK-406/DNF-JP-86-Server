# TASK-007 — 不确定事务与会话租约基础

## Task Unit Contract

- Revision: 1
- Outcome: 给后继effect命令fence提供可信事务结果分类、可复用且不会提前释放的会话生命周期Shared租约，修正配套清理锁序。对应Plan T-006，RQ-008/009与AC-007/008的基础依赖，不宣称全入口保护已经完成。
- Dependencies: TASK-006已接受；事务57跨TResult串行且包住UoW60；生命周期操作确有独立门例外。详见verification/preparatory-findings.md。
- Owned: Abstractions事务异常契约，DomainTransaction/Lease/UoW，SQLite事务参与者cause保留，SelectedSessionTransitionGate专属flow借用，disconnect observer及ReturnToSelection锁序，与这些基础设施直接相关测试。本Task结果/coverage。
- Excluded: 不接命令marker／pipeline45或58／fence／精确close／pet discard／claim重放／入站缓存；不改普通Online/Account gate策略，不部署或动运行库，不commit。后继按已验证API逐步接入。

## Detailed Plan

1. 新增具名TransactionOutcomeUncertainException与reason，递归Inner/Aggregate识别，至少覆盖非可失败Commit违约、Rollback失败、持久提交后cleanup失败、原提交或回滚与cleanup共同失败。保留原cause与cleanup cause，不能靠异常文本或把普通IOException全部视为不确定。
2. DomainTransaction：Prepare／fallible commit失败且全部rollback成功仍抛原异常；nonfallible commit违约与任何rollback失败抛typed uncertainty。Lease提供内部AbortAsync(cause)，UoW显式保留handler cause，避免Dispose rollback异常覆盖。不得重写journal或吞失败。
3. SqliteDatabaseParticipant跟踪durable commit是否成功；Settle失败不能伪装普通可重试failure；Commit/Rollback原失败加Settle失败需聚合保留两cause。失败加确认cleanup／rollback保持原异常。读取实际facet settle顺序及pending清空，决定最小可靠改动。
4. 仅SelectedSessionTransitionGate提供受控BorrowOrAcquireSharedAsync。追踪实际同gate／同account／同async-flow已成功获得且仍有效的Shared或Exclusive租约；取消／pending／失败不视为held，拒绝自报skip bool。普通显式Acquire语义及其他gate FIFO／writer优先不变。
5. AsyncLocal安装须在同步Acquire入口发生，不能仅在await之后子调用设置而假定回传。可用mutable frame追踪取得状态、frame active与共享owner ref；每个borrow自己的frame／idempotent lease。父dispose但borrow未结束时底层保护继续；延迟Task.Run继承已关闭frame不能因其他borrow refs仍在而再借，必须真正排队。不同account/gate不借，不新增隐式Shared→Exclusive升级。选择是否virtual化基类入口须核对上转型实际caller，不能藏漏。
6. Observer和Return现在Online→lifecycle；新增scope顺序将是lifecycle45→Online50→Account55→57。把这两清理路径改为lifecycle→Online，保持已捕获identity／connection／session重新检查与原补偿通知。账户绑定改变时安全释放重取，不能拿Online后再反取lifecycle。保留orphan cleanup。不要顺带重写登录身份模型。
7. 意义明确的失败先测：取消安全的双flow barriers复现旧锁序反向等待或缺borrow保护；不把构造/编译错误当RED。确定性测试actualgate outerExclusive借Shared，queued writer时同flow借用而其他flow Shared排队，父结束child仍保护，延迟关闭frame不借，pending cancel／grant-cancel race／doubleDispose／exception／不同account及gate／entry归零；已有显式FIFO和取消保护保留。
8. 事务测试真实SQLite：确认rollback保留原IOException；数据库实际durable后nonfallible违约typed且DB已提交；真实facet cleanup失败typed；handler+rollback双cause；Commit或Rollback与cleanup双cause；递归wrapped/Aggregate分类。测试用临时绝对DB，故障注入准确标明阶段，不能假称真实commit失败。
9. 串行dotnet，build全solution、相关Application gate/UoW/Transaction及Protocol lifecycle/Return/Observer、Persistence参与者测试，保存TRX/日志到verification/TASK-007。根控制器复核diff、新未跟踪文件、结果/锁序与requiredchecks再接受，随后TASK-008接pipeline/fence。最终fullsuite留后继。

## Readiness / Attention Check

父目标仍为范围内所有影响归effect和可证明的一次结算；基础改动是已发现生命周期旁路与不确定重试风险所需，不扩功能范围。TASK-006产物已可用，本单位有唯一writer、临时DB验证和具体后继边界；依赖／权限／检查可执行。纯规则与基础实体责任不变。暂不套新pipeline，避免基础与业务保护相互掩盖。

## Result

```yaml
kind: rks-task-result
decision: pass
claim: TASK-007 owned foundation and required automated checks
controller_acceptance: accepted
```

### 已实现与后继API

- Abstractions新增`TransactionOutcomeUncertainException(reason,message,cause)`，`Reason`枚举为`NonFallibleCommitContractViolation`／`RollbackFailure`／`PostDurableCleanupFailure`／`SettlementCleanupFailure`。`Contains(Exception)`及`Find(Exception)`递归遍历Inner与所有Aggregate branches，不猜异常文字，不把普通IOException当成不确定。后继正常bus返回后publication失败需独立增加具名reason，本单位未接claim。
- DomainTransaction非可失败Commit违约和任何rollback失败均typed；Prepare／fallible Commit失败且rollback全部成功仍原样抛原cause。`DomainTransactionLease.AbortAsync(Exception)`为内部接缝，UnitOfWork catch显式Abort保留handler与rollback两cause，settled状态让后续Dispose不再覆盖异常。没有改journal。
- SqliteDatabaseParticipant显式区分实际durable Commit成功与cleanup；原Commit／Rollback失败和cleanup失败聚合保留。Settle逐项尝试transaction dispose、所有facet Discard及connection dispose，收集全部失败，不再让finally覆盖主cause。普通失败且cleanup／rollback确认成功保留原异常类型和stack。
- `SelectedSessionTransitionGate.BorrowOrAcquireSharedAsync(AccountId,CancellationToken)`为后继45行为的入口：同gate实例、同账户、当前flow真正已获得且仍active的Shared或Exclusive租约可借用；无此租约时按原Shared排队。同步override`AcquireAsync`安装AsyncLocal mutable frame（包括base上转型）；await完成才登记取得，pending／cancelled不算held。父frame关闭但借用未结束时底层lease继续，延迟继承closed frame不能借其他child引用。每frame独立且Dispose幂等；创建新frame时prune已关闭祖先，不改变继承者已有frame的失效状态。普通explicit Acquire不自动借用，不提供Shared→Exclusive升级，Online／Account基础门策略未改。
- Observer及Return改为lifecycle→online。Observer保留无selected不等lifecycle与orphan路径；若取得online后发现此前无selected而现在有，则释放重试，不反取门。已有selected在等待期间被替换时仍使用原捕获identity作精确检查，不清replacement。Return取得双门后检查当前account／connection匹配。原清理补偿、通知、代次检查与释放后close保留；physical context精确close由后继接。

### 真实RED与检查

| 检查 | 结果 | 证据 |
|---|---|---|
| 旧nonfallible违约分类反例 | 1失败；实际普通InvalidOperation，预期具名uncertainty | [transaction RED](../verification/TASK-007/TASK-007-red.trx) |
| 旧HEAD Observer／Return锁序，实际入口两flow barrier | 2失败；lifecycle持有者不能再取得被teardown抢占的online。取消finally清队列后两新源码恢复 | [lock-order RED](../verification/TASK-007/TASK-007-lock-order-red.trx) |
| 完整`dotnet build Server/86JP.Server.slnx --no-restore` | 退出0；0警告／0错误 | [build](../verification/TASK-007/TASK-007-build.txt) |
| Application Transactions／Online gate／Selected gate／UserService／SelectCharacterLifecycle／Return | 88通过／0失败／0跳过 | [Application](../verification/TASK-007/TASK-007-application.trx) |
| Protocol ReturnToSelection／SelectCharacter／Connection | 88通过／0失败／0跳过 | [Protocol](../verification/TASK-007/TASK-007-protocol.trx) |
| Persistence完整项目 | 392通过／0失败／0跳过 | [Persistence](../verification/TASK-007/TASK-007-persistence.trx) |
| `git diff --check`及新未跟踪源码复读 | 无错误；已读具名异常、Selected门及两个新测试文件 | 当前静态检查 |

全部dotnet串行，当前无运行测试；测试用临时绝对SQLite路径且Pooling=false，未触碰服务、运行DB、客户端或PVF，未提交。早期局部foundation／lifecycle／SQLite TRX为开发过程证据，最终上述TRX counters实际复读；筛选重叠不相加作独立覆盖数。没有宣称全solution test或实机通过。

### 证据边界

- 真实SQLite先durable提交，再注入nonfallible内存参与者违约；外部新连接仍读到已提交行且typed。真实facet Discard故障发生在durable之后；多facet故障均被保留，库内两行已提交。
- Commit故障用实际`DEFERRABLE INITIALLY DEFERRED`外键：INSERT／Prepare成功，真实SQLite COMMIT抛error19。cleanup成功时原SqliteException，额外facet cleanup故障时typed并保留两cause，外部连接读0。Rollback故障为实际关闭事务connection后调用Rollback，保留真实InvalidOperation和facet IOException；不是伪造SQLite Commit异常。
- 晚Prepare IOException注入参与者属于Prepare故障，确认rollback后仍相同IOException引用、库读0，未伪称Commit故障。UoW handler failure＋rollback failure和DomainTransaction Prepare＋rollback failure均保留完整cause；递归分类测试含Aggregate非首branch及wrapper。
- Selected门覆盖actual CommandBus中的callee借用、父结束仍保护child、queued writer／别flow排队、普通显式FIFO、base上转型、延迟Task.Run继承closed frame、pending取消后正常取门、100轮grant/cancel、doubleDispose、异常退出、不同账户及gate真实entry数量；最终entry归零。bus与teardown barrier均有取消finally，未靠Task.Delay猜锁状态。其他gate既有250轮取消竞态及FIFO回归保留。
- actual Observer／Return锁序反例只验证取得边界，Return业务／delivery在该barrier下不可达；已有Protocol响应组包与Application完整选角清理回归另执行。新增同connection replacement等待测试证明旧observer不清新selected且保留在线代次；orphan replacement、正常补偿与无selected不等待既有断言均保留。

### 后继与停止点

尚未接45／58 pipeline、命令marker、fence、dispatcher、exact connection close、Creature discard、claim不确定出口或全入口replay；这些必须由TASK-008及之后在上述真实API上接入。当前shared executor和范围内命令还不能据本结果声称AC-007已完成。跨账户／gate借用隔离与lease生命周期已具基础证明，后继必须验证完整命令读取→UoW结算→uncertain discard都在lease内部，close不能等待反取同门。

唯一writer已完成本单位required checks，记录本Task与coverage后停止写入和dotnet。registry与后继Task由根控制器推进。

### Controller Acceptance

根控制器复读当前事务／SQLite／租约源码、新未跟踪测试、实际cleanup diff与最终TRX：Application88／Protocol88／Persistence392全通过0skip，build0警告错误，diffcheck clean。独立effects_foundation_stage_audit复读源码及证据未发现阶段明确缺陷，未重跑测试；其首次Persistence391快照已被writer最终392实际记录更新。接受基础产物，继续实现完整保护，不认定父AC-007已完成。
