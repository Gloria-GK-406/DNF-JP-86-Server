---
kind: rks-plan
spec:
  path: .rks-agent/tasks/inventory-slot-boundary/spec.md
mode: direct
state: ready
---

# Implementation Plan: 库存槽位边界单源化

## Planning Choice

使用 direct 详细计划。任务虽然跨协议、应用和持久化适配，但只有一个不可拆分的坐标边界，后续步骤的接口和依赖已经能一次确定；分成独立 Task Unit 反而会在中间制造不能验收的新旧混合状态。

## Approach and Shared Decisions

- 在共享 Data 层定义 `GameSlot`、`BusinessSlot` 和业务空间枚举；一个地址始终作为一个值传递。
- 在 Abstractions/Application 建立唯一双向映射规则服务。映射服务采用强返回值，失败抛出携带源地址的专用异常，没有 `TryXxx`、默认值或普通业务拒绝降级。
- `GameSlot` 仅供协议请求、协议响应和持久化/投影适配边界使用；`BusinessSlot` 供 ICommand、领域接口、槽位规则和业务回执使用。
- 现有数据库继续保存 `list_type/slot_index`。领域容器访问通过一个业务槽位绑定入口定位既有 backing container 与存储槽，调用者不重新计算协议偏移。
- 物品放置从“绝对槽起止”迁移为“目标业务空间 + 局部容量”；快捷栏是独立业务空间，但仍可映射回 Main 3..8。
- 客户端虚拟货币槽和未建模空间不进入普通物品 CMD；状态快照中的专用虚拟投影保持原行为。

## Work and Dependencies

### T-001 建立坐标值对象和唯一映射（RQ-001、RQ-005、RQ-006）

1. 在库存数据模型中新增 `GameSlot`、`BusinessSlot`、业务空间枚举和映射异常。
2. 在库存抽象中新增双向映射规则接口，在 Application 中实现 Spec 映射表并注册 DI。
3. 先写映射区段首尾、往返、未知/保留/虚拟/越界异常测试。
4. 映射表内部集中保存基址与长度；不在调用者暴露偏移常量。

### T-002 将领域槽位和物品放置规则迁移为业务坐标（RQ-003、RQ-008、RQ-009、RQ-010）

1. 将 `ItemPlacement` 从协议绝对 `SlotStart/SlotEnd` 改为目标业务空间和局部范围/容量。
2. 将 `IItemSlotRuleService` 改为校验 `BusinessSlot + ItemPlacement`，删除 Main 专用绝对槽判断。
3. 增加业务槽位绑定入口：由角色与 `BusinessSlot` 得到 backing `IItemContainer` 和存储下标；所有转换复用 T-001 的权威映射。
4. 调整发放、到达、补充、商城和容器操作，使它们在业务空间内选择局部槽，再由持久化适配映射到既有数据库坐标。
5. 保留旧数据库兼容测试，确认加载/写回和客户端投影仍得到原 GameSlot。

### T-003 迁移六条 CMD 输入、命令和领域调用（RQ-002、RQ-003、RQ-008）

1. Decoder 在读取两个 wire 字段时立即构造 `GameSlot`；请求 DTO 不再暴露分离地址。
2. 协议 Handler 在调用 CommandBus 前执行 `GameSlot -> BusinessSlot`；ICommand 只携带业务槽位。
3. 迁移 MoveItemSpace 的起点/终点及同容器、跨容器、穿戴/卸下分支；路由依据业务空间，不再依据 wire list 数字。
4. 迁移 UseStackable、DeleteItem、SellItem、RepairItem、DisjointItem 的 Handler、CMD Handler、领域接口和领域服务。
5. 分解在业务槽位内容合法后再建立 `EquipmentInstance`；保留 `contextValue` 原行为。

### T-004 迁移业务结果和协议响应出口（RQ-004、RQ-005、RQ-010）

1. 将出售、丢弃、修理、分解和移动结果中的位置改为 `BusinessSlot`。
2. 协议 Handler 在响应构造前统一反向映射为 `GameSlot`；响应 DTO/Encoder 只接受 `GameSlot`。
3. 删除为回包旁路保存原始 space/index 的逻辑；成功和普通业务失败都使用同一反向映射。
4. 更新通知/状态差量中由这些 CMD 产生的槽位，使其通过公共映射投影。

### T-005 异常隔离与旧路径清除（RQ-006、RQ-007、RQ-010）

1. 确认映射异常在 Game 网络边界被记录并只影响当前连接；不得在六条业务 Handler 中转成普通拒绝。
2. 增加异常隔离测试，证明另一独立连接仍可继续分发。
3. 扫描并删除六条链路和公共槽位规则中的分离地址签名、私有偏移与旧 Main 区段判断。
4. 若仍需持久化原始字段，只允许存在于 Persistence/协议投影适配代码，并由公共映射生成。

### T-006 集成回归与文档收口（RQ-001 至 RQ-010）

1. 更新受影响的协议、应用、持久化测试构造器与断言。
2. 运行映射、六条 CMD、库存移动、状态同步、持久化定向测试，再运行完整解决方案构建和相关完整测试项目。
3. 更新库存坐标设计文档，明确 GameSlot/BusinessSlot 边界及虚拟槽非普通 CMD 目标。
4. 对完整差异执行独立结果审查，修复所有阻塞发现后重新运行新鲜验证。

依赖顺序为 `T-001 -> T-002 -> T-003 -> T-004 -> T-005 -> T-006`。T-003/T-004 可以在同一次编译驱动迁移中交错，但不能在任一步留下可运行的旧入口。

## Verification

- 映射测试覆盖每段首尾、双向往返和所有非法类别，期望只产生强返回或专用异常。
- 协议 codec 测试继续验证既有二进制正文；Handler 测试验证 CommandBus 仅收到 `BusinessSlot`。
- 移动测试覆盖 Main 同区、Main 跨区、快捷栏、穿戴/卸下、不同 backing container。
- 分解测试覆盖 `Main,4 -> QuickSlot,1` 的装备成功、非装备业务拒绝和槽位内容不变量失败。
- 持久化测试从既有 `list_type/slot_index` 数据加载、修改并回投相同 `GameSlot`。
- 静态扫描确认六条 ICommand/领域接口不存在分离地址，公共代码不存在另一套区段换算。
- `dotnet build Server/86JP.Server.slnx` 和受影响测试项目必须在任务工作树中取得新鲜通过结果。
