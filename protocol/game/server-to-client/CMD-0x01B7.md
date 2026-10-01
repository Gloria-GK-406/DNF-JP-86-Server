# CMD `0x01B7` — 武器锻造结果

## 证据等级

- `ClientStatic`：A21 原版 SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` 注册 CMD 响应 RVA `0x0119A040`。解包 b1 SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A` 的 VA `0x0159A080` 检查通用成功标志；成功分支 `0x0159A0F7–0x0159A161` 顺序读 `u16 materialSlot,u32 remaining,u8 oldLevel,u8 outcome,u8 newLevel,u8 targetList,u16 targetSlot`；失败分支 VA `0x0159A502` 读取错误码。
- `UpstreamReference`：`ServerS4A21/Server/DfoServer/Network/Builders/Inventory/SeparateUpgradeAckBuilder.cs` 成功体 `01,i16 materialSlot,i32 remaining,u8 old,u8 outcome,u8 new,u8 list,i16 slot` 共 13B；`outcome=0` 成功、`1` 概率失败；业务拒绝 `00,error` 共 2B。A21 静态读取宽度与顺序可容纳该形态，精确字段语义与错误文案仍待实帧。
- `CurrentImplementation`：提交后发送该候选 ACK，并刷新目标装备与材料槽；同序号重复请求不再结算资产。锻造仅改 `GenuineUpgrade`，不改普通强化 `Attribute` 低5位（`ReinforcementLevel`）或独立附魔升级次数 `EnchantUpgradeCount`，不凭 PVF 推断金币费用。

没有 A21 原服响应实帧、GUI 或重登验收；这是 `ClientStatic + UpstreamReference + CurrentImplementation`，不标 `ClientVerified`。参见[人工测试清单](../../../Document/tasks/equipment-development/attempts/spec-005-forging/Human-Test-List.md)。
