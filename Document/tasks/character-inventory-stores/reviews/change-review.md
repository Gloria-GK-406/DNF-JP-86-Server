# 角色物品业务 store 完整变更独立审查

```yaml
kind: rks-review
verdict: approved
```

审查目标是 2026-09-29 产品冻结后的本地工作树，相对基线 `9c630591` 的完整 tracked diff，以及六个未跟踪生产源码/测试文件和 `Document/tasks/character-inventory-stores/` 新文档。按 `TaskContract.md`、`spec.md` 的 RQ-001～006/AC-001～005、`plan.md`、`boundary-design.md`，使用 `reviewing-work` 的 Code 与 Deliverable 准则审查。结论只批准本轮代码及文档审查门：未发现尚未解决的阻断缺陷；不替代主控的任务完成核验或客户端实机验收。审查者未运行构建和测试。

## 要求覆盖与代码证据

| 要求 | 冻结代码及直接回归 |
|---|---|
| RQ-001/AC-001：业务归属、局部索引 | `ICharacterBusinessInventoryDomainService.cs` 将 Item、Avatar、Creature 分 store，Medal 仅预留；`CharacterBusinessInventoryDomainService.cs` 在局部读写时由 `LegacyInventoryStorageCoordinates` 转到旧 SQLite list/index。同一物理 Main 中的不同业务空间不会把各自的 index 0 混淆；`ContainerAdditionTests.Two_business_containers_can_both_hold_local_slot_zero` 验证。 |
| RQ-002/AC-002：wire、业务、存储边界 | `InventorySlotMappingRuleService.cs` 与 `LegacyInventoryStorageCoordinates.cs` 分别维护候选 wire 与旧库映射；`MoveItemSpaceSlice.cs` 入站转 `BusinessSlot`，通知/回执及新增 `ItemGrantWireProjection.cs` 在协议边界投影。`InventorySlotMappingRuleServiceTests` 覆盖往返和未知槽拒绝。`SelectCharacterEntryInventoryDomainService.cs` 的旧快照组装是明确投影边界。 |
| RQ-003/AC-003：统一模板归类与发放 | `PvfEntryLifecycleContentSource.cs`、`PvfItemPlacementDefinitionSource.cs` 把宠物、神器、宠物消耗品、装扮解析为各自的业务目标；`ItemSlotRuleService.cs` 对目标类型和范围准入。`ItemGrantDomainService`、`CharacterContainerAddition`、NPC/CeraShop、邮件、掉落/奖励及分解使用该目标；`ContainerAdditionTests`、`CreatureKindAgreesWithTheArchiveTests`、`CeraShopPurchaseTests`、商店及邮件测试覆盖来源一致性、首格和离线发放。 |
| RQ-004/AC-004：移动、交换、实例和回滚 | `ItemMovementDomainService.cs` 与 `MoveItemSpaceSlice.cs` 在双端写入前检查目标和被换回物的准入。完整实例通过 `PutTransferredAsync` 携带源容器及源槽；`SqliteItemDomainStore.cs:184-192,344-400` 在同一事务按原位置解析 UID、拒绝重复或失效来源，并在后续同槽普通 Put 时保留已声明来源。`BusinessEquipmentSwapTests.cs:15-125` 覆盖完全相同 core 的双 UID 交换、穿脱交换、连续移动及移动后耐久更新；Avatar/Creature 事实和 UID、非法目标无写入也有回归。旧 `IItemContainer.MoveToAsync` 生产接口与物理移动实现已移除，`ItemContainerOperations` 仅保留业务容器的消耗操作。 |
| RQ-005、RQ-006/AC-005：证据与兼容 | `LegacyWornItemNormalizer.cs` 在入场事务前按模板/kind 批量归位旧宠物/神器，先检查冲突再写，普通装备物理 12..24 原位不平移；`CreatureGoesBackOnTests` 核对旧宠物 24→25 的 UID 和幂等。`Document/inventory-slot-coordinate-boundaries.md`、`protocol/game/client-to-server/0x0013.md`、本任务的证据表及实机清单区分 CurrentImplementation、UpstreamReference 与 ClientVerified。 |

## 本轮发现及关闭

前轮四项核心问题的原始路径与局部复审见 `core-pre-review.md`。冻结版复核确认：穿戴交换返回槽的反向准入、旧宠物查询、统一 Grant 创建 creature facts、同模板非堆叠物交换均已修复。后续完整审查发现的“同 core 双 UID 原位匹配”已用显式源位置和 SQLite 回归关闭；同一事务中再次移动或原位改变耐久也保留 UID。Opening Main/289..351 请求现由 `OpeningDelivery.cs:112-124` 转成受控 `InvalidRequest`，`PackageOpeningTests.cs:23-35` 验证未写库。邮件发送在临时容器绑定下由 `MailboxSendDomainService.cs:35-43` 拒绝为 `NotOwned`，`MailboxProtocolIntegrationTests.cs:19-37` 验证库存和收件箱不变。没有剩余需改动的具体缺陷。

## 验证判读与边界

主控在真实 PVF 环境串行运行同一解决方案，`verification/controller-final.log` 和八个 `inventory-controller-final.trx` 显示 **4241 通过、0 失败、2 跳过、退出码 0**；两项跳过是与真实 PVF 可用性互斥的负向测试。`git diff --check` 无报错。此前并行 `verification/full-final-v2.log` 的 4240 通过、1 失败为未改动 `NotificationDeliveryTests.The_deadline_a_stalled_recipient_runs_into_is_the_configured_one` 的墙钟断言超时（4730ms）；原测试定向复测 1/1 通过 175ms，主控随后未改断言的串行全量亦通过。本结论保留该验证过程，不把失败日志改写成一次全绿。

宠物穿戴 wire 25、神器 26..28 和 Pet 装备首格 140 目前仍是明确标注的上游候选，未取得 A21 实机直接验证。历史错放 Main 的宠物装备不能仅凭槽位自动判定，实施报告已记录为非自动修复数据边界。仓库新玩法、完整勋章与技能快捷键不在本次交付范围；未运行或修改生产数据库，也未部署客户端。
