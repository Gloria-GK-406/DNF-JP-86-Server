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
