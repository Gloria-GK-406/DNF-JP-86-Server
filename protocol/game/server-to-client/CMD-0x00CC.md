# CMD `0x00CC` — 异界气息净化结果

## 证据等级

- `ClientStatic`：原版壳 SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` 对应的 A21 解包 `DNF.unpacked-b1-debug.exe` SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`。结果 handler RVA `0x00D274E0`（VA `0x011274E0`）在 VA `0x011274FE` 检查通用成功标志；成功分支 VA `0x01127508–0x01127537` 顺序读取 `u16` 材料槽、`u32` 剩余数量、`u16` 目标槽、`u8` 属性类型、`u16` 属性数值，随后刷新两槽相关 UI；失败分支不消费字段。
- `UpstreamReference`：`ServerS4A21/Server/DfoServer/Network/Builders/Inventory/PurifyItemAckBuilder.cs` 的成功体是 `01, i16 materialSlot, i32 remaining, i16 targetSlot, u8 amplifyType, u16 amplifyValue`；失败体四个零字节。当前本服按该形态编码，并在命令提交后另外刷新权威装备/材料槽。

无 A21 原服实帧、通知顺序或 GUI 结果；上述字段业务名由静态读宽度与上游交叉推断，不等于 `ClientVerified`。人测见[清单](../../../Document/tasks/equipment-development/attempts/spec-004-outworld-amplify/Human-Test-List.md)。
