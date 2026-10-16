# CMD `0x03F1` `WEDDING_RESPONSE`

## A21 正文（2026-10-16）

ClientStatic / Confirmed：handler RVA00891FD0在CMD结果信封非零时读12B类型载荷，线上正文 **13B**。

| Body offset | Size | Enc | 含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | ok=1 |
| 1 | 4 | i32 LE | status；0成功，当前业务失败为-3 |
| 5 | 4 | u32 LE | peerWireId |
| 9 | 1 | u8 | action；1求婚，2婚礼预约 |
| 10 | 1 | u8 | 当前实现写0 |
| 11 | 1 | u8 | 当前实现写0 |
| 12 | 1 | u8 | outcome；0接受、1拒绝、2超时取消 |

CurrentImplementation：返回请求的action/outcome及权威发起者WireUserId；业务结果看status，不能仅看ok。对已匹配的有效婚礼邀请，答复重校验失败时还向申请者发送NOTI03CE/status=-3，使申请方结束等待。无有效邀请或会话不匹配不能任意通知第三方。

编码向量（peer=71，非抓包）：婚礼接受 `01 00000000 47000000 02 00 00 00`；普通拒绝 `01 00000000 47000000 02 00 00 01`。当前客户端兼容补丁在婚礼自动超时时也发送普通拒绝，服务端沿用现有outcome1分支，不额外区分自动/手动。

证据：[反汇编与本地化资源](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)、[超时构包补证](../../../Document/MarriageTimeoutProtocolEvidence-20261016.md)。支持客户端EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。

与[C2S03F1](../client-to-server/0x03F1.md)、[NOTI03CE](NOTI-0x03CE.md)配合。双端顺序、持久化映射和验收边界见[流程](../../patterns/flow-marriage-proposal.md)。
