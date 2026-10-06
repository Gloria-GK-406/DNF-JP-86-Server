# TASK-002 — 共享材料容器与原子迁移

## Task Unit Contract

- Revision: 1
- Outcome: T-002；RQ-002/RQ-004、AC-001/AC-004。11 种材料使用普通物品存储和容器；本单元交付底层、默认放置、账号访问及迁移，下一单元清理所有用例与协议的旧结果分支。
- Dependency: TASK-001 已验看结果；ContainerOwner 复用 InventoryOwnerType；账号多容器唯一约束已修正。
- Owned boundary: Data/Abstractions 的共享空间及映射；Application 的容器工厂、放置、容量、底层物品准入；Persistence 的 Schema 迁移及 store 支持；对应定向测试。本任务文件可追加结果。
- Restrictions: 不提交、不部署、不读写线上库、不改父级任务工件；串行运行 dotnet。需要改变其他用例时先告知父级，避免把 T-003 混入本单元。

## Detailed Plan

1. 增加服务端 SharedMaterial 存储种类和 BusinessInventorySpace，固定局部槽 0..10；Game 映射只输出 Main/354..364，内部存储种类不能编码到客户端。LegacyStorageCoordinates 区分业务槽、内部持久化槽、Game 槽。
2. 复用 IBusinessItemContainer/PersistentItemContainer/IItemDomainStore，提供显式 ContainerOwner 的通用 GetStore 重载；保留现有角色调用接口作为兼容入口。账号访问不依赖已登录角色；共享材料与账号金库绑定到正确账号身份。容量、物品种类和固定槽准入必须拒绝非法绑定。不要把 SharedMaterial 全局加入 IsCarriedItemSpace。
3. 共享材料默认放置到账号 SharedMaterial；此容器允许 int.MaxValue 堆叠；移出后目的容器仍遵守原有规则。ItemArrival 的槽位搜索仅限自身固定槽，不掉到 QuickSlot。移除按物品 ID 全局禁止写入普通容器的旧 guard，改为目的容器实际准入；保留其他物品行为。
4. 新增真实 SQLite 迁移回归并观察有效 RED。迁移在 schema Ensure 中有一次性标记与事务；导入所有账号 11 个非零余额到 items+item_slots，0 不建物品，负数/超过 int.MaxValue 拒绝。已有共享目标或遗留角色/金库材料发生冲突时明确失败，不能猜测叠加。故障回滚不留下部分物品、marker 或余额变化，重复 Ensure 不重复入账。
5. 复用规范的 stack core 构造值；必要时把纯数据工厂移入 Data，Persistence 不得引用 Application。旧材料列最终只用于迁移，后续单元删除旧 store/DI；本单元可保持旧接口暂时可编译，但必须标清切换尚未完成，不形成长期双写方案。
6. 验证同账号容器身份/读写共享、不同账号隔离、高数量、默认固定槽、错误物品槽拒绝、原始迁移失败原子/重跑不增量。只运行当前单元相关回归；旧余额写入测试在 T-003 集中迁移，列出需要调整的位置。
7. 追加 Changed/Why/Checks/Remaining，并报告新接口签名、尚未切换的旧路径及测试种子注意事项。

## Prior investigation notes

- Schema.Ensure 原无统一外层事务；需注意 ApplyIndexes 中旧索引删除要在导入前。
- 许多旧测试 Ensure 后直接 UPDATE accounts.cube_*，今后不再代表当前材料；不能用双写兼容来让这些测试通过。
- InventoryEntryAssembly 的 11 个 VirtualResources 可保留为线级投影，但下一单元必须改读容器；不能同时把它们加到 Main.Items。
- 最终不保留 IAccountMaterialBag 的独立写入模型。本单元的临时编译接缝必须交接并删除。

## Result — 2026-10-03 / implementation 1

- **Changed:** 增加内部 `InventoryContainerType.SharedMaterial = 240`、`BusinessInventorySpace.SharedMaterial`。业务与持久化使用局部槽 0..10，A21 经 `InventorySlotMappingRuleService` 转成 Main/354..364；输入 list=240 仍拒绝。`LegacyInventoryStorageCoordinates` 将共享业务槽映射到 240/0..10，未将 SharedMaterial 加入 IsCarriedItemSpace。
- **Changed:** `ICharacterBusinessInventoryDomainService.GetStore(ContainerOwner owner, BusinessInventoryStoreKind kind)` 提供显式账号入口；`new ContainerOwner(InventoryOwnerType.Account, accountId.Value)` 的 Item store 提供 SharedMaterial/AccountCargo，无需角色在线或注册。旧角色重载保持；角色访问 SharedMaterial 只使用当前注册，不制造角色所属共享容器。选角绑定将 SharedMaterial 注册到真实账号，客户端 `CharacterInventory.Containers` 不追加内部 240 列表。继续使用 BusinessItemContainer/PersistentItemContainer/IItemDomainStore。
- **Changed:** `IItemPlacementRuleService.PlacementForAsync(int itemId, BusinessInventorySpace destination, CancellationToken)` 返回目的空间的放置与堆叠规则；无目标重载将 11 种材料默认送入各自共享固定槽，共享上限 int.MaxValue。已知材料内容必须为普通 kind=3、Interchangeable。共享 Add 容量不足整笔 NoRoom，不截断发放、不回退快捷栏。普通目的空间继续使用 PVF 范围/上限；空槽、合并与交换反向均在移动写入前检查。底层 Put 从禁止所有普通材料写入改为实际目标准入，拒绝非法共享归属、固定槽/物品/种类/数量，以及普通目的的错误槽或超限；其他物品原 Put 行为保持。
- **Changed:** `StackableItemCore.Create(byte, int, int)` 原样迁到 `Dfo.Server.Data.Inventory`，Persistence 与 Application 复用同一规范核心。邮件实现只有一处全限定名称改动；一个 Infrastructure 测试补 Data using，两个现有容器测试将 target-typed new 显式写成 CharacterId 以消除新增 overload 歧义。
- **Changed:** `SharedMaterialMigration` 在 Schema 完整建表/修列/删除旧账号槽索引之后运行，使用独立 SQLite 事务及 `schema_migrations` 的 `account-shared-material-v1` 标记；每账号每非零余额生成普通 template 槽和标准 kind=3 core，0 不建行，负数、非整型或超过 int.MaxValue 拒绝。已有 240 目标行或历史材料 custody 明确报位置并拒绝自动合并；故障回滚全部新 items、slots 和 marker。11 个旧列保留原值作为迁移历史输入，marker 阻止再次入账；它们不是新容器事实，也没有新增双写。
- **Why:** 共享上限只约束共享容器，不能随物品 ID 外溢到金库。固定槽 Add 保持整笔成败，避免调用方移除整份掉落后只存入可容纳的部分。迁移遇到旧实体材料/已有目标时缺乏来源证据，选择明确失败而非猜测相加。
- **Checks / RED:** `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter 'FullyQualifiedName~SharedMaterialMigrationTests' --nologo -v normal` 退出 1，最初 7 条全部按缺失迁移行为失败：预期 12 个非零槽实际 0；异常余额/已有 custody 未拒绝；插入故障未触发。补旧索引场景后 GREEN 8/8。`SharedMaterialContainerTests` 初始 5 条在固定槽未映射、默认角色放置、快捷栏回退、全局材料禁写处出现真实 RED；显式账号入口/目的规则 3 条及普通目标准入 2 条也分别观察到行为 RED 再实施。
- **Checks / final:** `dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~SharedMaterialContainerTests|FullyQualifiedName~InventorySlotMappingRuleServiceTests|FullyQualifiedName~SelectCharacterEntryLifecycleServiceTests|FullyQualifiedName~WarehouseStorageTests|FullyQualifiedName~ContainerIdTests|FullyQualifiedName~ContainerAdditionTests|FullyQualifiedName~BagPlacementVerbTests|FullyQualifiedName~PersistentContainerBoundaryTests|FullyQualifiedName~MoveItemSpaceHandlerTests' --nologo -v quiet` 退出 0：173 通过、2 个 PVF 跳过。覆盖同账号双角色共享、离线账号隔离及账号金库同槽独立、全部 11 槽映射、int.MaxValue-1 加 2 无写入、错误 owner/物品/种类/槽/数量、普通目的空槽和合并上限、交换反向上限及入场账号绑定。
- **Checks / final:** `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter 'FullyQualifiedName~SharedMaterialMigrationTests|FullyQualifiedName~SqliteItemDomainStoreTests|FullyQualifiedName~ItemCarrierShapeTests' --nologo -v quiet` 退出 0：69 通过。额外对纯工厂命名空间受影响的 `PvfMailItemDefinitionSourceTests` 定向构建/测试退出 0，1 个 PVF 测试跳过；不把跳过当内容验证。`git diff --check` 通过。
- **Remaining / TASK-003:** 尚未切换的旧入口包括 `IAccountMaterialBag`/其 DomainService/Store/DI、ItemGrantDomainService、CharacterContainerAddition、InventoryEntryAssembly、MaterialConsumption、Discard、强化/制作/开盒及协议结果投影。必须在下一单元移除旧列 live reader/writer；当前中间态不可部署。旧 tests 在 Ensure 后 `UPDATE accounts.cube_*` 不会再修改新容器；应改用显式账号容器、kind=3 的共享 Add/Put。典型待迁测试为 AccountMaterialBagTests、AccountMaterialConsumptionTests 及旧材料奖励/故障 trigger 测试，不能增加双写让旧种子继续生效。历史迁移测试应在第一次导入前准备旧库（本单元用真实 Schema 加移除 marker 的隔离临时库模拟）。未提交、部署或触碰线上数据；仅 CurrentImplementation/自动化证据。
