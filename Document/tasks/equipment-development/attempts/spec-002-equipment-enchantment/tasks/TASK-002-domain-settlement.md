> 2026-09-30实机语义更正：此前将 `EnchantUpgradeCount` 当普通强化等级的解释错误。普通强化为 `Attribute & 0x1F`（`ReinforcementLevel`）；`EnchantUpgradeCount` 是独立附魔升级次数。以下早先实现/测试记录保留其当时范围，不证明客户端普通强化等级正确。TASK-004纠正读写和wire/存储oracle；升级珠子效果仍不在当前支持范围，拒绝行为保持。

# TASK-002 — 宝珠内容与装备附魔领域结算

## Task Unit Contract

- Revision: 1
- Outcome and references: 按 T-001 可支持的 A21 PVF 宝珠→卡→装备类型规则，实现原装备附魔字段的原子结算及拒绝/覆盖/提交失败验证；覆盖 Plan T-002、Spec RQ-002/RQ-003 与 AC-002/AC-003。附魔师摊位 `0x025A` 不在此单元伪造。
- Owned boundary: `Server/src/Dfo.Server.Abstractions/Content` 和 `Inventory` 契约、`Dfo.Server.Infrastructure/Content` 真实 PVF 源、`Dfo.Server.Application/Inventory` 领域规则及相应单元/SQLite 测试；必要 DI 注册。暂不写 `0x0110` 协议 handler。
- Required checks: 真实 PVF 样本 `2600294→3600→[weapon]` 与 `101010437` 可解析；错部位/卡关联/权限/列表、锁定及不安全升级宝珠拒绝；成功/覆盖只改原 UID 的 `EnchantCardId` 且材料减一，不改 `EnchantUpgradeCount` 等无关字段；提交失败全回滚、重读一致。
- Dependencies: T-001 已产出宝珠路径证据与停门；附魔师摊位范围另待用户选择，不影响此单元。

## Detailed Plan

1. 先写真实 PVF 内容测试，覆盖确定单卡宝珠、卡 `[string data]` 类型、装备 `[equipment type]`、错误/缺失/多候选/升级等级样本；内容源按 `stackable.lst`/`equipment.lst` 寻址，不把假卡片或多结果随机解释成确定值。
2. 先写领域与真实 SQLite 测试：已穿戴装备及主背包装备的成功、覆盖、错部位、材料不足、目标不存在/锁定、物品 UID 与未修改字段、拒绝资产不变；提交故障和同账户串行/重试在命令阶段进一步覆盖。
3. 加域接口和服务，以选中会话/在线角色核对归属；从目标和宝珠现存实例读取事实，通过内容源确定附魔卡 ID。拒绝不可持久化的升级宝珠；在环境事务中更新原装备核心字段并扣宝珠 1，生成已提交后可投影的结果，不直接发网络包。
4. 运行领域/真实 PVF 定向测试、解决方案构建及受影响库存回归；记录执行结果和遗留的 ACK/通知输入给 T-003。不能将自动测试称为 A21 GUI 已通过。

## Result

- 已建立 `IEquipmentEnchantContentSource` 与真实 A21 PVF 解析源；`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 的样本测试 1/1 通过，覆盖宝珠 `2600294`、卡 `3600`、武器 `101010437` 与错部位/错材料。
- 领域服务只接收当前选中角色的背包宝珠与自有装备，拒绝宠物目标、锁定、过期租赁和升级宝珠；环境事务内扣宝珠一件并只修改原装备的 `EnchantCardId`。真实 SQLite 领域测试现为 9/9 通过，包括穿戴/背包、覆盖、原 UID、强化等级、重登和强制提交失败回滚。PVF 源额外拒绝缺失/重复附魔索引，内容测试现为 2/2。
- T-003 尚需把此 receipt 经命令事务提交后投影为 `0x0110` ACK 与两个槽刷新，加入序列重放保护并扩大协议/回归检查。本单元没有把自动测试称为客户端 GUI 验收。
