# A21 同区公共队伍目录实机闭环（2026-09-13）

## 结论

当前服务端的 town-visible 非成员 party 投影已由两个固定 A21 客户端在原生 ID `0x11`
`IntegratePartyListWindow` 完成 add/update/delete 闭环，覆盖：

- 远端 leader `NOTI 0x0002 subtype 0` 先于该队 `NOTI 0x0009 type 0`；
- 同一区域内两名非队员各自建立 A/B 队，双方目录显示两行；
- A 从 inline-title 切换到 `titleIndex=1` 的本地预设“强强联合”，observer 同 party 行更新且不重复；
- A 解散后 observer 收到 type-3 clear，行先显示“已取消组队”，原生刷新后删除，只剩 B；
- 正常退出时另一端收到 `USER_LEAVE(user=1) -> PARTY_INFO clear(party=2)`。

该范围升级为 `ClientVerified`。未实测的跨 scope UI、满员加入按钮与非队长拒绝继续保持自动化或静态等级。

## 固定身份与证据

- accepted baseline：`365baed09ee5a19c08550a4f5dbdaecdafca7c8f`；
- DNF.exe SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；
- pro1/PID `37064`/character `2`：A 队创建、更新、解散；
- pro2/PID `63228`/character `1`：B 队队长与 A 队 observer；
- task proxy raw frame log SHA-256：`82A15A331324FD1EAC506EE0311C1CD1B12046529278516122DE03DB96ED471F`；
- Host raw stdout SHA-256：`9E5EB48AE5028C8E8C6F4C9AED51FEDD2593B069EE769162364047AF40F0AB95`；
- UI 截图 SHA-256：`09A6B0EFB0F571BED2FB944FEAB9838EC8A02E8AB5A02538861028AF8403C1B4`，
  `947x706`；标题“队伍信息(I)”，三标签、搜索、刷新、分页和“我的队伍”均与
  `IntegratePartyList.xui` 一致；删除后显示 B、`1/4`。

原始 runtime 位于任务临时目录，仓库内的无凭据时间线和客户端只读回读位于当前 Spec 的
`evidence/runtime/`。Channel 流量从未落盘。

## Exact-session 顺序

| UTC | connection | 证据 |
|---|---|---|
| 10:22:38.640 | `0012` | C2S `0x000C` 17B inline-title `A`，Host accepted。 |
| 10:22:38.661 | `0012` | S2C `0x0009` type-0，`partyId=1`、leader user `2`。 |
| 10:22:46.617 | `0013` | observer 收到 leader user `2` 的 `0x0002 subtype 0`。 |
| 10:22:46.624 | `0013` | `7 ms` 后收到 `partyId=1` type-0；关联顺序成立。 |
| 10:23:04.675 | `0013` | C2S `0x000C` 17B inline-title `B`，Host accepted。 |
| 10:23:04.682 | `0012/0013` | 两端收到 `partyId=2` type-0。 |
| 10:23:27.169 | `0012` | C2S direct mutation：`special=1,titleIndex=1,capacity=4,category=Normal`。 |
| 10:23:27.173 | `0013` | observer 收到 `partyId=1` 的 61B type-0 refresh。 |
| 10:23:37.242 | `0012` | C2S `0x000D`，Host `LEAVE_PARTY character=2 accepted=True`。 |
| 10:23:37.245 | `0012/0013` | 两端收到 `partyId=1` 5B type-3 clear。 |
| 11:12:41.009 | `0012` | pro2 正常退出后收到 `0x0006 user=1`，随后 `partyId=2` clear。 |

## 客户端对象与 UI

截图后对 pro2 仅以 `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ` 回读：

- CNUser `1 -> party object 0x412F3F20 -> partyId 2`；
- party `2` 的 leader slot `0`、唯一有效 roster user `1`；
- CNUser `2` 的 party pointer 为 null，客户端中无 `partyId=1` 对象。

这与截图“只剩 B、1/4”及用户报告的 A 行删除一致。尝试扫描静态 final vptr `0x031FFFD4` 时，两个
进程都只命中构造/代码常量，没有堆实例；因此本轮不声称直接回读了 `this+0x188`，以 CNUser/CParty
等价对象状态和原生 UI 支撑客户端层。该 vptr 生命周期差异保留为独立逆向问题，不影响已经观察的目录行为。

独立审查要求把对象层扩展到 add/update/delete 三个时点。补证会话在同一二进制 hash、同一 exact pro2
PID `62412` 上以第二次目标 `partyId=3` 完成：

| 阶段 | observer wire | pro2 CNUser/CParty 回读 |
|---|---|---|
| add | 11:58:35.284，66B type-0 | `CNUser 1 -> CParty 0x563BB820 -> partyId 3`，`titleIndex=0` |
| update | 12:01:39.603，61B 同 party refresh | CNUser、partyId 与对象地址均不变，`titleIndex=1` |
| delete | 12:02:25.088，5B type-3 clear | CNUser `1` party 指针为 null，`partyId=3` 对象不存在，B/partyId=2 仍存活 |

`titleIndex` 的 CParty `+0x08` 偏移由固定 B1 parser 的实际写入链
`0x01172206 -> 0x01A04FF0 (lea eax,[ecx+4]) -> 0x0117220F mov [eax+4],edi` 确定；读回探针
有 synthetic-memory 回归保护，且仍只申请 query/read 权限。补证 raw frame log SHA-256 为
`4B641862D68444CCE475DCD035C4060C3534B7017DB1BDE117B0BEE5B0ACDAF6`，Host stdout SHA-256 为
`867E651D798CCC3BA6B545A512042B4E825E8EE9C278EE99B96BE40FF018961B`。

## 样本

- C2S inline-title A/B：
  [`0x000C-20260913-01.hex`](../../../game/client-to-server/samples/0x000C-20260913-01.hex)、
  [`0x000C-20260913-02.hex`](../../../game/client-to-server/samples/0x000C-20260913-02.hex)；
- S2C directory add/update/clear：
  [`NOTI-0x0009-20260913-05.hex`](../../../game/server-to-client/samples/NOTI-0x0009-20260913-05.hex)、
  [`NOTI-0x0009-20260913-06.hex`](../../../game/server-to-client/samples/NOTI-0x0009-20260913-06.hex)、
  [`NOTI-0x0009-20260913-07.hex`](../../../game/server-to-client/samples/NOTI-0x0009-20260913-07.hex)；
- S2C normal-exit user leave：
  [`NOTI-0x0006-20260913-01.hex`](../../../game/server-to-client/samples/NOTI-0x0006-20260913-01.hex)。
