# CMD `0x03F0` `WEDDING_REQUEST`

## 2026-10-16 普通婚礼预约接入

当前补充：action2预约ACK正文12B：01 + i32 status + u32 peerWireId + 02 00 00。金币预检不足返回-7；未支持档和其它失败返回-1，均为原生失败分支。

CurrentImplementation：实现边界、A21证据及双端验收步骤见[本次报告](../../../Document/MarriageReservation-20261016.md)。不是婚礼完成或已婚状态；本节优先于下面的历史阶段说明。

2026-10-16，A21 ClientStatic / Confirmed；CurrentImplementation 已接入 action=1，待双端实机。

客户端 RVA `00894CD0` 检查传入的 CMD `ok` 参数，非零时从**越过结果信封后的流**读取 11B。
因此线上正文是 **12B**，不能把 handler 读取长度直接当正文长度：

```text
u8  ok=1         @0
i32 status       @1
u32 peerWireId   @5
u8  action       @9
u8  opaque=0     @10
u8  opaque=0     @11
```

`ok=1` 使此 handler 收到数据；本协议业务结果由 `status` 决定，0 为成功，非零进入错误映射。
当前 action=1 的失败使用 -1（字符串0x5704，未搜索到玩家）或 -151（0x11C88，对方已有对象）。
未确认的后续 action 不授予成功。

正常向量（peer=71）：`01 00000000 47000000 01 00 00`。
已有对象（peer=72）：`01 69FFFFFF 48000000 01 00 00`。

证据：[定向反汇编和 dstr 文本](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)，
[只读取证脚本](../../../Document/outputs/marriage-proposal-20261016/probe_client.py)。
与 [C2S03F0](../client-to-server/0x03F0.md) 及 [NOTI03CD](NOTI-0x03CD.md) 配合。
