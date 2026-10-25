# TASK-48 — 地下城结算、翻牌与其延时入口归并

本Task承接T-007，迁移SetPlayResult（65582）、ScoreScrollState（65605）、CardSelectRightState（65606）、SelectCard（65607）、BossDieCheck（65653），两fine GrantClearExperience／SelectCard，以及实际共享的source:card-delay，共八行。TASK-47已独立批准并以72fe9382／d0dd43d2提交，工作区干净；剩余翻牌fine同时被协议与延时入口消费，必须共同消除。目标未偏离，按T-010已允许的提前迁移收口这一来源，不闭其它Timer、GiveUp／Eplp或T-007整簇。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-47；承接RQ-001～009／013～015、AC-001～005／008及F-86。
- Outcome: 五消息的唯一whole薄handler路由、两typed内部延时CMD；两fine及旧card业务编排清零，保唯一原延时状态所有者。精确八行；不增Main／raw／Self、不改runner／bus核心。
- Gates: Spec B8 ready R1／Plan P10 ready R1为具名固定阶段能力前置。一个sol6.1执行、一个fresh Astra先B8再P10并最终Result，主控消费后方可实施产品；此前仅旧正式测试，无挑战。原根／子／中心与外部收尾门不改。

## Detailed Plan

1. **旧行为先行。** 精确d0dd43d2上跑原结算／卡牌／付费价格／奖励事务、PresentationCoordinator／Timers／Shutdown、shared party reveal强例，补实际decoder→dispatcher／Internal来源、真实Tx／SQLite、actual encoder／production outbox最小旧新oracle。必须覆盖Grant的scope在durable之后、调用者设置durableCommitted之前抛错时的claim／资产与重试，不能只比帧；另保发布失败、missing mirror、Despair、card fixing时点与原各拒绝／fallback。
2. **结算保持阶段。** Grant旧selected actor／physical／metadata及NonRequest足迹变一次ordinary Write，内部Despair→Mirror→luckyStar→Exp→attendance→difficulty原序；原claim／cards在外，提交后CommitSettlement／attendance／level-skill-offer刷新／PlayResult→Exp→unlock→available→ClearReward→Despair帧→非Despair调度均在原位置。实际规则判断经真实领域拥有者，禁止规则／配置注入新CMD；有意scope／失败保护差异列VC，不能默默修范围外业务。
3. **卡牌与布局归属。** 手动Free index仍规范为0，published／current run／InWorld／frozen门保持；原plain SelectCard一次empty-selected／None ordinary Write，claim、费用、gold、normal／blackdiamond、inventory-then-mail与commit同边界，失败回滚及重试只支付一次。个人确认、最后一位共享翻面和peer过滤／原帧序保持。三布局调用方（Score／Right／Timer）与选择两调用方（wire／auto）共享真实算法，不做CMD互调或藏fine；BossDieCheck原只读／无帧语义保持。
4. **具名生产者控制。** 按B8／P10同singleton alias固定ScheduleAfterSettlement／ScheduleAutoFlip／Cancel；禁止任意时间／callback／world查询能力。布局／自动选择都转typed内部CMD，保新Timer根／无来源连接、抑制flow＋yield、2s／4s、token身份替换、入队None而body观察取消、drain。原未迁GiveUp／Eplp／Wipe的Cancel仍指同owner，来源行只闭card-delay；真实同实例与类型正反例必须通过。
5. **F-86与正式证据。** 用真实reservation单执行器证明3次循环能否在尝试间获得可改变结果的状态；fake AlreadyFlipped→success不作依据。无业务来源按Plan删循环，遇真来源回主控。保早请求与timer只开一次／不重启动画、手动取消、替换run、取消后已入队无业务、停机等语义；same-root scope／encoder错误、original cause、资产／claim重试和精确关闭差异由正式旧新测试守护。旧fixture改同算法driver不减断言，不冒scope证据。
6. **冻结交付。** Release solution build0/0＋聚焦＋八项目fresh full（默认类并行、项目严格串行、5m／detailed／独立DFO／TRX，首次dotnet require_escalated）。全runner结束方可临时旧源复验，纯准备最终旧绿独立patch；逐display／status／重数以47有效库存及未影响四项目库存比较，仅原两Dimension与18skip可保留，DFO74分类不扩。核实际五唯一route／两Internal binding、两fine0、八行之外／original／行序、strict／八坏例和含新文件diff-check。Result与快审各一页，写实际限制；停止写入交同一Astra结果快审，主控接受后分段本地提交继续，不执行外部操作。


## Planning Gate Consumption R1

主控完整读取唯一fresh Astra的`reviews/task-48-plan-review.md` R1：先Spec B8、再Plan P10／Task revision1均approved，无阻塞项。接受其实际旧调用点、同producer固定控制、Grant／SelectCard不同事务与保护、typed Internal来源及取消／drain判断；Grant scope／claim／资产旧新实证与F-86真实来源仍是实施验收，不提前声称结果。根／子／中心与其它能力不变，不追加挑战或取证Task。先形成仅五份规划文档的本地检查点，随后放行执行者在旧正式表征通过的基础上实施；最终Result仍交同一Astra，全部外部收尾门保持。

## Result

- Outcome: done
- Changed: 五消息各唯一 whole 薄入口、两 typed Internal Timer；Grant 原一段改 ordinary Write＋精确 actor／physical／metadata／NonRequest，SelectCard 原一段改 ordinary `[]`／None。卡牌布局／选择／投影在同 CMD 家族真实共享，结算只供自己的算法归 own partial；两 fine、旧 producer 业务／sender／queue body、零生产 caller 旧 delivery 删除。原 singleton 只保固定阶段与取消／drain，窄 control 同实例及类型正反例守护；仅指定八行闭合，AGENTS 只加清单与一句能力边界。
- Why: 结算规则收进已有 run 生命周期／claim owner 调原规则，未造伪 DomainService。真实预约与已排队 release 的三作业证明 F-86 三次尝试不能获得中间状态推进，故保 Free0 单次拒绝。旧 Grant scope 确有30→60重复支付；新 whole scope 已提交 claim 后只付30，默认失败与 Timer 原吞错差异具名 VC-B23／24，与 whole 归并不可分；独立纯准备补丁可分段。
- Checks: 精确旧 d0dd43d2 最终 App334/334（313原＋21 oracle）、Protocol270/270，恢复后当前同334／270。实际 wire／Internal／SQL／encoder／outbox／physical 核资产、claim、付款和双奖励回滚重试、原 cause／Completion、scope／调度、取消／FIFO／drain；真实 auto2s＋4s ordinary `[]`／None一次支付。Release build0/0；八有效 fresh **9410＝9390pass＋2原Dimension＋18skip**，App r4 N24/D0、Protocol r3仅F-86 N1/D1、其余六r2 N0/D0。DFO74＝67反例＋7control，outside／exe／orphan0；runtime五唯一Protocol／两Internal／两fine0。八行外／original／行序、Result后strict／八坏例及含新文件diff-check通过，见[核验摘要](../verification/task48-verification.md)。
- Remaining: 原两 Dimension仍7100／maze0／(1,0)／Left缺图。旧复验先被自动审批拒绝，经完整 recovery patch／固定46项manifest同动作安全重审后获准，finally全部恢复零错误；纯准备patch reverse-check通过。受控故障／transport不等于自然事故或A21；T-007／T-010整体未关闭，无外部操作。产品／测试及runner已停止，待同一Astra结果快审、主控接受与本地提交。

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**当前 Result R1 的局部结果批准；未发现阻断项。** 审查 TASK-48 revision 1、已消费 B8／P10、VC-B23／24，以及 `58004de8` 工作区相对旧产品 `d0dd43d2` 的本 Task 未提交差异（含新增文件）；已收到执行者停写确认，最后只删除 BossDieCheck 文件末空行。按 `reviewing-work` Task Unit＋Code 判据与 process-reset 检查，不重跑测试、不改产品、不新增挑战。

实际五个薄 handler／Protocol Direct 与两个 Internal Direct 注册、原两 fine 删除，与 runtime capture 一致；布局和选择由同 CMD 家族实际算法共享，结算 grant 是 own partial，测试 driver 只调用这些算法，不是生产 fine 转发。run 生命周期所有者调用既有结算规则；窄 control 精确类型正反例及 `CardRewardTests` 的同实例断言成立。生产者只保固定阶段、同 pending／token、取消与 drain；已核独立 Timer 根、无 source、None 入队、body token、替换 Complete 身份比较及异步排空，未迁退出路径仍用同 owner。

Grant ordinary Write 的 actor／原 physical／metadata／NonRequest 与 Card 的 ordinary `[]`／None 对应旧边界；cards、settlement claim、发布位置未塞入新事务。抽查正式 `CardSettlementLifecycleTests` 的真实 decoder／bus／SQLite／encoder／scope 路径：旧 scope 的30→60与新30／claim不可再领有直接断言，scope前已调度、encoder失败不调度；实际付款 Prepare、第二奖励 SQL 回滚与新 physical 重试一次支付，及真实自动翻牌写库、queued cancel／FIFO／drain 均有证据。VC-B23明确默认生命周期及 Timer 原吞错变化，不冒称纯搬迁。F-86 的真实预约三作业证明、当前唯一预约消费方及事务提交／回滚实现支持 VC-B24；原 fake冲突→成功用例已按新单次拒绝语义替换。两项与本次 whole 归并不可分的说明接受。

独立读取最终八项目 TRX并重算 display／status／重数：App N24/D0，Protocol N1/D1仅上述 F-86，其余六 N0/D0；合计9410＝9390通过＋2原 Dimension＋18skip，21个新 oracle全在 App正式全量内。旧最终334／270、恢复后当前重编译检查及 build-r3 0警告0错误与记录相符；DFO原分类74＝67反例＋7control，outside／exe／orphan均0。独立比较迁移账，只有五消息＋两fine＋card-delay八行变化，全部 original、行序和其它行保持；未闭合 T-007／T-010。旧源复验恢复记录 errors／mismatches均0；恢复后仅末尾空白差异不影响该历史结论或测试语义。

批准只覆盖本 Task；受控故障不等于 A21 实机验收，原两项内容失败继续保留。主控消费后按既定纯准备／产品分段检查点推进；不据此封存 Spec 或进行外部收尾。评审者已停写。

## Controller Consumption R1

主控完整读取同一 Astra 的 Result R1，接受局部 approved，无阻断或需返工项。独立核实八份有效 TRX／逐用例差集、原 DFO 分类、实际注册与八行台账；最终旧334／270及46项完整恢复由主控实际执行并检查，恢复后当前334／270也通过。Result后另跑strict与八坏例均符合预期，纯准备cached-check通过；末尾空白修正不改变已验证业务。接受VC-B23／24及其不可分说明，先提交独立正式测试准备，再提交产品与结果记录；只作本地检查点，不关闭T-007／T-010或Spec，不触发任何外部收尾。
