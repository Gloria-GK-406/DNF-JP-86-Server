# TASK-01 — 迷妄之塔内容与房间生成规则

## Task Unit Contract

- Revision: 1
- Outcome and references: T-001；RQ-002/AC-002。提供当前PVF 15层、固定/随机APC和奖励参数，规则服务用显式种子产生完整房间内容。
- Owned boundary: 新增 `Abstractions/World/IllusionTower/` 的内容/规则契约、`Infrastructure/Content/PvfIllusionTowerContentSource.cs`、`Application/World/IllusionTower/IllusionTowerRuleService.cs` 及必要模块注册、对应内容/规则测试。本单元不写运行领域服务、协议或资产代码。
- Required checks: 编译；15层和代表地图真实PVF读取；随机生成同种子一致、预算/出生位约束、纯APC层不为空；现有相关内容测试。
- Dependencies: 当前研究与只读PVF，现有Pvf解析设施。

## Detailed Plan

1. 参考现有 PvfDocument/LST/token 内容源组织，新增塔自己的不可变内容契约；层序来自 DGN 而非 mapId 排序。
2. 解析DGN入场/等级/层序和必要旗标、15MAP普通演员/固定APC/随机点数及位置、APC候选属性、ETC迷妄经验/奖励参数。保留字段来源，不把未确认参数命名为已确认语义。
3. `IIllusionTowerRuleService` 提供定义读取与给定层/种子的 `TowerRoomSeed`；种子包含层/map/seed/演员列表，演员为ListIndex、模板ID、等级、类型、是否阻挡清层。运行UID交由后续dgn分配，不借普通Cell上下文。
4. 随机APC参考已有候选/appearancepoint预算机制；明确标为参考规则，使用显式种子而非全局Random。避免将预算不足或候选缺失误发布成可直接清场的空层。
5. 塔规则允许调用已存在无状态基础内容/规则能力，但不调用普通副本运行服务。注册遵循现有模块；不新增公共塔父类。
6. 测试观察真实内容和生成行为，不仅断言DTO构造。静态配置测试不制造虚假的TDD记录；新增规则行为若采用TDD，保留实际RED/GREEN证据。

## Result — 2026-10-03 / round 1

- Changed: 新增 `IIllusionTowerContentSource` / `IIllusionTowerRuleService` 与迷妄专属内容记录；Infrastructure 从 DGN/MAP/AIC/ETC 一次解析并缓存不可变内容，Application 按显式 seed 生成房间演员。两模块已注册。新增 `IllusionTowerRuleServiceTests`；未修改运行服务、协议、资产、DB或Host，未提交Git。
- 接缝：`GetDefinitionAsync(CancellationToken) -> IllusionTowerDefinition`；`CreateRoomAsync(int level, uint seed, CancellationToken) -> TowerRoomSeed`。层号从1起。`TowerActorSeed` 包含 `ListIndex/TemplateId/Level/ActorType/BlocksClear/Position`，无运行UID。`TowerRoomSeed.Objects` 保存 passive-object 模板和坐标，不生成普通副本掉落。dgn自行分配跨层UID。
- Why: 普通演员与固定APC分别从0计算列表索引；随机APC从64起，候选按appearance point预算、出生位置数和不重复选择约束生成。这部分来自 `ServerS4A21/DeathTowerMapLoader` 的参考语义，确定性选取用本地 `Random(unchecked((int)seed))`，不主张与A21原始PRNG一致。等级模式1使用basis+offset，固定APC保持AIC等级。无可用随机候选、无出生位或无阻挡清场演员时明确失败，不发布空层。
- 内容修正证据：真实AIC `sandspoon.aic` 两次声明相同0 appearance point，因此只合并同值重复，冲突仍拒绝；DGN高阶技能冷却倍率为Single，输出保留double。诊断原文分别在 [duplicate-apc](../verification/task-01-duplicate-apc.log) 和 [dgn-tokens](../verification/task-01-dgn-tokens.log)。
- Checks: 下列命令均在工作区根运行、串行执行；测试前设置 `$env:PVF_ARCHIVE_PATH = 'E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'`。当前SHA-256复核为 `3b2fe3da115fee949282558a864a319fc9ca197e1d1f22e656e74316a0f04b06`。
  - `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~IllusionTowerRuleServiceTests --logger 'console;verbosity=normal'`：exit 0，5通过/0跳过；包含生产依赖编译、15层顺序/非空、普通等级40、纯APC层、末层APC608、奖励表、3/9层各32种子的稳定/预算/位置/不重复检查和异常输入。完整 [pass log](../verification/task-01-content-pass.log)。
  - 同命令早期失败保留：[编译分析器错误](../verification/task-01-initial.log)，[AIC重复字段](../verification/task-01-content.log)，[Single倍率误用Int32](../verification/task-01-content-fixed.log)。这些属于实现后验证与修正，不是TDD RED。
  - `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --filter 'FullyQualifiedName~PvfDungeonRoomActorSourceTests|FullyQualifiedName~PvfDungeonRoomActorRankTests|FullyQualifiedName~PvfDungeonDefinitionSourceTests' --logger 'console;verbosity=normal'`：exit 0，25通过/0跳过；完整 [regression log](../verification/task-01-regression.log)。
  - `git diff --check`：exit 0。
- Remaining: 本单元无未完成检查。后续单元仍需消费规则输出建立dgn/TowerRoom运行态、UID、入场扣券、推进、结算和协议。奖励参数仅解析，不发资产；随机候选池/索引算法仍为UpstreamImplementation参考，需集中A21实机验收，未升级ClientVerified。
