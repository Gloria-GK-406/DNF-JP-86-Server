# T-001 存储实现交接

- 工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。
- 已接受基线：`32039260ded8d251c5052dfcec597213c561668a`；本单元修改未提交。
- 范围：完整物品值、会话能力、SQLite 生命周期、事务提交校验、易失测试后端、独立真实 SQLite 行为测试。没有修改应用容器、CreatureFacet、在线数据或旧方案补丁。

## 最终接口

`IItemDomainStoreSession` 不再继承泛型裸写 store：

```csharp
ValueTask<ItemReference?> GetAsync(ContainerId container, int slot, CancellationToken ct);
ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(ContainerId container, CancellationToken ct);
ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct);
ValueTask<ItemReference> PutAsync(ContainerId container, int slot, ItemRecord record, CancellationToken ct);
ValueTask<ItemReference> UpdateAsync(ItemReference reference,
    Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct);
ValueTask<ItemRecord> CreateAsync(ItemSnapshot snapshot, CancellationToken ct);
ValueTask DeleteItemAsync(ItemRecord record, CancellationToken ct);
ValueTask ApplyDecidedWritesAsync(CancellationToken ct);
```

`ItemReference`：`ContainerId`、`StorageSlotIndex`、`Generation`、可空 `BusinessSlot`；`SlotIndex` 返回业务局部 Index，否则真实存储槽。`WithBusinessSlot(BusinessSlot)` 共享原会话及占用代次。会话 token 不公开。所有业务值通过 `Snapshot` 的深复制返回；Reference 和 Record 均提供 `ItemId/Count/Type/IsPile/ItemUid` 只读便利属性，没有 Core 镜像。

`ItemSnapshot` 为扁平 record：统一 `ItemTraits Type`、`IsPile`、`Count`、`ItemUid`、`ItemId`、全部旧 Core 实例字段及 AvatarDetail/CreatureDetail。`LegacyKind/InstanceValue` 只服务兼容边界；材料 ToCore.Value 来自 Count。提供 `DeepCopy()`、`CanStackWith(other)`、`ToCore()` 和静态 `FromCore(core, bool isPile, ItemIdentity? itemUid = null, avatar = null, creature = null)`。FromCore 显式读取 pile 身份，不由 Traits 推断。CreatureDetail 已拆到独立文件，原 CreationFacts 父包装暂保留供集成删除。

第三方生命周期服务无需猜发行 store：

```csharp
var scope = ItemLifecycleScope.In(currentTransaction);
await scope.DeleteItemAsync(record, ct);
var (taken, remainder) = await scope.SplitAsync(record, count, ct);
```

Split 仅允许 pile 且 `0 < count < Count`；旧 record 被消费，两份新 record 必须再接收或销毁。Scope 的 Issue/Accept/IsForeign 是存储实现协作入口，不应作为普通业务发放 API；业务 Create 仍走校验后的 session.Create。

## 实现事实

- SQLite 在参与者当前未提交事务中立即写入。Take 整件只删 item_slots；Put 空槽保留原行 UID，材料合并明确删除被合并的 backing 行；DeleteItem 明确删除 detached 行；Update 修改同一 UID 全部状态，并同步材料数量。
- Put 的普通拒收和 checked 数量溢出发生在写入及消费之前，因此记录仍可改投其他槽。进入多语句 mutation 后的异常标记会话 faulted，禁止捕获异常后继续提交半个操作。
- 完整 Take 和新占位递增 generation；partial Take、Update、并入已有材料堆不改变原占用。Update 回调作用于最新深复制状态，避免旧引用快照覆盖其他已更新字段。
- 事务级 Scope 登记 record 对象身份，控制接收一次、删除一次及未安置检查。相同数据库各容器共享会话；相同事务 SQLite 与易失后端只允许材料跨 store接收，源材料 backing 行在目标接收时删除。独立物品跨存储后端不支持；两个独立 SQLite 数据库仍受原单个可失败提交参与者约束。
- `ISqliteStoreFacet.ValidateForCommit()` 为默认空实现；SqliteDatabaseParticipant.Prepare 在 HasStagedWork 提前返回之前调用。Item facet 与 scope 均执行未安置检查，中途 ApplyDecidedWrites 不清生命周期登记。
- Commit/Rollback 终结引用及 record 权限；快照本身仍可读取。无事务 Get 所用临时事务由应用层既有 AmbientStoreAccess 管理，此单元未改该文件。
- 易失后端在 Prepare 前持工作副本；Commit 交换已准备状态，Rollback 放弃。与 SQLite 使用相同 scope、record消耗和引用generation规则。

## 验证与 TDD 证据

已阅读并应用 test-driven-development。新增测试保护用户可观察的丢物/复制/错误引用写入：oracle检查真实 item_slots、items行、UID、数量和回滚结果；不固定内部字典或方法调用次数。既有测试不覆盖 detached 提交与同 UID 回槽后引用失效。

首个行为测试是 `UnplacedRecordAfterFlushRejectsCommitAndRestoresSource`。RED 时接口及基础立即 SQL 操作已编译，但尚未实现 detached 提交检查；不是用旧 API 编译错误冒充 RED。

- RED 命令：`dotnet test Server/tests/Dfo.Server.ItemLifecycle.Tests/Dfo.Server.ItemLifecycle.Tests.csproj --nologo -v minimal`。
- RED 退出码：1；三个项目均编译成功；测试失败原因为 `Assert.Throws() Failure: No exception was thrown; Expected: System.InvalidOperationException`，1 failed / 0 passed。失败点是 Take 后 ApplyDecidedWrites 再 Commit，说明未安置物品确实被错误落定。
- 加入 Scope 与 Prepare 校验后，同命令退出码0，1 passed。后续16项是实现后的回归证据，不声称每项均走过TDD RED。
- 最终命令：`dotnet test Server/tests/Dfo.Server.ItemLifecycle.Tests/Dfo.Server.ItemLifecycle.Tests.csproj --nologo -v minimal --logger "trx;LogFileName=store-lifecycle-green.trx" --results-directory artifacts/item-container-lifecycle`。
- 最终结果：17 passed / 0 failed，退出码0。日志及TRX：`artifacts/item-container-lifecycle/store-lifecycle-green.log`、`store-lifecycle-green.trx`。RED原始输出保留在本次工具执行记录，此处是明确摘录，未伪造事后RED日志。
- 测试覆盖：Take保留独立行、明确删除行、重复接收/删除、伪造record与跨事务引用、同UID回旧槽引用失效、拒收不消费、交换回滚、材料partialTake/Split/merge/delete数量、属性不兼容及溢出拒收、最新值Update、业务槽视图、Avatar数组隔离、Creature完整状态、账号容器转移、SQLite↔volatile提交/回滚、Prepare失败全体回滚、新建UID回滚、全部82字节兼容往返。

## 父代理集成点与限制

1. 容器、新建工厂、普通业务及旧测试仍调用 Read/Write/Clear/StoredItem，必须按新接口迁移。独立测试项目链接实际 SqliteItemDomainStore、参与者、DomainTransaction和易失实现，仅引用Data/Abstractions项目，因此无需把整套消费者编译破裂当作本单元RED。它未加入solution，后续可加入或将测试并入现有Persistence.Tests。
2. Create/Put的PVF分类、容量、锁定、期限、实际账号授权、堆叠上限由应用工厂/容器保留；存储仅处理真实slot.kind、完整状态、数量及生命周期，不伪装为客户端验证。
3. CreatureFacet旧实现仍是独立延迟写队列，必须按计划变为Item会话适配器；不得保留它与完整Snapshot的两个写事实源。ExistingIn(owner) 保留给当前中途Flush调用；SqliteItemDomainStoreSession.In内部现在需要scope，旧Creature文件本身未修改。
4. 邮件持有尚未接入。Scope不提供任意ImportUid/Export；需要集成时增加有实际持有证明的存储适配，而不能用普通item_slots缺失作为合法邮件所有权证明。
5. 易失后端属于测试支撑；没有新增跨库协议或独立物品跨后端迁移。
6. 整个解决方案、完整Application/Persistence/Protocol及真实PVF回归尚未执行，须等消费者集成完成后由父代理负责。此报告仅确认T-001独立核心套件通过，不表示总任务完成。

写入权已交还父代理。
