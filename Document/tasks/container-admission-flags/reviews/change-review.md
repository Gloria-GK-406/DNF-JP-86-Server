---
kind: rks-review
verdict: approved
---

# 完整结果独立评审

当前裁定为 round 2 的 `approved`；round 1 的发现与证据保留如下。

## Round 1 — changes_required（历史裁定）

2026-10-03。目标工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`，已核实 HEAD 为 `c2d647e2e46395d4df542fa565d742be2c81470a`。目标为该基准之后当前完整工作区 diff、新增 `ItemTraits.cs` / `ItemTraitsTests.cs` 及本任务目录工件；包括评审期间追加的 `InventoryLoadCharacterizationTests.cs` 六处派生属性 golden。

依据：仓库 `AGENTS.md`，本目录 `TaskContract.md`、`spec.md`、`implementation.md`、`routing-decision.md`，以及 reviewing-work 的 `references/code.md`、`references/deliverable.md`。审阅者独立于实现者；未再委派、未修改产品或测试、未运行 dotnet。

## 裁定及关键证据

生产静态审阅未发现已证实的玩法缺陷，但评审结束前收到并核实 Protocol 全量回归 41 项失败，当前目标暂不通过。原先在验证仍进行时写入的 approved 已由本裁定取代。

### R1：同步与准入模型冲突的协议测试夹具并重新验证

`artifacts/container-admission-flags/protocol.trx` 的 41 项失败集中在 `CardRewardTests`（25）、`EitherEndMayBeHoldingItTests`（10）、`TheAvatarBagIsServedTests`（6）。后两者的 SlotBook 将已知时装 413500000（以及第二类测试中的 101500690）构造成 kind 1 / Equipment，兼容容器没有 placement provider，新增写入检查因此按 core 的 Equipment 特性拒绝 Avatar/WornAvatar；卡牌夹具将 kind 2 消耗品落到物理装备槽 12，后续业务写入同样被新准入门槛拒绝。这些输入与测试所声明的真实时装模板/消耗品类别不符；目前没有证据说明生产 PVF 路径存在同样问题。

最小修正：将上述夹具的物品类别与物理位置修正为其预期语义，保留奖励、移动、拒绝及帧字段断言；重跑 Protocol 回归。不得仅删除新准入守卫或放宽生产接受集合来消除失败。修改后复审这些差异及其直接影响。

- **分类和子集语义**：`ItemTraits.cs` 将 kind 1..11 投影为八种粗特性，药品/礼盒不另设位；`Accepts` 明确拒绝 None，现有接受集合不含未知位。CreatureEquipment 等组合的接受集合只表达允许位，不把每一位变成必需条件，符合用户选定的子集语义。具体物理槽、世界类别及模板范围仍由 `ItemSlotRuleService.CanOccupySlot` 与穿戴路径约束。
- **同一接受表进入实际调用链**：槽位规则、仓库大类过滤、到达、商城、NPC 商店、邮件、装备转移及开启物品的快捷栏候选均调用公共接受表；旧快捷栏对未知 kind 的排除式放行被移除。仓库对 kind 1/2/3/10/11 的原有边界保持，期限、交易方式、锁定与双向交换检查未被集合判断替代。
- **模板权威与写入**：`ItemPlacementRuleService` 两个 placement 入口都补齐模板 ID；PVF 源也返回该 ID。`BusinessItemContainer.WritableIndexAsync` 在有模板时用 placement 的 Traits 判断，直接写入伪装成消耗品的装备有拒绝测试。生产 DI 同时注册业务容器服务与 placement 规则。模板缺失/无 provider 的既有 kind 回退已在 implementation 明示；本次没有将只读 Traits 新增为可设置或持久化的独立权限字段，也没有把该兼容路径宣称为完整的模板存在性验证。
- **共享属性和数量所有权**：AccountShared 只由原有 `AccountMaterialCatalog` 模板目录派生，材料移入个人或账号仓库不改变特性。实际操作仍绑定具体容器/所有者；没有新增按该 flag 跨角色扣数的路径。底层 `StoreBackedItemContainer.WriteOneAsync` 对共享目录、kind、数量、位置与账号容器的原有检查保留。`SharedMaterialPvfTests` 对全部十一种材料在两类仓库的拆分、归还及 Traits 一致性提供真实 PVF 检查。
- **已有堆例外**：`WritableIndexAsync` 仅在原项为堆、替换数量为正且严格下降、去掉 Value 后整个 core 相同时免于新增准入检查。它不允许借此更换模板、种类或实例元数据，且随后仍核验当前槽所属业务空间；调用继续进入原底层写入。容量关闭后的取出例外沿用同一条件，扩堆和新写入仍受限制。
- **保行为范围与文档一致性**：未修改数据库模型、UID 结算或协议编码。时装/宠物测试范围的调整与当前 `PvfItemPlacementDefinitionSource.DestinationFor` 已有独立空间一致。Persistence golden 仅增加五行 Equipment 和一行 Avatar 的派生属性，原持久字段未变化。任务工件没有将静态或自动测试结果升级为 ClientVerified。

## 已核对的验证证据与边界

- 直接解析 `artifacts/container-admission-flags/application-final.trx`：total/executed/passed 均为 2833，failed/error/notExecuted 均为 0。
- 审阅全部新增/修改测试，包括类别与子集检查、仓库边界、未知快捷栏 kind、真实 PVF 药品/礼盒及共享材料测试；`git diff --check` 无输出。
- 本裁定是当前目标的完整结果评审，不代替完成验证。Persistence 最终全量及 Protocol/Infrastructure 的最终命令结果由控制器另行核实并记录；本文件不将尚未核对的结果描述为已通过。

## Round 2 — approved

2026-10-03。目标沿用同一工作区和基准，包含 R1 后三个 Protocol 测试文件的夹具修正。本轮仅复审未解决的 R1 及其直接影响；未变的生产代码与完整范围证据沿用 round 1。未运行 dotnet。

R1 已解决。实际 diff 只修改夹具输入和相关说明：`CardRewardTests.Bag.OpenedSlot` 从 12 改为 65，与 `LegacyInventoryStorageCoordinates` 的消耗品物理范围 65..120 一致，原有堆仍在不同的快捷槽 7；两份 SlotBook 为时装模板提供 kind 8 / Avatar，并在 Put 后保持正确世界类别。`TheAvatarBagIsServedTests` 同时覆盖 413500000 和 101500690，第二件时装交换场景没有漏掉。普通装备及未知模板测试输入保持原样。

三个测试文件的实际 diff 未删减或放宽奖励、移动、拒绝、回执及帧字段断言，也没有通过调整生产守卫或接受集合让测试通过。固定夹具位置改变后，帧断言仍核对本次实际落点；测试所验证的行为保持。

直接解析 `artifacts/container-admission-flags/protocol-final2.trx`：total/executed/passed 均为 1246，failed/error/notExecuted 均为 0；按原 `protocol.trx` 的 41 个失败测试名称逐项匹配，最终 41 个全部为 Passed。较早的 `protocol-final.trx` 不作为完成证据。`git diff --check` 无输出。

当前已无未解决的阻塞发现，完整结果评审通过。本裁定不取代控制器的最终完成验证，尤其不替尚在执行的 Infrastructure 回归宣称通过。
