# Application 测试迁移报告

- 日期：2026-10-03
- 工作树：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`
- 范围：T004，`Server/tests/Dfo.Server.Application.Tests` 全项目；必要的链接夹具 `Persistence.Tests/Support/VolatileItemDomainStore.cs`。
- 状态：接口与夹具迁移完成；完整真实 PVF 运行还存在 7 个生产实现失败，不能声明全绿。
- 本轮没有修改生产源码、golden、独立其他测试项目，没有提交。

## 已完成

- 移除 Application 测试中的旧 `ContainerSlotItem`、`StoredItem`、`PutNewAsync`、`PutTransferredAsync`、`InventoryItemCreationFacts` 调用，迁至 `ItemReference`、`ItemSnapshot` 和实际 Create/Take/Put/Update/Delete 会话操作。
- 纯规则输入使用脱离存储的只读测试投影；会写入的宠物容器和内存容器使用真实 lifecycle custody 规则，不手工伪造可接收 ItemRecord。
- 宠物与装备移动后的旧句柄断言改为失效后重取；保留 UID、字段、回滚、跨事务失效断言。
- `ShopFixture.SeedCore` 使用真实存储会话构造既存状态，允许故意非法/历史准入用例的前置状态；零数量的普通堆叠播种表示该物品不存在。`SharedMaterialContainerTests.DirectMaterialWritesStillRespectTheActualOrdinarySlotAndStackLimit` 与 `AccountMaterialBagTests.A_material_core_with_wrong_kind_cannot_be_written_to_a_character_container` 改为直接调用公开容器 Put，继续验证准入，未靠 raw seed 绕过断言。
- 故障注入触发器迁至当前实际的 `UPDATE OF item_core ON items` / `UPDATE OF count ON item_slots`；SQL 现在可在 Apply 阶段执行，回滚断言覆盖 Apply 和 Commit 整个边界。
- 并发移动夹具补入生产 `TransactionalExecutionGate`，避免原来绕过生产 admission 的直接测试总线制造两个冲突的 volatile 事务。
- 新增 `ContainerAdditionTests.First_reward_in_an_empty_ordinary_slot_creates_and_commits_one_pile`：通过实际 DI 的 IItemGrantDomainService 首次授予普通材料，验证空槽、数量、pile 无 UID、提交后数据库槽位。完整运行中通过。

## 验证

环境：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。

命令：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --logger 'trx;LogFileName=application-lifecycle-run3.trx' --results-directory artifacts/item-container-lifecycle/test-results
```

最终完整运行：**2834 总计，2827 通过，7 失败，0 跳过，1 分 14 秒**。

- 日志：`artifacts/item-container-lifecycle/application-test-migration-run3.log`
- TRX：`artifacts/item-container-lifecycle/test-results/application-lifecycle-run3.trx`
- `git diff --check`（Application 测试及链接 Volatile 夹具）：通过。
- 前两轮：2611/2833 通过与 2808/2833 通过，保留 run1/run2 日志供故障分类追溯；它们不代表最终状态。

## 待主线程修复的生产问题

### 1. 装备 UID 查找扫描了不支持的业务空间（6 个失败）

`EquipmentCollection.FindAsync` 在 `EquipmentInstances.cs:35` 把 `VirtualResource` 传给 `BusinessInventoryOwnership.StoreOf`，抛 `InventorySlotMappingException`。主线程只读确认扫描到底还可能遇到未实现的 GuildMedal/GuardianGem，因此修复时需同时处理未支持空间，不只跳过 VirtualResource。

保持正确断言的失败用例（均在 `Inventory/EquipmentInstanceTests.cs`）：

- `Multiple_handles_merge_changes_on_only_the_selected_uid`
- `Destroyed_uid_invalidates_every_handle_without_affecting_other_equipment`
- `Another_owner_cannot_find_or_modify_the_uid`
- `Rollback_preserves_original_state_and_old_handles_cannot_join_new_transaction`
- `Replacing_a_destroyed_slot_with_identical_core_cannot_revive_old_handles`
- `Moving_invalidates_old_handles_even_when_the_same_uid_returns_to_the_same_slot`

### 2. 耐久已经为零时仍进行无变化数据库写入（1 个失败）

`Inventory/EquipmentDurabilityTests.Wear_stops_at_zero_and_still_acknowledges_an_already_broken_piece`：第一次扣减将耐久降到零；随后安装禁止 item_core 更新的触发器；第二次 Wear 应保持零、无写入且仍 ACK。当前继续走 `SqliteItemDomainStoreSession.UpdateAsync → SaveAsync`，触发 `SQLite Error 19: unexpected item write`。

原有“不再写但仍 ACK”语义未放宽，失败留给生产修复。

## 交接

本报告写入后停止所有编辑和构建，并将独占写入/构建权归还主线程。生产修复后需要重跑上述完整命令；当前没有后台测试进程或未结束构建。
