# CMD 0x019D TITLE_BOOK_GET响应

角色称号簿存取结果；不等同于同type的NOTI。

成功17B：u8=1，i32space，i32slot，i32category，i32index。失败10B：u8=0，u8error，i32space，i32category。客户端收到成功后本地移动并刷新窗口，因此异步权威刷新必须在ACK后发送。2026-10-08 修复：称号簿通过 NOTI000E 槽位更新保留原实例，不再在该操作后用0166重建整类缓存；装备与背包仍发送000D快照。新窗口效果待实机验收。

ClientStatic读取点与上游参考见[核对记录](../../../Document/tasks/achievement-title-book/protocol-evidence.md)。CurrentImplementation已编码与测试，实机待验收。
