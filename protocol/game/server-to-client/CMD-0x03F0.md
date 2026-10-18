# CMD `0x03F0` `WEDDING_REQUEST`

## A21 正文（2026-10-16）

ClientStatic / Confirmed：handler RVA00894CD0在CMD结果信封非零时读11B类型载荷，线上正文 **12B**。不能把handler消费量直接当正文长度。

| Body offset | Size | Enc | 含义 |
|---:|---:|---|---|
| 0 | 1 | u8 | ok=1，使handler继续读取类型载荷 |
| 1 | 4 | i32 LE | status；0成功，非零走业务错误分支 |
| 5 | 4 | u32 LE | peerWireId |
| 9 | 1 | u8 | action；1求婚，2婚礼预约 |
| 10 | 1 | u8 | 当前实现写0 |
| 11 | 1 | u8 | 当前实现写0 |

CurrentImplementation：action1失败使用-1或-151（对象已被占用）；action2金币预检不足为-7，未支持档与其它申请失败为-1。ok=1不代表业务成功，也不能用空帧替代错误结果。

编码向量（peer=71，非抓包）：求婚申请成功 `01 00000000 47000000 01 00 00`；普通婚礼申请成功 `01 00000000 47000000 02 00 00`。

证据：[反汇编与dstr](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)、[只读取证脚本](../../../Document/outputs/marriage-proposal-20261016/probe_client.py)。支持客户端EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；镜像/RVA绑定见证据头部。

与[C2S03F0](../client-to-server/0x03F0.md)、[NOTI03CD](NOTI-0x03CD.md)配合。发送顺序与有限范围实机结果见[婚姻申请/答复流程](../../patterns/flow-marriage-proposal.md)，未将单个ACK单独提升为完整WireCaptured。
