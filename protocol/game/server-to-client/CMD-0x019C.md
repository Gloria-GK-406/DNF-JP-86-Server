# CMD 0x019C TITLE_BOOK_PUT响应

角色称号簿存取结果；不等同于同type的NOTI。

成功17B：u8=1，i32space，i32slot，i32category，i32index。失败10B：u8=0，u8error，i32space，i32category。客户端收到成功后本地移动，因此异步权威刷新必须在ACK后发送。

ClientStatic读取点与上游参考见[核对记录](../../../Document/tasks/achievement-title-book/protocol-evidence.md)。CurrentImplementation已编码与测试，实机待验收。
