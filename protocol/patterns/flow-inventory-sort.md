# 自动整理

状态：2026-09-29 WireCaptured（装备默认请求）+ 70Reference/UpstreamReference（响应编排）+ CurrentImplementation；自动整理及整理锁用户实机验收通过（ClientVerified，限报告范围）。

1. C2S CMD0014：list/category/condition 三个字节。
2. 在账号与角色排他事务中验证会话，映射业务分区，整理并持久化；只改变选中分区的内部位置。
3. 提交后定向当前会话，顺序发送 CMD0014 成功 ACK、NOTI000D 对应列表完整快照、NOTI00FB 装备锁列表。完整快照保留虚拟资源。
4. 无有效会话、非法分类/条件时只回失败 ACK；事务异常不产生成功通知。

70 `DisPatcher_SortItem::send` 0x081C9DBA 与 A21 上游 SortItem handler 都采用 ACK→完整列表→锁列表。用户已确认本服自动整理实机效果正常。

[协议字段](../game/client-to-server/0x0014.md)；[实现、算法与测试边界](../../Document/自动整理实现与70端证据-20260929.md)。

## 整理锁（2026-09-29）

自动整理本体已获用户实机验收，整理锁入口随后补齐并获用户验收。CMD02CA 的 list/slot 映射到业务槽，在账号/在线用户事务内翻转 SortLockFlag。提交后锁定回复 CMD02CA、解锁回复 CMD02CB；body 都为 `01 list slot(i16)`。不发“获得物品”增量；重登时完整背包列表携带既有标记。另兼容上游显式 CMD02CB 解锁请求（无本轮实帧）。部署 102449aa 后用户反馈锁定、重登保持与解锁整理验收通过；不据此认定 C2S02CB 已被实际发送。
