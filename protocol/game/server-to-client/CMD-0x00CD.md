# CMD `0x00CD` — 异次元属性赋予/扭转结果

## 证据等级

- `ClientStatic`：与请求记录相同的 A21 解包文件 SHA-256 `D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A`。结果 handler RVA `0x00D3EAD0`（VA `0x0113EAD0`）在 VA `0x0113EB14` 检查通用成功标志；成功分支 `0x0113EB1E–0x0113EB6A` 顺序读取 `u8 action,u16 materialSlot,u32 remaining,u16 targetSlot,u8 amplifyType,u16 amplifyValue`，action2 另读 `u8`；失败分支读取 reason 字节。当前只接 action0/1 的固定请求，action2 动态尾未闭合而安全拒绝。
- `UpstreamReference`：`ServerS4A21/Server/DfoServer/Network/Builders/Inventory/InvestItemAmplifyOptionAckBuilder.cs` 对 action0/1 编码 `01,action,i16 materialSlot,i32 remaining,i16 targetSlot,u8 type,u16 value`，错误 `00,reason`。本服采用此兼容形态并在提交后刷新两槽；错误码具体文案尚无实机确认。

这是 `ClientStatic + UpstreamReference + CurrentImplementation`，没有 A21 原服响应实帧或 GUI 验证，不能标 `ClientVerified`。参见[人工测试清单](../../../Document/tasks/equipment-development/attempts/spec-004-outworld-amplify/Human-Test-List.md)。

## 2026-10-01 状态拒绝原因修复

- `CurrentImplementation`：规则分别返回 `AlreadyUpgraded`、`AlreadyHasAmplifyOption`、`NoAmplifyOption`，CMD 失败体分别为 `00 12`、`00 14`、`00 15`。不再把已强化或无法扭转都投影为已有异界气息。失败及失败重放不扣材料、不修改装备，也不发送成功刷新。
- `UpstreamReference`：`ServerS4A21/Server/DfoServer/Game/Inventory/InventoryModels.cs` 的 `ErrorAlreadyUpgraded=18`、`ErrorAlreadyHasAmplifyOption=20`、`ErrorNoAmplifyOption=21` 提供语义对照；它不是 70 或 A21 原服响应实帧。
- `ClientStatic`：A21 handler RVA `0x00D3EAD0` 的失败跳表将 `12/14/15` 分别映射至文本 ID `4AB0/4AAF/4AB2`，证明三者是不同提示。用户此前实际看到 `14` 对应“该物品已蕴含有异界气息”；其余中文文案仍待本次部署后实机检查。
- `Automated`：领域/handler 验证拒绝原因与资产不变，协议测试验证两个字节的失败体；自动通过不代表客户端文案已验收。
