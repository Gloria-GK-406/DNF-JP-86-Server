# TASK-57 — 两塔内部返回来源与剩余旧命令归并

本 Task 承接 T-008 两塔剩余依赖：StartBossTower、LeaveBossTower、SynchronizeBossTowerLeader、LeaveIllusionTower、SynchronizeIllusionTowerLeader 五 fine，及 BossTowerReturnSweepService／IllusionTowerReturnSweepService 两 timer 来源，共七行、零消息。TASK-56 已获唯一 fresh Astra 批准、主控接受并以 072caf6d／94746fe1 本地分段提交，八fresh full为9632通过／原2失败／18跳过；T-007完成，开工前工作区干净，现有两塔协议已共享实质算法，剩余旧消费者主要在 sweep／departure 和仅注册的旧协调器。目标未偏离：迁真实内部来源并消除最后 fine 消费，不把仍有选角／日切消费的 RefillOnlineBossTowerTicket 提前关闭。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-56、已接受 P8／P10；相关 RQ-002／004～009／013／014、AC-001～005／008；真实来源依赖使 T-010 此两行提前随 T-008 迁入，不关闭整个簇。
- Outcome: 两 tower source 只做计时／typed内部CMD调度；世界读取、departure drain、due sweep、独立事务和投递归整CMD，五fine类型／注册／生产调用及无消费者的旧bus编排适配清零。共享已迁协议的实质算法不复制；真实departure队列唯一状态所有者、原写断言和经实际来源核定的physical／metadata精度保留。
- Gates: 执行者先读实际源码并补正式旧表征，暂停产品变更；主控依据证据拟定有界 Spec B9 与跨簇 P11，唯一 fresh Astra先审Spec、通过后审Plan，主控接受并metadata检查点后才能改共享core。若触根／子／中心须停用户门；不加raw／Self／Main／恢复白名单／legacy fallback。默认whole故障边界经具名旧新用例追加VC，最终同一Astra审Result。八freshfull必须。

## Detailed Plan

1. **旧正式表征与设计输入。** 确认五fine全部真实消费者；用真实executor／timer／domain／SQLite／outbox覆盖两源drain先于clock、due列表冻结、两due及两departure前项失败后项行为、每member独立Tx、Boss dead-first与固定Members、取消位置、timer来源的SourceConnection／metadata为空与Boss fine另行捕获reporter physical分开核定，不把来源为空误判为参与者physical为空；原提交后内存临界序列／精确参与者保护、11000库存恢复及Illusion响应差异保持。补旧producer取消executor日志、hosted失败Warning、refusal、coalescing、FIFO/root/parent/drain，不以仅返回Task证明。提交旧绿及具体P11建议给主控，无产品修改。
2. **有界共享HOW。** 仅在原TrustedInternal入口为ordinary writable unit允许显式非空唯一owner集合的DeclaredNonRequest；保ProtocolNonEffect原许可和NonRequest单actor不变，效果请求、缺请求、ReadOnly／Main／恢复等拒绝，request/job/action/Tx身份及sticky守护不弱化。typed producer只能收命令和已验证origin，不收业务Func或raw GameplayWork；BodyOnly取消如需修正，完整Explicit cleanup／FailAsync后只搬出同一producer的单独OCE，不掩盖typed／独立scope／sticky失败，queue drain保持。来源分解／交错与实际旧缺陷修正由B9审定，确切API与反例由P11审定；不可将typedbus包回旧rawjob。
3. **真实迁移。** ordinary Write与原fine逐一等价，footprint owners独立于protected participants且实际来源明确；保Boss／Illusion原保护差异。旧每item catch遇whole sticky时不得假称可继续，尤其不能继续dequeue而丢后项；以正式旧新试验裁定默认whole变化或需要的边界，禁止清sticky／造恢复权限。队列如需注入，以同一实际state owner的窄DomainService契约提供，不造forwarder或第二队列。删适配时保全部强测试断言，静态Stale工具迁适当纯协议归属，不为它保空业务壳。
4. **验证与收口。** 新旧表征必须真实source入口＋typedCMD，并有内部footprint所有拒绝方向／foreignjob／漏出／owner与保护分离的正式反例，协议P8不回归。最后主控独占执行冻结old回放与finally恢复，后fresh非增量Release solution零警告错误、八full严格串行原类并行5m/detailed/DFO，逐例失败／增删不放宽。runtime真实注册／同实例／fine清零、严格七行账与八坏例、全owned恢复与whitespace。Result与验证摘要一页，唯一既有Astra审结果、主控消费后本地分段提交；不做外部收尾。

## Controller Boundary Decision — B9／P11 待审

主控已读当前真实来源与授权旧算法测试、原TRX：旧基线App213／Protocol42、旧准备286通过；真实定时Leave在领域动作前缺footprint来源回滚，合法内部回城两例业务RED。真实Boss departure以Start后同事务受控gold marker核action1811／native Commit787、nonfallible违约和实际scope失败，后cohort仍提交；授权旧sweep算法另证相同行为，后者不冒生产可达性。当前typed BodyOnly单独producer OCE会额外记执行器Error，旧定时入口不会。

据此拒绝将整个tick变成首错即停的whole，也不以清sticky或新恢复白名单维持假隔离。拟采用纯producer顺序提交“departure身份快照→逐项完整CMD→此时读clock的due扫描→逐run完整CMD”；快照不pop、精确head领取防重复、新arrival延后、hosted busy整轮／手工不合并。根为departure扫描，departure与due扫描为其子，due-run为due扫描子，链明确且每轮最大深度2。新增项间交错／分项outbox交付／子链及默认whole已提交scope保护列B9，不冒行为全同；不扩大其他run保护或改A封存。原SourceConnection／metadata为空与Boss实际reporter physical必须区分。

另发现现共享FailAsync按session取第一项会让先记的null physical遮掉后记的真实physical，执行者已报告机制RED；主控将读原测试／TRX后纳入P11去重精度修正，按引用而非record值去重，不能额外capture未声明连接。多成员旧第二Leave uncertain的physical边界须与新whole累计声明范围明确对照。上述皆为待独立审查提案，不是产品许可。先由TASK-57唯一fresh Astra审B9及其根／子／中心边界，通过并主控消费后才写并审P11；规划检查点前执行者仅可完成旧正式测试准备。

## Controller Consumption — B9 R1

接受唯一 Astra 的 `reviews/spec-b9-review.md` approved。主控已核最终旧准备 App 289／Protocol 43 的原 TRX、child exit 0、DFO 各一个正对照且额外违反为零，并读真实 departure encoder、授权旧 sweep、多成员第二次不确定与 physical 漏保护测试。它们证明旧失败隔离与两处实际缺陷，不证明新实现完成。

按根 Goal 的具名有意变化出口、SC-8B 的 B 变化例外及本次独立审查，B9 仅改变两条来源的批次粒度；唯一执行器、完整协议 CMD、无嵌套、事务点和显式链不变，不修改根／子契约或中心方案，也不借 F-07 自动授予例外。B9 R1 规格门通过。下一步形成 P11 并交同一 Astra 审查；产品仍关闭，直到 Plan approved、主控消费与 metadata 检查点完成。

## Controller Consumption — P11 R1

主控已全文读取并接受同一 Astra 的 `reviews/task-57-plan-review.md` approved。实际核对 bus／request／ordinary runner／declared footprint、CommandInvoker 的 handler 解析边界、producer drain 与 executor lifetime。P11 的发生身份记录是待实施机制，不能把现有异常引用去重当作已满足；独立清理复用同 cause 必须保留。TrustedInternal 只增加 ordinary writable 的显式 owner，累计 physical 只保已声明引用，其他权限与保护不扩大。

跨簇 HOW 以 P11 末节为准，具体七行搬迁仍归本 Task。metadata-only 检查点完成后由原执行者继续实施，恢复正式回归并保原强例；最终旧回放由主控独占执行，其后 fresh build／八项目 full，仍由本 Task 唯一 Astra 审 Result。此处只接受规划，不声明产品或新测试完成，不作外部收尾。

## Result

Outcome: done

按已审 B9／P11 完成五 fine＋两 source 七行、零消息。每塔四类 Direct／Internal CMD，producer只驱动身份快照／精确head领取／此时clock扫描／逐run完整CMD；原每member Tx、shared算法、队列唯一owner、来源差异、显式0／1／2链及整轮drain保留。删除最后旧fine与Delivery／bus Coordinator；Refill及其他开放行不闭合。

共享修正严格限于TrustedInternal ordinary Write的显式owner、真实body取消发生记录与完整清理后搬出、已声明physical按引用累计去重。取消、owner寿命及foreign-job反例保留；未加raw／Self／Main／恢复白名单。B9具名交错／timer原缺footprint修复、whole累计保护及Illusion已commit取消转typed按VC-B37～39承认，不虚称纯搬迁。FinishLoading fault1的leader实际两段声明null→physical使关闭0→1，原Tx／帧／Fence／cause／链断言保持。

主控最终旧回放App289／Protocol43及原3项正式RED匹配，74owned／8699nonowned独立确认恢复后，fresh非增量Release build零警告错误、八full9720＝9700pass＋原2Dimension fail＋原18skip；逐例失败消息／跳过身份、DFO及增删均核。实际八注册／同实例、七行账和全部证据见 `verification/task57-verification.md`。中间编译与fixture签名适配日志保留；原强业务断言迁共享算法driver而非留旧fine。纯准备十路径patch独立供主控分段；跨core缺陷修复与结构迁移不可冒称仅测试准备。

R1 F1复审修正：新增两正式反例先RED（Error／EndedAJob实际0／false），仅在invoker正常返回后、DemandFinished前撤销取消搬运资格；原真正body／健康action取消路径不变。修后聚焦87/87，再以task57-final-r2 fresh build及八full、runtime-r2、strict／八坏例收口；r1证据保留。相对原freeze只有CommandBus和InternalProducerCancellationBoundaryTests两路径变化，旧三patch及其余69 Server路径未变，不重复old swap；未改Spec／Plan或权限。

仅本地交付；未Git写入／外部动作／部署／清理worktree，不开始TASK-58。完成收口后STOP，交同一Astra审Result与主控消费。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: changes_required
```

**结论：一项取消来源判定缺口须修正，当前 Result 不通过；B9／P11 的既有批准不变。** 复核对象为 TASK-57 revision 1、已消费 B9／P11、metadata HEAD `3645930e823fc0bfae8ccceefe540e1f25f39159` 之后的当前未提交交付及 `task57-final-r1`。本轮沿用同一独立 reviewer，按 Task Unit／Code 标准只审本七行与所触共享机制，不是全 B 外部终审。

**F1（P2）：已完成但未观察的 action 取消仍可取得纯 producer 取消资格。** `CommandBus.cs:228–235` 在 invoker 正常返回后直接调用 `request.DemandFinished()`，没有撤销 action 候选；`ProducerCancellationObservation.cs:51–57` 只要求 Qualified、同一失败发生和 `OperationsCompleted`。具体路径：已取消的 producer token 下，ordinary Write 的 action 同步返回 faulted OCE，runner 健康确认回滚并签发 Qualified；handler 丢弃这个已完成的 `WriteAsync` 返回值，然后正常返回。此时 `DemandFinished → DemandCurrent` 只重抛已记录的同一个 OCE；外层 catch、drain 和 FailAsync 均把它当传播，操作又已完成，最终被搬出为零 executor Error、非 EndedAJob 的纯取消。也就是说，失败并非经真实 invoker body 冒出，却获得了 P11 明确禁止赋予未 await／结束检查路径的豁免。handler 捕获该 OCE 后正常返回亦有同一出口。当前 `Unawaited_cancelled_action_is_drained_before_scope_end_and_cannot_erase_the_unfinished_guard` 只覆盖操作尚未完成的情况，提前生成 unfinished guard，不能覆盖本例。此结论来自实际控制流核对，未运行新测试。

最小修正是在 **invoker 正常返回之后、DemandFinished 之前** 撤销取消搬运资格（或提供等价的“必须经 body 失败出口传播”约束）；真正 await 到的原 action OCE 仍走现有 catch，不增恢复能力或另一套请求追踪。补正式反例覆盖同步已完成但未 await，以及捕获后正常返回，断言一条 Error、EndedAJob、原 cause、实际回滚与完整清理；现有正常 body／健康 action 纯取消对照继续零 Error。先证本版 RED，再修成 GREEN，保强例并按本 Task 共享 core 的最终验证约定更新当前交付证据，交同一 reviewer R2。

其余已核：实际八个 Internal CMD 与四阶段来源、精确队首领取、显式父链、整轮 drain／DI 同实例；TrustedInternal 的 ordinary Write 闭合许可与 owner／participant 分离；physical 枚举按 session＋引用保全已声明目标；五 fine 最后注册及旧 bus 适配删除。已读相关源码／diff、取消正反例、P8 参数化差异及 FinishLoading 的 null→physical 真实声明断言；后者关闭 0→1 与已审保护修复一致。未发现需另改根／子契约或 Spec 的缺口。

正式证据并未伪造或漏报本轮已运行失败：独立读取最终 build／八项目 run 与 TRX，确认串行自然结束、9718＝9698pass＋原两项 Dimension 精确失败＋18skip；两条失败完整 Message 与记录一致。DFO 原 parser 输出 outside／exe／orphan 均0；runtime 探针实际核八注册、五 fine 清零和来源／队列 alias，同一已编译实现。旧回放／恢复、测试增删和局限已在 verification 与主控证据中区分，不能由这些绿结果推导 F1 已覆盖。纯准备与语义改动的分段提交仍由主控收口；无 A21、专项性能或全 B 验收结论。

本轮只追加本节，未 build／test、未改产品／测试、未作 Git mutation 或旧树交换。STOP，交主控处置 F1。

## Controller Consumption — Result R1

接受唯一 reviewer 的 F1，当前 Result 暂不接受。主控已核真实控制流：已完成的 action 取消若未由 invoker body 失败出口传播，不能借 DemandFinished 重抛取得搬运资格。原执行者仅为“丢弃已完成返回值”和“捕获取消后正常返回”补正式 RED，再在 invoker 正常返回后、结束检查前撤销资格，保持已有纯取消正对照与完整回滚／清理断言。

这是 P11 已批准边界的遗漏修复，不改 Spec／Plan 或权限。旧绿色准备及旧缺陷回放证据不变，不重复整个旧树交换；须以实际差异核其未改。修后 fresh build、八项目全量、实际注册和账验收完成，再交同一 reviewer R2。此前 R1 原日志全部保留，不用新结果覆盖。

## Independent Quick Review R2

```yaml
kind: rks-review
verdict: approved
```

**结论：R1 F1 已关闭，TASK-57 当前 Result 通过本地单元快审。** 对象为同一契约 revision 1、B9／P11、metadata HEAD `3645930e823fc0bfae8ccceefe540e1f25f39159` 后的 R2 未提交交付，最终验证为 `task57-final-r2`，身份记录为 `/private/tmp/task57-final-owned-r2/manifest.json`。本轮只复核 F1 修复和直接回归、更新后的正式证据；其余沿用 R1 已审范围，不重开 Spec／Plan，也不代替全 B 外部终审。

**F1 修复符合最小边界。** 已读当前 `CommandBus` 与 `f1-current-diff-r2.patch`：唯一产品增量是在 invoker 正常返回后、`DemandFinished` 前执行 `cancellation?.Invalidate()`。正常返回不能再利用结束检查重抛取得取消搬运资格；真正 body／健康 action 的 OCE 仍从原 catch 抛出，不经过新增语句。未清除 request.Failure、未改变回滚／清理、未增加恢复许可或追踪机制。

两个新增参数 case 分别覆盖已同步完成的 Write 被丢弃，以及 await 捕获后 handler 正常返回。用例先确认真实同步完成、ConfirmedRollback、环境事务结束、一次 None-token 回滚、原 cause、scope／request 完整关闭和下一请求健康，再核 Error／EndedAJob。独立读取原 run／TRX：`task57-f1-red-r1` 两例均以预期 `(1, True)`、实际 `(0, False)` 失败；`task57-f1-focused-r1` 87／87 通过，含新两项及原 body／action、不同 exception token、独立清理等正反对照。它们补上 R1 的“已完成未观察”空档，没有用已有的 unfinished guard 用例冒充。

**最终证据匹配当前修复。** 独立解析 R2 build 与八项目 run／TRX：fresh build child0，八项目严格串行自然结束；总计9720＝9700pass＋原两项 Dimension 精确失败＋18skip。按 testName／outcome／重数直接比较 R1，只有 F1 两项 pass 新增，无删除，其余七项目完全相同；两条原失败完整 Message 与 R1 相同。DFO 原 parser 各 exit0，outside／exe／orphan 均0。`runtime-r2` 两次 child0，八个唯一 Internal／Direct、binding／exits0、Self None，五 fine CLR／handler／注册0，Refill 保留，同 provider 的来源／drain／队列别名一致；探针载入的 Application／Protocol DLL 与当前测试输出副本逐字节摘要一致。

已读 R2 source-change audit 与最终 manifest：相对原冻结只有 bus 和新增反例所在测试两路径变化，其余69个 Server 路径及旧三份准备／RED patch 未变；沿用主控已接受的原旧回放／恢复证据合理，无须为新机制这一处修正重复旧树交换。七行 ledger、strict／八坏例及其余 R1 证据保持原边界。未发现新的阻断项；本地分段提交与接受仍由主控完成，不包含 A21、专项性能、全 B 验收或外部收尾结论。

本轮只追加本节，未运行 build／test、未改产品／测试、未作 Git mutation 或旧树交换。STOP，交主控消费 R2。

## Controller Consumption — Result R2

接受同一 Astra 的 approved，F1 已关闭。主控独立核了两项 RED 的实际断言、87 项绿复核、唯一正常返回资格撤销，以及最终 fresh build／八 full 9720＝9700通过＋原2失败＋18跳过；R1 至 R2 只增加两个通过用例。原 DFO、实际注册及同实例、DLL 身份、精确七行账、strict／八坏例和纯准备可独立暂存均已复核。

确认原旧回放有效，最终相对原冻结只改授权的 bus 与取消反例两路径，其余69个 Server 路径和旧准备／RED patch 不变。按原流程将十文件旧行为准备与产品语义迁移分为两个本地提交。TASK-57 至此完成；从 TASK-58 切换用户 §8 的两阶段流程，不将本次单元审核冒作 B 外部终审。
