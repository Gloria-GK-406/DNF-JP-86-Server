# TASK-001 — 事务内装备实例
## Task Unit Contract
- Revision: 1
- Outcome and references: 真正按拥有者+UID操作的装备实例及同事务最新状态；T-001、RQ-001/002、AC-001。
- Owned boundary: 新装备实例/集合契约、Application实例实现及DI注册、新实例测试；如暂存身份读取确实阻断可最小修正item store并补真实行为测试。旧修理/耐久/分解调用保持编译，迁移由TASK-002负责。
- Required checks: 新实例行为测试（最新状态/换槽/同模板不同UID/越权/回滚/提交后失效/销毁）及现有repair/durability/disjoint回归；构建。
## Detailed Plan
1. 读取当前ICharacterBusinessInventoryDomainService、IBusinessItemContainer、IItemDomainStoreSession和事务参与者及测试fixture，明确暂存写入后Identity可见性；不得用slot代替身份或猜未知UID。
2. 新IEquipmentDomainService.Of(owner)集合，FindAsync(uid)与FindAtAsync(slot)取得实例，ReadAsync返回包含UID、当前slot及core的只读事实。实例持有事务寿命且只操作本owner库存/可修理仓库，不缓存业务状态。
3. 提供SetReinforcementLevelAsync、SetEnchantmentAsync、SetForgingLevelAsync、SetDimensionalAttributeAsync（type/value原子组）、SetMagicSealOptionsAsync（清暂态与当前实现一致）、SetDurabilityAsync、DecreaseDurabilityAsync、DestroyAsync；字段界限来自存储类型，禁止玩法公式进入实例。
4. 为新可观察身份/事务能力添加真实存储测试，记录先前失败证据；现有结构迁移仅复用回归。保留旧临时实例为过渡兼容（若必须），T-002明确移除，别迁移业务。
5. 跑上述检查，附实际API、文件、证据和剩余工作；不commit、不部署、不编辑其他养成服务或本任务控制器。

## Task Unit Result
- Decision: pass within TASK-001 boundary; full refactor and final acceptance remain open.
- 新增 `IEquipmentDomainService.Of(owner)`、`IEquipmentCollection.FindAsync(uid)/FindAtAsync(slot)`、`IEquipmentInstance`。读取和设置返回 `EquipmentInstanceFacts(Owner, ItemUid, Slot, Core)`，销毁返回销毁前事实；旧快照类保留供下一任务迁移。
- 每次操作重新按当前事务内 owner+UID 定位。容器只读投影真实 Identity；SQLite staged 读取复用现有 Survivors 映射，并补读尚未暂存的 transfer source，避免合法交换中途读取失败。不自动结算、不分配临时 UID、不接受 UID 盲写。
- 新装备在创建行尚未分配 UID 时不能取实例；创建提交后重新取得。连续移动按现有生产 Move 的事务内 Flush 路径验证；未扩展裸 staged 多跳转移语义。实例合法空间沿已有角色库存及修理边界，按空间取得实际 store；AccountCargo 不属于此 SQLite item store，未纳入。
- 销毁共享事务内 UID tombstone，因此另一集合/handle 或原槽同 core 重放不能复活旧实例；tombstone 不缓存 core/slot。它不改变底层 `PutAsync` 同模板写入的历史身份保留语义，不能把该写入当成分配新身份的创建接口。
- 新增 9 项真实 SQLite 行为测试：staged UID、多 handle 合并、更新后连续换槽及同模板替换、越权、销毁、同 core 重放失效、回滚、提交后失效、创建未分配 UID。字段 setter 没有镜像实现测试。

### Test-value gate and RED / GREEN
装备在一次事务内更新后丢 UID 会使后续操作失去目标，换槽与同模板多件可进一步导致操作错误装备；真实 SQLite 读回身份和持久化状态是有效判据。已有服务用例没有保护这种公开实例组合契约，新增行为测试独立于实现类型。

基线 `6e60c4c6`，以下命令在工作树根运行：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter FullyQualifiedName~EquipmentInstanceTests -v quiet --logger 'trx;LogFileName=equipment-instance-red.trx'
```

- RED：exit 1，1 failed；既有 store/container API 写后读取 `Expected: ItemIdentity { Value = 1 }; Actual: null`。测试准备阶段缺少 namespace 的编译错误已修正，不计入行为 RED。
- 同命令改日志名 `equipment-instance-store-green.trx`：exit 0，1 passed，证明最小 store 修复。其余新实例测试属于后续行为验证，不把新增接口编译缺失声称为 RED。

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~EquipmentInstanceTests|FullyQualifiedName~EquipmentRepairTests|FullyQualifiedName~EquipmentDurabilityTests|FullyQualifiedName~EquipmentDisjointTests|FullyQualifiedName~BusinessEquipmentSwapTests' -v quiet --logger 'trx;LogFileName=equipment-instance-regression.trx'
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter 'FullyQualifiedName~EquipmentInstanceTests|FullyQualifiedName~EquipmentRepairTests|FullyQualifiedName~EquipmentDurabilityTests|FullyQualifiedName~EquipmentDisjointTests|FullyQualifiedName~BusinessEquipmentSwapTests' -v quiet --logger 'trx;LogFileName=equipment-instance-pvf-green.trx'
dotnet test Server/tests/Dfo.Server.Persistence.Tests/Dfo.Server.Persistence.Tests.csproj -v quiet --logger 'trx;LogFileName=equipment-instance-persistence.trx'
dotnet build Server/86JP.Server.slnx --no-restore -v quiet
git diff --check
```

- 初次回归 exit 0：52 passed、2 skipped，两个 skip 为真实 wear swap 检查未设置 `PVF_ARCHIVE_PATH`。
- 设置真实 PVF 复跑 exit 0：54 passed、0 skipped，其中 9 项新实例测试。
- Persistence 全量 exit 0：385 passed、0 skipped。
- solution build exit 0：0 warning、0 error；diff 检查通过。
- 额外检查 `ItemIdentityArchitectureTests`：1 failed、2 passed。原“任何 ItemUid 都是行键”的判据已经拒绝基线宠物接口 IEggInstance/IPetInstance，也拒绝新增纯装备世界实例身份；下一任务须保持 pile 禁身份约束并最小更新合法世界实例边界，本任务未修改判据。

### Integration boundary
后续业务迁移先 `FindAtAsync(slot)`、取得 `ReadAsync().Core` 只读事实，再问规则、扣资源，最后调用字段组设置；返回值可直接构建 receipt。旧 `EquipmentInstance.From/Repair/DisjointCandidate` 尚未移除。没有提交、部署或修改控制器状态。
