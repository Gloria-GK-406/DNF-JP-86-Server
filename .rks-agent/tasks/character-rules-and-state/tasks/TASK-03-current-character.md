# TASK-03 — 当前角色事实与消费者

## Task Unit Contract

- Revision: 1
- Outcome and references: ../plan.md T-003；../spec.md RQ-005/006，AC-005/006。角色现查基础属性/技能事实、相关选角和升级消费者迁移、存储兼容及架构说明。
- Owned boundary: 角色领域服务/实例与 Abstractions、既有选角及经验消费者、临时库测试、AGENTS.md 和 Document 设计说明。必要的前两项接缝修正属于本单元；不得扩大至新玩法。
- Dependencies: TASK-01 与 TASK-02 的规则服务。
- Required checks: 当前状态/提交/回滚/重载行为，既有创建、选角、经验与物品回归，全解决方案测试逐项 TRX 核验；记录实际证据，不提交。

## Execution Round 1 — 2026-09-09

实现基线：`8b19eaac9863e23da4703fa644bfd0cb882b6cd6` 加 TASK-01/TASK-02 已接受的未提交改动。实现已收敛，交还独立 review；未修改 review 状态或控制 YAML，未提交。

### Changed / Why

- `ICharacter` 暴露当前基础属性、已学技能和完整技能点账本；`PlayingCharacter` 与领域服务每次读取当前持久化事实，复用前两单元的共享规则。旧协议剩余点数入口保留兼容。
- 选角通过 owner 检查后，以当前职业、成长型、等级覆盖 19 个已建模基础属性；局部协议刷新只更新身份、进度、基础属性、技能与点数，保留背包、装备位置及 extra、动态血蓝百分比等其他投影。
- 副本选择、进入、击杀、结算、任务和新手升级消费者读取当前事实。新手升级在既有事务内读取实际等级，防止陈旧会话等级把高等级角色降到 2；翻牌按当前角色等级结算，已公布的固定卡价保持原规则。
- 更新 `AGENTS.md` 与 `Document/CurrentCharacterFacts.md`，说明当前事实、不可变规则与协议快照的职责。未改变 schema、运行数据库、客户端或新增玩法。

### Owned boundary and identity

本单元 50 个源码、测试及架构文档路径列于 [task03-owned-manifest.json](../evidence/task03-owned-manifest.json)，包含逐文件 Before/After SHA-256；独立增量为 [task03-owned.patch](../evidence/task03-owned.patch)。Manifest SHA-256：`2231742756DB3E7FB2BB7AA9EB91DA89CF54787D1A8CF53C50BD1153EC9B13DF`。

开始指纹捕获于 RED 用例加入之后。该测试文件此前没有前单元改动，所以其 Before 按 HEAD 原始内容记录并在 manifest 中注明；其余路径使用开始指纹。与前单元重叠的 `ApplicationModule.cs` 和 `CharacterRepositoryProductsTests.cs` 均重建本单元修改前文本并与开始指纹逐字节校验；前单元内容未重复计入本单元 patch。

### Checks / Evidence

回归风险以“陈旧等级请求不得把当前等级 5 降为 2”的可观察行为作 TDD 切入。实现前 [task03-tutorial-red.trx](../evidence/task03-tests/task03-tutorial-red.trx) 中唯一用例实际 Failed，返回 Granted 而期望 AlreadyPast；[日志](../evidence/task03-tutorial-red.log) 保留失败证据。RED shell 包装末尾读取日志，包装退出码为 0，未独立保存该次 dotnet 退出码，因此不将包装退出码作为 RED 依据。

新增真实 PVF、生产 DI、临时 SQLite 的行为验证：同一角色跨事务修改、回滚、提交及独立容器重载始终读当前值；真实选角和局部刷新保留完整物品信息；真实库当前等级 5 拒绝陈旧新手升级。还覆盖负剩余点数、超过 ushort 的奖励、未知技能保留及诊断，以及当前等级/职业与陈旧协议快照不同的消费者路径。最终三个集成用例均通过，见 [round4 TRX](../evidence/task03-tests/task03-current-sqlite-round4.trx)。早期夹具的 pc_room_state/非法负奖励修正及协议 fixture 缺失依赖记录仍保留，没有据此放宽生产校验。

最终 `dotnet build Server/86JP.Server.slnx` 返回 0，0 warning / 0 error，见 [构建日志](../evidence/task03-final-build.log)。设置 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，按七个测试项目执行 `dotnet test <csproj> --no-build` 并写出独立 TRX，逐条核验 `UnitTestResult`：

| 测试项目 | Passed | NotExecuted | 最终退出码 |
| --- | ---: | ---: | ---: |
| Application | 1162 | 0 | 0 |
| Infrastructure | 260 | 2 | 0 |
| Networking | 52 | 0 | 0 |
| Protocol | 656 | 0 | 0 |
| Pvf | 60 | 0 | 0 |
| Persistence | 333 | 0 | 0 |
| Host（修正后完整重跑） | 6 | 0 | 0 |
| 合计 | 2529 | 2 | 0 |

Host 首轮的 1 项失败为组合测试夹具缺少 `AddLogging()`，添加夹具注册后重新构建且 Host 全部 6 项通过；原失败 TRX 和退出码保留，最终依据 `Dfo.Server.Host.Tests-task03-corrected.trx`。Infrastructure 的两项缺失档案场景因设置真实 PVF 按设计跳过，随后清除 `PVF_ARCHIVE_PATH`，使用 `FullyQualifiedName~A_missing_archive_fails` 筛选，两项均 Passed、退出 0。因此 2531 个不同用例在各自适用环境下全部得到通过结果。未出现 SQLite ObjectDisposedException 遗留失败。

证据索引：[全部 TRX 逐项审计](../evidence/task03-trx-audit.json)、[七项目首轮退出码](../evidence/task03-full-exits.json)、[最终构建及 Host 退出码](../evidence/task03-corrected-exits.json)、[缺失档案 TRX](../evidence/task03-tests/Dfo.Server.Infrastructure.Tests-task03-no-archive.trx)、[缺失档案退出码](../evidence/task03-no-archive-exit.txt)。早期红灯与失败尝试均列在审计中；统计采用各项目最终成功轮次，未用重复轮次虚增用例数。

[最终自检](../evidence/task03-final-selfcheck.json) 确认 50 个交付文件的当前 SHA-256 与 manifest 一致，`git diff --check` 返回 0。

### Remaining / Handoff

实现写入权已释放，由父流程按上述独立增量进行 review。无待实现项；后续 review、控制状态推进及整个工作区的提交/整合由父流程负责。该轮不声明客户端实机协议验证或其他超出当前任务合同的完成。

## Independent Quick Review — TASK03-IQR-20260909-review_current-01

- Reviewer: `/root/review_current`，与执行者独立的 fresh reviewer；使用 `reviewing-task-units-quick`。本轮只追加此审查，不改实现、合同、控制 YAML 或提交。
- Reviewed result: Execution Round 1；Task Unit Contract Revision 1；`plan.md` T-003、`spec.md` RQ-005/006 与 AC-005/006，并核对技能账本的 AC-004 接缝。
- Owned diff: `8b19eaac9863e23da4703fa644bfd0cb882b6cd6` 加已接受 TASK-01/02 为前置；仅审 `evidence/task03-owned.patch` 的 50 路径独立增量。独立计算 patch SHA-256 为 `158085B21E9D87707B69831B6F256B3585DB14975CFA7BF98A3B3F92CD509AC1`，manifest SHA-256 为 `2231742756DB3E7FB2BB7AA9EB91DA89CF54787D1A8CF53C50BD1153EC9B13DF`；50 个当前文件均匹配 AfterSha256。没有把完整 HEAD 差异归入本单元。
- Verdict: `passed`。

### Review evidence and judgment

核对新增角色入口、事实/账本/投影服务、全部生产消费者增量及相应测试修改，并沿依赖读取 `CharacterSkillPages`、共享技能账本算法、SQLite 表查询及事务参与者。`BaseStatsAsync` 用一次 standing 读取决定职业与等级；技能事实从普通页和当前奖励读取；无新增角色值缓存。`GrantTutorialLevelCommand` 已移除会话等级参数，生产注册仍是 Transactional，当前等级检查与写入位于同一现有事务内。19 个基础属性映射逐字段核对无错配，owner 检查仍先于选角覆盖，局部刷新保留库存、页 slot/extra、活动页索引与未建模动态字段。

副本选择刷新后再构帧；进本、新手升级、任务职业以及无授予结果的击杀/结算发帧不再回落到旧等级/经验。翻牌去掉会话等级输入，当前等级经角色读取，已公布卡价继续由既有结算保留。未知技能保留在已学事实，账本显式携带缺失 ID 并在当前点数服务记录警告；long 余额与 ushort 协议夹取分开，未增加存储余额或新玩法。

独立逐条读取 `evidence/task03-tests/*.trx` 的 `UnitTestResult`，最终七项目轮次为 Application 1162、Infrastructure 260、Networking 52、Protocol 656、Pvf 60、Persistence 333、Host corrected 6，合计 2529 Passed；Infrastructure 另 2 项 NotExecuted 在 no-archive TRX 中均 Passed。早期 RED、SQLite 夹具和 Host 首轮失败未被计作最终成功；最终构建日志及 corrected exits 对应 0 warning / 0 error、BuildExit=0、HostExit=0。`git diff --check` 亦为 0。

审查时另设 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，执行 Application 项目的 `dotnet test --no-build --filter 'FullyQualifiedName~CurrentCharacterStateTests|FullyQualifiedName~TutorialRequestWithStaleLevelNeverLowersCurrentProgress' --logger 'console;verbosity=normal'`：4 个用例逐项 Passed，退出码 0。覆盖真实生产 DI/临时 SQLite 的当前读取、事务内变更、回滚、提交、独立容器重载、共享静态对象不污染、页配置/未知技能/负余额与大额奖励、局部投影保留，以及实际等级 5 不被教程降到 2。该现场复跑结果记录于本段，不冒称另有新增 TRX 文件。

未证明局部阻塞缺陷，无实际 blocker。事务外多个查询之间的全局原子性、整个会话聚合并发覆盖及客户端实机确认仍是已声明的边界，本次未扩张验收范围。此结论仅覆盖 TASK-03 的局部 quick review，不替代 AC-006 后续完整独立 change review。审查写入权已释放。
