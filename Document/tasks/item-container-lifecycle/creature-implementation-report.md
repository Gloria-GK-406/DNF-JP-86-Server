# T003 宠物迁移交接

## 已实现

- `ICreatureDomainStoreSession` 新增 `FindItemAsync(owner, uid)`、`ReadItemAsync(reference)`、`WriteAsync(reference, CreatureState, int? expiration, ct)`，删除同步全状态 `Write(owner, state)`。UID 查询仅用于首次定位；后续读写读取同一存储地址并验证 `SameOccupancyAs`。
- `PetInstance` / `EggInstance` 持有初次取得的 `ItemReference`。Take、换位、移回原槽或事务结束后旧句柄失效；新 Find 可获得当前句柄。孵化后旧 egg 再次 Birth 返回 null，由新模板的非 egg 规则拒绝。
- 宠物属性写入只经过既有 item session 的 `UpdateAsync`。CreatureFacet 不再缓存第二份完整 CreatureState，也不直接 SQL 覆盖全部宠物列；Evolve/Hatch 同一次 Update 写模板和全部 CreatureDetail，Hatch 的有效期也在同一次更新中设置，其他物品字段保持最新值。
- `CreatureState.FromItem/ToDetail` 是宠物行为投影和 detail 适配；实际 UID 来自 ItemSnapshot.ItemUid，绝不拿 InstanceValue 推断身份。
- 入场兼容 `CreatureSection.Write` 保留同步条件登记。Flush 时验证真实 UID、character/account、delete_flag、OnlyWhenAtMostZero，随后调用同一 itemSession.Update 修改 Field04。工厂 SessionIn 提前获取 ItemLifecycleScope 和 itemSession，Prepare 阶段不再 Enlist。
- 在线缓存改为每 UID 的 `(Value, Baseline, At)`：仅当当前整数 Field04 与 baseline 相符才继承 fractional 和 elapsed。外部整数变化丢弃旧 fraction，并从本次观察时间重新计时；仅 Name/Progress 等变化保留 fraction。Rename 写相同 balance 不推进旧时间锚，避免静默丢掉未结算时间。
- `CreatureTransformationReceipt.Core` 和 `CreatureRenameReceipt.RemainingSourceCore` 改为 ItemSnapshot；保留属性名以减小调用改动。

## 验证

本轮未构建全解、未提交。独立项目直接链接本轮真实 Application/Creatures、Persistence/Creatures 和 CharacterDomainStore 源码，不依赖旧 Application 大型夹具。

- RED：`dotnet test Server/tests/Dfo.Server.ItemLifecycle.Tests --filter FullyQualifiedName~LaterItemUpdate --nologo -v minimal`，日志 `artifacts/item-container-lifecycle/creature-red.log`。真实 SQLite 先宠物 Write 胃口40，再统一 item Update 胃口85，提交后 Expected85 / Actual40。初次失败被 fixture 清理时文件占用遮蔽；将只读事务结束放在断言前后重新运行，获得上述明确行为失败。
- GREEN / 最终：`dotnet test Server/tests/Dfo.Server.ItemLifecycle.Tests --nologo -v minimal`，exit0，38 passed / 0 failed。日志 `artifacts/item-container-lifecycle/creature-tests.log`。
- 新增7项（含Theory两例）：提交写顺序；移动及同UID回原槽失效、事务结束失效；属性更新保留其他最新字段及进化UID；孵化完整字段和重复调用；真实UID与InstanceValue不一致入场复活；条件守卫失败整事务回滚；在线小数保留和外部整数更新重置。
- `dotnet build Server/src/Dfo.Server.Persistence --nologo -v minimal`，exit0，0 warning / 0 error。日志 `artifacts/item-container-lifecycle/creature-persistence-build.log`。

## 父任务待接接口

产品代码本轮允许范围之外仅3处协议边界需更新：

- `Protocol/Game/Creatures/CreatureRuntimeDelivery.cs:96`：transformation.Core 已是 ItemSnapshot，InventoryItemState 边界用 `.ToCore()`。
- `Protocol/Game/Creatures/HatchCreatureHandler.cs:60`：receipt.Core 同上。
- `Protocol/Game/Creatures/RenameCreatureHandler.cs:65` 附近：remaining 同上。

未为大量旧夹具扩展本轮：`Persistence.Tests/Creatures/CreatureDomainStoreTests.cs` 的同步 Write、StoredItem、TransferFrom 须迁移；`Application.Tests/Creatures/TestCreatureCollection.cs` 的 ICreatureDomainStoreSession fake 须实现新引用接口；`CreatureTransformationDomainServiceTests` 内旧存储及容器夹具亦须迁移。旧 Runtime 全套测试依赖未迁移的 Effects / FakeMainInventory，未整体链接；新增独立 SQLite runtime 回归已通过。本轮没有把未跑旧套件或客户端行为写成验证通过。

父任务计划变更 Scope发行钩子可见性、item模板变更和创建合法性；本轮没有使用或改动 Issue/Accept。宠物变形仍使用既有规则源验证目标宠物规则、CreatureId、非egg等业务条件。

共享工作区写入权已归还主代理；从此不再写入或构建，除非再次获委派。
