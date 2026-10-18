# CMD 0x001E INCREASE_STATUS

Game / Server → Client / selector01 / type001E，ClientStatic + CurrentImplementation，实机待验。

| 偏移 | 成功字段 | 证据 |
|---:|---|---|
| 0 | u8 success=1 | VA0112DFB0 分支 |
| 1 | i16 Main来源槽 | VA0112E00A；用于VA0112E058的来源查找，并非玩家ID |
| 3 | u8 statType | VA0112E018 |
| 4 | i32 statValue | VA0112E024 |
| 8 | u16 extra0 | VA0112E030，完整语义Unknown |
| 10 | u16 extra1 | VA0112E042，完整语义Unknown |

失败为2B：`u8 success=0, u8 error`。成功路径VA0112E095/9B或来源特例16F34D0处理pending；
失败分支VA0112EF1A同样调用16F3360（end-=4，非空时）。这次经验道具发送statType=FF、
statValue/extra均0，无直接基础属性增量，再通过现有NOTI0025同步经验。错误映射沿用参考服务端，
不能称为全错误码已实机验证。

证据文件：`DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`，
SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，imagebase00400000。
本页地址为VA。静态成功布局、pending释放和服务端测试不等价于原生客户端验收。

关联：[请求与本次实帧](../client-to-server/0x001E.md)。
