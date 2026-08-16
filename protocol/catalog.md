# 协议清单

本页记录**已在线观察到、正在做或已做完**的协议：各自处于哪一阶段、细节在哪个文档。
细节不写在这里——每行链接到该协议自己的文档。

> **找一条这里没有的协议？** 去 [Game 协议全量索引](game/index.md)。
> 那里覆盖静态普查可见的全部 1410 条协议身份，每条都有官方名与静态分析得出的 body 初稿；
> 它是**地图**，本页是**进度**。两者的关系见[清单的完整性](#清单的完整性)。

## 阶段

| 阶段 | 含义 |
|---|---|
| `未知` | 已在线观察到报文，但布局或语义尚未确立。可能只有长度、方向和出现时机。 |
| `确定语义` | 布局、字段宽度、条件分支与编码方式已有直接（指令级）证据，且至少一个真实样本可被无损解释。 |
| `实现服务端功能` | 服务端已解码为 typed message 并有消费者，parser/codec 与 handler 有回归测试。 |
| `双向验证结束` | 真实客户端端到端验证通过，双向行为（含"是否需要响应"）已用可观察结果确认。 |

进入 `双向验证结束` 需同时满足：

1. 方向、触发时机、是否需要响应均已确认；
2. 客户端构包或解包路径有指令级证据；
3. 字段顺序、宽度、条件分支和编码方式已记录；
4. 至少一个真实样本可由记录的布局无损解释；
5. 服务端 parser/codec 与 typed message 有回归测试；
6. 实机客户端验证通过，并记录可观察结果。

## Game / Client → Server

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` | `0x0001` | LOGIN | Query | 未知 | — |
| `0x01` | `0x0002` | — | 未知 | 未知 | — |
| `0x01` | `0x0003` | EXIT | Query | 未知 | — |
| `0x01` | `0x0005` | `CREATE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0005.md](game/client-to-server/0x0005.md) |
| `0x01` | `0x0006` | `DELETE_CHARACTER` | Mutation / 有响应 | 实现服务端功能 | [0x0006.md](game/client-to-server/0x0006.md) |
| `0x01` | `0x0008` | GET_USERINFO | Query / 有响应 | 双向验证结束 | [0x0008.md](game/client-to-server/0x0008.md) |
| `0x01` | `0x008F` | `CHANGE_TUTORIAL_FLAG` | Report / 无响应 | 双向验证结束 | [0x008F.md](game/client-to-server/0x008F.md) |
| `0x01` | `0x00AB` | `CLIENT_SPEC_STATISTIC` | Report / 无响应 | 双向验证结束 | [0x00AB.md](game/client-to-server/0x00AB.md) |
| `0x01` | `0x00C2` | `FRAME_LAG_STATISTICS` | Report / 无响应 | 双向验证结束 | [0x00C2.md](game/client-to-server/0x00C2.md) |
| `0x01` | `0x00FA` | `LODING_TIME_REPORT`（分段指标流） | Report / 无响应 | 双向验证结束 | [0x00FA.md](game/client-to-server/0x00FA.md) |
| `0x01` | `0x0117` | `LAG_STATISTICS`（多态信封） | Report / 无响应 | 未知 | [0x0117.md](game/client-to-server/0x0117.md) |
| `0x01` | `0x01BA` | `MERCENARY_INFO` | Query / 有响应 | 实现服务端功能 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| `0x01` | `0x01DD` | `COMPATIBILITY_INDEX` | Report / 无响应 | 双向验证结束 | [0x01DD.md](game/client-to-server/0x01DD.md) |
| `0x01` | `0x0286` | `CHARAC_VIEW_HIDDEN_CHARAC_INFO` | Query / 有响应 | 实现服务端功能 | [0x0286.md](game/client-to-server/0x0286.md) |
| `0x01` | `0x02A8` | `LOAD_EXTEND_CHARACS` | Report / 无响应 | 双向验证结束 | [0x02A8.md](game/client-to-server/0x02A8.md) |
| `0x01` | `0x02B5` | `CHECK_DOUBLE_CHARACTER_NAME` | Query / 有响应 | 实现服务端功能 | [0x02B5.md](game/client-to-server/0x02B5.md) |

## Game / Server → Client

| Command | Type | 名称 | 角色 | 阶段 | 文档 |
|---:|---:|---|---|---|---|
| `0x01` CMD | `0x0001` | LOGIN response | Response | 确定语义 | [CMD-0x0001.md](game/server-to-client/CMD-0x0001.md) |
| `0x00` NOTI | `0x0002` | 角色列表 | Notification | 未知 | — |
| `0x01` CMD | `0x0003` | EXIT response | Response | 未知 | — |
| `0x00` NOTI | `0x0289` | `HONOR_LEVEL_INFO` | Notification | 实现服务端功能 | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |

角色生命周期五条 CMD 的响应与其请求同 type、同属一次交换，因此记在各自的 C→S 文档内而不另建
S→C 文档；理由见
[角色生命周期协议族](patterns/character-lifecycle.md#为什么请求和响应写在同一个文档里)。

S→C 的 command 取自服务端编码器（`GameLoginResponseEncoder` / `AccountCharacterRosterResponseEncoder`
/ `GameExitResponseEncoder`），并与客户端注册表归属交叉核对一致——**抓包对 S→C 的 command
字节零信息**，日志里的 `hdr` 是证据强度标志而非 command，详见
[game-receive](patterns/game-receive.md#抓包对-sc-的-command-字节零信息)。

服务端实现映射：CMD/NOTI 语义位于 `Dfo.Server.Protocol.Game`。每个出站 Game message
只属于 `IGameCmdMessage` 或 `IGameNotiMessage`，专属 encoder 固定写入 `0x01` 或 `0x00`，
专属 sender 方法在调用点约束类别。`Dfo.Server.Networking` 仅以不透明 `Selector + Type`
拆帧、路由和编码；全部 C→S Game decoder 由 Protocol 的 CMD helper 注册。

## 清单的完整性

上面两张表只列**已在线观察到**的协议，不是客户端支持的全集。两个方向都已做过全量普查，
且**每一条普查到的身份现在都有自己的文档**——见 [Game 协议全量索引](game/index.md)：

| 方向 | 锚点 | 结果 | 数据 |
|---|---|---|---|
| C→S | `MakeHeader` 构包点 | 1082 点 / **604 type**（`0x0000`–`0x043D`） | [protocol-identity.tsv](patterns/data/protocol-identity.tsv) |
| S→C | `Register` 注册点 + 两个运行时转发器 | 1271 点 / **1266 个 `(command,type)` 对**（CMD 538 + NOTI 728，**零未解析**） | 同上 |

> **2026-08-16**：普查已重建为单一的[身份普查](patterns/data/census-identity.py)，
> 补上了此前缺的两样东西——每条记录的**族归属**（CMD 表 / NOTI 表），以及 439 个
> **运行时转发器**注册点。旧的两份 TSV 记载"尚未写回动态注册点"的缺口至此闭合。
> 新普查复现了旧记载的全部数字（604 / 538 / 728 / 两表共有 389），并把唯一一个
> 未解析注册点也解出了——它的首个 `push` 落在调用点前 67 字节，超出了旧脚本 64 字节的窗口。

**S→C 不能只看一个 type 数字。** 客户端按帧头第 0 字节
（command）把入站帧分给两张互不相干的注册表，它们对应两个**协议族**：

| 协议族 | command | 方向 | 客户端注册表 | 已普查 type | 空间容量 |
|---|---|---|---|---:|---:|
| **CMD** | `0x01` | 双向（C→S 请求 / S→C 响应） | 表 A | 538 | 1086 |
| **NOTI** | `0x00` | **仅 S→C**（服务端推送） | 表 B | 728 | 1036 |

**客户端只发 CMD**——`MakeHeader`（所有出站帧唯一必经点）把 command 写死成 `1`。
所以 C→S 的 604 个 type 全部属于 CMD 族，其最大值 `0x043D` = 1085 正好用满 1086 的容量。

证据、分表依据与三条独立佐证见
[game-receive 的 CMD 与 NOTI](patterns/game-receive.md#两张注册表--cmd-与-noti)。

**写 S→C 的 CMD 响应前必读**：每一条 CMD 的 body 都以两字节结果信封开头
（`u8 ok`，`ok == 0` 时再加 `u8 reason`），由客户端接收层在分发之前读掉，各 type 的载荷从
第 2 或第 3 字节才开始。把状态码直接放首字节会被读成"成功"，拒绝时漏掉 reason 字节会
**吃掉下一帧的首字节**。规则与指令见
[game-receive 的两字节结果信封](patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。
NOTI 没有信封。

方法见 [game-frame](patterns/game-frame.md) 与 [game-receive](patterns/game-receive.md)。
所有数字仍是**下限**：C→S 有被混淆到第二代码区、扫不到的构包器（已观察到的 C→S `0x0002`
就是其一）；S→C 的 handler 虽然拿不到帧缓冲，但会从全局入站流里取字段（读取原语见
[game-receive](patterns/game-receive.md#入站流与读取原语sc-的取字段方式)），
约 25% 的 handler 完全不读 body，这部分布局无法从指令推出。
上表的 538 / 728 已包含此前遗漏的 439 个运行时注册点，且**已写回**
[protocol-identity.tsv](patterns/data/protocol-identity.tsv)。

因此本清单的作用是"哪些协议已经在做/做完"，不是"客户端有哪些协议"；后者查
[Game 协议全量索引](game/index.md)。挑下一条协议时应先在索引里查它的构包点/handler 入口
与 body 初稿，再决定是否需要抓包。

### 已定语义但尚无实机样本

下列协议已有指令级结论，但**从未在抓包中出现过**（服务端只在选角色之后才发下行帧，
而选角色协议未实现），因此不进上面两张表。细节全部在族文档里，抓到样本后再逐条建独立记录。

| Command | Type | 方向 | 名称 | 文档 |
|---:|---:|---|---|---|
| `0x01` CMD | `0x00C5` | C→S | 主游戏选项上传 | [game-settings](patterns/game-settings.md) |
| `0x01` CMD | `0x00C6` | C→S | 按键槽上传 | 同上 |
| `0x01` CMD | `0x0170` | C→S | 快速聊天上传（带 bankIndex） | 同上 |
| `0x01` CMD | `0x01C0` | C→S | 角色设置上传 | 同上 |
| `0x00` NOTI | `0x00AD` | S→C | 游戏选项催传信号（body 被丢弃） | 同上 |
| `0x00` NOTI | `0x01C7` | S→C | 按键设置下发 | 同上 |
| `0x00` NOTI | `0x0187` | S→C | 角色设置下发 | 同上 |

## 命名与身份

- 名称来源必须可区分。**2026-08-16 起本清单内已无描述性命名**：上游源码树进入工作区后，
  `CmdPacketType`（1086 条）与 `NotiPacketType`（1036 条）覆盖了两族的全部 type 空间，
  此前的三个描述性命名均已换成官方名并在各自文档保留旧名映射：

  | 旧的描述性命名 | 官方名 | 文档 |
  |---|---|---|
  | `CLIENT_SPEC_PROFILE` | `FRAME_LAG_STATISTICS` | [0x00C2.md](game/client-to-server/0x00C2.md) |
  | `CLIENT_HARDWARE_DETAIL` | `COMPATIBILITY_INDEX` | [0x01DD.md](game/client-to-server/0x01DD.md) |
  | 荣誉等级下发 | `HONOR_LEVEL_INFO` | [NOTI-0x0289.md](game/server-to-client/NOTI-0x0289.md) |

  另有四条此前记为"未命名"的协议取得官方名：`0x008F` `CHANGE_TUTORIAL_FLAG`、
  `0x00FA` `LODING_TIME_REPORT`、`0x0117` `LAG_STATISTICS`、`0x02A8` `LOAD_EXTEND_CHARACS`。
  枚举值即 wire type——该映射经本清单中 16 条已双向验证协议交叉核对，含同号异义样例，零错位。
  **名称到位不等于字段到位**：官方名说明协议整体是什么，不说明每个字段是什么。
- 同一 type 在另一方向或端点出现时，必须新增独立行和独立文档，不能覆盖已有含义。
  `0x0001`、`0x0002`、`0x0003` 都已在两个方向观察到，即为此例。
- `未知` 行没有文档链接。它们的已知观察（长度、时机、字节片段）记在下方"未知协议的现有
  观察"里，避免重复发现；一旦开始逆向，就为它建独立文档并把观察搬进去。

## 未知协议的现有观察

采样自 2026-08-14 / 2026-08-15 四次实机会话（登录 → 角色列表 → 退出），使用
`[FrameCapture]` 全量双向抓包。**方向与长度是实测的**，语义均未确认。

### 登录握手段

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0001` | C→S | 73 | LOGIN。服务端已实现，含账号凭证与启动参数 auth hash（样本不得入库） |
| `0x0002` | S→C | 18 | 角色列表；样本为空名单。`0x0008` `mode=2` 的响应；服务端编码器含旧服移植的字面量尾部，未验证 |
| `0x0002` | C→S | 31 | **已定名：`SET_UDP_IP_PORT`**（上游 `CmdPacketType`，2026-08-16 由上游服务端日志 `SET_UDP_IP_PORT registered: bodyLength=31` 确认）。客户端上报自己的 UDP 端点，供 P2P 使用；本服务端不实现，客户端 handler 也不读 body。与 S→C `0x0002` 同 type 反方向。**条件发送**：五次会话中出现四次，2026-08-15 第五次会话完全没发（其后所有 seq 相应下移一位）。出现时只有一个 u32 变化（offset 12，`0x0005C0E2`/`0x0005C022`，像计数或时间戳），其余恒定，含 `0C000000` + 12 字节 ASCII 十六进制串 `30e3a448ed0f`。构包器在**被混淆的第二代码区**（抓包 stack[1] = `RVA 0x04876980`），`.text` 普查查不到 |

### 退出 flush 段

客户端**正常退出**时按固定顺序发出（Confirmed，四次会话逐帧一致）：

```text
0x0002(31B) → 0x00C2(361B) → 0x00FA(50B) → 0x0117(12B) → 0x0003 EXIT ⇄ → 0x008F(5B)
```

四条都已建独立文档：`0x00C2` / `0x00FA` / `0x008F` 均已双向验证结束，`0x0117` 仍为未知。
`0x008F` 严格说不属于 flush——它是 EXIT **响应**的反应，详见其文档。

| Type | 方向 | Body | 观察 |
|---:|---|---:|---|
| `0x0003` | C→S | 1 | EXIT 请求（`00`）。服务端已实现 |
| `0x0003` | S→C | 1 | EXIT 响应（`01`）。服务端已实现；目前唯一已观察到的**有响应**对 |

只进频道并停留在角色列表的会话不会出现 flush 段；**强制结束进程也不会触发 flush**。
要采样退出序列的协议，必须走游戏内正常退出。

### 抓包并不完整：seq 4 与 seq 6（**已解决**）

五次会话的发送 seq 全部形如 `0…3, 5, 7, 8, …`——**seq 4 与 seq 6 从未被抓到**。
seq 只在真正发送时递增（第五次会话少发一帧，其后每个 seq 都相应下移一位，证明了这一点），
所以这两个号确实被某次发送消耗了，只是抓包看不到。

抓包引擎挂在 `Cipher::Encrypt` 上，因此**任何不走加密路径的发送都会漏**。
影响范围：可以断言"某两帧之间没有**入站**帧"（入站有独立的 Decrypt 钩子），
但**不能**断言"客户端只发了这些帧"。各协议记录里的"无响应"结论不受影响，
"发送序列完整"这类表述则必须留有余地。

**2026-08-16 补上了缺的那一半仪器。** 服务端此前对没有 decoder 的入站帧是静默丢弃——
不记日志、不回字节；补上「未识别入站帧」日志后，这两帧当场现形：

| seq | Type | Body | 文档 |
|---:|---:|---:|---|
| 4 | `0x0286` | 0 | [0x0286.md](game/client-to-server/0x0286.md) |
| 6 | `0x01BA` | 0 | [0x01BA.md](game/client-to-server/0x01BA.md) |
| 7 | `0x0002` | 31 | 见上「登录握手段」 |

客户端侧仍只记到 `0,1,2,3,5`，两边拼起来正好连续 `0–7`。

上面关于抓包的限制**依然成立**，但现在有了互补的第二个仪器：**服务端入站日志记录一切实际到达
的字节，不管发送方走没走加密**。两者盲区不重叠，合起来才能断言"客户端确实什么都没发"。
方法上的推论见
[角色生命周期协议族](patterns/character-lifecycle.md#顺带解决的旧悬案seq-4-与-seq-6)。
