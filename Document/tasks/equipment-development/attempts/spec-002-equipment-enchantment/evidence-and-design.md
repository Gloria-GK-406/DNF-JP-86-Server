> 2026-09-30实机语义更正：此前将 `EnchantUpgradeCount` 当普通强化等级的解释错误。普通强化为 `Attribute & 0x1F`（`ReinforcementLevel`）；`EnchantUpgradeCount` 是独立附魔升级次数。以下早先实现/测试记录保留其当时范围，不证明客户端普通强化等级正确。TASK-004纠正读写和wire/存储oracle；升级珠子效果仍不在当前支持范围，拒绝行为保持。

# 装备附魔：取证与初步设计（T-001）

## 样本与证据等级

| 样本 | SHA-256 | 角色 |
|---|---|---|
| `Research/1995chen-dnf/other/等级补丁/70/df_game_r` | `CF45F4E8514C3BF560DD322C146F8441249F6C28246EC8566DCF9EBC4E1B2EB7` | 70Reference，VA 不等于 A21 |
| `DNFClient-A21-debug/DNF.exe` | `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` | 客户端身份 |
| `DNFClient-A21-debug/DNF.unpacked-b1-debug.exe` | `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A` | ClientStatic 反汇编载体，PE VA=RVA+`0x400000` |
| `DNFClient-A21-debug/Script.pvf` | `F1FD58E6873F0A3EB050265BF912A01F2B2B0782A1BE17578BAF45B4D4BC2D41` | A21 内容数据，不是线协议/运行帧 |

工具：`llvm-nm`、`llvm-objdump`、`Tool/PvfProbe`。尚无本项实帧或 UI 观察，不使用 `WireCaptured`、`ClientVerified` 标签。

## 宝珠附魔 `0x0110` 四源对照

| 决策点 | 70Reference | UpstreamReference | ClientStatic / A21 PVF | 本服边界 |
|---|---|---|---|---|
| 入口与请求 | `Dispatcher_EnchantByBead::read` `0x081d2218` 在 `0x081d2234/2279/22be/2300` 读取 `u8,i16,u8,i16`；70 回应号 `0x0113` | `EnchantByBeadRequest.cs` 读 6B，`InventoryListType.Main=0, Equipment=3, Pet=7` | A21 构包 VA `0x01A76B44–1A76B99` 先发 `0x0110`，依次写 `u8 0, i16 beadSlot, u8 3/7, i16 targetSlot`，VA `0x01A76B00–1A76B36` 对装备分支扫描穿戴槽；机器记录的 6B 是静态下界，无实帧 | 解码 6B 并只把 A21 已证值/分支作为准入线索；不能用 70 `0x0113` 替代 `0x0110`。 |
| 装备与材料资格 | `check_error` `0x081d23b7/23f3` 要求两物存在，`0x081d2434–24a7` 检查目标 ID 白名单；`OnEnchantByBead` `0x0849ed1a` 检租赁、堆叠物、卡片和目标适用性 | `InventoryEquipmentMutationService.TryEnchantByBead` 限珠在 Main、目标 Main/Equipment、未锁，`ItemMetadataResolver.TryValidateEnchantByBeadTarget` 查珠/卡的目标 ID、装备类型、卡等级表 | A21 PVF：宝珠 `2600294` 在 `stackable/professional/bead/bead_Sieghart.stk` 声明 `[stackable type] [enchant waste] 0` 和 `[monster card id] 3600`；卡 `3600` 在 `stackable/monsterCard/mcard_sieghart.stk` 的 `[string data]` 声明 `[weapon]`，`[enchant table]` 仅 `[enchant index] 0`；装备 `101010437` 是 `[equipment type] [weapon] 17` | 从实际角色槽位和 PVF 元数据重算类型/白名单，优先使用带卡片关联的可验证宝珠；非装备/错部位/错材料拒绝。 |
| 成功与覆盖 | `OnEnchantByBead` `0x0849ef21` 扣宝珠 1 件；`0x0849ef8c–efa5` 保存旧附魔 ID 并将目标 `+0x0d` 改为卡 ID；未见金币或成功率 | 上游把目标 `EnchantCardId` 写为卡 ID，**同时**把目标 `EnchantUpgradeCount` 写为宝珠该字段，可能覆盖装备既有强化等级 | 当前本服 `InventoryItemCoreState` 将 `EnchantCardId` 与 `EnchantUpgradeCount` 分为两个字段；普通强化已接受并使用后者。A21 接收/请求本身不发送新的附魔等级字段 | 宝珠的直接效果先限于 `EnchantCardId` 覆盖，不复制上游可能破坏 +N 的写法；保留装备 UID 和所有无关字段。升级宝珠的额外效果如不能安全存储则显式拒绝，不能清掉强化。 |
| 结果与刷新 | 70 成功发 `0x000e` 物品更新后 `0x0113` 成功字节，错误发双字节 `SendCmdErrorPacket(0x113,码)` | `EnchantByBeadAckBuilder` 成功 body `01,u8 targetList,i16 targetSlot`，错误 `00,reason`；handler 在 ACK 前刷新目标及宝珠槽 | A21 `0x0110` 接收 handler RVA `0x00D1B110` / VA `0x0111B110`；成功 VA `0x0111B12E–1B14B` 读 `u8 list,i16 slot`，按 list `3/7/其他` 刷新。错误 VA `0x0111B1C8–1B244` 对 `0x11/0x13/0x17` 有分支，前置通用错误处理可截获 | ACK 和受影响槽通知按提交后投递；字节顺序可由 ClientStatic + 上游联合约束，提示语义与时序仍待实帧。 |
| 宠物目标 | 本轮 70 宝珠业务没有宠物分支证据 | 上游 `InventoryHandler.Equipment.cs:80–85` 对 `TargetListType.Pet=7` 走 `PetCreatureEnchantService` | A21 同一构包 VA `0x01A76B75–1A76B8C` 可以写 target list `7`；成功 handler 有 list `7` 特例 | 装备服务绝不将宠物槽映射为装备。若本子项不复用独立已验宠物附魔领域，返回已证实的错误而不扣珠。 |

### 另一个客户端入口：卡片经附魔师摊位

70 的 `Dispatcher_PutItemForEnchant` `0x0820b09a`、`TryEnchant` `0x0820ae84`、`AgreeEnchant` `0x0820adc6` 进入 `CEnchanter`：配方/卡/目标核验、`get_rand_int(100)` 成功率、成功才覆写附魔 ID，费用/材料与结果通知是独立多人交易流程。A21 客户端另有 `CMD 0x025A USE_ENCHANT_STORE`，其[静态记录](../../../../../protocol/game/client-to-server/0x025A.md)为 13B（ownerUserId、recipeId、mode、目标/卡列表与槽位）。上游 `EnchanterStoreUseService.TryEnchant` 要求已进入在线摊位、摊主职业/配方/熟练度/耐久、跨人金币、卡片与目标，成功率不保证。当前本服没有附魔师摊位运行状态。它不能简化成“持卡自己直接附魔”的 `0x0110`，也不能把 70 的配方交易规则搬进宝珠路径。是否纳入本次附魔验收入口已向用户询问；在得到边界决定前不实施 `0x025A` 的资产变更。

## 事务和实施方案

宝珠操作从稳定选中角色取源与目标容器；源应是可堆叠宝珠，目标是原 UID 的装备（本服 Main 与 WornEquipment 可映射），两者均核对当前所有权、锁与 PVF 卡关联/装备类型。更新原装备 core 的 `EnchantCardId` 并扣源 1 件，使用现有命令事务一次提交；提交前的任何拒绝不触碰两者。成功后仅向请求者发送 A21 可读取的 ACK 与源/目标槽刷新，不发未经证明的广播。相同序列与请求重发不能再次耗珠；会话变动不得回放旧成功。

内容源需拒绝多候选随机卡片却无可控抽样、`[enchant index]` 无可解释卡片、超出持久字段能力的升级宝珠等不安全样本；对可安全样本如 `2600294→3600→weapon` 建真实 PVF 解析测试。70 仅参考准入和覆盖；上游修改强化等级与 A21 现有持久化冲突，故不照搬。装备穿戴槽与主背包槽的客户端刷新结构应分别用现有投影验证。

## Unknown / 停门

1. `0x0110` 尚无实帧；静态构包确认六个写入字节，未见变长写入，但生成的机器记录仍标结构部分可得。成功/错误字段语义高置信但不标实机。
2. A21 对已附魔目标覆盖时的确认 UI、升级宝珠附魔等级存储位置及卡片对目标类型的所有格式尚未完全闭合。T-002 先支持有确定卡关联和等级 0 的真实 PVF 样本，其余拒绝；不能为了覆盖功能改写已接受强化等级。
3. 宠物分支有直接客户端静态证据但本项不授权重新实现宠物系统；T-003 需验证错误返回不误扣资产，或复用独立已存在且可验证的宠物路径。上游 `EnchantByBeadResult.cs` 的 `0x11/0x13/0x17` 与客户端静态错误分支对应，但客户端提示文案及实机行为仍是 Unknown。
4. 附魔师摊位 `0x025A` 是另一个直接客户端入口、且需要跨玩家状态和事务；范围选择待用户答复。当前产品代码尚无其状态基础，T-002/003 不得用伪造店铺绕过先决条件。
