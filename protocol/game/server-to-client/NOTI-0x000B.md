# NOTI `0x000B` `USER_UDP_IP_PORT`

<!-- rks:machine:begin -->

> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**
> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。
> 本区之外的一切内容属于人工区，重新生成不会触碰。

## 身份

| 项 | 值 | 来源 |
|---|---|---|
| Endpoint | Game | 帧协议基线 |
| Direction | Server → Client | 接收注册表普查 |
| Command 族 | `0x00`（NOTI） | 客户端两张注册表的分表证据 |
| Type | `0x000B` | 同上 |
| 名称 | `USER_UDP_IP_PORT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 1 + n×22   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 成员记录数 n（u8）：其后按此数量重复 22 字节的成员端点记录 | Confirmed | 上游 PartyIpInfoBuilder.cs:25 写 members.Count；PvpPeerInfoBuilder.cs:74 写 count（PvP 房间用同一结构），取自 `[ebp - 1]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 2 | `u16` | 该成员的 uid（u16 LE） | Confirmed | 上游 PartyIpInfoBuilder.cs:28 写 m.UserId；PvpPeerInfoBuilder.cs:91 写 member.Player.UserId，取自 `[ebp - 0x10]` |
| &nbsp;&nbsp;↳&nbsp;2 | 4 | `u32` | 内网 IPv4：4 个点分字节 a.b.c.d 直写（不是整数字节序）。组队路径填该会话 TCP 远端的 IPv4；中继模式下对端记录改填中继服务器公网 IP | Confirmed | 上游 PartyIpInfoBuilder.cs:29-30（中继分支 :91-95）；取值来源 PartyHandler.cs:344-345 TCP 远端 octets；PvP 侧 PvpPeerInfoBuilder.cs:122 与 :144，取自 `[ebp - 0xc]` |
| &nbsp;&nbsp;↳&nbsp;6 | 4 | `u32` | 外网 IPv4：同样是 4 个点分字节。组队路径与内网同值；PvP 路径填经认证的 TCP 远端地址，取不到则写 4 字节 0（连同端口一并置 0） | Confirmed | 上游 PartyIpInfoBuilder.cs:31；PvpPeerInfoBuilder.cs:126-140 与 :145，取自 `[ebp - 0x10]` |
| &nbsp;&nbsp;↳&nbsp;10 | 2 | `u16` | UDP P2P 端口，按大端（网络序）写：高字节在前、低字节在后。中继模式下写的是「收包人→该对端」这条有序中继腿的端口 | Confirmed | 上游 PartyIpInfoBuilder.cs:32-33 先写 port>>8 再写 port&0xFF（中继腿 :81-98）；PvpPeerInfoBuilder.cs:146-147，取自 `[ebp - 0x14]` |
| &nbsp;&nbsp;↳&nbsp;12 | 4 | `bytes` | acc_id：账号 ID（u32 LE）。组队路径用 characterId 充当，PvP 路径优先用真实 AccountId、否则回落 characterId | Confirmed | 上游 PartyIpInfoBuilder.cs:34 写 m.AccId（来源 PartyHandler.cs:359 AccId=(uint)cid）；PvpPeerInfoBuilder.cs:149-153，取自 `[ebp - 0x18]` |
| &nbsp;&nbsp;↳&nbsp;16 | 1 | `u8` | nat_type：NAT 类型。组队路径恒写 0（open/LAN）；PvP 路径写客户端经 CMD 0x0002 SET_UDP_IP_PORT 上报的值，中继腿则恒写 0 | Confirmed | 上游 PartyIpInfoBuilder.cs:35；PvpPeerInfoBuilder.cs:141 与 :154（中继腿 :114）；上报入口 PartyHandler.cs:98-103，取自 `[ebp - 2]` |
| &nbsp;&nbsp;↳&nbsp;17 | 4 | `u32` | mtu（u32 LE）：组队路径恒写 1500；PvP 路径写客户端上报的 MTU，无上报则 0 | Confirmed | 上游 PartyIpInfoBuilder.cs:36 WriteUInt32(1500)；PvpPeerInfoBuilder.cs:155 endpoint?.Mtu ?? 0，取自 `[ebp - 0x20]` |
| &nbsp;&nbsp;↳&nbsp;21 | 1 | `u8` | char_attr（角色属性字节）：两条独立构造路径都恒写 0，只有名称、没有服务端取值 | High | 上游 PartyIpInfoBuilder.cs:37；PvpPeerInfoBuilder.cs:156，取自 `[ebp - 3]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D896E0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
