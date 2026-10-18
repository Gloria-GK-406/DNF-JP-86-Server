# CMD `0x03FA` `LOAD_GUEST_COMMENT`

ClientStatic / CurrentImplementation。C2S 固定19B：`opaque[14] + u32 key + u8 outsideChurch`。CMD成功：`u8 1 + i32 0 + u32 ignored=0 + i32 count + rows`。row为 `u32 id + name[30] + u32 sortKey + u32 textByteLength + raw text`（42B头）。sortKey不是作者ID；当前实现创建时间秒数，原生降序排序。失败仅5B状态。

完整请求、证据和权限见[对应记录](../client-to-server/0x03FA.md)。待本轮A21实机，不自动提升ClientVerified。
