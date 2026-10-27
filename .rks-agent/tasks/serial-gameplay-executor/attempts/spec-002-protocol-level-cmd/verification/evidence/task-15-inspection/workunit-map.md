# TASK15 实际边界与判据

此文件解释实际实现与已跑定向判据；最终正式轮身份以 source-freeze-03、formal-summary、test-delta 和命令 meta 为准。Task r2、37 个精确源码路径，不含未授权模块。

| 完整协议 | 原业务分支与新唯一入口 | 工作单元／拒绝／原成功输出 |
|---|---|---|
| PackageOpening 65696 / A0 | GamePackageOpeningCommand；AvatarInput 有值走 avatar，否则 package | 仅原有效 fine 分支一次 Main；普通业务 throw 带 cause 正常回滚后映原拒绝；成功原 A0 popup，继而 replaced clear、绝对 updates、cleared 等 |
| AvatarPackageOpening 66055 / 207 | GameAvatarPackageOpeningCommand；Choices.Empty 走 package，否则 avatar | 同样一次 Main；成功原 207 ACK、A0 reward popup，再原库存／premium／creature／gold/following 顺序 |
| MagicBoxSingleOpening 65744 / D0 | GameMagicBoxSingleOpeningCommand；原 5B 输入、count=1、expected IDs=0 | 同样一次 Main；原 D0 ACK/source/material/rows，随后原绝对库存 |
| MagicBoxBatchOpening 66640 / 450 | GameMagicBoxBatchOpeningCommand；原 15B count/expected IDs/material/luck | 同样一次 Main；原 450 ACK/primary/bonus/luck，随后原绝对库存 |

四 thin handler 只交原 Input/Dispatch 给同一 bus；四旧 fine CLR/handler/registration 删除。每个 exact logical/CLR 只绑定一个 Protocol/Direct whole，只既有 CommittedReplayCurrentStateBatch 权限。无 NonRequest、初购 raw、legacy bus 转发或延迟编码。四 slice 的 DTO/decoder/encoder 前段逐 byte 保留（仅必要 Dispatching using）；OpeningDelivery/composer、domain/规则/codec/持久 schema 均只读。

无 Input 优先 InvalidRequest，再判 session/online NoSession。初始 ProtocolEffectRejected 不因晚 selection 升权。无 Main 正常结束 Pending→Rejected；opening 的 domain 抛业务拒绝经 RequireRollback(E) 为 ConfirmedRollback，不能误写成其它装备协议的 RefusedCommitted。只在新的 admission 重试同 entry/原 Ordinal；Freeze 后的 rollback projection 真实保留，本 CMD 不重新 claim Main。

## 结果与来源

ICommandWorkUnits.WriteResultAsync 只由 runner 构造 Returned<T>/CommitRolledBack。Returned 包含正常无参或带 cause RequireRollback；CommitAttempt 仅声称实际 CommitAsync span，不声称都进入 native Commit。旧 WriteAsync/ReadOnlyAsync 不获此恢复。

DomainTransaction 在本次实际 participant Prepare／唯一 fallible Commit 调用前后采集 ITransactionFailureSource.LastFailure；同 source 实例、原 Exception 引用、匹配 phase、全新 fact/occurrence 才认事实，否则 Unknown。只有实际 SQLite Open/Flush 物化或 pending.Commit 位置的 provider SqliteException 是 KnownStorage。facet.ValidateForCommit 的窄 catch、SQLite readOnly/staged/unprepared，真实 ItemLifecycleScope active/custody、CharacterContainer.Changes 绑定版本及 RunItem session active/prepare/version/unprepared 为 Guard；所有消息、异常对象、业务条件、顺序不改。

runner 在 action 回值后也查本事务已入伙 Guard，故 catch guard 后 RequireRollback 不能逃过。Commit-only 恢复还要求 ConfirmedRollback、无取消/typed/既有 sticky/guard、请求/作业/current/精确 participants 健康以及全部 unit/lease cleanup 成功。World/Login/Creature 等未声明来源参与者仍 Unknown，不把“没发现 guard”等同于已证明可恢复。

正常带 E 回滚用实际 AbortAsync(E)；原反向 rollback 原样 prepend E。局部 failure stage 区分真实 guard/overload 拒绝/Abort(E) 的传播与后置独立操作；current 或 cleanup 又抛同 E 仍保两个 occurrence。先 E1 普通声明、随后独立 typed/cancel E2，不能以 RejectedRollbackCause==E2 丢 E1。没有全局 tracker、ambient 请求、bus/raw 收口算法变化。

## 实际测试保护

- Phase0 old route 两完整协议 oracle：Package 奖励 INSERT 19/1811；Batch 实际 FK1、奖励/child/receipt 已成功，真正 SqliteTransaction.Commit 19/787。两者原 ACK、旧 pipeline/handler Error 同 cause、ConfirmedRollback、资产/材料/gold/luck/冷却/事件回滚、同 entry/Ordinal retry/current replay、nofence/no source close。原 phase0 56 evidence manifest、r1快照不改。
- 新 route 同两 oracle：只将观察点从旧 fine wrapper 移到同 provider 的实际 domain wrapper；原 Error cause 保留，handled whole 只一处 Error，不保 legacy 重复 Error。原 raw body/hash/seq/ACK/资产和 retry 期望不变。
- Flow 全四真实 shape 成功／malformed/no Main／domain refusal rollback／初始 rejected 不升级；Package 纯选择与 avatar 选择、Avatar empty choices；ACK/popup 在绝对 NOTI 前；初次 NotiBus encoder failure 是正常逐目标 Failed，成功仍 committed/nofence，后续只 current replay。
- 真实 opening SQL E + 受控独立 rollback X（含同对象）：typed、两原 cause、真实资产 rollback、无失败 ACK正常返回、精确 reporter fence，participant close 后 source 最后 close（实际两个同物理连接记录）。独立 scope disposal 后已提交奖励保持且仍默认 typed/fence。
- 真实 post-current 四组合：业务 E 或真正 native Commit E 后，同 provider query 故障 X，不同/同一异常对象均两因；不产生正常结果/失败 ACK，按实际 precommit rollback 默认 no new fence/source close。
- KnownRollbackWorkUnitTests：真实 SQLite Commit 与 Prepare Flush 的 original provider cause/真实资产 rollback 正例；真实 detached SQLite custody 完整 unit 的 Guard 与 catch+RequireRollback 反例；unknown/no fact/stale/异 cause/phase/source 以及 plain InvalidOperation 反例；E+X/refusal post-current/typed/cancel cause/三个 phase cancellation/nonfallible after real SQL commit；原未 await guard 先 sticky，再 drain rollback/cleanup，不将其冒称已到 native Commit。
- Persistence 来源逐点：真实 custody 和 facet validation、真实绑定版本切换、两真实 RunItem session 的 winning state/version guard、active/unprepared、真实 Prepare SQL 与 native Commit facts；原 TransactionUncertaintyTests 七 case 原样执行。
- 六旧 fine caller 文件：OpeningDomainTestDriver 三具名 typed 方法，同 host.Services 外部 TestGameplay、实际 owner/current、真实 DomainTransactionScope、每调用新 test footprint lease。纯转换仅通过四个明确 typed CreateDelegate 调原 internal OpeningWireSlots，无复制算法/Invoke包装/guard反射。原 SQL/OpeningRejected Throws、60箱/luck/expiry/选择/复本字节等断言保留。DarkRepulsor 两 directhandler Empty metadata 改真实 decoder/dispatcher/fresh sequence，各次是新请求；其余纯领域探针继续 Throws。
- r2 唯一额外编译 caller GameLoginTests.DirectWorkUnits 新方法仅 NotSupported，旧方法和全部 Login case 保留。
- 旧默认 Write/ReadOnly、finitefamily、NonRequest、两 B2 raw/opaque attempt 正反例及所有原 case 随最终八 full 实跑。没有新 raw 许可或把 notification Failed 隔离泛化到外抛错误。

## Mutation

最终源码 mutation-guard-02 仅删除 owned runner 的 Guard/Unknown 恢复排除。真实 SQLite detached custody 出现 returned=1、rows=0、ConfirmedRollback、cause=none，原错误被误变结果，测试实际变红；action 中已报告 guard 对照仍红线保护通过。无编译失败冒充 RED。finally 原 bytes/SHA50336652fad2b84338641bd9243fb9fa48ada532982779f05c91124ce3ea4817 完整恢复，恢复后95 case GREEN，再 freeze02 全量正式轮；随后仅 RunItem 非fallible非法直接Commit Guard阶段精确为Action，现有case加false/Guard/Action断言，完整freeze03重新正式核验。

首轮 mutation-guard-01、formal01（8699/新增55/删除0）、所有编译/新 fixture 假设诊断、post-current 与重复声明实际 RED/GREEN 全保留；首轮不与最终轮混用。final03 两个变化路径只为该阶段标签及既有case断言，不加case/改enum/其它来源。最终源不再写入，待结果 required 独立快审与主控检查点。总目标其余135msg/180fine/72source、5保护/8过渡、T-012最终八 full/单CMD≤2秒测量及Spec/根封存仍 pending。
