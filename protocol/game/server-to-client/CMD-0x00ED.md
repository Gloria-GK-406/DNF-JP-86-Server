# CMD 0x00ED TELEPORT 拒绝响应

Endpoint Game，Direction Server→Client，Command 0x01，Type 0x00ED。

`ClientStatic / High`：A21 handler RVA0x00D1AF30区分通用成功标志；成功分支另读u16源槽及u32物品ID，失败分支不读取这些字段。本次重建exe身份见[00AC复核](NOTI-0x00AC.md)。具体失败文案仍未实机确认。

`CurrentImplementation`：当前TeleportResponse只用于拒绝，body hex `00 13`（通用失败及本服十进制错误码19）；成功通过库存、冷却和场景NOTI投影，未发送带槽及模板的成功CMD。该选择沿参考流程，不把服务端编码当作原服线级证明。真实使用及失败文案待[第三轮验收](../../../Document/tasks/item-effects-unification/human-test-list-20261001-round3.md)。
