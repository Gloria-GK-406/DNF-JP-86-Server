# CMD `0x01C8` RESET_RANDOM_OPTION

2026-10-09：ClientStatic / High（响应回调）+ 70Reference（信封、发帧顺序）；CurrentImplementation。ClientVerified 待实机。NOTI01C8 DUAL_RAID_PARTY_GROUP_INFO 是另一身份。

A21 身份：DNF.exe SHA-256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；内存 dump `DNFClient-A21-debug/dumps/dnf-dump-20260922-194214-883-23144/DNF-memory-rva.bin`，SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`。

成功 body 为 `01`；失败 body 为 `00 error:u8`。不包含词条类型、属性数值、材料数量或基础属性种子。

客户端回调 RVA `01113240`：先删除等待键13（`012FB9D0`），失败时按原因显示提示；成功直接返回，无额外字段读取。注册位置 `01115D55`，标准 CMD 信封由公共转发器处理。70 `DispatcherResetRandomOption::process` 尾部依次 SendUpdateItem(目标)、SendUpdateItem(材料)、SendCmdOkPacket(01C8)。

当前实现顺序：同一事务提交装备和材料 → EventService 投影目标 NOTI000E → 材料 NOTI000E（耗尽为物品ID=-1的标准清槽）→ CMD01C8 `01`。普通调级0051的9字节ACK不适用于本协议。失败仅回CMD，不扣材料、不更新装备。

| 原因 | error | 依据 |
|---|---|---|
| 锁定 | D5 | 70Reference；A21当前回调可能走公共提示 |
| 物品不存在或无效 | 11 | 70Reference + A21回调有该分支 |
| 请求/词条序号或装备内容不合法 | 13 | 同上 |
| 调级箱缺失或不支持 | 15 | 同上 |
| 词条不可调整 | 17 | 同上 |

请求及规则证据见 [01C8请求](../client-to-server/0x01C8.md)。自动检查不能证明真实客户端显示及错误文案；需验证等待释放、选中词条刷新、箱子数量、其他属性不变、重登持久化。
