# TASK-02 — 挑战实例、房间生命周期与原子入场

## Task Unit Contract

- Revision: 1
- Outcome and references: T-002；RQ-001、003–006。交付独立 IllusionTowerDomainService / IllusionTowerDgn / TowerRoom，以当前内容规则装载并运行完整15层，入场材料和运行归属原子提交。
- Owned boundary: Abstractions/Application 的 World/IllusionTower；Application 模块注册；普通 DungeonVisitDomainService 最小互斥检查；对应 Application 测试。不得改协议、用户运行目录、真实DB；奖励资产和会话退出入口由后续单元完成。
- Dependencies: TASK-01 的 GetDefinitionAsync/CreateRoomAsync 以及实际内容和通过证据；现有 DomainTransaction/SelectedSession/材料消费能力。
- Required checks: 入场成功、低等级/无券/无效会话/重复或跨模式拒绝、多人扣券失败回滚；加载/开始门、击杀去重、敌对APC清场、逐层推进、旧RunId/RoomVersion/UID拒绝、首层/中途失败/末层成功、离开/断线/重入、事务失败不发布状态。串行运行相关测试并记录真实结果。

## Detailed Plan

1. 新增独立契约与只读快照，包含挑战身份、成员的精确SelectedSessionIdentity与wire UID、领导者、当前房间版本、加载/战斗/完成阶段、实际完成层记录、结束原因和奖励计层。领域不保存网络连接对象；调用输入携带精确session、挑战和房间版本，不能仅按角色查当前挑战后执行旧动作。
2. IllusionTowerDgn 持有整局事实，TowerRoom 持有演员/对象/死亡/准备事实；演员UID整局不复用并排除参与者wire UID。用事务参与者暂存变化，prepare检查基线，commit发布，rollback不泄漏；避免复制整个服务器所有挑战或旁路第二套进度。
3. 入场校验成员身份、等级、普通副本与塔互斥，先完整准备首层和所有成员材料计划，再消费所需4183并发布。复用现有材料/物品生命周期；域外调用也进入已有事务/执行门。普通副本入口加塔归属检查，保持原有构造fixture兼容并防止DI环。
4. 明确 Loading → Ready/Started → Fighting → Cleared；所有有效成员加载后允许开始。击杀只接受有效战斗阶段和活演员，返回已提交击杀事实供后续经验/任务处理；纯APC层不能误清场。清层+客户端过渡才调用 NextLevel，由服务端选择下一层，准备失败保持旧层。重复开始/死亡/过渡均幂等。
5. 末层成功和全部有效成员死亡形成待结算结果；实际完成数与奖励计层独立，失败奖励层=max(1,当前层-1)。退出/断线移除精确成员，领导者交接，最后成员离开释放索引；原session离开不能清理重连新身份。结算资产留给后续单元，通过明确的事务内完成标记接缝配合。
6. 保留可扩展的已提交事实结果（演员、成员、结束/完成层），使后续协议、即时击杀XP、结算和任务可以在同一命令事务中调用，不能把奖励直接放入网络handler。禁止借用普通 Cell/迷宫推进或构建公共塔框架。
7. 以行为测试推进，覆盖上述生命周期和事务回滚；不冻结私有布局。记录测试输入、命令、退出码和必要日志。只完成本单元，不提交Git或声明整项完成。

## Result — 2026-10-03 / round 1

- Changed: 新增 `IIllusionTowerDomainService` 契约与不可变快照、`IllusionTowerDomainService`、有行为的 `IllusionTowerDgn` / `TowerRoom`，Application 注册；普通入口仅增加塔归属检查和当前事务模式预约。新增 `IllusionTowerDomainServiceTests`。运行态、DB资产同事务发布；未改协议、奖励规则、用户运行服务/DB，也未提交Git。
- 稳定接缝：`EnterAsync(IReadOnlyList<TowerEntryMember(Session, Slot)>, leader, seed, ct)` 保留真实稀疏队伍槽；`TowerActionContext(Session, RunId, RoomVersion)` 驱动 Load / Start / Kill(uid) / NextLevel / Die / Leave(disconnected) / MarkSettled(receipt)。`Find(session)` 在当前命令事务中读取自己的暂存变化。`TowerActionResult.Changed` 区分新事实与重复动作；Kill新增事实带演员，后续击杀XP/任务可在同一CMD事务消费。当前阶段为 Loading → Ready → Fighting → Cleared，Start是显式动作。
- 计时与结算：快照保留挑战 StartedAt、当前房间 StartedAt/ClearedAt、结束 CompletedAt；来源为注入的 TimeProvider。实际完成层与失败奖励计层分开。`TowerSettlementReceipt` 为后续奖励领域的可扩展不可变凭据，目前包含成员经验/金币/物品决定；`MarkSettledAsync` 将其与结束标记同事务附着，重复提交不覆盖原凭据，不抽奖或发资产。
- Why: 塔独立事务参与者只暂存触及的挑战实例，Prepare核验基线，Commit才更新运行和成员索引；普通副本 PreparePartyEntry 的暂存状态不能通过 Find 读到，因此普通入口在地图/资格/疲劳/首房准备成功后、扣疲劳前记录仅限当前事务的模式预约，阻止同一CMD内普通→塔入场；塔→普通则读取塔暂存成员归属。无第二份普通进度或持久索引。
- 准确会话清理：执行动作及入场需精确当前 SelectedSessionIdentity；Leave允许移除精确旧身份，不依赖其他旧成员仍在线。两个会话同时失效的清理问题已复现并修复，避免双方因对方离线永久滞留。最后成员离开释放挑战，剩余成员全死产生待结算结束事实。
- TDD与检查：先建立入场测试并观察 NotImplemented 行为失败，后完成真实临时SQLite券2→1与首房发布；初期fixture误把券放装备区9号槽，按业务映射改为消耗品65号存储槽，此次fixture修正不算产品RED。双会话失效清理有独立RED/GREEN。其他生命周期/互斥覆盖属于实现后行为验证。所有dotnet命令串行执行；测试数据库均由fixture在系统临时目录创建。
  - `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~IllusionTowerDomainServiceTests --logger 'console;verbosity=minimal'`：最终exit 0，22通过/0跳过；[领域日志](../verification/task-02-domain.log)。覆盖单/多人入场、低等级/无券/旧会话/重复拒绝、加载/开始门、纯APC十五层、击杀/过渡去重、整局UID及旧RunId/RoomVersion/UID拒绝、首层/中途失败和末层成功、房间准备失败、真实SQLite多人扣券回滚、Prepare失败回滚、同事务读取/撤销Kill与结算凭据、领导者离开与剩余全死、双会话失效、旧身份不能清理新入场、并发重复入场、普通双向及同事务互斥、普通拒绝/回滚不残留模式预约。
  - 入场RED：[日志](../verification/task-02-red.log)（测试结果1失败；当时shell末尾Get-Content使shell退出码0，dotnet测试失败见日志）；首次GREEN：[日志](../verification/task-02-entry-green.log)，1通过。双会话失效RED：[日志](../verification/task-02-disconnect-red.log)，exit 1，1失败；最终领域日志包含修复后通过。
  - `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~IllusionTowerDomainServiceTests|FullyQualifiedName~DungeonVisitDomainServiceTests|FullyQualifiedName~DungeonRunDomainServiceTests|FullyQualifiedName~DomainTransactionTests|FullyQualifiedName~AccountMaterialConsumptionTests|FullyQualifiedName~SkillMaterialConsumptionTests|FullyQualifiedName~CommandHandlerArchitectureTests' --logger 'console;verbosity=normal'`：exit 0，159通过/0跳过；[回归日志](../verification/task-02-regression.log)。
  - `git diff --check`：exit 0；普通入口文件换行归一化后只有必要的7行增量及构造器扩展，无整文件重排。
- Remaining: 本单元已交付。后续仍须在命令事务中接入击杀经验/任务、最终奖励资产与具体结算receipt，以及协议、会话退出调用点；返回的动作事实在外层事务提交前为暂存结果，不能提前发网络通知。内容规则由TASK-01覆盖真实PVF，本单元生命周期使用确定性规则fixture；尚未A21实机验收。写入权及所有.NET执行权交还主代理。
