# 核心实现提前审查（2026-09-29 18:49 +08:00）

```yaml
kind: rks-review
verdict: changes_required
```

审查目标：`solo-play-roadmap` 工作区相对基线 `9c630591` 的当时未提交实现快照；仅覆盖已迁移的业务 store、局部容器、移动/穿脱、旧宠物槽归位及统一发放。依据 `TaskContract.md` 的 SC-1～SC-5、`spec.md` 的 RQ-001～RQ-006 与 `boundary-design.md`，按 `reviewing-work/references/code.md` 检查。生产 worker 正在迁移 Opening/Mail/Durability/DailyRefill/PVF 接口，这些待完成项不在本次结论内。未运行构建或测试；这不是完整结果批准。

## 发现

1. **穿戴交换没有检查被换下物品能否返回来源槽。** `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs:532-552` 的 `PutOnAsync` 仅核对进入穿戴槽的物品及穿戴条件，`displaced != null` 时直接 `PutAsync` 到来源背包槽。旧数据确有宠物神器模板 64016 落在 `Main/40` 的样本（`Document/宠物装备入包分区错误-20260922.md`）；若该槽物品被穿到相应神器槽并换下已有神器，后者会被写回不允许宠物神器的 `Main/40`。这违反 SC-4 / RQ-004 的反向准入，并让旧错误落点继续传播。最小修法：在任一写入前，按被换下物品的 placement 调用 `MayOccupySlotAsync(bag.Slot, displaced.ItemId)`，拒绝时两端不变。以旧错放槽位、目标已有神器、反向拒绝为最小回归例。

2. **旧宠物归位后，入场仍从旧槽读取已穿戴宠物。** `Server/src/Dfo.Server.Application/Inventory/LegacyWornItemNormalizer.cs:27-36` 将宠物按 `WornCreature/0` 迁到 Equipment/25；`Server/src/Dfo.Server.Application/Inventory/InventoryEntryServices.cs:342-359` 的 `EquippedCreatureQuery` 仍只查 Equipment/24。`Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs:567` 把这个查询结果用于入场角色状态，因此归位后的已穿戴宠物被报告为 0。最小修法：查询使用 `LegacyInventoryStorageCoordinates.ToStorage(new BusinessSlot(WornCreature, 0))` 或等价的业务槽适配，不再硬编码 24；更新现有 `CharacterEntryInventoryTests` 并以旧 24 宠物归位后选择角色验证。此项只要求实现内部坐标一致，不将 25 提升为 A21 实机事实。

3. **统一 Grant 可创建缺少宠物详情的半成品。** `Server/src/Dfo.Server.Application/Inventory/AccountMaterialBag.cs:148-155` 把任意有效宠物模板交给业务容器；`Server/src/Dfo.Server.Application/Inventory/CharacterBusinessInventoryDomainService.cs:150-164` 的新槽分支只对装扮提供创建事实，宠物走 `PutAsync`。`Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs:629-657` 在没有 `CreatureDetail` 时不填写 creature 字段，也不把新物品 UID 写回 core；`Server/src/Dfo.Server.Persistence/Characters/CharacterEntryStateTables.cs:323-329` 的宠物列表只读取 `creature_field04 IS NOT NULL` 的行。故 `IItemGrantDomainService.GrantAsync(宠物模板, 1)` 成功落 Pet 槽，却不在角色宠物列表出现。最小修法：为统一 Grant 定义宠物创建事实来源并调用 `PutNewAsync`；若调用上下文不能给出必要初始值，就在写入前明确拒绝，避免成功回执对应半成品。隔离库验证槽位、core UID、creature 字段及宠物列表一起出现。

4. **同模板的非堆叠实例无法交换。** `Server/src/Dfo.Server.Application/Inventory/ItemMovementDomainService.cs:49-52` 仅凭 `displaced.ItemId == moving.ItemId` 进入合并分支，`StackLimit <= 1` 随即拒绝。两件相同模板、但 core identity 和属性不同的装扮同在 Avatar 列表两个槽位时，合法双槽交换必定返回 `MergeExceedsStackLimit`，与 AC-004 的交换及实例保留相悖。最小修法：只有两端都是可堆叠物才合并；非堆叠实例走已有双向准入交换分支。以同模板不同 UID/属性的装扮双槽交换验证。

## 范围说明

以上为当时可由源码和既有数据样本证明的局部缺陷；没有对完整差异、最终测试或 A21 宠物 wire 槽位作批准。`slot-evidence.md` 已将 Equipment/25、26..28 记为上游候选，缺少实机帧本身不构成本轮实现阻塞。

## 四项修复复审（2026-09-29 18:56 +08:00）

```yaml
kind: rks-review
verdict: approved
```

此 verdict **仅批准上面四项缺陷在当前未提交快照中的修复**；不是完整差异或任务结果的批准。按 `reviewing-work/references/code.md` 复读实际代码和直接回归测试，未运行构建或测试。生产 worker 报告的通过数量仅作为报告，复审者未独立复现。

1. 穿戴交换：`MoveItemSpaceSlice.cs:542-555` 在任何写入前校验被换下物品对来源槽的准入，拒绝时不会触碰两端。`RefusedEquipIsRecordedWithItsTemplateTests.cs:190-202` 使用真实 PVF 规则构造反向不准入，并断言拒绝及两端原样。原发现的写回漏洞已闭合。
2. 宠物归位后的入场读取：`InventoryEntryServices.cs:344-359` 通过业务槽映射辨认 `WornCreature/0`；`CharacterEntryInventoryTests.cs:34-54` 核对新位置 Equipment/25，`CreatureGoesBackOnTests.cs:123-138` 核对旧 24 归位后 UID 不变。原来的固定读 24 已移除。
3. 统一发放宠物：`CharacterBusinessInventoryDomainService.cs:159-178` 从宠物定义取初始等级，使用带 `Creature` 创建事实的 `PutNewAsync`，没有定义则写前拒绝；持久化 `SqliteItemDomainStore.cs:629-657` 据此填写 creature 字段和世界 UID。`CreatureKindAgreesWithTheArchiveTests.cs:55-123` 经业务容器、真实 PVF 和 SQLite 核对 kind、落点与 `creature_field04=100`。统一 Grant 调用该业务容器，原半成品路径已闭合。
4. 同模板非堆叠交换：`ItemMovementDomainService.cs:49-71` 只把两端都可堆叠且模板相同的物品合并，其余走双向准入交换。`BusinessEquipmentSwapTests.cs:13-45` 用同模板、不同 core value/耐久及 SQLite UID 核对交换后的身份和属性。原来的误拒已闭合。

后续完整审查仍需基于稳定的最终差异进行；本轮没有审查 Mail、Opening 等尚在修改的路径。
