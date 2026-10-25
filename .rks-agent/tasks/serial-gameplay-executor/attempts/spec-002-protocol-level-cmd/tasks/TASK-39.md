# TASK-39 — 队伍退队、踢人和换队长归并

本 Task 承接 Plan P7 T-006 剩余三协议，闭消息65549／65550／65657及 LeavePartyCommand、RemovePartyMemberCommand、TransferPartyLeadershipCommand 三旧 Direct 命令。TASK-38 已经独立快审并以 `59b2f37e`／`f04a5b1b` 提交，留下同一队伍投影算法／状态及两塔共享协调链。本次沿真实依赖处理两塔主机、离塔和必要返城阶段，不提前闭两塔／城镇其他消息或来源行，无目标偏离。

## Task Unit Contract

- Revision: 1；Dependencies: TASK-38；覆盖 RQ-001／003／006～009／014，AC-001／003～005。
- Outcome: LeaveParty、RemovePartyMember、ChangePartyLeader 三唯一完整协议 CMD／薄 handler，删除三旧 party fine 及全部引用；普通队伍、两塔协调、投影和精确失败范围保持，收口 T-006。
- Boundary: 三队伍入口及被它们实际触达的共用协调／投影／真实状态接缝、接线、正式测试、六条 ledger、AGENTS 清单与本记录。共用算法单一，未迁调用方保窄适配；执行器、runner、raw／恢复许可、领域规则不扩大。两塔其他 fine 有活调用时仍保留 pending，不为闭账提前迁后簇。
- Intentional change: 默认保原行为。正式旧新对照证明的可见差异追加 Spec 变化表随结果审，不修订 Spec／Plan；中心方案、根／子契约变化先返主控。纯结构准备可独立时分开提交，不用更名 Delivery／Coordinator 掩盖未吸收编排。
- Review: 一次 sol6.1 执行、一次 fresh Astra 快审；无挑战。保持旧强断言，不用模拟 bus 调用数替代领域结果。

## Detailed Plan

1. 在旧路径先复用／补齐两塔 ProjectionConcurrency、Party handler／projection、PartyTower／Party wire 正式表征：普通路径一次修改、stale／非current／虚拟／多run／非队长／目标失效／自己等拒绝；塔内外换队长和精确host；第三成员退出、替换session不误伤；取消在塔提交后仍完成内存修改、内存拒绝／抛错及投影错关闭全部原精确参与者。相关真实 encoder／注册失败以旧 decoder／dispatcher／生产sender验证，凡有意改变保旧新运行依据。先证旧绿，再迁同测试驱动且不弱化断言。
2. CMD吸收 handler 与两 Coordinator 的捕获／验证／提交／即时内存修改／投影／失败收口，复用 TASK-38 的 CMD 投影阶段和唯一 stream。不得注入旧 Coordinator／Delivery、sender、bus、Registry 或 locator；共享业务归领域／应用能力，纯投影可参数化，真实状态由原owner契约暴露。旧 EndClearReturn 等调用方继续经窄适配共享算法，不能复制两套世界读写流程。独立纯准备先以旧 route build＋正式聚焦证明，留可独立提交 patch，原始资料只在 ignored logs。
3. 逐一对应原提交点：两塔 Leave／SynchronizeLeader 的原 Transactional 各一个显式 Write；即时 party mutation 使用原 CancellationToken.None，不在已提交与内存写之间新增取消点／业务读取。捕获足迹覆盖报告会话／全部run／队伍／目标，保持原当前性验证、受保护参与者和精确替换隔离。Boss 离塔后 Ready 的 Start 是另一个普通 Write，不能并入 Leave；Illusion Ready 只投影，不虚构同一事务。无新增 Main／幂等。
4. 保原发送次序：Party通知先于塔离场／host；同批精确失败隔离、已提交投影重试、结算／当前视图、离塔应答和返城时点不变；Illusion dungeon11000的库存恢复与Boss区别保留。普通踢人仍按原时点捕获run／runtime／position、通知departureSink并返城。必要城镇阶段仅按真实依赖抽取，不顺带关闭 T-007 入口或 F-109。不把有世界读取／业务写／unit调用的帮助器伪称纯帧。
5. 冻结后 Release build、相关聚焦、8项目正式 full（原类并行、项目串行、5m、console detailed、DFO各项目独立）；真实PVF新增用例用已有前置属性，准确核两原Dimension失败及任何新失败、跳过、N/D和反例归属。产品或测试返工后重跑实际受影响项目，不用历史库存冒称最终受测输入。
6. 只闭三消息／三party fine六行（其它fine确实无活引用且需同步闭合时先通知主控确认真实依赖），全部original／非目标保持；strict／八坏例selftest、diff-check。Result一页：交付／非常规选择／真实检查及失败身份／遗留，交独立快审。主控消费、分段本地提交后报告T-006四类计数／有意变化／风险／八全量，继续T-007。

## 共享能力边界裁定（主控，2026-10-25）

旧 `EffectOperationContext.CaptureLegacy` 在 ActiveNonEffect／无 frame 时允许自洽的非入站效果 owner；Boss 另由旧 selected pipeline 保护报告会话与 run 全员，Illusion plain command 无该集合。现 `CommandWorkUnits` 的 NonRequest 单 actor 门无法表达两塔 Leave，不能用空足迹、legacy 或模拟 unit 绕过。

这是既定显式足迹的遗漏消费形状，未否定根／子目标或协议 CMD 中心方案。Task Contract 修订为 **revision 2**：范围增加 Plan P8 的独立 DeclaredNonRequest 模式、显式 owner 集合与必要共享 runner／正式判据；原 Participants 保护集合及其失败语义不变，不新增恢复／raw／Main。先完成旧成功与新真实守卫拒绝的正式回归，再经唯一独立 reviewer 审 P8、主控消费并固定规划检查点后实施。执行者继续普通路径和不依赖该增量的验证；同一独立 reviewer 最后审 Task 完整结果。无额外挑战、证据 Task 或后簇闭账。

### P8 主控消费

已读取独立 `reviews/task-39-plan-review.md` 的 approved，核对真实 runner／生产两塔＋SQLite 缺口四例 4/4，以及旧 ActiveNonEffect 足迹和两种原保护集合。接受其全部实现核验义务：runner 必须独立复制冻结 owner 集合；不能用字段声明代替剩余成员真实奖励、捕获守卫后禁写、Boss 全员／Illusion 空 selected 及精确来源关闭的行为证明。P8 与 Task revision 2 可执行，规划检查点只收此 Plan／审核／Task／路由四份记录；当前产品与正式测试仍为未完成 TASK-39 工作，不在此检查点提交。随后执行者实施机制并交完整 Result，同一独立 reviewer 再作结果快审。
