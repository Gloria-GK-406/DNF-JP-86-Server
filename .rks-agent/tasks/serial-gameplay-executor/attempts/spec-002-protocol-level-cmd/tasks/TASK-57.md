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
