# CMD `0x03F9` `DELETE_GUEST_COMMENT`

ClientStatic / CurrentImplementation。C2S 固定23B：`opaque[14] + u32 key + u32 commentId + u8 outsideChurch`。CMD成功9B：`u8 1 + i32 0 + u32 commentId`；失败5B：`u8 1 + i32 status`。成功本地删除，不自动03FA。

完整请求、证据和权限见[对应记录](../client-to-server/0x03F9.md)。待本轮A21实机，不自动提升ClientVerified。
