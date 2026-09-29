# 自动整理

状态：2026-09-29 WireCaptured（装备默认请求）+ 70Reference/UpstreamReference（响应编排）+ CurrentImplementation；尚未 ClientVerified。

1. C2S CMD0014：list/category/condition 三个字节。
2. 在账号与角色排他事务中验证会话，映射业务分区，整理并持久化；只改变选中分区的内部位置。
3. 提交后定向当前会话，顺序发送 CMD0014 成功 ACK、NOTI000D 对应列表完整快照、NOTI00FB 装备锁列表。完整快照保留虚拟资源。
4. 无有效会话、非法分类/条件时只回失败 ACK；事务异常不产生成功通知。

70 `DisPatcher_SortItem::send` 0x081C9DBA 与 A21 上游 SortItem handler 都采用 ACK→完整列表→锁列表。本服尚未观察新回包的客户端效果。

[协议字段](../game/client-to-server/0x0014.md)；[实现、算法与测试边界](../../Document/自动整理实现与70端证据-20260929.md)。
