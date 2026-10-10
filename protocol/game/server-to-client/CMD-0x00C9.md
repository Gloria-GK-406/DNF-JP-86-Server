# CMD 0x00C9 钻孔/镶嵌响应

Game / Server → Client / selector=01 / type=00C9。与同号 NOTI 无关。

证据等级：ClientStatic / High；当前实现已接线，未 ClientVerified。
成功为`01 | u8 材料堆数 | n×(u16 材料槽+i32绝对剩余量)`；同一堆用于多个孔只回一项。
失败为2B：`00 | u8 error`。锁定16、材料不足11、镶嵌颜色不符08、其他不支持/非法04；错误文案及分支完整性待实测。

详细读写地址、静态取证及上游失谐理由见[请求及响应说明](../client-to-server/0x00C9.md)。首次成功经EventService刷新材料和目标NOTI000E；请求重放仅恢复权威库存，避免再次消耗。
