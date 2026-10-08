# 成就 / 称号协议核对（2026-10-08）

对象：A21 `DNF-memory-rva.bin`，目录 `DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384`。客户端基线SHA256见对应协议机器区。本轮没有新抓包或 ClientVerified。

| 协议 | body与语义 | 证据 |
|---|---|---|
| C2S CMD019C/019D |20B：5×i32，space/slot/itemID/category/index |UpstreamReference InventoryHandler.Trade.cs444–453；请求构包未完全跟出 |
| S2C CMD019C/019D |成功17B：u8=1+4×i32 space/slot/category/index；失败10B：u8=0,u8error,i32space,i32category |ClientStatic D1CD64/6D/76/7F、D1D035/03E/047/050；失败D1CF9F/CFA8、D1D320/329 |
| C2S CMD01A1 |10B：i32ID+3×u16 delta |ClientStatic A53DA1…DD1、A53FF7…A5402E |
| S2C CMD01A1 |成功11B：u8=1,i32ID,3×u16剩余；失败6B：u8=0,u8error,i32ID |ClientStatic D2FCAE、D2FCC4/CD0/CDC；失败仍先读取ID |
| NOTI0167 |i32count+n×(i32ID+4×u16)，4+n×12B |ClientStatic D6F59F、D6F5B7/5C0/5C9/5D2/5DE；原DTO已是ushort，不存在原编码i32错位问题 |
| NOTI0168 |4×i32：ID/category/index/itemID；u16 indexRepeat，共18B |ClientStatic D82325/331/33D/349/355 |
| C2S CMD01A8 |u16在线角色ID；FFFF忽略；不需要CMD ACK，以0166答复 |ClientStatic与最新UpstreamReference；限定同频道在线会话 |
| NOTI0166 |11B头：u8info,u16owner,i32category,count；每条22B前缀+未知helper段 |ClientStatic D82098…D8210C；D82126调用虚拟化helper234FBE0。CurrentImplementation采用上游26B/条，追加i32零扩展 |

0167和01A1的P1..P3是剩余量，饱和减到0表示完成；P4含义未知，本实现新行置0、旧行保留。当前只推进可以权威判断的单条件，不清空旧多条件P2/P3伪装完成。服务器忽略客户端delta，只显式核对持久化事实。

0168客户端会在本地创建默认称号并填书槽，随后0166权威分类刷新。存取只由CMD完成客户端本地移动，不对存取发送0168。01A8的最新上游查看路径用InfoType=1且Owner为目标在线ID，五分类均发送；旧机器区InfoType=2/四类说明不代表本轮实现。

0166尾段沿 `234FBE0→532B79D→5344E06→530F7E8→537D7CC→VM→537D822` 进入动态执行，无法普通静态证明消费4B。若非空列表实机异常，最小无patch验证：D82126调用前与D8212B返回后只读比较游标VA03BAAE38及剩余VA03BAAE3C；ReadU32 RVA23C5310对其增减4。不可把上游26B升级为本客户端已确认。

错误码1是当前通用拒绝实现，其具体客户端文案仍待实机验证；长度和分支必须返回完整，避免请求悬挂。
