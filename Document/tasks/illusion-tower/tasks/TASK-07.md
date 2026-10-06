# TASK-07 — 完整入口串联与交付验证准备

## Task Unit Contract

- Revision: 1
- Outcome and references: T-005，SC-1至SC-8的完整调用路径与回归证据。把已分层测试的真实内容、领域事务、协议入口和返回行为串成可断言的玩家循环。
- Owned boundary: 必要的跨层集成/回归测试，发现确定缺陷时回报主代理决定有界修复；本单元不擅自重构生产架构或扩展玩法政策。完整solution检查、最终结果/协议文档和独立评审由主代理接手。
- Dependencies: TASK-05/06完成并交还产品/.NET执行权；TASK-01至04的内容、状态和协议实现。
- Required checks: 真实入口入场至15层、结算、回城、重入；旧到期回调不能作用于新挑战；0092的注册编码字节向量；普通相关路径回归。

## Detailed Plan

1. 优先复用Application.Tests现有隔离SQLite夹具以及TASK-06可能建立的塔组合夹具。该测试项目引用Protocol是仓库明确允许的测试组织方式，不新增测试基础设施项目。生产DI、塔领域/CMD/奖励事务保持真实；传输使用假连接/记录器。与本任务无关的选角准备/显示数据可以在明确的查询接缝提供最小有效替身，不用它替代塔、资产或队伍事实。
2. 增加一条从现有0010 SelectDungeonHandler进入11001的完整路径测试，证明不走普通maze/fatigue路径、券只扣一次、城镇存在撤销、8E/8F发出。通过已注册共享handler驱动0025装载、009F开始、0027怪物/APC死亡与009F/002D推进，连续到15层，断言每层重新装载、未清場不可推进、最后层有settled receipt与0090/91/92。
3. 该场景优先用当前PVF的真实15层内容与奖励规则，持久化用隔离SQLite。若另有展示准备替身，在结果中明确它的边界；不能把每层一个演员的规则替身称为真实PVF全流程。内容路径依赖 `PVF_ARCHIVE_PATH`，本轮显式配置并要求零跳过。
4. 通过结果选项或0084回城，确认塔归属解除、精确城镇存在重新发布，并可重新入场扣第二张券；回城/重新入场后的旧消息不应改变新run。增加一个可控竞态：到期sweep已捕获旧context但被gate阻塞期间，结束旧run并建立新run；释放后旧回调不能结束新run、重复发奖或发送旧返回帧。
5. 补0092的真实注册encoder输出向量0/1；已有其他五种协议字节向量不重复重写。沿完整入口记录实际消息身份/顺序和持久化前后值，不只断言被调用的方法名。
6. 运行新增集成测试、受影响普通副本/返回和架构集合，所有.NET串行。每个涉及PVF的exec进程显式设置当前路径。只使用隔离测试，不访问在线Host、玩家库或客户端；不提交Git。
7. 在TASK文件和对应verification记录执行命令、结果、真实/替身边界、覆盖的失败路径及还需主代理执行的solution/独立评审。若串联暴露生产缺陷，先给出最小复现和原因，由主代理授权同接受边界内的修复，再重复受影响检查；不要用弱化断言通过验收。

## Completion boundary

本单元通过也不等于整个Spec完成。主代理需检查全部TaskContract成功条件、补齐最终协议及结果文档、完成完整结果独立评审和新鲜解决方案验证，然后才可做一次本地结果提交。ClientVerified仍由human test list取得。

## Result — round 1, 2026-10-03

- Changed: 新增 `IllusionTowerPublicFlowTests`。第一条真实PVF用例从公共0010 `SelectDungeonHandler`进入11001，经0025、009F mode 1、逐个0027死亡与交替009F mode 2/002D推进，实际遍历15层地图 `31001, 31002, 31003, 31004, 31005, 31007, 31008, 31010, 31011, 31006, 31019, 31015, 31020, 31024, 31025`。它证明未清场推进无效、每层重新进入Loading、首张券只扣一次、TownArea撤销，并在最后一层取得settled receipt及严格有序的0090/0091/0092。
- Changed: 同一用例沿公共0084返回，验证塔归属解除、精确TownArea恢复；随后补第二张券并重新记录入场intent，再从公共0010建立不同RunId的挑战，第二张券仍只扣一次且再次发送008E/008F。
- Changed: 第二条用例以透明 `IIllusionTowerDomainService` 包装器只控制 `FindReturnsDue` 后同一sweep线程的下一次 `Find`。旧run到期context已捕获但被gate阻塞时，用公共0084结束旧run并用公共0010建立新run；释放后验证新RunId/RoomVersion/未结算状态、消息数、0090/91/92数、SQLite经验/金币与TownArea归属全部不变，旧回调未返回或奖励新run。
- Changed: `IllusionTowerCodecTests` 增加从真实 `ProtocolModule` 注册表解析0092 encoder的false/true向量，分别断言完整帧type为 `0x0092`、15字节头后的body为 `00`/`01`。
- Why: 既有测试分别证明真实PVF内容、单层领域状态、协议字节和真实TCP，但没有把玩家公共入口、15层状态推进、持久结算、返回与重入串成一条可观察闭环，也没有制造旧sweep已捕获context的竞争窗口。
- Checks: [TASK-07 bounded verification](../verification/TASK-07-verification.md)。新增公共流程2通过；Application塔与CMD架构70通过；Protocol塔、0010、0084和0025相关71通过；0092所在codec集合6通过；均0失败、0跳过，`git diff --check`通过。
- Evidence boundary: 塔定义/房间、物品放置、技能点、塔领域、Transactional CMD、奖励、SQLite及选角当前视图均为生产实现。替身只限隔离SQLite初始角色附表、记录传输的 `ShopSender`、不会被成功路径调用的无操作连接关闭器，以及竞态测试中委托全部行为、仅阻塞一个指定 `Find` 的透明包装器；没有使用每层单actor规则替身。
- Remaining: 本单元未运行全solution、最终Release全量、A21客户端实机或玩家数据库；这些由根流程和human test list完成。未启动Host/客户端、未部署、未提交Git，也未修改R3奖励批次重试策略。
