# CMD `0x04E8` `JOUST_MATCH_HISTORY` 响应

日期：2026-10-06；实现基线 `f9dd4c9a`。Endpoint Game，Direction Server→Client，command族0x01 CMD，与同型请求配对。

## 当前载荷

3505B：u8 flag=1 + u32 result + 固定500×(u16 RoundNo,u8 WinnerKnightIndex,f32 Odds)。RoundNo0终止；错误也保留3505B并归零记录。

固定字段宽度的客户端消费证据见 [请求及响应逆向记录](../client-to-server/0x04E8.md)，属于 ClientStatic；编码与调用点见 `Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`，属于 CurrentImplementation。此页用于显式登记已注册S2C身份，不表示新增WireCaptured或扩大ClientVerified。

最终验收范围见 [0.2.2收口](../../../Document/closeout-0.2.2-20261006.md)。
