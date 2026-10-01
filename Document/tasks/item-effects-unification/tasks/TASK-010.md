# TASK-010 — 全入口请求身份、提交账本与当前状态重放

## Task Unit Contract

- Revision: 1
- Outcome: 18 Request CLR／20 wire／20主结算命令统一安全去重，抽签付款仅一次；重传只刷新当前绝对状态，合法下一次相同参数正常执行。关闭旧六guard及Lottery/Vending业务缓存。Plan T-006，RQ-008/009、AC-007/008。
- Dependencies: 009已正式停笔、独立检查approved并根接受；pure snapshot／typed preencoded batch／exact context／Exclusive刷新API真实可用。007／008的typed uncertainty与recipient fence、45／57／58／60实际保护保持。
- Owned: raw metadata、18请求marker／20主命令marker、dispatcher operation ledger/context、precommit typed footprint、主提交／确认rollback状态；旧guard关闭及入口测试迁移；本单位结果与coverage。
- Excluded: 不增加道具效果／概率费用，不改3XP业务claim，不扩shop/combat/quest accept/clock/selection维护；不deployment/commit/push/liveDB/PVF/client。最终同库组合缺口、全方案回归与人工清单留011。

## Detailed Plan

1. 先读verification/replay-design-findings.md、preparatory-findings.md、009真实接口及actual source。Abstractions新增具名request marker，不让Application引用Protocol；18请求全部实现metadata入口，明确20wire（MagicSeal／Outworld双wire）、20main（0204两主命令／001A两主命令），与3XP及ReadReplay区别。trusted direct bus可无operation frame，真实dispatcher不得让Metadata.Empty绕过。
2. NetworkPlugins在完整Game frame transport边界、decoder前已有all-frame counter Observe保持；metadata增加immutable full body SHA256 hex＋原length，包含decoder忽略尾部，不用parsed fields或checksum代替。完整raw bytes不留可变引用。真实A21 API当前14字节头，reserved11／sequence12；不要照旧UnknownInbound helper offset11。unknown帧仍计ordinal，身份和序号证据只称CurrentImplementation，返回选角reset尚Unknown。
3. Dispatcher以ConditionalWeakTable原physical NetworkConnectionContext建bounded ledger和Semaphore，保留same-ID新对象隔离；gate前捕获exact SelectedSession/recipient，等待后Demand同一recipient/context，不重新绑定角色。同步安装AsyncLocal具名operation frame，覆盖整个handler/发送。首次执行不能跨handler拿lifecycle Shared，否则Fatigue/Lottery/Vending已有Exclusive会自锁。重放直接调用009RefreshAsync(originalContext,exactSession,request)，不预持Shared；其内部Exclusive保护read至send。
4. 准入完整身份含selector/type/sequence/fullbodyhash+length及exactSession，窗口128所有inbound帧ordinal。Fresh next执行新操作，参数相同不是重传；非Fresh只能命中同exactSession、窗口内、完整身份相符条目。Committed刷新当前；ConfirmedRollback重试同请求；Rejected/Blocked/冲突/过旧无可用条目拒绝。Fresh和新Session不清008物理fence。合法全ushort圈回绕可重复seq新操作；没有nonce的古老raw碰撞保持Unknown，不能声称绝对识别。条目预分配，状态Pending／Committed／ConfirmedRollback／Rejected／Blocked分开。
5. 20主marker进入45前后校验operation exactSession与实际command.Session一致；null只用于可信直调。主调用执行identity具名记录，只允许一个main；第二main必须在新UoW打开前拒绝，不覆盖首个Committed。3XP不属于此request主提交但继续008保护；后续ReadPremiumSnapshot／ReadReplay等只读Transactional不得改写旧主结果。
6. ApplyEffects累积实际typed AppliedEffect/EffectChange的projection footprint，只有operation本main执行期间生效。exhaustive visitor覆盖ContainerConsumption源、EquipmentChange.Before.Slot（含destroy）、Stackable实际槽、Prepared Slots、BasicGrant实际目标、virtual/account/gold、fatigue／usage、QuestRows／offers、skill／build／grow／XP、pet stomach/name。Cooldown／Spirit／SeriaLuck没有安全wire绝对投影则具名known no-message；DoublePot quota关联Premium当前状态。未知change必须提交前抛出，不default静默忽略；不缓存旧receipt／奖励／mutable name／委托。Request minimum needs补前置拒绝／合法空plan／普通no-refund Learn等无Apply情况。
7. 冻结不能在58.next正常返回后才做（那时60已durable）。真实UoW60在await next结束与CommitAsync之前，针对当前main execution identity执行Prepare/Freeze（或等价实际Prepare参与者），复制并验证immutable ReplayProjectionRequest。多次Apply只累积，不能第一个Apply就freeze。此钩子严格main marker且同execution identity，不作用后续只读事务。未知footprint／prepare失败走真实Abort确认rollback。
8. 58包60，在57仍持有时，next正常返回当下仅同步标记已预分配Committed，早于handler通知。不能依赖dispatcher最后返回（handler可吞异常或发送已失败）。60确认Abort后的普通错误在58记ConfirmedRollback，UoW前拒绝分开；recursive typed uncertainty保持008fence并标Blocked。不把任意IOException当unknown。markCommit本身意外故障属于postcommit typed uncertainty。已提交后send/encode/publication失败仍Committed，不返Pending、不再抽签支付。
9. 关闭Reinforce/Enchant/MagicSeal/Compound/Outworld/Forging六guard类／DI／handler分支，而非旁边再套一层。删除Lottery Completed/CompletedChange与Vending Completed/Remember／分钟sequence缓存及无意义业务sequence参数。保留现首次ACK／NOTI顺序／zero晶体event过滤及纯business前置行为；replay不再发历史ACK或奖励增量。
10. 迁移现direct-handler replay tests至真实ShopFixture DI Dispatcher→CommandBus→临时SQLite，并实现实际batch测试sender。保留原经济、保护、销毁、过旧、其他unknown帧／wrap、当前数量和lateSQL rollback断言，不为通过删除。Reinforce/Amplify现helper metadata只有4字段须真正counter Observe补ordinal/fresh，不能一律fresh=true。Enchant/Forge/Outworld/MagicSeal已有counter轨迹保留补hash；Recipe first/replay/next/late轨迹保留；Lottery/Vending网络dup迁移dispatcher，可信business tests不再依赖已移除缓存。
11. 新真实行为RED优先选普通Use已durable后sender失败再重复导致二次扣费／抽签，或confirmed rollback与commit mark缺口；保留实际失败证据。GREEN覆盖并发同raw只一次random、lateSQLite触发器确认rollback后同fingerprint可重试、ignored raw尾部冲突、selector/type/sequence冲突、下一fresh同参数、128allframe窗口／65535→0／合法fullround、新Session／same-ID新Context、连接gate等待前capture不重bind、replay发送屏障不超车、后来current库存／空物理槽／zero余额。Sender I/O部分投影不等于业务可重付。
12. 注册检查从真实DI decoder/ClientMessageProductionRegistration／MessageHandlerRegistry／CommandHandlerRegistry获得，分别断言18／20／20精确集合及3XP已有保护，不用同一个数量充当覆盖。源扫描old六guard／cache／首次原handler绕过为零；old test替身残留unused definition不算production旁路但记录。根与只读stage actor逐部分核对：先ledger+58/60+footprint边界，再所有旧入口关闭与测试，不只末尾一次。
13. 串行fresh build及Application完整（新marker/UoW影响全项目）、Protocol完整、Networking rawcounter/实际TCP+sender/registry测试，设置既有PVF archive、临时绝对DB与隔离socket。保留所有开发失败，按实际命令/counters记录，不声称全方案已经通过。源码包括新增未跟踪文件必须复读。
14. 写Result／checks／coverage，列真实API、状态边界、RED/GREEN、未验证wire seq-reset及011缺口；全部写完正式停止production/tests/dotnet，再由根及独立只读阶段检查接受。无中间commit。

## Readiness / Attention Check

009依赖真实接受；只读fixture scout确认ShopFixture真实DI/SQLite接缝、所有旧replay断言、raw完整body transport位置、14字节现API、实际DI集合与60 precommit freeze位置。根复读45／58／60和Apply实际source，明确首次handler不跨生命周期门、重放自身Exclusive。剩余目标是统一全部范围内入口与关闭旧机制，没有新增物品能力或服务部署授权。此Task唯一production/dotnet writer，独立actor只读。

## 实施中阶段检查

1. 真行为RED：production Shop/Dispatcher/Bus/SQLite source3→2，发送故障后同请求再次2→1，Expected2 Actual1，TASK-010-red.trx保留。核心GREEN改用valid fullrawidentity并确实进入009 current snapshot→registered encoder typed batch，原context引用／000D数量2／无旧Use ACK均断言；不把无效metadata拒绝算机制成功。根直接复读测试和RED。
2. 首次core只读检查确认closed inherited frame被Current投null而隐式降级trusted、non-main清共享ActiveMainExecutionId两缺口。修正为明确closed拒绝与独立flow Execution token；真实继承flow和只读不覆盖主执行证据由实现者补，不能仅据结构称已通过。
3. 根进一步读58出口确认Failed的closed检查可先throw，绕过008 fence并遮蔽postdurable typed原因。限定ForSettlement只匹配原raw frame+main ExecutionId用于Failed/ConfirmedRollback记账，不再通过授权Current；禁止授予新的执行权限。下一具体postdurable fault test须证明DB durable、原cause与精确session封锁，不能拿普通SQL rollback代替。
4. 真实本repo收帧Observe→Decode/hash→Dispatcher→Ledger.Wait前无await。未取得TouchSocket实际ReceiveLoop source，不声称framework永不并发；目前没有已证实实际回调重排缺陷，不扩大调度系统。all-frame expected-next仍CurrentImplementation，选角reset仍Unknown。

本节是施工期间边界／修正记录，不是最终通过声明。所有入口迁移和正式完整检查仍须完成。


## Result

```yaml
kind: rks-task-result
decision: pass
controller_acceptance: accepted
```

14步骤实施与单位内检查已完成。18 request CLR／20 wire／20 main commands使用真实DI精确集合确认；完整 raw body hash/length＋all-frame ordinal经网络入口进入physical-context CWT ledger。45保护captured exact operation session，60提交前冻结实际 typed effect footprint，58在handler通知前同步标记Commit或确认Abort／typedBlocked。六guard与Lottery/Vending业务缓存和sequence参数已删除，首次业务协议保留，重复请求只刷新当前绝对状态。

核心API、pure009接入、closed inherited flow／readonly token、失败记账不绕fence、unknown footprint提交前rollback、refusal-only兼容及所有旧测试迁移，详见 [TASK-010-checks](../verification/TASK-010-checks.md)。真实行为RED source3→2→1保留，GREEN确实生成当前000D batch且不重发旧ACK。并发Lottery随机／付款仅1；连接gate捕获旧session后替代拒付；第二main不覆盖首Commit；same-ID新context只有distinct selected generation才独立，value-equal Session fence保留。

最终 fresh 整方案 build 0警告／0错误；完整 Application2590、完整Protocol1226、Networking受影响实际TCP／counter／batch／registry／close30，均0失败0跳过；阶段core/registry23、设备Recipe/core110、Lottery/Vending38为重叠专项，不累加。所有正式TRX、最初RED与开发失败历史保留；git diff --check clean。新增未跟踪生产及测试源均复读，源扫描旧guard/cache零匹配。

证据分层：核心支付与rollback为实际bus／临时SQLite；concurrent readonly token另为context-method测试。raw metadata是实际TCP＋捕获dispatcher seam；无selected等待后选角反例是ledger／bus／SQL，三个原拒绝路径另有TCP。postdurable Probe证明SQL durable＋原typed cause＋exact fences，真实precise transport close证据来自008 Networking。网络I/O可能部分投影，不等于可重新付款；reset／无nonce古老raw碰撞仍Unknown。本轮不新增道具、未部署或实机验证，不标ClientVerified。

所有代码／测试／本Result／checks／coverage写完后进入单位停止点，不再执行production/test修改或dotnet；等待根与独立只读阶段接受。没有commit/push、liveDB/PVF/client改动或用户服务启动。四组同库、动态effect DI／最终扫描、Host临时DB／全方案suite／人工清单继续TASK-011，不能将本单位等同父AC／整个重构完成。

### 控制器接受

2026-10-01：实现者正式停笔后，独立[TASK-010-stage-audit](../reviews/TASK-010-stage-audit.md) approved。根已复读实际请求／主结算集合、账本／58／60的提交与失败边界、新增未跟踪源码和实际测试；最终Application2590、Protocol1226、Networking30均0失败0跳过，fresh build零警告错误。接受010，剩余四组同库组合、完整覆盖扫描及最终验收文档由011完成。本接受不替代父任务最后独立审查。
