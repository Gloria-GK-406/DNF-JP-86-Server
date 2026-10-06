# TASK-001 — 通用容器归属与账号存储隔离

## Task Unit Contract

- Revision: 1
- Outcome and references: T-001；RQ-001/RQ-005，AC-001/AC-005。统一现有容器身份的所有权读取，修正账号多容器的存储冲突和金库查询假设；不启用共享材料写入。
- Owned boundary: `IItemContainer.cs` 容器身份及必要关联模型；Persistence 的物品存储、账号金库读取、Schema 索引；Application 的现有容器所有权消费者；相应测试。本任务文件记录结果。
- Required checks: 真实 SQLite 中同账号不同容器同号槽互不冲突且金库读取仅返回金库；原角色/账号金库读写与身份回归；有效/无效身份及越权行为不放宽。顺序运行相关 Persistence 和 Application 定向测试。
- Dependencies: ready Spec/Plan；干净产品基线 fd14e717，只有本任务工件未提交。

## Detailed Plan

1. 查所有 `ForAccountCargo/TryGetAccountCargoOwner/TryGetCharacterOwner` 生产引用及现有测试；通用化所有者表达/解析，保留既有规范字符串兼容和账号、角色命名空间分离。业务访问资格不得因存储支持扩展而放宽。
2. 将账号金库特定所有权判断改为通用所有者解析；容量等玩法仍可按容器种类选择规则。允许保留兼容辅助方法，但新的核心解析不得以 AccountCargo 为账号所有权定义。
3. 先编写有意义的 SQLite 回归：在同账号不同 list_type 同号槽建立数据，金库查询只返回 list_type=12。观察现有唯一索引/宽查询导致的 RED；用实际数据结果作 oracle。
4. 幂等移除错误账号槽唯一索引或替换为完整容器键约束；原表完整唯一约束仍保证同容器槽位唯一。收紧 `SqliteAccountCargoItemTable` 的范围。与 Schema 现有迁移方式保持一致。
5. 按实际依赖改消费者并运行定向回归；不要在此单元加入新共享材料写入、删除账号余额、修改协议布局或部署运行环境。
6. 在下面追加 Changed/Why/Checks/Remaining，明确留给 TASK-002 的实际接口。不得提交、改其他任务工件或推进父级执行状态。

## Result — 2026-10-03 / implementation 1

- **Changed:** `IItemContainer.cs` 新增 `ContainerOwner(InventoryOwnerType Scope, int Value)`，复用既有所有者枚举；新增 `ContainerId.ForAccount(AccountId, InventoryContainerType)`、`TryGetOwner(out ContainerOwner, out InventoryContainerType)`、`TryGetAccountOwner(out AccountId, out InventoryContainerType)`。旧账号金库辅助方法保留为包装，字符格式兼容。解析统一要求正数 owner、精确 enum 名称和规范数字拼写；旧角色 owner=0 现拒绝。`SqliteItemDomainStore.OwnerOf` 复用通用解析；角色支持集合不变，账号允许同一集合 Main/Avatar/PersonalCargo/Equipment/Pet 加 AccountCargo，其他已定义协议种类仍不自动成为可持久化容器。
- **Changed:** Schema 幂等删除遗漏 list_type 的 `idx_item_slots_account_slot`，继续由表上 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 保证槽位唯一。`SqliteAccountCargoItemTable` 增加 list_type=12 过滤。容量与金库移动使用通用账号解析，但仍显式限定 AccountCargo，保持会话、角色/账号归属、容量和物品准入检查。
- **Why:** 容器所有者与当前操作者分离；存储层种类支持不代表业务访问许可。旧索引与表上完整唯一约束冲突，无需再建重复索引。旧测试中的“其他账号容器属于损坏金库”改为过滤断言；依赖残缺自建 schema/错误索引的重复槽测试及索引形状断言，由生产 Schema 的真实 SQLite 升级、同号槽共存、重复槽拒绝行为测试取代。
- **Checks:** 在产品基线 fd14e717 上先加 3 个真实 SQLite 测试，再运行 `dotnet test Server/tests/Dfo.Server.Persistence.Tests --no-build --filter 'FullyQualifiedName~AccountContainersWithTheSameSlot|FullyQualifiedName~AccountCargoQueryExcludes|FullyQualifiedName~SchemaUpgradeAllows' --nologo -v normal`：退出 1、3 个预期失败，分别为 account:6:Main 被 OwnerOf 拒绝、SQLite Error 19（owner_id/slot_index 唯一冲突）、金库查询返回 2 条而非 1 条。实施后同过滤带构建运行退出 0、3/3 通过。
- **Checks:** 最终 `dotnet test Server/tests/Dfo.Server.Persistence.Tests --filter 'FullyQualifiedName~Inventory|FullyQualifiedName~ItemCarrierShapeTests' --nologo -v quiet` 退出 0，191/191 通过（28 秒）；覆盖原物品、账号金库 UID 跨角色移动、真实旧索引升级与重复 Ensure、隔离读取和未知/不支持容器拒绝。
- **Checks:** 最终 `dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~ContainerIdTests|FullyQualifiedName~WarehouseStorageTests|FullyQualifiedName~OpeningCapacityRegressionTests|FullyQualifiedName~CeraCargoCapacityRefreshTests' --nologo -v quiet` 退出 0，64 通过、2 个 PVF 用例跳过（2 秒）。包含账号/角色身份往返及无效拼写、跨账号拒绝、错误容器种类/owner 绑定拒绝与数量保持、金库容量和升级行为。`git diff --check` 通过；生产代码中 `TryGetAccountCargoOwner` 仅保留兼容方法定义。
- **Remaining / TASK-002 接口:** 新服务端 SharedMaterial 种类可直接经 `ForAccount` 命名及 `TryGetOwner` 解析；需在 store 的显式支持集合增加该种类，并实现材料容量、固定槽映射与迁移。不要把“enum 已定义”当作业务准入。此单元未新增共享材料写入、删除余额或改变 A21 编码；未提交、部署、触碰线上数据或修改父级执行状态。全部结果是 CurrentImplementation/自动化证据，不是 ClientVerified。
