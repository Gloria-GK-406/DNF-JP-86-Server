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

“捕获守卫后禁持久写”沿现有 sticky 与真实事务回滚语义：后续足迹、单元和正常结束拒绝；action 内已暂存的 SQL 必须回滚，不能持久化。若测试确有暂存 SQL 到达，应如实记录，不能声称调用前已拦截；本次不新增全局 SQL 拦截或隐式请求读取机制。

普通 outbox 登记并不读取 request.Failure；P8 不因此新增 outbox admission 状态或改造登记机制。新足迹的真实 program guard 可使用既有 GameplayJob.RecordProgramError，确保外来旧能力的错误即使被当前 job 捕获也不能正常成功。事务内条目按回滚丢弃，既有事务外条目和来源关闭仍遵守原顺序；测试与 Result 不得把原普通 Register 的接纳写成“立即拒绝”。

## Result

**Outcome: done**（执行轮次 1，revision 2／P8；最终受测 R3）。

**交付与选择。** 三条唯一 whole CMD／薄 handler、三个旧 party fine 及源码／测试引用删除；协调、两塔投影、必要返城进入同一 CMD 的 partial 阶段，未迁调用方仅窄适配共用原算法和状态。补回 legacy coordinator 原先经 fine 获得的 run 索引清理，已有两塔外部队长用例以真实独立 run 索引守护。仅闭六 ledger 行，AGENTS 只加清单，无有意可见变化。真实 runner 四例先证实单 actor 足迹不能承接两塔 Leave，再按已审 P8 增加独立 DeclaredNonRequest；owner 冻结与保护参与者分离，保 Boss 原全员／Illusion 原空集合、原普通事务点及取消后的即时队伍修改，不增 Main／账本／raw／恢复。

**旧新证据。** `verification/logs/task39-prep.patch` 可独立应用于 `f04a5b1b`，已同步 readonly 修正；实际恢复旧 route 后 build **0警告／0错误**、App **101/101**（含 census／反例／架构）、Protocol **106/106**、Networking **11/11**（`task39-prep-readonly/`）。新真实领域＋SQLite＋生产 runner 覆盖非入站队员踢出、剩余死成员奖励、提交后取消、Boss 独立 Start／Illusion 仅投影及两者差异。新增 host encoder 故障对照是当前窄适配／whole 共用算法的双驱动验证；该发送／逐目标关闭算法静态对应两塔原 `ProjectionDelivery.cs @ f04a5b1b`，不冒称独立旧源码故障运行。其最小 fixture 明确无返城当前视图；返城强断言沿原用例保留。P8 反例核冻结集合、身份／来源／生命周期／跨 job、catch 后真实 SQLite 暂存写被回滚、unit 内登记丢弃、unit 外登记与精确 source close；普通 Register 未新增立即拒绝门。

**检查。** 新鲜 `dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1` **0/0**。项目串行、原类并行，`dotnet test <csproj> -c Release --no-build --no-restore -m:1 --blame-hang-timeout 5m --logger 'console;verbosity=detailed'`，各项目独立 DFO／TRX；最终聚焦 **105＋156＋11 全过**。`verification/logs/task39-final-r3/` 为八 fresh full 的命令、日志、TRX、数量与断言归属入口：

| 项目 | 总数 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|---:|
| Application | 5436 | 5418 | 2 | 16 |
| Protocol | 1781 | 1781 | 0 | 0 |
| Networking | 203 | 203 | 0 | 0 |
| Pvf | 80 | 80 | 0 | 0 |
| HttpApi | 57 | 57 | 0 | 0 |
| Infrastructure | 976 | 974 | 0 | 2 |
| Persistence | 484 | 484 | 0 | 0 |
| Host | 203 | 203 | 0 | 0 |

合计 **9220＝9200通过＋2失败＋18跳过**。失败仅原 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` 与 `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`，均仍为 7100 左入口无兼容 map。相对 TASK38 三项目＋TASK37 其余五项目，按 display／status／重数 **N=41／D=0**，全为 App 新例；DFO **74＝原67反例＋7 controls**，outside／exe／orphan **0**。六行／全部 original／非目标比对、preclose、diff-check通过；最终 **strict valid=true／八坏例 selftest 全部 rejected=true**，输出归 `task39-final-r3/`。

**遗留。** 交同一 fresh Astra 完整结果快审及主控分段提交。两条内容基线、A21 实机边界及后簇仍保留；未推送／PR／部署／清理。产品、测试与记录停写。

```yaml
kind: rks-verification
decision: pass
```

## Independent Quick Review R1

```yaml
kind: rks-review
verdict: approved
```

**TASK-39 执行轮次 1／最终受测 R3 通过本单元结果快审，无阻塞发现。** 对象为 revision 2／已批 P8 及主控的 sticky／outbox 语义澄清；产品基点 `f04a5b1b`，当前 `64c1b056` 仅为规划检查点，审核涵盖停写后的本 Task 未提交产品、测试、记录与六行 ledger。依 `reviewing-work` Task Unit／Code 判据及 process-reset 执行，不生成挑战，不代替 spec-002 最终审查。

- **结构与原边界。** 实读三 whole／薄 handler、ProtocolModule 双向注册、两塔协调和投影 partial、共享返城及 legacy 适配；精确检索三旧 fine 已无源码／测试引用。真实编排在 CMD partial，旧调用方只转接同一算法；GiveUp 和旧 coordinator 复用带原 run 索引清理的状态方法，Registry 新契约映射原单例、未增状态。原提交后 `CancellationToken.None` 队伍修改、Party→塔投影次序、Boss 另一次 Start 写、Illusion 不加 Start、11000 库存恢复及精确会话关闭均保留。已有外部队长用例补真实独立 run 索引断言，真实两塔新例另核取消后仍完成修改、非入站踢人及剩余死人实际经验／结算 receipt。
- **P8 实现。** runner 先复制 owner，再由 recorder 冻结为 FrozenSet；owner 与 Participants 独立，Boss 原全员／Illusion 空 selected 不互换。只给 NonEffect ordinary Write，拒绝 Main、Internal、ReadOnly、恢复入口及错误声明；旧 NonRequest／Cera 不放宽。能力在 action 后、Prepare 前关闭，request.Failed 与既有 RecordProgramError 使外来／过期能力错误被 catch 后仍不能健康结束。正式反例实测 SQL 暂存后真实回滚、unit 内条目丢弃、unit 外帧后关闭来源、替换身份不继承围栏；没有新增普通 Register 即时拦截，GameplayOutbox 未改。
- **证据核验。** 独立解析最终全部 TRX：聚焦 105／156／11 全过；八 full **9220＝9200通过＋原两条 DimensionDungeon 失败＋18跳过**，失败均为 7100／cell(1,0)／Left 无兼容 map。按各基准 TRX 重算 display＋status＋重数，**N41／D0**；直接计 DFO 原始记录 74、controls 7，归属摘要 outside／exe／orphan 0。已读最终 build 0/0、纯准备 build 0/0 与 101／106／11 TRX、20 路径 prep patch、strict valid／pending 空和八坏例拒绝；独立比较 ledger 仅六目标行变化、original 和非目标不变，`git diff --check` 通过。本轮未重复运行八 full。

**证据边界保留。** host encoder 新例是现有窄适配／whole 双驱动，旧发送／逐目标隔离算法另与 `ProjectionDelivery @ f04a5b1b` 静态对照；其无返城当前视图的 fixture 不代表完整返城动态验收。原返城强断言和全量仍通过；本结论不宣称独立旧源码 encoder 故障实跑或 A21 实机通过。主控可消费本审核并按已声明纯准备／whole 分段本地提交；不授权推送或分支收尾。

## Controller Consumption R1

接受独立结果快审 R1，TASK-39 通过。主控已读实际改动、审核及全部最终 TRX，独立重算八 full 9220＝9200通过＋原两条失败＋18跳过、N41／D0；重新运行 strict／八坏例 selftest 并逐 JSON 核对只有三消息／三 fine 六行变化，original 与非目标保持。纯准备独立应用检查通过，已提交为 `3e1af209`；whole、P8 能力及正式测试另行提交，分段暂存不改变最终受测文件。P8 规划检查点为 `64c1b056`。

T-006 至此收口：业务消息71/159、旧命令58/204、来源1/73；保护0/5、过渡0/8。此簇无有意可见行为变化；P8 只补显式表达，原两塔保护集合保持。host encoder 双驱动及返城 fixture／A21 实机局限按 Result 披露，两条内容失败继续保留。未推送或收尾，继续 T-007。
