# 安图恩攻坚：攻坚前与编队的上游参照

2026-11-19。范围是攻坚队生命周期里开打以前的部分：招募目录、建团、加入与申请、等待列表、团长操作、改团信息、离团、开团准备与开始、重入、断线、跨频道招募、最近玩家、团内消息，以及上游把团员映射成实际小队的方式。阶段推进、地图与能源、奖励结算不在本文。

本文是参照资料，不是 A21 事实。来源标签：

| 标签 | 含义 |
| --- | --- |
| `UpstreamStatic` | ServerS4A21 源码，提交 `1870c85`。只能当线索，见 [证据效力](../../../../protocol/evidence.md) 第 5 级 |
| `ThirdPartyStatic` | 第三方商业端反编译 v3.3，比上游新 |
| `ClientStatic` | JP86 协议页机器区，即从 A21 客户端静态读出的布局 |
| `Unknown` | 没有证据，或证据互相矛盾 |

路径缩写：

- `U/` = `E:\Project\DNF\ServerS4A21\Server\DfoServer\`
- `T/` = `E:\Project\DNF\Research\third-party-s4a21-v3.3\decompiled\DfoServer\`
- `P/` = `E:\Project\DNF\DNF-JP-86-Server-anton-raid\protocol\game\`

上游自测 `U/SelfTests/A21RaidProtocolSelfTest.cs` 断言逐字节的期望。下文所说的「自测」都指这个文件。它验证的是上游自己的编码器，不是 A21 客户端行为。

---

## 0. 先看结论

1. **上游所有团本入口只在团本频道生效。** 判定条件是监听端口属于频道类型 `0x17`（团本频道号 200、端口 10200）。普通频道发来的团本请求一律回失败或不处理。`UpstreamStatic`：`U/Network/Core/GameNetworkConfig.cs:12,21,26,191-193`、`U/Network/Handlers/RaidHandler.cs:105-112`。JP86 已经认识这个频道类型：`ChannelScript.cs:78` 要求 `[raid]` 行必须是 type 23，团本城镇 19 见 `IChannelSources.cs:37`。
2. **招募期没有实际小队。** `RAID_MANAGER_WORK` op0 只改团员的 `PartyIndex` 标签。实际普通队在两个时机才出现：一是开团准备期，由客户端走普通组队协议自建；二是攻坚中和阶段间歇，由服务端替换队伍。`UpstreamStatic`。JP86 的 [租借设计](../../../AntonRaidDesign.md) 可在建团时先建空容器。A21 实际组队请求见 [准备通知](../../../../protocol/game/server-to-client/NOTI-0x0253.md)。
3. **团长离团或掉线就解散整团，没有自动移交。** 只能用 `RAID_MANAGER_WORK` op1 主动移交。`UpstreamStatic`：`U/Game/Raid/RaidManager.cs:1293-1310`。
4. **上游的断线就是离团。** 第三方商业端改成保留 300 秒，期间显示离线，到期后才离团。`UpstreamStatic` 与 `ThirdPartyStatic`。
5. **JP86 有四个协议页的机器区「可能含义」引用的是旧版上游**，与 `1870c85` 不符：0x024C、0x024E、0x029E，以及 NOTI 0x0007 漏了 type 10。见 §1.4。

---

## 1. opcode 总表

### 1.1 C→S（CMD）

「回包」指同 type 的 CMD 帧。上游的通用 ack 是 1 字节 body，成功为 `01`，失败为 `00`（`U/Network/Handlers/RaidHandler.cs:413-416`）。上游没有细分结果码，只有成功与失败两种。

| type | 名称 | 上游 handler | 上游读法（请求 body） | JP86 机器区（`ClientStatic`） | 一致性 |
| --- | --- | --- | --- | --- | --- |
| 0x0298 | RAID_MESSAGE | **未注册** | — | 部分可得：u8 + dstring + u8；客户端无回包 handler | 上游团内聊天走普通 `SEND_MESSAGE` 0x0011 的 mode 52/53（`U/Network/Handlers/ChatHandler.cs:98,135-141,232-235`），不用 0x0298。A21 何时发 0x0298 是 `Unknown` |
| 0x0299 | CREATE_RAID | `RaidHandler.HandleCreateRaid` `U/Network/Handlers/RaidHandler.cs:114-141` | `TryReadTitle`（`:457-472`）：body[0] 跳过，[1..5) i32 标题长度，[5..] 标题字节。空标题替换为 `"Raid: {cid}"`（`:474-477`） | 部分可得：u8 + dstring；有回包 handler `0x00D13AC0` | 布局一致，前提是 dstring 为 i32 长度加字节，长度单位待样本确认。body[0] 的含义：上游忽略，客户端未标注，`Unknown` |
| 0x029A | LEAVE_RAID | `HandleLeaveRaid` `RaidHandler.cs:150-178` | **不读 body** | 闭合：2B，u16；有回包 handler `0x00D14150` | 不一致。第三方按 u16 目标 uid 读取，目标不是自己时就是团长踢人（`T/DfoServer.Network.Handlers/RaidHandler.cs:2299-2357`），与客户端宽度吻合 |
| 0x029B | START_RAID | `HandleStartRaid` `U/Network/Handlers/RaidHandler.Progression.cs:16-52` | 要求 body 为空；State 5 时转第二阶段 | 闭合：0B；有回包 handler `0x00D13D90` | 一致 |
| 0x029C | SET_RAID_WAITING | `HandleSetRaidWaiting` `U/Network/Handlers/RaidHandler.Recruitment.cs:18-29` | **不读 body** | 闭合：2B，u8 + u8；有回包 handler `0x00D143B0` | 宽度已知，含义 `Unknown`。上游与第三方都忽略这 2 字节 |
| 0x029D | REJOIN_RAID | `HandleRejoinRaid` `Recruitment.cs:255-268` | **不读 body** | 闭合：4B，u32；有回包 handler `0x00D13F90` | 不一致：上游**不回 CMD 帧**，客户端却注册了回包 handler。u32 推测是团号，`Unknown` |
| 0x029E | RAID_MANAGER_WORK | `HandleRaidManagerWork` `RaidHandler.cs:265-326`；解析在 `U/Network/Handlers/RaidHandler.Parties.cs:43-61` | 必须 12B：u32 op、u32 目标 uid（1..65535）、u32 参数。op0 的参数是小队序号，取值 ≤10；op1 的参数低字节必须为 0。其余 op 拒绝 | 闭合：12B（u32,u32,u32）；有回包 handler `0x01E31720` | 宽度一致。机器区写「只接受 op0」，已过时，见 §1.4 |
| 0x029F | MODIFY_RAID_INFO | `HandleModifyRaidInfo` `RaidHandler.cs:328-338` | 同 `TryReadTitle` | 部分可得：u8 + dstring；**客户端无回包 handler** | 不一致：上游回 1B ack，客户端不注册该 CMD handler。见 §7 |
| 0x0331 | RAID_OTHER_CHANNEL_USERINFO | **未注册** | — | 闭合：4B，u8 + u16 + u8；无回包 handler | 上游空白 |
| 0x0332 | RAID_OTHER_CHANNEL_REQUEST_JOIN | **未注册** | — | 部分可得：已读出 9B（u8,u16,u32,u16），后续经虚表；有回包 handler `0x00D1F1F0`，与 0x0333 共用 | 上游与第三方都空白 |
| 0x0333 | RAID_OTHER_CHANNEL_RESPONSE_JOIN | **未注册** | — | 部分可得：已读出 13B；有回包 handler，与 0x0332 共用 | 上游空白。页面 H1 写作 `EVENT_LOGIN_AND_GIFT`，与身份表名称不一致，见 §7 |
| 0x0334 | RAID_RECENT_FRIEND_LIST | 注册为 `HandleRaidWaitingListRequest` `Recruitment.cs:31-40` | 不读 body | 结构未知；有回包 handler `0x00B9FB20` | 上游回 `u32 0`，是空表占位。第三方回真实的最近玩家表，见 §5 |
| 0x033C | RAID_OTHER_CHANNEL_LIST | `HandleRaidOtherChannelList` `Recruitment.cs:294-310` | body[0]==1 时发等待列表，否则发团目录 | 闭合：1B u8；无回包 handler | 一致。页面 H1 写作 ADVANCE_ALTAR_…，身份表是 RAID_OTHER_CHANNEL_LIST，见 §7 |
| 0x0363 | RAID_REQUEST_RAID_MEMBERS | `HandleRaidRequestMembers` `Recruitment.cs:222-253` | 4B 时重绑会话并补发团状态。12B 时走上游私有扩展 RCP1（u32 团号 + `"RCP1"` + u32 版本 1），见 §1.5 | 闭合：4B u32；无回包 handler | 4B 一致，u32 推测是团号。12B 扩展不是原生协议 |
| 0x0364 | RAID_CHECK_RAID_USER | `HandleRaidJoinRequest` `Recruitment.cs:156-186` | 3B：u8（自测填 200，即频道号）+ u16 目标 uid | 闭合：3B，u8 + u16；有回包 handler `0x00D0DD50` | 宽度一致。不一致处：上游不回 CMD 帧，客户端却有回包 handler。H1 写作 `USER_CHOICE_EVENT`，见 §7 |

另有三条在团本流程里使用的非团本 CMD：

| type | 名称 | 上游在团本里的用途 | 位置 |
| --- | --- | --- | --- |
| 0x000A | REQUEST_PEER | 子类型 10 是攻坚队邀请。body 7B：u16 目标 uid、u8 10、i32 peerInt | `U/Network/Handlers/PartyHandler.cs:1541-1546`、`:132-210` |
| 0x000B | RESPONSE_PEER | 子类型 10 是攻坚队应答。body 7B：u16 请求者、u8 10、u32 结果（0=同意）；其他长度或结果视为拒绝 | `PartyHandler.cs:212-220`、`:1713-1730`、`:222-256` |
| 0x000C / 0x000D / 0x000A,0x000B 子类型 0 | SET_PARTY_INFO、LEAVE_PARTY、普通邀请 | 开团准备时客户端用它们自建小队；攻坚中用于自组或退出小队 | `PartyHandler.cs:827-850`、`:918-963`、`:1816-1842` |
| 0x00D9 | （复用） | body `01 99 02`（u8 1、u16 0x0299）表示关闭建团弹窗，上游只记日志 | `RaidHandler.cs:340-357`；`U/Network/Protocol/GameProtocolHandler.cs:504-510` |

上游的 type 来自 `U/Network/Core/PacketTypesA21.cs:1895-1902,2048-2059,2098-2099`，注册见 `GameProtocolHandler.cs:484-511`。第三方注册相同，见 `T/DfoServer.Network/GameProtocolHandler.cs:777-808`，此外把 CMD 0x0003 接到 `HandleGameExit`（`:1385`）。

### 1.2 CMD 回包（上游，`UpstreamStatic`）

客户端为它们注册了回包 handler，但 handler 里的布局 JP86 还没有静态读出，因此全部是 `Unknown`。

| type | 成功 | 失败 | 位置 |
| --- | --- | --- | --- |
| 0x0299 | 5B：`01` + u32 团号 | 1B `00` | `U/Network/Builders/Raid/RaidPacketBuilder.cs:88-94` |
| 0x029A | 1B `01`。第三方：自己离团回 2B `01 00`，被踢者收到 2B `01 01` | `00` | `RaidHandler.cs:170`；`T/.../RaidHandler.cs:2326-2343` |
| 0x029B | 1B `01` | `00` | `RaidHandler.Progression.cs:26,33,35` |
| 0x029C | 21B：`01` + 5 个 u32 0 | `00` | `RaidPacketBuilder.cs:308-317` |
| 0x029D | **不回** | 不回 | `Recruitment.cs:255-268` |
| 0x029E | 1B | 1B | `RaidHandler.cs:289,318`；自测断言 `[15]==1`（`A21RaidProtocolSelfTest.cs:3377`） |
| 0x029F | 1B（客户端无此 CMD handler） | 1B | `RaidHandler.cs:332,335` |
| 0x0334 | 4B：u32 0 | — | `Recruitment.cs:35-37` |
| 0x0364 | **不回** | 不回 | `Recruitment.cs:156-186` |

### 1.3 S→C（NOTI）

| type | 名称 | 上游布局（`UpstreamStatic`） | 收件人 | JP86 机器区（`ClientStatic`） | 一致性 |
| --- | --- | --- | --- | --- | --- |
| 0x024C | RAID_STATE | 2B：u8 State + u8 StateArgument（`RaidPacketBuilder.cs:292-306`） | 全团（`RaidHandler.cs:388-392`） | 下界 2B：u8,u8 | 宽度一致。机器区引用的含义已过时，见 §1.4 |
| 0x024D | RAID_MESSAGE | 上游不发 | — | 下界 1B | 上游空白。第三方把系统公告以 0x024D 发出，body 是 `BuildRaidNotice` 格式（`T/.../RaidHandler.cs:5943`），属于攻坚失败流程 |
| 0x024E | RAID_MEMBER_STATE | 3B：u16 uid + u8 状态（`RaidPacketBuilder.cs:488-494`） | 全团 | 下界 3B | 宽度一致。状态值在攻坚中使用，不属本文；机器区值域已过时，见 §1.4 |
| 0x024F | RAID_LIST | u32 条数，然后每条：团对象（见下）+ u8 人数（`RaidPacketBuilder.cs:248-266`） | 团本频道内全部会话 | 下界 `4 + n×25` | 一致。25 字节是团对象去掉两个 dstr（标题、团长名）后的定长部分加 u8 人数，逐字段对得上 |
| 0x0250 | RAID_MODIFY | u32 团号 + u32 op，然后按 op：0 为团对象 + 成员表；1 为删除，无后续；2 为团对象；3 为成员表（`RaidPacketBuilder.cs:109-184,239-246`） | 团本频道内全部会话 | 下界 8B：u32,u32 | 一致 |
| 0x0251 | RAID_WAITING_LIST | 上游有编码器但没有调用：u32 条数，每条 u16 uid + u32 PartyIndex（`RaidPacketBuilder.cs:276-290`） | — | 下界 5B：u16,u16,u8 | **上游不一致**。第三方编码为 u16 条数，每条 u16 uid + u8 ServerIndex（`T/DfoServer.Network.Builders.Raid/RaidWaitingPacketBuilder.cs:BuildLocal`），与机器区吻合 |
| 0x0252 | RAID_WAITING_MODIFY | 上游不发 | — | 下界 4B：u8,u16,u8 | 上游空白 |
| 0x0253 | PREPARE_START_RAID | 0B（`Progression.cs:42`） | 全团 | 结构未知，handler 进入打包区 | 0B 是否就是全部，`Unknown` |
| 0x0327 | RAID_MEMBERS | 上游不发 | — | 下界 5B。页面 H1 写作 `OBJECT_DESTROY_REWARD` | 上游空白 |
| 0x0328 | RAID_DISCONN_INFO | 上游与第三方都不发 | — | 下界 6B：u32 + u16 | 推测与 0x029D 的 u32 对应（团号？），`Unknown` |
| 0x02EA | RAID_OTHER_CHANNEL_WAITING_MODIFY | 上游不发。第三方的删除形态是 4B：u8 1、u8 频道、u16 uid（`RaidWaitingPacketBuilder.cs:BuildRemove`） | 等待列表观察者 | 下界 40B：u8 + u16 + u8 + u8 + u16 + u8 + u8 + 1B + 30B | 机器区像「u8 op + 39B 等待记录」的新增形态。第三方删除形态把频道放在 uid 之前，与机器区 offset1=u16 不符，`Unknown` |
| 0x02EB | RAID_OTHER_CHANNEL_WAITING_LIST | u32 条数，每条 39B：u16 uid、u8 频道、u8 服务器序号、u16 等级、u8 GrowType、u8 职业、u8 0、30B 名字补零（`U/Network/Builders/Raid/RaidWaitingPacketBuilder.cs:8-32`，自测 `:933-936`） | 请求者 | 下界 43B = 4 + 39 | 总长一致。**字段顺序有争议**：第三方把服务器序号写在频道之前（`T/.../RaidWaitingPacketBuilder.cs:Build`）。机器区 offset 6、7 两个 u8 都取自 `[esi+2]`，判断不了。`Unknown` |
| 0x02EC | RAID_OTHER_CHANNEL_LIST | 上游不发 | — | 下界 `4 + n×25`，与 0x024F 同形 | 推测是跨频道版的团目录，`Unknown` |
| 0x02ED | RAID_OTHER_CHANNEL_USERINFO | 上游不发 | — | 下界 52B | 上游空白 |
| 0x02EE | RAID_OTHER_CHANNEL_REQUEST_JOIN | 上游不发 | — | 下界 50B | 上游空白 |
| 0x02EF | RAID_OTHER_CHANNEL_RESPONSE_JOIN | 上游不发 | — | 下界 5B | 上游空白。catalog 里 0x02EF 在退出序列中以 424B 出现（`P/../catalog.md:1023`），那是 CMD 方向的另一条协议，不要混淆 |
| 0x02F0 | RAID_RECENT_FRIEND_LOGIN_INFO | 上游不发。第三方：u8 条数，每条 u8 服务器序号 + dstr 名字 + u16 uid + u8 频道（`T/DfoServer.Network.Builders.Raid/RaidRecentPlayerPacketBuilder.cs:BuildPresence`） | 请求者 | 下界 5B：u8,u8,u16,u8 | **第三方一致**：机器区 u8 条数、u8 服务器序号、（dstr 未计）、u16 uid、u8 频道 |

**团对象**（`WriteRaidObject`，`RaidPacketBuilder.cs:211-228`）：u32 团号、dstr 标题（i32 长度 + 字节，`U/Network/Core/GamePacketWriter.cs:80-85`）、u8 0、u8 State、u8 StateArgument、u32 0、团长成员记录。

**成员记录**（`WriteMember`，`:571-583`）：u16 uid、u8 1、dstr 名字、u8 职业、u8 GrowType、u8 PartyIndex、u8 0、u32 cid、u8 0、u8 0。第三方对离线成员改写两处：uid 写 0，cid 位置写断线开始的 Unix 时间（`T/DfoServer.Network.Builders.Raid/RaidPacketBuilder.cs:521-533`）。

**成员表**：u8 条数，然后是成员记录（`:230-237`）。

团本流程顺带发出的其他 NOTI：

| type | 用途 | 位置 |
| --- | --- | --- |
| 0x0002 USERINFO（subtype 0） | 发成员表、团长变更、团内聊天和申请弹窗之前，先给收件人补发相关玩家的 USERINFO，否则客户端解析不了 uid | `U/Network/Handlers/RaidHandler.Directory.cs:94-118,145-153` |
| 0x0007 REQUEST_PEER（子类型 10） | 攻坚队邀请或申请弹窗。body 13B：u16 发起者、u8 10、i32 peerInt、u16×3 0 | `RaidPacketBuilder.cs:76-86`；自测 `:3025` 断言整帧 28B |
| 0x01F7 SERVER_NOTICE_MESSAGE | 入团、拒绝、过期、开团失败等中文提示。body：u8 mode + dstr（GBK 编码，即 cp936） | `U/Network/Builders/ServerNoticeMessageBuilder.cs:7-14` |
| 0x0255 RAID_SET_TIMER、0x0256 RAID_REMAIN_TIME | 开团准备倒计时（默认 3 秒） | `Progression.cs:40-41`；`U/Game/Raid/AntonRaidRewardProvider.cs:15,116-120` |
| 0x0257 RAID_ENTRY_COST_INFO | 门票就绪表：u32 条数，每条 u16 uid + u8 就绪 + u16 持有数 | `RaidPacketBuilder.cs:496-515`；`U/Network/Handlers/RaidHandler.Admission.cs:75-99,153-161` |
| 0x04BD RAID_USER_FINISH_LOAD | 团本城镇（town 19）载入完成，先于团目录刷新 | `U/Network/Handlers/TownHandler.cs:377-389,1737-1749` |
| 0x0009 PARTY_INFO、0x0099、0x000B PARTY_IP_INFO | 攻坚中由服务端替换队伍后发出：旧队成员收 type 3 清屏，新队收 type 0 全量、实时信息与 P2P 端点 | `PartyHandler.cs:105-117`；`:2279-2420` |
| 0x000C MESSAGE（mode 52/53） | 团内聊天与团长指挥。53 只允许团长发 | `ChatHandler.cs:232-262` |

### 1.4 JP86 机器区「可能含义」的过时引用

meanings 表引用的上游行号和语义来自更早的上游版本，与 `1870c85` 不符。本文只记录，不改 meanings 表。

| 页 | 机器区写法 | `1870c85` 实际 |
| --- | --- | --- |
| `P/client-to-server/0x029E.md` offset 0 | 「上游只接受 op==0，其余回失败」，引用 `RaidHandler.cs:236` | op0 分配小队，op1 移交团长（`RaidHandler.Parties.cs:43-61`、`RaidHandler.cs:277-300`） |
| `P/server-to-client/NOTI-0x024C.md` offset 0 | 「`WriteUInt32(state)`；超时失败终局写 3」 | state 写成 u8（`RaidPacketBuilder.cs:292-306`）。超时失败的终局是 State 4、StateArgument 1，发的是 RAID_RESULT 0x025A 而不是 RAID_STATE（`RaidHandler.cs:388-392`、`RaidManager.cs:1116-1118`） |
| `P/server-to-client/NOTI-0x024E.md` offset 2 | 「1=存活，2=死亡」 | 死亡写 0，复活写 1（`RaidHandler.Progression.cs:195,207`） |
| `P/server-to-client/NOTI-0x0007.md` offset 2 | 子类型只列 0、1、2 | 还有子类型 10，即攻坚队邀请与申请（`PartyHandler.cs:1541-1546`、`RaidPacketBuilder.cs:76-86`） |

### 1.5 上游私有扩展（不是 A21 原生协议）

`RAID_REQUEST_RAID_MEMBERS` 的 12B 请求（`"RCP1"`，见 `RaidPacketBuilder.cs:59-74`），以及 RAID_MODIFY 成员表后的 `"RMC1"` 列尾（`:186-209`，魔数 826491730），都是上游客户端补丁协商出来的成员列扩展。它们追加每人的阶段通关次数，原生 A21 不会发 RCP1。JP86 不要照抄。自测 `CheckMemberColumn*`（`:2153-2633`）断言的都是这个扩展。

---

## 2. 时序

记号：`→S` 表示客户端发给服务端；`S→A` 表示服务端发给角色 A；「频道」指同一团本频道的全部在线会话，包括正在看目录的非团员；「全团」指当前团员。上游的「频道」广播不区分是否打开了招募界面。

### 2.1 进入团本频道或团本城镇，打开招募目录

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` CMD 0x0004 选角 | — | `GameProtocolHandler.cs:431-440` |
| 2 | 对频道内每个团：USERINFO（该团成员）+ RAID_MODIFY op0。请求者自己所在的团如果已推送过，改为 op2 + op3 + 0x0257 + 计时器 | 请求者 | `Directory.cs:155-170`、`Recruitment.cs:285-292` |
| 3 | RAID_LIST | 请求者 | 同上 |
| 4 | 选角后若仍是某团团员：重绑会话（`HandleRebindResyncAsync`） | 请求者 | `Recruitment.cs:270-283` |
| 5 | 在城镇 19 换区或离开赛丽亚房间：0x04BD → USERINFO → op0 → RAID_LIST，只发给非团员 | 请求者 | `TownHandler.cs:377-389`；`Recruitment.cs:328-336`；自测 `:3854-3911` |
| 6 | `→S` 0x033C body[0]≠1：再发一次 2、3；body[0]==1：发 0x02EB 等待列表 | 请求者 | `Recruitment.cs:294-310` |

### 2.2 建团

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x0299（u8 + 标题 dstr） | — | `RaidHandler.cs:114` |
| 2 | 若已在别的团，先离开旧团（`Create` 内部调用 `LeaveLocked`） | — | `RaidManager.cs:67`。注意：这里不广播旧团的离团，见 §7 |
| 3 | USERINFO + RAID_MODIFY op0 | 建团者 | `RaidHandler.cs:127` |
| 4 | USERINFO + RAID_MODIFY op0 | 频道内其他会话 | `:129-136` |
| 5 | CMD 0x0299 `01` + u32 团号 | 建团者 | `:138`。NOTI 先于 CMD ack 发出 |
| 6 | RAID_LIST | 频道 | `:139`、`Directory.cs:65-69` |
| 7 | 用户关闭建团弹窗：`→S` 0x00D9 `01 99 02`，上游忽略 | — | `RaidHandler.cs:340-357` |

建团不检查等级、门票或是否已在普通队。`UpstreamStatic`。

### 2.3 他人加入：申请后由团长同意

A21 没有「直接加入」。上游的两种形态都要对方确认。

**申请**（非团员向团长申请）：

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x0364：u8 频道、u16 团长 uid | — | `Recruitment.cs:156-186` |
| 2 | 校验：双方都在同一团本端口、城镇就绪、不在副本、`UserState==0`；团长 session 有效；团处于 State 0 且未在准备；人数 <20；申请者不在任何团 | — | `Recruitment.cs:76-102`；`RaidManager.cs:271-287` |
| 3 | 登记待定请求，键为（发起者，目标），60 秒过期。同一入团者只保留最后一条；重复申请不重发弹窗，也不续期 | — | `PartyHandler.cs:144-178`；自测 `:3075` |
| 4 | USERINFO（申请者）+ NOTI 0x0007 子类型 10 | 团长 | `PartyHandler.cs:184-194`；自测 `:3025` |
| 5 | 申请者被加入频道等待表 | — | `Recruitment.cs:181-184` |
| 6 | `→S` 团长 0x000B：u16 申请者、u8 10、u32 0 | — | `PartyHandler.cs:1713-1730` |
| 7 | 拒绝、过期或请求失效：0x01F7 提示 | 申请者 | `PartyHandler.cs:237-255` |
| 8 | 同意后：同账号检查，同账号已在团内则拒绝（`Recruitment.cs:188-220`）。原子入团（`RaidManager.cs:289-312`），要求团实例、团长、`LeadershipVersion`、团长 session 都未变，仍在 State 0、未在准备、入团者不在任何团 | — | |
| 9 | 从等待表移除 | — | `Recruitment.cs:121` |
| 10 | USERINFO + op0，然后 USERINFO + op2 + op3 + 0x0257 + 计时器快照 | 入团者 | `Recruitment.cs:123-124,312-326` |
| 11 | USERINFO + op0，然后 USERINFO + op3 | 频道 | `:125-126`、`RaidHandler.cs:371-386,406-411` |
| 12 | RAID_LIST | 频道 | `:127` |
| 13 | 0x01F7「您已加入攻坚队。」给入团者，「玩家[名]加入攻坚队」给其他团员 | 入团者、全团 | `:128-137` |

**团长邀请**：团长发 `→S` 0x000A 子类型 10（body 7B，目标 uid、10、peerInt）。被邀请者收到 USERINFO + 0x0007 子类型 10，然后 `→S` 0x000B，其后同上 8–13。规则：普通团员不能邀请，也不能接收申请；发起邀请的团长不能替对方同意（自测 `:3138-3156`）。`TryCreateRaidPeerRequest` 根据哪一方是团长判断谁是入团者（`Recruitment.cs:85-102`）。

### 2.4 团长编队（招募期，State 0）

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029E：op0、目标 uid、小队序号 0..10 | — | `RaidHandler.cs:301-316` |
| 2 | `TryAssignParty`：只改 `PartyIndex`；值有变化时更新 `AssignmentVersion`。团长可以改任何人，普通团员只能改自己。**不检查每队人数** | — | `RaidManager.cs:374-402` |
| 3 | CMD 0x029E ack | 请求者 | `RaidHandler.cs:318` |
| 4 | USERINFO + op0（State≠0 时为 op2 + op3） | 频道 | `:321`、`:371-386` |
| 5 | USERINFO + op3 | 频道 | `:322` |
| 6 | 0x027B 怪物状态（只在攻坚中有内容） | 全团 | `:323` |

招募期不产生任何实际队伍，也不发 0x0009。`UpstreamStatic`。

### 2.5 团长编队（攻坚中 State 2 或阶段间歇 State 5）

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029E op0 | — | |
| 2 | `ReassignLiveRaidPartyAsync`：受影响的人包括目标、团长、目标原队成员、目标新队成员。这些人都必须在线、在同一端口，而且**不在副本、不在选图中**；任一不满足就整次失败 | — | `PartyHandler.cs:60-130` |
| 3 | 拿到每个人的角色过渡租约，然后在选图与入场两道队伍闸门下提交（`RunWithDungeonSelectionAndEntryPartyTransitionGatesAsync`） | — | `:81-128` |
| 4 | `TryAssignLiveParty` 做乐观校验：快照的 `InstanceId`、`AssignmentVersion`、State、PhaseIndex、名单与 `PartyIndex` 都必须与当前一致；调用者是团长；目标队已满 4 人则失败 | — | `RaidManager.cs:477-507` |
| 5 | `PartyManager.ReassignLiveRaidMember`：作废全部受影响的实际队伍，按新编组新建替换队伍，分配新 PartyId，队长取组内团序第一人 | — | `U/Game/Party/PartyManager.cs:210-326`、`:728-758` |
| 6 | 对每个旧队：关闭 UDP 中继，给旧成员发 0x0009 type 3 清屏 | 旧队成员 | `PartyHandler.cs:105-112` |
| 7 | 对每个新队：0x0009 type 0、0x0099、0x000B，按 P2P 或中继模式发出 | 新队成员 | `:114-117`、`:2279-2420` |
| 8 | CMD 0x029E ack；USERINFO + op2 + op3 | 请求者；频道 | `RaidHandler.cs:318-322` |

### 2.6 普通组队动作反向改写团编组（State 2 或 5）

这是上游特有的双向同步：

- **团员之间互邀普通队**：`TryCommitPartyJoin` 只允许同一团的团员互邀，且不能在准备中（`RaidManager.cs:112-133`）。入队成功后，`BroadcastPartyInfoWithinGate` 调用 `TrySynchronizeJoinedParty`，把这支实际队伍的成员写入同一个 `PartyIndex`：优先沿用队长原来的序号，被占用时取 1..10 中最小的空号。随后广播 op2 + op3（`PartyHandler.cs:2308-2316`、`RaidManager.cs:426-475`、`RaidHandler.Parties.cs:84-93`）。
- **团员与非团员互邀**：拒绝（`RaidManager.cs:126-129`；自测 `:1530-1547`）。
- **团员 SET_PARTY_INFO**：State 0 时按普通队处理。准备中或 State 2/5 时强制改写为团本小队设置：`UserMax=4`，设置块 `00 00 04 FF FF FF FF 05 00 02 00 00`，其中 [9]=2 是团本模式标志（`PartyManager.cs:54-108`）。
- **团员 LEAVE_PARTY**：先退普通队，成功后把该团员 `PartyIndex` 置 0，再广播 op2 + op3 + 0x027B + RAID_LIST（`RaidManager.cs:404-424`、`PartyHandler.cs:918-963`、`RaidHandler.Parties.cs:100-125`）。准备中退队会使准备作废（自测 `:3545`）。

### 2.7 改团信息

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029F（u8 + 标题 dstr） | — | `RaidHandler.cs:328` |
| 2 | `TryUpdateTitle`：**不检查是否团长**，任意团员都能改，也不限制 State | — | `RaidManager.cs:334-347` |
| 3 | CMD 0x029F 1B ack（客户端未注册此 handler） | 请求者 | `RaidHandler.cs:335` |
| 4 | RAID_MODIFY op2 | 频道 | `:336`、`:400-404` |

注意：改标题后上游**不重发 RAID_LIST**，目录里的标题要等下一次目录刷新才更新。

### 2.8 团长移交（op1）

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029E：op1、目标 uid、0 | — | `RaidHandler.cs:277-300` |
| 2 | 条件：请求者是当前团长、session 正确；目标是团员、在线、同端口、TCP 连接中；不在准备中；目标不是自己 | — | `RaidManager.cs:349-372` |
| 3 | 更新 `LeadershipVersion` 与 `AssignmentVersion` | — | |
| 4 | CMD 0x029E ack | 请求者 | |
| 5 | USERINFO（新团长）→ RAID_MODIFY op2 → RAID_LIST | 频道 | `RaidHandler.Parties.cs:63-82`；自测 `:3377-3384` 断言帧序为 `[0x0002, 0x0250, 0x024F]` |

各状态的允许情况：State 0、2、5 都允许，准备中不允许（自测 `:3278-3345`，准备中的断言在 `:3311`）。移交会作废旧团长发出的待定邀请（自测 `:3080-3086`）。

### 2.9 离团

**团员离团**：

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029A（上游忽略 body） | — | `RaidHandler.cs:150` |
| 2 | `LeaveLocked`：先从等待表移除，再移出名单、更新 `AssignmentVersion` | — | `RaidManager.cs:1278-1319` |
| 3 | CMD 0x029A `01`，然后 RAID_MODIFY op1（删除）发给离团者 | 离团者 | `RaidHandler.cs:170-171` |
| 4 | 对频道每个会话：团仍存在则发 RAID_MODIFY op3，然后 RAID_LIST | 频道（含离团者） | `:543-559`；自测 `:2952-2971` |

上游离团**不碰普通队**：已在团本小队里的人离团后，实际队伍仍在。`UpstreamStatic`。

**团长离团**：`LeaveLocked` 发现离开的是团长或团已空，就解散整团，清除全部索引和副本参与记录（`RaidManager.cs:1293-1310`），然后清理运行时状态（`RaidHandler.cs:529-541`）。频道内每个会话收到 RAID_MODIFY op1 + RAID_LIST。**没有自动移交团长。** 先移交再离开的旧团长不会触发解散（自测 `:3339`）。

**第三方的踢人**：0x029A 的 u16 是目标 uid。团长只能在可招募状态、且不在准备中时踢人（`T/DfoServer.Game.Raid/RaidManager.cs:1014-1031`）。被踢者收到 CMD 0x029A `01 01` 与 op1（`T/.../RaidHandler.cs:2335-2343`）。

### 2.10 开团准备与开始（第一阶段）

| # | 帧 | 收件人 | 位置 |
| --- | --- | --- | --- |
| 1 | `→S` 0x029B（0B） | — | `Progression.cs:16` |
| 2 | 门票检查：每个团员都持有 ≥1 个 10096296。不满足时回 CMD `00` 和 0x01F7「攻坚队中存在未准备入场材料的队员。」 | 团长 | `Progression.cs:24-30`；`RaidHandler.Admission.cs:101-122` |
| 3 | `TryBeginStart`：请求者是团长、State 0、未在准备。设 `StartPending=true`，生成新的 `PreparationGeneration`，冻结 `PreparationMembers`，清空应答集 | — | `RaidManager.cs:509-522,1329-1335` |
| 4 | CMD 0x029B `01` | 团长 | `Progression.cs:35` |
| 5 | USERINFO + RAID_MODIFY op0（此时 State 仍为 0） | 频道 | `:38` |
| 6 | RAID_SET_TIMER（类型 0、键 0、3 秒）、RAID_REMAIN_TIME（0，3）、PREPARE_START_RAID（0B） | 全团 | `:39-42` |
| 7 | 定时 3 秒，到点执行 `CompleteRaidPreparationAsync` | — | `:43-51` |
| 8 | **准备窗口处理普通组队命令**：上游按编组校验 SET_PARTY_INFO 及 RES_PEER，后者走 `TryCommitPreparationResponse` → `AcceptPreparedRaidMember`。A21 的 `0x0253` 已证实单成员组建队、多人组非首位主动应答；多人首位与邀请顺序待实机 | 组内 | `PartyHandler.cs:1816-1842`；`RaidManager.cs:533-558`；`PartyManager.cs:157-208`；自测 `:1585-1625`（测试里称为 "raid auto-response"） |
| 9 | 到点检查：团长在线；准备仍有效，即名单、session、`PartyIndex` 都与冻结时一致；每个非 0 组都已形成一支实际队伍，且队长是团序第一人、成员恰好一致、设置块 [9]==2 | — | `Progression.cs:785-807`；`PartyManager.cs:363-382` |
| 10 | 失败：`StartPending=false`，发 RAID_STATE(0,0) 与 0x0257 给全团，发 0x01F7「攻坚小队组建未完成，请确认队员在线且未加入其他队伍后重试。」给团长。不扣门票 | 全团、团长 | `:796-806` |
| 11 | 成功：在锁内再次确认小队，扣除全员门票（`RaidEntryCostCommitService`），State 2、Arg 0、Phase 0，记录开始时间 | — | `RaidManager.cs:589-626`；`Progression.cs:808-822` |
| 12 | 背包刷新 → op2 + op3（频道）→ op3（频道）→ RAID_STATE(2,0)（全团）→ 0x024A、局势、0x0248、计时器（全团） | | `Progression.cs:823-838`。这些属于攻坚中，**交给别的调研** |

冻结名单期间，任何入团、离团、改编或重连都会让 `PreparationIsCurrent` 失效（`RaidManager.cs:524-531`；自测 `:1644-1650`）。旧代次的定时续体不能取消或完成新一轮准备（自测 `:1634`）。

**未编队团员**（`PartyIndex==0`）不需要组队就能开团，在攻坚中作为单人局势组（`RaidManager.cs:793-835`），但同样要有门票。`UpstreamStatic`。

第二阶段在 State 5 由团长再发 0x029B 或由间歇计时器触发，流程类似，并多一步按实际队长重排团序（`ResolveRaidPreparationOrder`，`PartyManager.cs:328-361`）。这属于阶段推进，**交给别的调研**。

### 2.11 重入攻坚队

上游 `HandleRejoinRaid`：用当前会话重绑团员 session，即 `RebindSession`（`RaidManager.cs:314-332`），然后发 USERINFO + op0、USERINFO + op2 + op3 + 0x0257 + 计时器快照。State≠0 时再补通关数、增益和怪物状态。**不回 CMD 0x029D，也不恢复实际小队**（`Recruitment.cs:255-268,312-326`）。

上游的断线就是离团（§2.12），所以 REJOIN 只对仍在团里的同一角色有效，例如返回选角后重选。`HandleRebindResyncAsync` 在 0x0004 选角后自动做同样的事（`Recruitment.cs:270-283`）。

第三方在断线保留期内重入时：清掉离线标记，把断线代次加 1，取消到期计时；在 State 2/5 用 `LivePartyAssignment` 把人放回原 `PartyIndex` 的实际队伍；向频道广播 USERINFO + op3 恢复在线显示；给重入者补发 RAID_STATE（`T/.../RaidHandler.cs:4671-4761,4796-4815`）。

### 2.12 断线

**上游**：`OnClientDisconnected` → `ClearSessionAsync` → `OnSessionDisconnected` → `LeaveLocked`，与主动离团相同。团长断线即解散。频道收到 op3 或 op1，再收 RAID_LIST（`GameProtocolHandler.cs:366`、`RaidHandler.cs:515-527`、`RaidManager.cs:988-996`；自测 `:2867-2995` 覆盖团员断线、团长断线、坏连接团长离团）。会话结束时，该会话的待定攻坚队邀请一并删除（`PartyHandler.cs:389-398`）。上游**不发 RAID_DISCONN_INFO**。

**第三方**：

- 断线保留 `DisconnectMemberWaitSeconds`，默认 300 秒（`T/DfoServer.Game.Raid/AntonRaidRewardProvider.cs:189-197`）。
- 名单中显示为 uid 0，cid 位置写断线时间（`T/DfoServer.Game.Raid/RaidManager.cs:1033-1086`、`T/.../RaidPacketBuilder.cs:521-533`），频道收到 USERINFO + op3（`T/.../RaidHandler.cs:2696-2749`）。
- 到期后正常离团；断线者若是团长则解散（`RaidManager.cs:1088-1115`、`RaidHandler.cs:5979-6010`）。
- 同样不发 0x0328。

### 2.13 等待列表、跨频道与最近玩家

| 动作 | 上游 | 第三方 |
| --- | --- | --- |
| `→S` 0x029C 登记等待 | 不在任何团时加入本频道等待表，回 21B ack（`Recruitment.cs:18-29`） | 同上，并刷新所有等待列表观察者 |
| `→S` 0x033C body[0]==1 | 0x02EB：本频道等待表里在线、未入团、身份一致的人（`Recruitment.cs:42-74`） | 不再用等待表：列出全服在线、城镇就绪、未入团的玩家（跨频道）。依次发：对上次已展示者的 0x02EA 删除 → 本频道玩家的 USERINFO → 0x0251 本频道简表 → 0x02EB 全表（`T/.../RaidHandler.cs:4413-4448`） |
| `→S` 0x0334 最近玩家 | 回 u32 0 | 回 CMD：u8 1、u8 条数，每条 u8 服务器序号 + dstr 名字 + u8 等级 + u8 职业 + u8 GrowType；另发 NOTI 0x02F0 在线信息。数据来自持久化的同团记录，每人最多 100 条（`T/.../RaidHandler.cs:4369-4406`、`T/DfoServer.Game.Raid/RaidRecentPlayerRepository.cs`） |
| 0x0331 / 0x0332 / 0x0333 跨频道邀请 | 未实现 | 未实现 |
| 等待表清理 | 入团、建团、离团、断线时移除（`RaidManager.cs:193,1280`） | 同上 |

---

## 3. 上游领域模型

### 3.1 类型与字段（`U/Game/Raid/RaidAggregate.cs`）

| 类型 | 字段 | 含义 |
| --- | --- | --- |
| `RaidMember` | `UserId` u16 | 线上 uid（`Player.UserId`，缺省为 cid） |
| | `CharacterId` u32 | 角色 id，`TryBuildMember` 要求 ≤65535（`RaidHandler.cs:418-438`） |
| | `SessionId` | 当前会话代际。几乎所有校验都同时比对 uid 和 session |
| | `NameBytes`、`Job`、`GrowType` | 入团时的快照，不随角色变化刷新 |
| | `PartyIndex` u16 | 0 表示未编队，其他值是小队序号。协议允许 ≤10，上游没有「五队」概念 |
| | `PhaseClearCount` | 阶段内个人通关数，属攻坚中 |
| `RaidAggregate`（内部可变） | `InstanceId` | 每次建团生成的 Guid，防止团号复用 |
| | `RaidId` | 团号，见 §3.3 |
| | `TitleBytes` | 团标题，原样字节 |
| | `State`、`StateArgument`、`PhaseIndex` | 见 §3.2 |
| | `StartPending` | 开团或下一阶段准备中 |
| | `PreparationGeneration`、`PreparationMembers`、`PreparationResponses` | 准备代次、冻结名单、已应答者 |
| | `LeaderUserId` | 团长 |
| | `AssignmentVersion`、`LeadershipVersion` | 见 §3.4 |
| | `_members` | 团员表，最多 20 人，**顺序有业务含义**：每组第一人是小队队长 |
| `RaidSnapshot` | 上述字段的只读克隆；`Leader` 找不到团长时退回第一人 | |
| `RaidManager`（`U/Game/Raid/RaidManager.cs`） | `_raids`、`_userToRaid`、`_sessionToUser`、`_channelWaiting`，以及参与、通关等攻坚中字典 | 进程内唯一的团注册表 |

### 3.2 状态机（`UpstreamStatic`）

| State | Arg | Phase | 含义 | 进入 | 离开 |
| --- | --- | --- | --- | --- | --- |
| 0 | 0 | 0 | 招募 | `Create` | 开团成功 → 2 |
| 0 + `StartPending` | 0 | 0 | 第一阶段准备（3 秒） | `TryBeginStart` `RaidManager.cs:509-522` | 取消 → 0（`:569-582`）；完成 → 2（`:589-626`） |
| 2 | 0 / 1 | 0 / 1 | 攻坚中；Arg 与 Phase 同为 0 是第一阶段，同为 1 是第二阶段 | `:615-623`、`:1248-1251` | 阶段完成 → 3；超时 → 4 |
| 3 | 0 | 0 / 1 | 阶段通关后的过渡（结算） | `TryEnterPhaseBreak` `:1074-1095` | `TryCompletePhase` → 5（第一阶段）或 4（第二阶段）（`:1167-1206`） |
| 5 | 0 | 0 | 阶段间歇（第二阶段前待命），可再进准备 | 同上 | 准备完成 → 2 |
| 4 | 0 | 1 | 最终成功，结果停留态 | 同上 | 解散 |
| 4 | 1 | 当时阶段 | 超时失败；上游发 RAID_RESULT 并解散 | `TryFailPhase` `:1097-1122`、`TryFailAndDisband` `:1144-1165` | — |
| 1 | — | — | 上游不用。第三方给阶段间歇中途入团、未付门票的人依次发 RAID_STATE(1,0)、(2,0)（`T/.../RaidHandler.cs:4535-4542`），含义 `Unknown` | | |

本文范围内用到的值：0（招募与准备）、RAID_STATE(0,0)（准备取消的回推），以及开团后的 2。

### 3.3 团号、容量与团长

- **团号**：`(频道号 << 16) | 团长 cid`，冲突时加 1 顺延，0 跳成 1（`RaidManager.cs:68,1370-1376`）。频道号就在团号的高 16 位，目录和广播按 `RaidId >> 16` 取频道（`RaidHandler.Directory.cs:28-38`）。
- **容量**：每团 20 人（`RaidAggregate.cs:139-147`、`RaidManager.cs:279`）。小队序号 ≤10（`RaidHandler.Parties.cs:55`）。每队 4 人只在攻坚中改编（`RaidManager.cs:488`）、准备应答（2–4 人，`:547`）和实际队伍上限中检查。**招募期把 5 人编进同一序号不会被拒绝，但开团时这一组无法组建成功。**
- **局势分页**：每页 5 组（`RaidManager.cs:837-842`），属攻坚中显示。
- **团长**：建团者即团长。只能经 op1 移交。团长离开或掉线即解散。只有团长能开团、改编他人、移交、发团长指挥聊天（mode 53）、同意申请或发出邀请。普通团员可以：改自己的 `PartyIndex`（`RaidManager.cs:383`）、改团标题（无校验）、发团聊（mode 52）、离团。

### 3.4 版本号

| 版本 | 何时更新 | 谁校验 |
| --- | --- | --- |
| `InstanceId` | 每次建团 | 所有异步续体、待定邀请、计时器，防止团号复用后误操作 |
| `AssignmentVersion` | 增删团员、`PartyIndex` 变化、移交团长、准备重排（`RaidAggregate.cs:139-174`、`RaidManager.cs:363-364,394-397,416-420,465,496-498`） | `TryAssignLiveParty` 的乐观并发（`:482`），防止旧名单上的拖动撤销已提交的改编（自测 `:1319`） |
| `LeadershipVersion` | 移交团长 | 待定的入团邀请或申请（`PartyHandler.cs:163`、`RaidManager.cs:294`），移交后旧审批作废 |
| `PreparationGeneration` | 每次进入准备，全局单调递增（`RaidManager.cs:1332`） | 准备计时到点的续体（`IsPreparationReady`、`TryCancelPreparation`、`TryCompletePreparation`）；旧代次不能操作新一轮准备 |
| `PreparationMembers` | 进入准备时冻结 | `PreparationIsCurrent`：人数和每人的（uid、session、`PartyIndex`）都必须与冻结时一致 |

### 3.5 并发与持久化

- `RaidManager` 用一把 `_lock` 串行全部操作（`RaidManager.cs:19`）。若干方法在持有团锁时回调 `PartyManager`，后者有自己的锁（`PartyManager.cs:384`），锁顺序固定为团锁先、队伍锁后。改编还要叠加角色过渡租约和选图、入场闸门（`PartyHandler.cs:81-128`）。网络发送都在锁外，基于快照进行。
- 计时器用 `ClockService` 一次性定时，以 `InstanceId`、类型、键、名称和版本号判断续体是否仍有效（`RaidHandler.Runtime.cs`，属攻坚中）。
- **上游没有持久化**，团只存在于内存；进程重启或断线后团就没了。第三方只持久化最近玩家（MySQL `raid_recent_players`）、奖励上限和门票退款。

---

## 4. 业务规则与校验（`UpstreamStatic`，括号内为失败结果）

上游失败时一律回 1B `00`，或者干脆不回，没有结果码，原因只写在服务端日志与 0x01F7 中文提示里。A21 客户端怎样解读 `00`、是否需要具体的结果码，都是 `Unknown`。

### 4.1 入团条件

- 所有入口要求团本频道会话（不满足时回 `00`）。
- 申请与邀请的双方都要：同一监听端口、城镇载入就绪（`TownPresenceReady`）、`UserState==0`、不在副本、会话仍是当前会话（`Recruitment.cs:76-83`）。不满足时请求被忽略，0x0364 不回包。
- 团处于 State 0、未在准备、人数少于 20、入团者不在任何团（`RaidManager.cs:271-312`）。迟到的同意若遇到团已满会被拒绝（自测 `:3204-3219`）。
- **同账号不能有两个角色在同一个团**（`Recruitment.cs:188-220`）。
- 邀请或申请 60 秒过期；拒绝、非 0 结果或过期都会消耗这条待定请求，不能重放（自测 `:3045-3127`）。
- 上游**没有**等级、职业、门票、疲劳或角色状态方面的入团限制。门票只在开团时检查。
- 已在普通队的人**可以**入团，上游不强制退队。

### 4.2 编队约束

- op0 目标必须是团员；非团长只能改自己；序号 ≤10。
- 招募期不检查每队人数和队长归属。
- 攻坚中改编：只能由团长发起；涉及的人都在线、同端口、不在副本或选图中；目标队未满 4 人；名单版本未变。失败时团编组和实际队伍都不变（自测 `:1278-1350`）。
- 准备中（`StartPending`）任何改编都会让准备失效。上游没有直接拒绝改编，而是靠准备失效来兜底。

### 4.3 开团前置条件

- 请求者是团长，State 0，未在准备。
- 每个团员持有 ≥1 个 10096296（入场券）。
- 3 秒内每个非 0 组都由客户端组成团本模式的实际队伍，队长是组内团序第一人，成员恰好吻合，且没有人在别的普通队或有未决的普通邀请（`PartyManager.cs:161-197`）。
- 团长在 3 秒后仍在线。
- 上游**没有最少人数限制**，一个人就能开团。第三方用配置项 `MinimumStartMembers`（1–20，默认 1）在两处检查（`T/DfoServer.Game.Raid/RaidManager.cs:564`、`T/.../RaidHandler.cs:3385-3391`）。

### 4.4 何时允许调队

| 时机 | 上游 |
| --- | --- |
| 招募（State 0，非准备） | 只改标签 |
| 准备中 | 请求会被接受，但让本轮准备失效 |
| 攻坚中（State 2） | 改实际队伍；涉及的人不能在副本或选图中 |
| 阶段过渡（State 3） | 拒绝 |
| 间歇（State 5） | 同 State 2 |
| 结束（State 4） | 拒绝（自测 `:1335`） |

---

## 5. 第三方商业端差异（`ThirdPartyStatic`）

只列与上游不同或新增的部分。

| 项 | 第三方 | 位置 |
| --- | --- | --- |
| 断线保留 | 保留 300 秒（可配），名单中 uid 写 0、cid 位置写断线时间；到期才离团，团长到期则解散；重连时恢复在线，并在 State 2/5 放回原小队 | `T/DfoServer.Game.Raid/RaidMember.cs`（`DisconnectGeneration`、`DisconnectDeadlineMilliseconds`、`DisconnectStartedTimestamp`）；`RaidManager.cs:356-380,1033-1115`；`T/DfoServer.Network.Handlers/RaidHandler.cs:2696-2749,4703-4761,5979-6010`；`AntonRaidRewardProvider.cs:189-197` |
| 踢人 | 0x029A 的 u16 是目标 uid；被踢者收到 `01 01`；限团长、可招募状态、非准备中 | `RaidManager.cs:1014-1031`；`RaidHandler.cs:2299-2357` |
| 退出游戏即离团 | CMD 0x0003 body `00` 时先离团 | `RaidHandler.cs:2290-2297`；`GameProtocolHandler.cs:1385` |
| 间歇期招募 | `CanRecruit` 允许 State 5、Arg 0、Phase 0 时入团。中途入团者要补付门票（`EntryCostPaid`），并收到 RAID_STATE(1,0)、(2,0) | `RaidManager.cs:305-341`；`RaidHandler.cs:4535-4542`；`HandleStartNextRaidPhaseAsync` 检查门票，`:3420-3426` |
| 最少开团人数 | `MinimumStartMembers` | `RaidManager.cs:53-58,564`；`AntonRaidLocalRewards.cs:20,175` |
| 第一阶段准备按实际队长重排 | `TryBeginStart` 也接受 `resolveOrder` | `RaidManager.cs:562-580` |
| 开团就绪更严 | 除了队伍已组成，还要求每支队伍的 P2P 端点（0x000B）已发布，且每人连接未关闭 | `T/DfoServer.Network.Handlers/PartyHandler.cs:220-250,668-680` |
| 等待列表 | 改为全服在线未入团玩家；增发 0x02EA 删除、0x0251 本频道简表；0x02EB 记录里服务器序号在频道之前 | `RaidHandler.cs:4408-4466`；`RaidWaitingPacketBuilder.cs` |
| 最近玩家 | 开团成功时记录同团角色（每人 100 条，MySQL），0x0334 回真实数据加 0x02F0 | `RaidRecentPlayerRepository.cs`；`RaidRecentPlayerPacketBuilder.cs`；`RaidHandler.cs:4343-4406`（记录点 `:4225,4276`） |
| 准备倒计时 | 第一阶段固定 3 秒，第二阶段用配置值 | `RaidHandler.cs:3413`、`:3469` |
| 失败公告 | 用 0x024D 发「攻坚时间已耗尽，攻坚队已解散」 | `RaidHandler.cs:5943` |
| 公开测试队与诊断队 | `DiagnosticOnly`、`TryCreatePublicStandby`，管理用 | `RaidManager.cs:1503-1540` |
| 内容开关 | `IRaidContentGateway` 与 `DisabledRaidContentGateway`，**上游 `RaidContentGateway.cs` 已有同样的接缝**，默认关闭，不在常规流程中调用。不算差异 | `T/DfoServer.Game.Raid/IRaidContentGateway.cs`；`U/Game/Raid/RaidContentGateway.cs` |

---

## 6. 对 JP86 设计的影响

对照的是 `Document/AntonRaidDesign.md`（2026-11-19 草案）。

1. **五个借出容器与上游「招募期无实际队伍」相冲突。**
   - 上游招募期 `PartyIndex` 只是标签，准备期接收普通组队命令并强制团本设置。A21 的两条准备构包分支见 §2.10；其他组队入口仍待实机。
   - 如果 A21 收到 PREPARE_START_RAID 后确实会自动组队（`Unknown`，上游自测把它当客户端自动应答），服务端预先建好的容器会让客户端的建队请求撞上「已在队中」。届时需要决定：把客户端的 SET_PARTY_INFO 和邀请翻译成租借句柄操作，还是在准备期再把成员放进容器。
   - 草案「建团即借出五个空容器」可以保留为服务端内部结构，但「招募期拖人是否立即形成实际小队」，上游的答案是否定的。
   - 这是实机首要验证项（§7 第 1 项）。
2. **小队数目和序号范围。** 草案固定五队，上游协议接受序号 0..10，局势按 5 组一页。A21 编队界面实际发送的序号范围要实机确认。如果客户端可能发 6..10，容器数量和序号映射都要重新定。
3. **普通组队入口：草案「一律拒绝」，上游「团员间放行并反向同步」。**
   - 上游在 State 2/5 允许同团团员之间互邀、SET_PARTY_INFO、LEAVE_PARTY。这些操作会改写团的 `PartyIndex`，团员与非团员之间则拒绝。
   - 第二阶段准备时，上游还按实际队长重排团序。
   - 这说明上游认为 A21 的小队 UI 可能直接走普通组队协议。草案的拦截规则只有在 A21 小队面板不发 0x000A/0x000B/0x000C/0x000D 时才安全；否则团员会在客户端点了「退出队伍」却没有反应。需要实机看团本小队面板发什么帧。
4. **入团前已在普通队。** 草案要求先走普通离队再入团；上游不退队，开团时组队失败并提示「确认未加入其他队伍」。草案的做法更干净，但多出的 0x0009 清屏与离队广播要在入团时序里安排好，而上游的入团时序里没有这些帧。
5. **掉线语义。** 草案「掉线保留团员身份」与第三方一致，与上游（掉线即离团，团长掉线即解散）相反。如果采用保留：
   - 要决定团长掉线时是保留、移交还是解散。上游和第三方都没有自动移交；第三方是到期后解散。
   - 名单里怎样显示离线：第三方用 uid=0 加断线时间。A21 是否另有 RAID_DISCONN_INFO 0x0328 的专门表示，`Unknown`。
   - 保留期多长。
6. **重入。** 草案「重入后放回原小队、其他小队还是未编队区，由实例决定」。上游 REJOIN 不动队伍，第三方在 State 2/5 放回原 `PartyIndex`。另外 REJOIN 请求带 u32（推测是团号），客户端注册了回包 handler，上游却不回包，JP86 需要定回包格式（`Unknown`）。
7. **团长规则需要补进草案。** 团长离团的处理（上游解散）；op1 主动移交（需要目标在线，准备中禁止）；移交后旧邀请作废。草案没有写。
8. **委任小队队长没有独立动作。** 上游 MANAGER_WORK 只有 op0 和 op1，小队队长由团序决定（每组第一人）。草案「团长委任或撤换小队队长」在上游没有对应的客户端动作。若 A21 也没有，小队队长只能由团序或团长身份推出；容器允许「无队长」的不变量，在开团前要由实例补上。
9. **团标题权限。** 上游任意团员都能改团标题，JP86 应限定团长，客户端是否只允许团长打开改名入口待实机确认。改名后上游不刷新 RAID_LIST，JP86 应该刷新。
10. **目录广播范围。** 上游把 RAID_MODIFY 和 RAID_LIST 发给团本频道的**全部**会话。JP86 的协议投影要有「团本频道会话集合」，而不是「打开了目录的观察者」。另外每次发团员数据之前先补发 USERINFO，这一前置条件要沿用。
11. **门票。** 入场券 10096296 在开团时检查、准备完成时扣除，失败不扣。草案写「库存提交门票」，扣费时点建议沿用上游的「准备完成时在团锁内一次性提交」，并把取消路径的不扣费作为验收项。

---

## 7. 未知与待实机项

按对设计的影响排序。

1. **PREPARE_START_RAID 之后客户端做什么。** 是否自动由每组第一人发 SET_PARTY_INFO 和普通邀请，被邀请者是否自动应答（上游依赖这一点，`Unknown`）。0x0253 body 是否为空（机器区进入打包区，`Unknown`）。
2. **招募期拖人编队后，客户端是否期待 0x0009 小队通知。** 以及 RAID_MANAGER_WORK 的小队序号实际取值范围（5 还是 10）。
3. **团本小队面板的退队、邀请和改设置发哪些帧**：普通 0x000A–0x000D，还是团本专用协议。
4. **各 CMD 回包布局**：0x0299、0x029A（1B 还是 2B）、0x029B、0x029C（21B 是否正确）、0x029D（上游不回而客户端有 handler）、0x029E、0x0332/0x0333、0x0334、0x0364（上游不回而客户端有 handler）。客户端回包 handler 地址见 §1.1，可以静态读出布局。另外 0x029F 客户端没有 CMD handler，上游发出的 ack 会被怎样处理也是未知数。
5. **结果码**：失败 ack 除了 `00` 以外是否有原因码，客户端是否据此弹出不同提示。
6. **0x029A 的 u16**：是不是第三方说的踢人目标 uid。
7. **0x029C 的两个 u8、0x0364 的 u8（上游自测填 200）、0x029D 和 0x0363 的 u32 的含义。**
8. **RAID_DISCONN_INFO 0x0328 的触发与含义**：u32 + u16，推测是团号加某种时长或标志；以及它与 REJOIN 的配合。
9. **0x02EB 记录里频道与服务器序号的先后**（上游与第三方相反）；**0x02EA 的增删形态。**
10. **跨频道招募 0x0331/0x0332/0x0333 与 0x02EC–0x02EF**：上游和第三方都没有实现，只有机器区下界。
11. **团内聊天走 0x0298 还是 SEND_MESSAGE mode 52/53**；NOTI 0x024D 的 body。
12. **RAID_STATE 值 1 的含义。**
13. **协议页 H1 标题与身份表名称不一致**：0x0333 的 H1 是 `EVENT_LOGIN_AND_GIFT`，0x0335/0x0336/0x033C 的 H1 是 ADVANCE_ALTAR_…，0x0364 是 `USER_CHOICE_EVENT`，NOTI 0x0327 是 `OBJECT_DESTROY_REWARD`；身份表与 census 都用 RAID_…。上游与第三方都把 0x033C、0x0364 注册为团本入口。哪一套名称属于 A21 待核，这关系到 0x033C 和 0x0364 是否真的是团本请求。
14. **§1.4 列出的四处机器区过时引用**，需要在 meanings 表里修正。
15. **上游 `Create` 隐式离开旧团时不广播**（`RaidManager.cs:67`），旧团其他人收不到离团通知。这是上游缺陷，JP86 不应照搬。
16. **同账号限制、20 人上限、门票物品 10096296** 是否与 A21 原服一致。上游没有给出依据，`Unknown`。

---

## 附：上游自测覆盖索引

| 自测函数 | 行 | 覆盖内容 |
| --- | --- | --- |
| `CheckWaitingPlayers` | 903-973 | 0x02EB 逐字节，39B 记录 |
| `CheckOrdinaryPartyRejoin` | 975-1100 | 普通队与团编组同步 |
| `CheckLivePartyAssignment` | 1278-1350 | 攻坚中改编 |
| `CheckRaidPartyCreationMode` | 1352-1463 | SET_PARTY_INFO 团本设置块 |
| `CheckLiveRaidInviteMode`、`Guards` | 1465-1561 | 团员间普通邀请 |
| `CheckPreparation` | 1563-1738 | 准备、冻结、代次、自动应答 |
| `CheckRaidTimerRejoinAndTermination` | 1740-1807 | 重入时的计时器快照 |
| `CheckRaidDepartureNotifications` | 2867-2995 | 离团、解散、断线的帧序 |
| `CheckRaidJoinConfirmation` | 3001-3236 | 申请与邀请的全部失效分支 |
| `CheckRaidLeaderTransfer` | 3257-3397 | op1 帧序与状态限制 |
| `CheckRaidPartyLeave`、`Guards` | 3399-3567 | 普通退队对团编组的影响 |
| `CheckRaidTownJoinRefresh` | 3832-3919 | 团本城镇目录刷新帧序 |
