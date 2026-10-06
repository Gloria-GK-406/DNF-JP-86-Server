# CMD `0x04E7` `JOUST_BET` 响应

日期：2026-10-06；实现基线 `f9dd4c9a`。Endpoint Game，Direction Server→Client，command族0x01 CMD，与同型请求配对。

## 当前载荷

5B：u8 flag=1 + u32 result。成功result0，当前拒绝result6。扣料与投注记录提交成功后刷新权威资产/资料，最终返回ACK。

固定字段宽度的客户端消费证据见 [请求及响应逆向记录](../client-to-server/0x04E7.md)，属于 ClientStatic；编码与调用点见 `Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`，属于 CurrentImplementation。此页用于显式登记已注册S2C身份，不表示新增WireCaptured或扩大ClientVerified。

最终验收范围见 [0.2.2收口](../../../Document/closeout-0.2.2-20261006.md)。
