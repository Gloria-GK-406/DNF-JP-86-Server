# CMD `0x00FB` `USE_SHARED_EFFECT`

ClientStatic / CurrentImplementation。C2S 固定6B：`u32 itemId + u16 mainSlot`。CMD成功4B：`u8 1 + u16 slot + u8 unknown=0`，本地扣一件；失败2B：`u8 0 + u8 reason`。勿与NOTI00FB装备锁状态混淆。

完整请求、证据和权限见[对应记录](../client-to-server/0x00FB.md)。待本轮A21实机，不自动提升ClientVerified。
