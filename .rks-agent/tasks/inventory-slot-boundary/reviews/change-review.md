---
kind: rks-review
review_kind: complete-code-change-re-review
verdict: approved
baseline: 0421896b58641f4630485d48fddb895834014a5d
target: inventory-slot-boundary working tree (uncommitted)
---

# Change Re-review: inventory-slot-boundary

## Review Scope

- 审查目的：独立复审第一次完整代码审查提出的五项问题是否闭合，并检查修订是否引入新的阻断问题。
- 治理来源：仓库 `AGENTS.md`、`TaskContract.md` revision 1、`routing-decision.md`、`spec.md`、`plan.md`、`sdd-execution.yaml`。
- 目标基线：`0421896b58641f4630485d48fddb895834014a5d`。
- 目标内容：当前最新未提交工作树，包括生产代码、测试、设计文档和 SDD 工件。
- 审查标准：`reviewing-work` 的 complete code change 标准；复查完整差异、边界归属、失败语义、回归证据及计划交付完整性。

## Verdict

```yaml
kind: rks-review
verdict: approved
```

第一次审查提出的五项问题均已闭合。六条普通物品路径已形成同一条可逆边界：decoder 原子构造 `GameSlot`，Protocol handler 在创建 ICommand 前强制映射为 `BusinessSlot`，命令、业务服务及回执只携带业务坐标；需要回写的成功结果在 Protocol 通知/响应构造阶段经唯一 mapper 投影为 `GameSlot`。本轮未发现新的阻断问题。

## Findings

无 High、Medium 或 Low 阻断 finding。

### 已关闭 1 — `ItemPlacement`/T-002 的绝对槽位双模型

`ItemPlacement` 现只公开 `BusinessSlotRange Destination`，不再有 `SlotStart/SlotEnd`（`Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs:53-60`）。`ItemSlotRuleService` 只按 `BusinessInventorySpace` 和局部下标判断（`Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs:42-52`）；到达、补充、商城、开包、分解和容器候选选择也使用 `BusinessSlot`/`BusinessSlotRange`。

仍可搜索到的 `SlotStart/SlotEnd` 只属于 `PvfEntryLifecycleContentSource.RefillItemMetadata` 的私有 PVF 原始读取模型，并在 Infrastructure 内容入口立即通过中央 mapper 转换为 `BusinessSlotRange`（`PvfEntryLifecycleContentSource.cs:84-95,117-129`）；`PvfItemPlacementDefinitionSource` 同样在返回 Abstractions 内容模型前完成转换（`PvfItemPlacementDefinitionSource.cs:43-70`）。这属于计划允许的外部内容适配边界，不是可继续驱动业务的第二套槽位模型。

商城、开包、分解和容器代码中剩余的 `ToGameSlot` 调用发生在业务槽位已经选定之后，用于把选择结果对接既有 backing container/slot 或持久化写计划；它们全部委托 `InventorySlotMappingRuleService`，没有私有偏移表。故其性质是容器/持久化适配，而不是旧绝对槽位规则。

### 已关闭 2 — `IBusinessInventorySlotDomainService` 的协议坐标泄漏

`IBusinessInventorySlotDomainService` 现在只接收 `BusinessSlot` 或 `InventoryStorageSlot`，返回 `BoundInventorySlot`，不再公开 `ToGameSlot`（`Server/src/Dfo.Server.Abstractions/Inventory/IBusinessInventorySlotDomainService.cs:13-21`）。

新增 `IInventorySlotProjectionDomainService` 只被 Protocol 的通知 composer 使用，唯一实现仅委托中央 mapper 完成 `BusinessSlot -> GameSlot`（`IInventorySlotProjectionDomainService.cs:5-13`、`InventorySlotProjectionDomainService.cs:6-9`）。虽然受现有 Notification 架构约束而使用 `DomainService` 后缀，它的语义和调用位置都是只读协议投影适配器，不参与业务决策或容器绑定；这与 `plan.md:19` 允许 `GameSlot` 出现在协议/投影适配边界一致，不构成 RQ-003 的领域泄漏。

### 已关闭 3 — Repair 把“结构可映射”误当成“行为允许”

`EquipmentRepairDomainService` 新增独立的 `IsRepairable(BusinessInventorySpace)` 行为允许列表（`EquipmentRepairDomainService.cs:54-67,86-93`），保留基线的 Main、Equipment、PersonalCargo 语义，同时拒绝 Pet `CreatureEquipment` 等只具备结构映射的空间。`Structurally_mapped_pet_equipment_is_not_a_repair_target` 回归测试证明该请求不扣金币、不写耐久并返回普通拒绝（`EquipmentRepairTests.cs:110-122`）。

### 已关闭 4 — Repair-all `space/-1` 成功回包丢失 wire space

`RepairItemRequest`、成功通知和 `RepairItemResponse` 以专门的 `RepairAllSpace` 保存非位置型全修哨兵的 wire space；单件地址仍只由 `GameSlot` 表达（`RepairItemMessages.cs:6-17`、`EquipmentRepairCompleted.cs:9-30`）。编码器在 `Slot == null` 时写回该 space 和 `-1`（`RepairItemResponseEncoder.cs:16-20`）。字节级测试覆盖 `PersonalCargo/-1` 解码及成功正文原样回写（`RepairItemRegistrationTests.cs:30-43`）。该字段没有进入 ICommand、修理领域服务或业务回执，未形成原始地址旁路。

### 已关闭 5 — 设计文档及 AC-006 专门证据缺失

新增 `Document/inventory-slot-coordinate-boundaries.md`，明确网络、内容、业务、持久化四类坐标，双向适配方向，虚拟槽/全修哨兵，以及结构映射与行为授权的区别。

新增 `Existing_storage_coordinates_round_trip_through_the_business_binding`，覆盖 `Main/70 <-> Consumable/5`、`Equipment/13 <-> WornEquipment/2`、`PersonalCargo/7 <-> PersonalCargo/7`，验证已有 storage container/index 通过实际 `BusinessInventorySlotDomainService.Get/FromStorage` 往返（`CharacterContainerDomainServiceTests.cs:131-156`）。结合未改动数据库格式及完整 Persistence 测试通过，补足 AC-006 的业务绑定证据。

## Acceptance Coverage

| Acceptance | Status | Evidence |
|---|---|---|
| AC-001 | Met | 六条 request DTO 均原子携带 `GameSlot`；六条 Protocol handler 在 CommandBus 前映射；ICommand/相关领域签名及回执的架构测试禁止 `GameSlot`/游戏空间枚举。 |
| AC-002 | Met | Move/Use/Delete/Sell/Repair/Disjoint 的响应在 Protocol 边界反向投影；既有 codec 样本通过；repair-all 非 Main space 有新增字节级覆盖。 |
| AC-003 | Met | 唯一 `InventorySlotMappingRuleService` 覆盖全部支持区段首尾、双向往返、未知/保留/虚拟/越界及携源坐标专用异常。 |
| AC-004 | Met | `InventorySlotMappingFailureIntegrationTests` 证明映射异常被记录并隔离到失败连接，后续独立 TCP 连接仍可完成请求；业务 handler 未把映射异常降级为普通拒绝。 |
| AC-005 | Met | 分解覆盖 QuickSlot 装备成功、非装备行为拒绝及存储内容/空间不变量失败；Repair 另有结构合法但行为不允许的 Pet 回归。 |
| AC-006 | Met | 数据库格式保持不变；Persistence 回归通过；新增实际业务绑定往返测试覆盖 Main、Equipment、PersonalCargo 三类既有地址。 |
| AC-007 | Met | `ItemPlacement`、arrival/refill/shop/opening/container 选择已使用业务空间；生产代码扫描只在中央 mapper 和内容/容器/协议投影适配点发现绝对坐标转换；完整构建与测试通过。 |

## Boundary Audit

- 六条输入路径：Move、Use、Delete、Sell、Repair、Disjoint 的 decoder 均先组合 wire 字段为 `GameSlot`，handler 在 ICommand 创建前调用同一 `IInventorySlotMappingRuleService.ToBusinessSlot`。
- 六条业务边界：ICommand、CMD handler、六条领域服务接口和 sale/discard/repair/disjoint 回执使用 `BusinessSlot`；没有分离的协议 `space + index` 地址签名。
- 六条输出路径：需要槽位的成功响应由 Protocol handler/notification composer 将业务槽投影为 `GameSlot`；encoder 只拆解协议 DTO 中的 `GameSlot`。普通失败保持既有 wire 形式；Repair-all 以显式非位置哨兵处理。
- 单一真源：协议绝对区段偏移只在 `InventorySlotMappingRuleService` 中；Application 出现的区段数字仅为业务局部容量或历史证据注释，没有第二个偏移换算器。
- 持久化/容器边界：`InventoryStorageSlot`、`BoundInventorySlot` 和映射后的写计划承接既有 container/index；业务槽位选择及行为判断不依赖协议绝对区段。

## Fresh Verification

- `dotnet build Server/86JP.Server.slnx --no-restore`：通过，0 warning / 0 error。
- 设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后执行 `dotnet test Server/86JP.Server.slnx --no-build --no-restore`：3519 passed，0 failed，2 skipped；两个 skip 均为 Infrastructure 的缺失归档负例。
- `git diff --check 0421896b`：通过。
- 生产代码扫描：`SlotStart/SlotEnd` 仅存在于 Infrastructure 私有 PVF 原始元数据；绝对偏移表仅存在于中央 mapper；没有发现第二个 mapper 或六条 CMD 的旧分离地址签名。

## Residual Risks

- AC-006 的新增专门测试验证了真实业务绑定对象与既有 storage 坐标往返；SQLite 加载、修改、状态投影分别由既有 Persistence/Protocol 回归覆盖，但没有单个测试把三段串成一条端到端场景。现有组合证据足以验收，后续若持久化格式变化，建议补一条单场景纵向测试。
- `IInventorySlotProjectionDomainService` 的命名是 Notification 依赖规则要求造成的架构折中。其生产实现目前保持无状态且仅委托 mapper；若未来加入库存读取或业务判断，就会重新越过协议投影边界。
- 映射表的结构合法范围有意大于各 CMD 的行为允许范围。Repair 已补反例测试；其余命令已有各自空间门，但参数化的“六命令 × 全业务空间”矩阵测试仍可进一步降低未来误扩权风险。
