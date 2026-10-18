# CMD `0x03F8` `UPDATE_GUEST_COMMENT`

ClientStatic / CurrentImplementation。C2S `u32 key + u32 byteLength + raw text`，无 opaque 前缀，byteLength<=20。CMD ACK `u8 ok=1 + i32 status`；成功后客户端自动03FA刷新。

完整请求、证据和权限见[对应记录](../client-to-server/0x03F8.md)。待本轮A21实机，不自动提升ClientVerified。
