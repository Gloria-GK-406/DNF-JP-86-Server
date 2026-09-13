# Exact-session 队伍目录时间线

原始 allowlist 帧位于任务专用外部 runtime 目录的 `directory-targeted-frames.jsonl`；下表仅保留本 Spec 所需的
无凭据字段。所有时间均为 UTC，两个客户端均连接公开代理 listener `10010`。

| 时间 | connection | 方向 / type | 观察 |
|---|---|---|---|
| 10:22:38.640 | `game-10010-0012` | C2S `0x000C` | character `2` 原生创建标题 `A`。 |
| 10:22:38.661 | `game-10010-0012` | S2C `0x0009` | 创建/刷新 `partyId=1`，leader user `2`。 |
| 10:22:46.617 | `game-10010-0013` | S2C `0x0002 subtype=0` | observer 先收到远端 leader user `2` 的 CNUser。 |
| 10:22:46.624 | `game-10010-0013` | S2C `0x0009 type=0` | 随后收到 `partyId=1`，满足 `0x0002 -> 0x0009` 关联顺序。 |
| 10:23:04.675 | `game-10010-0013` | C2S `0x000C` | character `1` 原生创建标题 `B`。 |
| 10:23:04.682 | `game-10010-0013/0012` | S2C `0x0009 type=0` | 两端均收到 `partyId=2`，leader user `1`。 |
| 10:23:27.169 | `game-10010-0012` | C2S `0x000C` | character `2` 提交一次队伍信息 mutation。 |
| 10:23:27.173 | `game-10010-0012` | S2C `0x0009` | leader 收到当前 revision 的 18-byte 设置更新。 |
| 10:23:27.173 | `game-10010-0013` | S2C `0x0009 type=0` | observer 收到同一 `partyId=1` 的 61-byte refresh；用户观察同一行更新、无重复。 |
| 10:23:37.242 | `game-10010-0012` | C2S `0x000D` | character `2` 原生离开/解散。 |
| 10:23:37.245 | `game-10010-0012/0013` | S2C `0x0009 type=3` | 两端收到 `partyId=1` clear；用户随后刷新并观察 A 行消失。 |
| 11:12:41.009 | `game-10010-0012` | S2C `0x0006` | pro2 正常退出后，另一端收到离场 user `1`。 |
| 11:12:41.009 | `game-10010-0012` | S2C `0x0009 type=3` | 紧接着收到存活 `partyId=2` clear，随后两端均正常结束。 |

Host 同一时间线记录：character `2`、character `1` 的 `SET_PARTY_INFO accepted=True`，character `2` 的后续
mutation `accepted=True`，以及 `LEAVE_PARTY character=2 accepted=True`。

注意：10:23:27 的 wire body 证明“队伍信息 mutation + observer refresh”，但该短 body 本身不携带可独立解码的
UTF-8/本地编码标题文本；“强强联合”仅授予用户原生 UI 观察层级，不把标题字节误标为 wire-confirmed。

## 独立审查补证会话

补证运行使用同一接受基线和同一组客户端二进制，外部 runtime root 为
`C:\Users\Gloria\AppData\Local\Temp\dnf-party-directory-reviewfix-20260913-1930`。本次实际身份为：

- `game-10010-0011`：pro1，user `1`，目标 A 队 leader；
- `game-10010-0009`：pro2，user `2`，B 队 leader兼 observer。

目标对象最终用第二次 A 生命周期的 `partyId=3` 固定，三个客户端快照均来自同一 pro2 PID `62412`：

| UTC | 阶段 | wire / 客户端只读回读 |
|---|---|---|
| 11:58:35.282–.284 | add | event 29：17B inline-title `A` 创建；event 31：observer 收到 66B `partyId=3` type-0。11:58:54.720 的快照显示 `CNUser 1 -> CParty 0x563BB820 -> partyId 3`，`titleIndex=0`。 |
| 12:01:39.601–.603 | update | event 32：12B direct `titleIndex=1`；event 34：observer 收到 61B 同 `partyId=3` refresh。12:01:58.079 的快照保持同一 CNUser、同一 CParty 地址与 partyId，且 `titleIndex` 从 `0` 变为 `1`。 |
| 12:02:25.086–.088 | delete | event 35：leader `0x000D`；event 37：observer 收到 5B `partyId=3` clear。12:02:50.775 的快照显示 CNUser `1` party 指针归零、`partyId=3` CParty 消失，B 的 `partyId=2` 仍存活。 |

对应文件为 `client-party-association-reviewfix2-{add,update,delete}.json`。`titleIndex` 的客户端对象偏移由
固定 B1 parser `0x01172206 -> call 0x01A04FF0 (lea eax,[ecx+4]) -> 0x0117220F mov [eax+4],edi`
确定为 CParty `+0x08`；探针只申请 `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`。
