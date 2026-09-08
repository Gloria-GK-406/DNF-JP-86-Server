# A21 商店购买采样（2026-09-08）

客户端 SHA-256：`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`。
源日志：`Server\src\Dfo.Server.Host\bin\Debug\net10.0\Logs\server-20260908-232038.stdout.log`；本轮从字节偏移 14787 起截取。
本次快照 SHA-256：`ac6bf16527c36e5e8dcf3e40d1074bd4941cc192e1b056e15cb63260dee0aae3`。
仅归档日志中真实记录的 body；不是完整 TCP 帧，没有补造 checksum、header 或 S2C 回应。

## 样本

每个四字节字按 little-endian u32 展示；数量位置的 0 不擅自归一化成 1。

| 源行号 | 连接 | seq | type | 原始四字节字 | body 样本 |
|---:|---|---:|---|---|---|
| 202 | Game:02-00-00-00 | 26 | `0x02CC` | `2` | [0x02CC-20260908-01.hex](../../../game/client-to-server/samples/0x02CC-20260908-01.hex) |
| 206 | Game:02-00-00-00 | 28 | `0x0015` | `490003532, 1, 15, 2` | [0x0015-20260908-01.hex](../../../game/client-to-server/samples/0x0015-20260908-01.hex) |
| 208 | Game:02-00-00-00 | 29 | `0x0015` | `490003534, 1, 15, 2` | [0x0015-20260908-02.hex](../../../game/client-to-server/samples/0x0015-20260908-02.hex) |
| 212 | Game:02-00-00-00 | 39 | `0x02CC` | `302` | [0x02CC-20260908-02.hex](../../../game/client-to-server/samples/0x02CC-20260908-02.hex) |
| 214 | Game:02-00-00-00 | 41 | `0x0015` | `27312, 0, 87, 302` | [0x0015-20260908-03.hex](../../../game/client-to-server/samples/0x0015-20260908-03.hex) |
| 216 | Game:02-00-00-00 | 42 | `0x0015` | `27312, 0, 87, 302` | [0x0015-20260908-04.hex](../../../game/client-to-server/samples/0x0015-20260908-04.hex) |
| 218 | Game:02-00-00-00 | 43 | `0x0015` | `27312, 0, 87, 302` | [0x0015-20260908-05.hex](../../../game/client-to-server/samples/0x0015-20260908-05.hex) |

## 对照来源

- 490003532：远古精灵的秘药（2倍），`stackable/chn_490003532.stk` 的 `[need material] 490003528 1`。
- 490003534：职业转换书，`stackable/chn_490003534.stk` 的 `[need material] 490003528 35`。
- 27312：锋利的印度古剑，`equipment/character/swordman/weapon/hsword/lswd_steelpattisa.equ` 声明 `[price] 18000`。
- 赛丽亚 NPC 2 的 `[item shop] 15`，商店 `(r)equipmentshop7.shp` 包含上述硬币商品。
- 塔娜 `npc/Viitala.npc` 为 NPC 302，`[item shop] 87`；`itemshop/Viitala.shp` 的职业 12 页包含 27312。
- 当前 PVF：`19f5fa85ff73cce94e299e0902269de2f0164934128f15b8a7b99249c881be1a`；以上声明用本仓 PvfProbe 直接读取。

请求尾部 15/2 与 87/302 分别匹配两组商店/NPC：语义 High，等待客户端全局字段追踪复核。
两类支付都走 CMD 0x0015；材料支付所需的币 ID、价格数量没有直接出现在这几份请求中。
实际售价、折扣、混合材料、限购与失败应答不由这些样本单独证明。
0x02CC SHOP_PURCHASE_COUNT 的一个 u32 分别为 2/302，同样匹配 NPC ID；其响应仍待重建。

## 原日志摘录

```text
202: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x02CC seq=26 body(4B)=02000000
206: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x0015 seq=28 body(16B)=4CDC341D010000000F00000002000000
208: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x0015 seq=29 body(16B)=4EDC341D010000000F00000002000000
212: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x02CC seq=39 body(4B)=2E010000
214: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x0015 seq=41 body(16B)=B06A000000000000570000002E010000
216: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x0015 seq=42 body(16B)=B06A000000000000570000002E010000
218: Unhandled Game frame Game:02-00-00-00: selector=0x01 type=0x0015 seq=43 body(16B)=B06A000000000000570000002E010000
```
