# CMD 0x01A1 ACHIEVEMENT_TRIGGER响应

成功11B：u8=1，i32achievementID，3×u16remaining；失败6B：u8=0，u8error，i32achievementID。失败也必须包含ID，因为客户端在判断分支前读取它。

正常delta请求不会直接改变计数，响应读取权威持久化值。ClientStatic D2FCAE、D2FCC4/CD0/CDC；完整证据与局限见[核对记录](../../../Document/tasks/achievement-title-book/protocol-evidence.md)。CurrentImplementation已编码与测试，实机待验收。
