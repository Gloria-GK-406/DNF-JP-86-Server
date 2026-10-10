# CMD 0x00CE 钻孔/镶嵌响应

Game / Server → Client / selector=01 / type=00CE。与同号 NOTI 无关。

证据等级：ClientStatic / High；当前实现已接线，未 ClientVerified。
成功为5B：`01 | u16 目标槽 | u16 材料槽`。客户端会本地扣一材料，ACK必须先于权威材料NOTI000E。
失败为2B：`00 | u8 error`。锁定16、材料不足11、镶嵌颜色不符08、其他不支持/非法04；错误文案及分支完整性待实测。

详细读写地址、静态取证及上游失谐理由见[请求及响应说明](../client-to-server/0x00CE.md)。首次成功经EventService刷新材料和目标NOTI000E；请求重放仅恢复权威库存，避免再次消耗。
