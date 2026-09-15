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
| &nbsp;&nbsp;↳&nbsp;10 | 2 | `u16` | UDP P2P 端口，当前 A21 实机工作编码为 u16 BE（网络序）。handler 的 ws2_32!htons 调用只证明后续 socket 转换，不能单独推出 wire 为 LE；把已工作的大端基线仅改为小端后，双端均已成功上报真实端口但稳定停在连接中并退队。中继模式下该值仍表示「收包人→该对端」这条有序中继腿的端口 | High | 2026-09-15 双客户端受控回归：C2S 端口 53644/53663 均 accepted，S2C 改 LE 后连接失败；旧上游 PartyIpInfoBuilder.cs:32-33 同为 BE；A21 handler 0x011896E0 与 [0x03029638]=ws2_32!htons 不能独立判定 wire 字节序，取自 `[ebp - 0x14]` |
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

## A21 服务端实现

**状态：Implemented（A21 双端均已上报端点；网络序回退等待再次实机验收）。**
[`PartyEndpointsEncoder`](../../../Server/src/Dfo.Server.Protocol/Game/Party/PartyNotificationSlice.cs) 按
`1 + n×22` 编码当前队员。2026-09-15 的双客户端受控回归否定了此前仅凭
`htons` 推出的 S2C 小端结论：C2S `0x0002` 上报的真实端口均被接受，只把
S2C 端口从已工作的大端改为小端后，两端稳定停在“连接中”并退队。因此当前
S2C 与 C2S 都按网络序编码；handler `0x011896E0` 的 `htons` 调用保留为静态事实，
但不再把它单独当作 wire 字节序证据：

| 记录内 Offset | Size | Enc | 值/含义 |
|---:|---:|---|---|
| 0 | 2 | `u16 LE` | wire user id |
| 2 | 4 | 4 个字节 | 内网 IPv4 |
| 6 | 4 | 4 个字节 | 外网 IPv4 |
| 10 | 2 | `u16 BE` | UDP 端口；与 C2S `0x0002` 的端口报告一致采用网络序 |
| 12 | 4 | `u32 LE` | 当前 A21 兼容路径写 character id（客户端字段名/参照注释称 acc_id） |
| 16 | 1 | `u8` | NAT 类型 |
| 17 | 4 | `u32 LE` | MTU |
| 21 | 1 | `u8` | char attr，当前为 `0` |

尚未上报 `0x0002` 的成员仍占一个记录，但不能写 `0.0.0.0:0`。2026-09-11 首轮双端
实测中只有一端上报 UDP；服务端随后向双方发送含零端点的两人 `0x000B`，两端在同一秒、
同一客户端地址异常退出。对照同版本旧服 `PartyMember`/`PartyIpInfoBuilder`，缺失上报时应写
内外网地址 `127.0.0.1`、端口 `10000`、NAT `0`、MTU `1500`。当前实现保留该回落，
端口同样按 A21 已工作基线编码为 BE；
这是结构安全兼容值，不冒充可跨主机连通的公网端点，character id 仍保留。
通知只投递给 snapshot 中仍是当前精确会话的成员；建队时位于
`0x0099 → 0x0008 → 0x000B → 0x0009` 的第三阶段；成员离开后的幸存者刷新不含 0x0008。
当前单独收到 C2S `0x0002` 只更新领域端点、不立即广播；它的最新值会进入下一次建队或成员变化快照。
2026-09-12 当前两个客户端均先上报真实端点，随后接受邀请并稳定形成同一两人队伍；
见 [`a21-town-party-detail-20260912.md`](../../patterns/data/traces/a21-town-party-detail-20260912.md)。
该实测没有覆盖副本 P2P 或“成员完全未上报端点”的回落分支，也没有逐帧归档本通知；城镇组队 UI
本身不能证明客户端消费了端点投影，因此不升级为 `ClientVerified`。

## 待验证

- [ ] 用逐帧发送记录或客户端读取 trace 证明双端实际收到并消费当前端点投影。
- [ ] 用副本内实际 P2P 数据流重新确认大端端口回退与 character-id 字段的完整连通语义。
- [ ] A21 双客户端确认缺失端点的回环/10000 回落不再触发接受后异常退出。
- [ ] 归档有端点和回落端点成员两种真实 S2C body。
