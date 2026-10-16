# CMD `0x03F1` `WEDDING_RESPONSE`

## 2026-10-16 普通婚礼预约接入

当前补充：action2答复ACK正文13B：01 + i32 status + u32 peerWireId + 02 00 00 + outcome。成功status0，业务失败-3；匹配有效邀请的失败还向申请者发03CE失败以关闭等待窗。

CurrentImplementation：实现边界、A21证据及双端验收步骤见[本次报告](../../../Document/MarriageReservation-20261016.md)。不是婚礼完成或已婚状态；本节优先于下面的历史阶段说明。

2026-10-16，A21 ClientStatic / Confirmed；CurrentImplementation 已接入 action=1，待双端实机。

handler RVA `00891FD0` 在 CMD `ok` 非零时读取 12B 类型载荷；加结果信封后线上正文 **13B**：

```text
u8  ok=1         @0
i32 status       @1
u32 peerWireId   @5
u8  action       @9
u8  opaque=0     @10
u8  opaque=0     @11
u8  outcome      @12
```

action=1：outcome0 进入成功窗口及双方角色表现，1 对应对方拒绝提示，2 对应超时取消。
字符串证据分别为0x11C34（订婚成功）、0x11C8A（拒绝请求）、0x11C37（超时取消）。
业务失败返回 `ok=1, status=-3` 走订婚条件错误提示；不以空帧或省略结果信封解除等待。

成功接受向量（peer=71）：`01 00000000 47000000 01 00 00 00`。
拒绝向量：`01 00000000 47000000 01 00 00 01`。

证据：[反汇编与本地化资源](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)。
与 [C2S03F1](../client-to-server/0x03F1.md) 及 [NOTI03CE](NOTI-0x03CE.md) 配合。
