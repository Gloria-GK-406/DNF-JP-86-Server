# CMD 0x019D TITLE_BOOK_GET响应

角色称号簿存取结果；不等同于同type的NOTI。

成功17B：u8=1，i32space，i32slot，i32category，i32index。失败10B：u8=0，u8error，i32space，i32category。space=0 是取出到 Main，不能用作无操作确认；space=3/17 走原生装备确认 helper，消费 pending 并处理装备/书槽。space=18+category 的上一版候选未清请求，已废弃。

当前先完成真实目标的原生 ACK，再通过 EventService 投影 `0002 subtype1` 穿戴、`0002 subtype0` 外观及五分类 `000E`。称号簿持久化保留原件，客户端投影隐藏当前已穿戴镜像的来源原件。GET 不再切换开关。此顺序为 ClientStatic + CurrentImplementation，尚无官方 wire 组合证明，实机待验收。详见[修复记录](../../../Document/tasks/achievement-title-book/native-title-confirmation-repair-20261008.md)。

ClientStatic读取点与上游参考见[核对记录](../../../Document/tasks/achievement-title-book/protocol-evidence.md)。CurrentImplementation已编码与测试，实机待验收。
