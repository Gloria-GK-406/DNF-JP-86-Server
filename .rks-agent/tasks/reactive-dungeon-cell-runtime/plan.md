---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan: Cell-owned dungeon runtime

## Planning Choice

使用 direct 详细计划。工作虽然跨 Abstractions、Application 和 Protocol，但所有变更必须在
一个切换点共同成立：Cell 成为唯一房间真源。单独交付模型、死亡或拾取中的任一部分都会形成
双真源，不能作为独立 Task Unit 接受；详细步骤可以在这一份 Plan 中维护，并在每个行为切片
完成后保持可编译和测试可观察。

## Approach and Shared Decisions

### Runtime model

- 在 `Dfo.Server.Abstractions/World` 定义只读 `DungeonCellState`、
  `DungeonCellActorState`、`DungeonGroundItem`、来源 identity 和 Cell change/claim 结果。
- `DungeonRunState` 暴露当前 `DungeonCellState`；Run aggregate 内部持有
  `Dictionary<MazeCell, DungeonCellAggregate>`。Cell aggregate 独占 Actor 存活状态、pending
  loot、GroundItems、拾取预占、清理状态和 revision。
- 保留 Run 级 `RuntimeRevision` 用于跨参与者/Cell 的投影排序，但每次 Cell 内提交同时带上
  `CellRevision`。删除 `DeadActors`、`MonsterDrops`、`TakenGroundDrops` 及其推导方法。
- GroundItem 使用一个稳定编号空间。初始声明物按现有协议占用低号；运行期产生物从当前最大号
  后递增。来源保存 declared object index 或 actor sequence，协议 identity 仍由来源映射。
- 不增加虚构坐标。模型允许以后增加已证实的 placement 数据，但本任务只保存当前协议与规则
  已知字段。

### Creation boundary

- 增加 Cell 创建输入/结果边界与 Application 实现。创建器一次读取 room actor/drop source、
  item placement 分类、普通掉落规则和任务掉落事实，生成完整未发布 Cell seed。
- 将当前 `MonsterDropService` 的“抽规则 + 写 Run”拆开：创建阶段只生成每个敌对 Actor 的
  pending loot；运行期死亡不再调用该服务。普通掉落保持现有 deterministic seed；任务掉落
  保持 QuestLinked、leader owner、背包上限和任务状态语义，并在创建时冻结。
- 初次进本采用 detached run preparation：布局、RunId、参与者和初始 Cell context 可供创建器
  使用，但 `Find(character)` 在 initial Cell 构造完成前不可见。成功后一次发布 Run 与 initial
  Cell；失败只释放 preparation。
- 换房采用受 `RunId + RoomVersion + target` 保护的 entry claim。新 Cell 先在 claim 下构造，
  成功后一次发布并切换；失败释放 claim。已存在 Cell 直接复用。一个 Run 同时只允许一个有效
  entry claim，从而避免并发创建采用多个实例。

### Runtime transactions and projection

- Actor death claim 从当前 Cell 取得 Actor facts；持久化经验/任务效果仍在 CMD 管线中完成。
  Cell death commit 原子设置死亡、转移 pending loot、更新清理状态并返回
  `DungeonActorDeathChange`。handler 使用该 change 构造原有死亡通知。
- Ground pickup claim 直接从 Cell GroundItems 取得完整 `GroundPiece`；背包/金币写成功后提交
  移除，异常路径释放。现有选择性重试和命令串行化不改变。
- `MonsterKillDomainService` 和 `GroundPickupDomainService` 改为读取 Cell snapshot 的纯领域
  解释，或在职责完全被 claim 覆盖后移除；不得继续引用 room/content source。
- START_MAP、DIE_MONSTER 与 PICKUP 的 codec 不改变。首次进入由 Cell snapshot 投影 actor 与
  初始声明物；A21 重访仍可发送空的重访部分，但判断依据是复用 Cell，而非重新合成空房间。

## Work and Dependencies

### T-001 — 建立行为保护与 Cell 模型（RQ-001, RQ-002, RQ-006, RQ-008）

1. 在 Application 测试项目增加 Cell 生命周期行为测试：初始 seed、稳定 actor/drop identity、
   统一 GroundItems 和 revision。
2. 先观察测试因缺少 Cell 模型失败，再增加 Abstractions 模型与 Run aggregate 的 Cell 容器。
3. 暂时通过兼容投影让既有消费者可编译，但不得保留可写的第二套房间状态。
4. 聚焦运行 `Dfo.Server.Application.Tests` 中的新增测试和既有 Run 测试。

### T-002 — 原子创建、首次发布与重访（RQ-001, RQ-002, RQ-003, RQ-004）

1. 增加 detached party-entry preparation 和 cell-entry claim 的失败/并发 RED 测试。
2. 实现 Cell 创建器，集中读取 room actors、declared drops、item classification 和每 Actor 的
   pending loot。
3. 重写 `DungeonVisitDomainService`：initial Cell 完成后才发布 Run；move 先 claim/构造再提交；
   已创建 Cell 直接返回镜像且不调用创建器。
4. 增加计数 fake，证明重访和 actor runtime 操作不会重复调用 content/rule；验证取消/异常会
   释放 preparation/claim 并保持原位置。
5. 适配进入和组队进入协议测试，保持现有帧字段不变。

### T-003 — 迁移死亡与清理到 Cell（RQ-003, RQ-004, RQ-005, RQ-009, RQ-010）

1. 增加 Actor pending loot、死亡成功转移、失败释放、重复死亡和旧版本 claim 的 RED 测试。
2. 让 death claim 携带 Cell Actor facts；删除运行期 declared-actor 查找与 drop content 调用。
3. 重写单人与组队死亡路径，以持久化击杀效果成功作为 Cell death commit 前置；commit 返回同一
   change 中的 drops、clear state 和 revisions。
4. 保持 participant effect 的选择性重试、经验/任务写入与死亡通知字段；更新死亡与 clear 测试。
5. 删除 `MonsterDropService` 的 Run 写入职责及不再使用的 `AllocatedMonsterDrop`、分配 API。

### T-004 — 迁移统一拾取事务（RQ-006, RQ-007, RQ-009, RQ-010）

1. 增加声明物与怪物物共存、成功移除、持久化失败释放、重复拾取、owner 拒绝和过期 Cell
   claim 的 RED 测试。
2. 让 pickup claim 从当前 Cell 的同一 GroundItems 集合返回物品快照；commit 原子移除并生成
   change，release 不改变 revision。
3. 修改拾取 CMD/handler 和投影 identity 映射，去除运行期 `IDungeonRoomRuleService` 与
   `IItemPlacementRuleService` 查询。
4. 更新背包、金币、任务拾取和组队观察者测试，验证创建 change 先于移除 change 的 revision。

### T-005 — 清除双真源并收敛消费者（RQ-008, RQ-009, RQ-010）

1. 全仓迁移 `DungeonRunState` 构造、测试 fake、clear/settlement/quest 消费者到 CurrentCell
   snapshot。
2. 删除 Run 级 `DeadActors`、`MonsterDrops`、`TakenGroundDrops` 字段、查找/编号帮助方法及等价
   缓存；静态搜索不得在生产代码残留。
3. 保持 Application 不引用 Infrastructure，并检查领域模型不含 Session/Connection/发送器。
4. 运行 Application、Protocol、Networking 相关测试，处理只由新模型暴露的真实回归。

### T-006 — 文档、集成验证与交付证据（RQ-011；全部 AC）

1. 在 `Document` 新增 Cell runtime 设计文档，记录首次创建、重访、死亡转移、拾取补偿和投影
   数据流，以及当前没有坐标证据的边界。
2. 更新现有与旧 `MonsterDropService`/Run 号源归属直接冲突的文档段落；历史抓包事实保持原样。
3. 运行格式/静态搜索、相关项目测试、完整解决方案测试与 Release build。
4. 对照每项 AC 汇总证据；若实际交付留下已知结构缺口，更新技术债表，否则保持空表。

## Verification

- TDD 行为切片：每个新增行为先运行聚焦测试得到因缺失行为导致的 RED，再完成最小实现并取得
  GREEN；接口机械迁移只作为结构演进，不把编译失败伪装成 RED。
- 聚焦测试：`Dfo.Server.Application.Tests` 的 DungeonRun/DungeonVisit/transaction/Inventory，
  `Dfo.Server.Protocol.Tests` 的 DungeonEntry/MonsterDeath/GroundPickup/DungeonClear，以及相关
  Networking integration tests。
- 静态检查：生产代码中不存在 Run 级 `MonsterDrops`、`TakenGroundDrops`、`DeadActors`；死亡
  和拾取运行路径不依赖 room/content source；Application 引用边界测试通过。
- 集成检查：`dotnet test Server/86JP.Server.slnx -c Release`，随后
  `dotnet build Server/86JP.Server.slnx -c Release --no-restore`，要求全部退出码为 0 且构建零
  warning/zero error。
- 完整结果由独立 reviewer 对照 TaskContract、Spec、Plan、实际 diff 和测试证据审查；修复任何
  阻断项后重新审查并执行新鲜完成验证。
