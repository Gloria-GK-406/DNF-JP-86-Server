# A21 公会地下城组队 Buff

公会地下城按本次入场确认的公会队伍人数选择 Buff。每名队员首次加载地图后收到同一档，换房间和重复加载沿用本局档位。中途离场者清除 Buff，留在图内的队员继续使用入场档位。整队回城及服务端自动返回会向本局离场的会话发清除。下一次进图按当次成员重新选档，单刷没有组队 Buff。普通组队经验仍由原有普通地下城结算处理。

| 入场人数 | Buff ID | A21 PVF 效果 |
| --- | --- | --- |
| 2 | 205 | 移动速度 +20、攻击速度 +15、施放速度 +25、MP 恢复 +70 |
| 3 | 206 | 继承 2 人档，各加 150 物理攻击力、物理防御力、魔法攻击力和魔法防御力 |
| 4 | 207 | 继承 3 人档，火、水、光、暗属性强化各 +80 |

数值沿用 PVF 脚本参数。当前验收 PVF 的 `etc/guild/guilddungeon/0_2way6dungeon.etc` 声明人数到 ID 的映射，`etc/variousbufflist.etc` 声明效果。日服公告也说明公会地下城的 Buff 随组队人数而变。[日服同期公告](https://arad.nexon.co.jp/news/update_view.aspx?no=7398)

## 协议接线

- **CurrentImplementation**。公会地下城领域服务从 PVF 读档位并保存到本局模式。首房间 `FINISH_LOADING (0x0025)` 通过精确会话与房间检查后，按顺序发送 `0x01E8`（Buff ID，附加三个零值）和 `0x01EA`（一条激活中的副本 Buff）。
- **CurrentImplementation**。放弃、退队、整队回城、返回选图以及通关或全队战败自动返回，按已提交的副本离场向原会话发 `0x01E9` 移除对应 ID，再发空 `0x01EA`。已断开的会话结束后重新登录按登录状态构建角色。本局的临时 Buff 无持久化条目。
- **ClientStatic / ReferenceOnly**。A21 的三个通知入口和读取字段见 [0x01E8](../../../protocol/game/server-to-client/NOTI-0x01E8.md)、[0x01E9](../../../protocol/game/server-to-client/NOTI-0x01E9.md)、[0x01EA](../../../protocol/game/server-to-client/NOTI-0x01EA.md)。通知顺序参照上游 `DungeonBuffNotificationSender`。
- **隔离线路**。伤城单刷不下发 Buff，2、3、4 人分别为每个成员下发 205、206、207，重复加载无重复通知。检查了单人放弃、剩余队伍保留 Buff、整队回城和全队战败自动回城。双人普通地下城无该通知。地下城进出图和公会协议的 231 项回归测试通过。

## A21 实机验收

登录地址 `http://127.0.0.1:54980/`。Client-61 为 `1123`，Client-62 为 `GuildPilot`，Client-63 和 Client-64 为 `PacketMate02` 与 `PacketMate03`。四个角色同公会。后两个角色仅在验收库补齐 86 级角色资料。

实机组队需要验收服启用 A21 UDP 探测。首次验收中，服务端接受组队请求后，客户端以 NAT 类型 5 上报了与本机实际 UDP 监听不符的端口。保持数据库和 TCP 地址不变重启，开启 `Networking:UdpDiscoveryEnabled` 后，已用隔离探针确认 UDP 12311 正常响应，UDP 12312 正常发送换端口响应。角色需要重新选角触发探测。

1. 用 `1123` 和 `GuildPilot` 登录 CH10，在公会区域组队后进伤城。加载完成后确认双方的公会地下城组队 Buff，并观察移动、攻击和施放速度。换房间检查效果继续生效。
2. 返回城镇后确认 Buff 消失。分别加一人和两人重进，检查面板增加物理、魔法攻防以及四种属性强化。
3. 对照单人进伤城和多人进普通地下城，两边都应没有公会地下城组队 Buff。该对照已通过隔离线路。

**ClientVerified（2026-11-28）。** 用户确认 2、3、4 人档的组队 Buff 全部符合预期。验收库中 1123 与 GuildPilot 装备了公会勋章，PacketMate02 与 PacketMate03 的勋章槽为空，因此两名新增角色不显示公会勋章光环。
