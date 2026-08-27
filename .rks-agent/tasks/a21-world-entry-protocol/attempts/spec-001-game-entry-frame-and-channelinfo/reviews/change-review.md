---
kind: rks-review
verdict: passed
---

> **本文件含两轮评审。** 第一轮（下文「独立评审」全篇）判 `changes_required`，
> 列出 A-1/A-2/A-3 与 B-1..B-5 共八条阻塞发现。第二轮见文末
> [「七、整改复核」](#七整改复核)，判 **`passed`**。frontmatter 的 `verdict` 是**第二轮**的结论。

# 独立评审：spec-001 A21 游戏端点入站帧边界与初始连接通知

评审对象：工作树 `a21-world-entry-protocol` 相对基线 `0d37e9a` 的全部改动——12 个已跟踪文件
（294 增 42 删）加 4 项未跟踪（`Server/src/Dfo.Server.Protocol/Game/ConnectionCheck/`、
`Server/tests/Dfo.Server.Protocol.Tests/Game/ConnectionCheck/`、
`Server/tests/Dfo.Server.Protocol.Tests/Game/InitialConnection/`、
`protocol/game/client-to-server/0x04DD.md`）。`.rks-agent/` 只作证据阅读，不计产品改动。

评审在全新上下文中进行，不采信执行者的结论。所有"实测"断言我都回到原始字节自行复核；
所有守卫我都用真改坏、真跑来验证。改坏实验一律在临时目录下的两份独立副本里做
（`baseline/` = `git archive HEAD`，`mutant/` = 工作树内容），**本工作树的产品代码与文档
一个字节未动**（评审结束时 `git status --short` 与开始时逐行一致）。

## 结论

**changes_required**。

**线级结论本身是对的，而且比规格声称的还结实**——我把每一条具名取值都独立追回原始字节，
逐条成立，没有一条是照抄上游（上游确实读 seq@11，本改动有据地背离了它）。守卫也是真的：
四次改坏全部变红。

阻塞的不是字节，是**证据与文档**：三条 A 级发现都指向"把没有证据的东西写成了已验证的结论"，
其中一条**被它自己引用的那份抓包直接推翻**。这正是根契约 SC-4 与本规格 Constraints
第一条要防的失败形态，只是它发生在因果结论与实机结论上，而不是发生在字段布局上。

---

## 一、我实际执行过的独立验证

### 1) 回到原始字节复核每一条具名断言

唯一的一手证据是 `ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt`
（`=== DfoServer packet capture started 2026-08-23 00:16:55 ===`，268 SEND + 190 RECV = 458 帧）。
我自己解析了它，没有采信任何转述。

| # | 被核对的断言 | 出处 | 结果 |
|---|---|---|---|
| V1 | 入站头 14 字节，`BodyLength = TotalLength - 14` | `GameClientFrameRequestInfo.cs:15` | **成立**。190/190 条入站帧的 `TotalLength` 字段与该帧原始字节长度**精确相等**（逐帧比对），因此 14 是构造上确定的，不是统计推断 |
| V2 | `[11]` 恒零、`[12]` 是逐帧加一的计数、`[13]` 恒零 | `GameClientFrameRequestInfo.cs:60-61`、`game-frame.md:35-37` | **成立**。190 帧里 `byte[11]` 取值集合 = {0}，`byte[13]` = {0}，`seq@12` = 0,1,2,…,189，**189/189 步全部是加一**；读在 offset 11 则 **0/189 步**是加一（得到 0,256,512…）。规格 line 111 与 `game-frame.md:39-41` 如实记下"计数最大到 189、从未越过 255，所以高位字节参与计数是推断"——**这一句我复核为准确**，最大值确实是 189 |
| V3 | 上游把 seq 读在 offset 11 | 契约、`game-frame.md:36` | **成立**。`ServerS4A21/Server/DfoServer/Network/Core/Packet.cs:106-107`：`seq = reader.ReadUInt16(); extra = reader.ReadByte();`。**本改动确实是有依据地背离上游，而不是照抄上游**——routing-decision 点名担心的头号失败形态没有发生 |
| V4 | NOTI `0x0001` 的 68 字节 body 是转录 | `GameInitialConnectionCodecTests.cs:18-37` | **成立且是逐字节转录**。与 `packet_log.txt:10` 的 `SEND cmd=0x00 type=0x0001 len=83 body=(68B)` **68 个字节全部一致**（逐字节比对）。这不是平行重写，是真样本 |
| V5 | 派发表大小 1271 / 1218 | `GameInitialConnectionPayloadCodec.cs:21,23` | **取值成立**（`F7 04 00 00` / `C2 04 00 00`），但其**解释是错的**，见 B-2 |
| V6 | 两个标记字节是 ASCII `'0'` | 同上 `:36` | **成立**，实测 `30 30` |
| V7 | 时间戳 `0x6A89CD02` = 2026-08-22T16:23:30Z | `NOTI-0x0001.md:71` | **成立**（1787415810 → 2026-08-22T16:23:30+00:00），与抓包起始 00:16:55 本地时（= 16:16:55Z）相隔约 6 分钟，"与该会话开始时刻相符"准确 |
| V8 | `0x04DD` 请求整帧 14 字节、body 空；响应同 type、body 单字节 `01` | `0x04DD.md:44-54` | **成立**。会话里 30 条 `RECV cmd=0x01 type=0x04DD len=14 body=(0B)` 与 30 条 `SEND cmd=0x01 type=0x04DD len=16 body=(1B) [01]`；文档里抄的响应原始字节 `01 DD 04 10 00 …00 01` 与日志一致 |
| V9 | "一次已知良好会话里出现三十次请求、三十次响应" | `0x04DD.md:56` | **成立**（30 / 30） |
| V10 | "458 帧会话（其中 190 条为入站）" | `game-frame.md:51` | **成立**（268+190=458）。顺带：根契约 line 26 写的 455 与此不符，以 458 为准 |
| V11 | "与 A21 CMD 枚举的 1272 条差一" | `NOTI-0x0001.md:79` | 数字成立（`CmdPacketTypeA21` 1272 个成员），但结论错，见 B-2 |
| V12 | "与 A21 NOTI 枚举条数相等" | `NOTI-0x0001.md:80` | **不成立**，见 B-2 |

### 2) 构建与全量测试（本工作树，未改动状态）

| # | 命令 | 结果 |
|---|---|---|
| T1 | `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| T2 | `dotnet test Server/86JP.Server.slnx -m:1`（`PVF_ARCHIVE_PATH`/`Pvf__ArchivePath` = `D:/project/DNF/DNFClient-debug/Script.pvf`） | **全绿：1193 通过 / 2 跳过 / 0 失败**（Pvf 60、Application 530、Host 4、Infrastructure 100+2skip、Networking 51、Persistence 237、Protocol 211） |

执行前确认 7001/10011 无监听、无 `DNF*`/`Dfo*` 进程，未打扰任何在跑的会话。

### 3) 改坏实验：守卫是不是真的

全部在 `mutant/` 副本里做，每次只改一处。

| # | 改了什么 | 跑了什么 | 结果 |
|---|---|---|---|
| E1 | `GameClientFrameRequestInfo`：把 seq 读回 offset 11（上游写法） | Networking.Tests | **红 3**：`The_frame_counter_is_the_last_two_bytes`(1,2) + `Game_client_header_is_parsed_…` |
| E2 | 同文件：`HeaderLength` 14 → 13，seq 回 offset 11 | Networking.Tests | **红 6**：含 `The_clients_empty_connection_probe_parses_with_no_body`、`A_login_frames_header_yields_the_measured_body_length` |
| E3 | codec：删掉 `WriteInt32(body, ExtraAddressCount)`（复现"少一个字段"这个真实缺陷形态） | Protocol.Tests | **红 2**：整段比对 + 68 字节长度 |
| E4 | codec：markers → `0x00`，`CommandPacketCount` → 1086 | Protocol.Tests / Networking.Tests | **红 2 / 红 1**（`GameLoginIntegrationTests` 也红） |
| E5 | `GameProtocol.ConnectionCheckType` **0x04DD → 0x04DE** | **全量** Protocol.Tests + Networking.Tests | **全绿**（211/51）。→ **B-3** |

结论：**E1–E4 证明布局守卫都是真的**；执行者担心的"`GameLoginIntegrationTests` 是平行重写因而
是假守卫"这一条，实测**不成立**——E4 里它照样红，且真正的布局守卫
（`GameInitialConnectionCodecTests`）的期望值是 V4 证实的逐字节转录，不是重写。
**唯一的假守卫是 E5。**

---

## 二、Blocking Findings

### A-1（A 级）实机结论被写成已验证，但仓库内外都找不到那份证据

`protocol/catalog.md:45,71,72` 把三行标为 `双向验证结束`。该阶段的准入条件写在同一文件
`catalog.md:26-33`，其中第 2 条要求"客户端构包或解包路径有**指令级证据**"、第 6 条要求
"**实机客户端验证通过，并记录可观察结果**"。这两条都没有满足：

- `0x04DD.md:62` 的 `E-001` 把证据写成"本仓服务端与 A21 客户端的**双向帧记录**"，
  `game-frame.md:50` 引同一份。**这份记录我找不到**：`DNFClient-A21-protocol/` 下
  `ClientHack.ini:66` 指定的 `clienthack-frame-capture.log` 没有生成；两次开了
  `[FrameCapture] Enabled=1` 的运行都在插件初始化时失败退出
  （`runs/20260823-164419/ClientHack.log` 末两行 `plugin initialization failed` /
  `[fatal] Readiness, IAT repair, or required plugin initialization failed.`，
  `run.txt` `exit code: 0x000000C1`；`runs/20260823-164046` 同样）；
  最后一次 17:57 的运行**根本没有加载 `0100_NetworkHook`**
  （`DNFClient-A21-protocol/ClientHack.log` 的插件清单里没有它）。
  全盘只有 A12 时期的 `clienthack-frame-capture-2026081*.log`。
- `0x04DD.md:89` 的 `## Verification` 写"Real-client result：A21 客户端在被应答后停止重发，
  并继续推进到登录"——这是本成果最终成败的那一句，没有任何可核实的记录，
  而且与它自己引的 `E-002` 相反（见 A-2）。
- 规格 RQ-007 / AC-006 / EV-004（"客户端不再断开并发出 LOGIN 的双向帧记录"）同样没有落地物。

需要的动作：要么补上可核实的实机记录并归档，要么把这三行的阶段降回
`实现服务端功能`，并把 `0x04DD.md:89`、`E-001` 改写成实际拿到的证据
（例如本仓服务端侧的逐帧发送日志——按 `protocol/evidence.md` 的效力顺序它是 1 级证据，
完全够用，只是它不是"双向帧记录"）。

### A-2（A 级）`0x04DD` 的因果结论被它自己引用的抓包推翻

同一条主张出现在四处：

- `protocol/game/client-to-server/0x04DD.md:72`：客户端拿不到应答就每十秒重发，
  "**并且在此之前不会发出登录命令**"；
- `Server/src/Dfo.Server.Protocol/Game/ConnectionCheck/ConnectionCheckSlice.cs:11-13`：
  "It is the first thing the client sends after the socket opens -- **before it will consider
  logging in**"；
- `protocol/patterns/game-frame.md:31`："A21 客户端在 Game 端点上发出的第一条命令是 `0x04DD`"；
- `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs:27-28`："repeats about every ten
  seconds **until it is answered**"。

我在 `E-002` 指的那份 458 帧会话里逐帧核对，两点都相反：

1. **登录不在探测之后**。该会话的第一条入站帧就是 `CMD 0x0001 LOGIN`（seq 0，
   `packet_log.txt:12`），第一条 `0x04DD` 出现在 **seq 13**。入站前 14 帧的
   (seq,type) 为 `(0,0x0001) (1,0x00C5) (2,0x00C5) (3,0x00C5) (4,0x00AB) (5,0x01DD)
   (6,0x04F1) (7,0x04D7) (8,0x04D7) (9,0x0008) (10,0x0286) (11,0x02A8) (12,0x01BA)
   (13,0x04DD)`。
2. **被应答也不会停**。30 条探测的 seq 是
   `13,15,16,…,179,181,182`，横跨整局；每一条都得到了应答（30 条
   `SEND … type=0x04DD len=16 body=(1B) [01]`）。它是**周期性保活**，
   不是"没人应答才重试、应答后就停"。`E-001` 那一栏写的"被应答后不再重发同一序号"
   也因此是空话（序号本来就逐帧递增，不可能重发同一序号）。

为什么这不只是措辞问题：这句因果是**本切片存在的理由**（`0x04DD.md:71` 把它称作
"不接会怎样的教科书例子"），而下一个成果就是 LOGIN。把"探测门控登录"当成既有结论传下去，
下一轮会照着一个错误的前提排查。按现有证据，客户端在本仓这边不发 LOGIN 的**已知**原因是
CHANNELINFO 那 8 个字节（子契约 Current Situation 第 3 点自己也是这么写的），
而不是未被应答的探测。

需要的动作：把这四处改成实际观察到的形态——"在本仓这边它是连上后的第一条帧且被静默丢弃"
（若这一条有服务端日志支撑，就写成那份日志）；同时删掉"在此之前不会发出登录命令"
与"until it is answered"这两个没有证据、且被已知良好会话反证的因果。

### A-3（A 级）每一条"实测确认"的引用都不可解析，唯一实体是会被下次运行覆盖的构建产物

`protocol/evidence.md` 的「引用怎么写」只有一条规则——"**引用必须能解析**"，并明确规定
整段会话的帧抓包"**日志会被下一次运行覆盖**，所以要先归档再引用"，归档去处是
`protocol/patterns/data/traces/`。

- 本次**没有向 `protocol/patterns/data/traces/` 新增任何文件**（该目录仍只有 2026-08-17 的
  三份 A12 产物）。
- `NOTI-0x0001.md:102-104` 写"实测会话的原始字节保留在**任务证据**里而未入库"——
  `.rks-agent/tasks/a21-world-entry-protocol/` 下只有 4 个 md/yaml，**没有任何抓包副本**；
  子契约 line 36 说的"已留存副本"我在全盘也只找到一份，就是
  `ServerS4A21/Server/DfoServer/bin/Debug/packet_log.txt` 本身——一个 `bin/Debug` 下的
  构建产物，下一次跑那个服务端就会被覆盖。
- `game-frame.md:50-51` 与 `NOTI-0x0001.md` 的整张表引的都是"一次已知良好会话"，**没有路径**。

也就是说：**今天我还能核实这些结论（我确实核实了），明天跑一次 `ServerS4A21` 之后就核实不了了。**
这正是那条规则要防的事，而它是本任务全部结论的唯一支点。

凭据禁令不构成豁免：可以归档去掉 `0x0001` LOGIN body 的会话，或只归档本成果涉及的那些帧
（`NOTI 0x0001` 的 68 字节 body、`0x04DD` 的请求/响应、三条示范 seq 的帧头）。

### B-1（B 级）帧基线文档只改对了一半，RQ-009 未达成

`Docs/NetworkingFrameProtocol.md:126-140` 的表改成了 14 字节，但同一文件的
`## 当前重构实现` 一节仍写：

```text
300: - `GameClientFrameRequestInfo`：解析 13 字节 Game 客户端包头及 Body。
```

这一句描述的是**当前实现**，与 `GameClientFrameRequestInfo.cs:15` 直接矛盾。
同文件 `:280`（"而不是复用 13 字节入站包头"）与 `:292`（"13/15 字节的方向差异必须保留为
两个独立的 codec"）也没跟上。规格 RQ-009 要求"帧基线记录与仓库说明中关于入站游戏帧头部长度的
表述与 RQ-001 一致"——未达成。

（`Docs/Protocol00AB-ReverseEngineering.md`、`Docs/debug-play-split-runbook.md`、
`protocol/game/client-to-server/0x00AB.md` 等处的 13 字节属于 A12 结论，
按 `game-frame.md:26-27` 定的"两条都为真而适用范围不同"是可以保留的，不在本条内。）

### B-2（B 级）两个派发表大小的解释是错的，并因此虚构了一个未解之谜

`NOTI-0x0001.md:80` 写"实测 `1218`；**与 A21 NOTI 枚举条数相等**"。不成立：

- `ServerS4A21/Server/DfoServer/Network/Core/PacketTypesA21.cs` 的 `NotiPacketTypeA21`
  有 **1219** 个成员（取值互不重复），最后一个是 `END = 0x04C2` = **1218**；
- `CmdPacketTypeA21` 有 **1272** 个成员，最后一个是 `END = 0x04F7` = **1271**。

也就是说两个线上取值**都是各自枚举的 `END` 哨兵值**（= 成员数减一），形态完全对称。
于是 `NOTI-0x0001.md:79` 说的"CMD 差一而 NOTI 相等"是不对称的假象，
`:112` 列的未知项"CMD 表数 1271 与枚举 1272 差一的原因"是个不存在的谜——
按这条线索去客户端里找比较指令会白跑一趟。

线上取值本身（1271/1218）由实测直接支撑，**不受影响**；错的是 Evidence 栏里那句推导，
而它被标成了 `High`。

### B-3（B 级）没有任何测试把 `0x04DD` 钉在线上

改坏实验 E5：把 `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs:95` 的
`ConnectionCheckType` 从 `0x04DD` 改成 `0x04DE`，**Protocol.Tests 211 与 Networking.Tests 51
全部照常通过**。原因是解包器注册键（`ProtocolModule.cs:262`）与响应编码器
（`ConnectionCheckSlice.cs:80`）用的是同一个常量，改错了两边一起错、自洽；
而 `ClientMessageType.GameConnectionCheck = 0x0001_04DD` 不参与线级匹配。

`ConnectionCheckCodecTests` 只覆盖 body，AC-005 要求的"服务器回**同一命令类型**"没有守卫。
本切片存在的唯一理由就是那个线级 type，它却是全套改动里唯一没被钉住的事实。
AGENTS.md line 335 的"测试三件套"里的第三件（`Networking.Tests` 线级集成测试）也确实缺席。

### B-4（B 级）`Medium` 不在知识库的置信度词表内

`protocol/README.md` 的「证据等级」只定义三级：`Confirmed` / `High` / `Hypothesis`。
`NOTI-0x0001.md:66,67,68,70,81` 用了 `Medium`。全库 1451 份协议文档里，
**只有本次新增的这一节用它**（`grep -rl "| Medium |" protocol/` 只命中这一个文件）。

### B-5（B 级）A21 结论写进了声明绑定 A12 客户端的文档

`protocol/README.md` 的「适用范围」要求"客户端版本变化时，不得直接假定地址、字段或行为仍然成立；
各协议文档均记录其绑定的客户端 SHA-256"。

- `game-frame.md:54` 与 `:137`、`NOTI-0x0001.md:47` 声明的都是
  `F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`
  ——我核对过，这是 **A12** 客户端 `DNFClient-debug/DNF.exe` 的哈希；
  A21 客户端 `DNFClient-A21-protocol/DNF.exe` 是
  `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。
- `0x04DD.md` 是纯手写记录，**完全没有记录绑定哈希**。

这一条与 A-3 是同一个病：新结论没有把"它绑在哪个二进制上"留下来，
而这套文档偏偏正在同时承载两个客户端的结论。

---

## 三、非阻塞观察

| # | 观察 | 位置 |
|---|---|---|
| N-1 | `The_frame_counter_is_the_last_two_bytes` 的注释说这是"Three consecutive frames from one connection"，实际是**合成头**：三条都用 type `0x04DD` 和同一个 checksum `8A D6 51 F4`（借自 seq=13 的真探测帧），而真实的 seq 0/1/2 三帧是 `0x0001`/`0x00C5`/`0x00C5`、checksum 各不相同。结论没错（我核实过 `00 00 00`/`00 01 00`/`00 02 00` 确实是那三帧的末三字节），但注释把构造向量说成了实测向量 | `FrameRequestInfoTests.cs:345-352` |
| N-2 | `The_two_markers_are_ascii_zero_characters` 用 `body.AsSpan(body.Length - 14, 2)` 从**尾部**定位，尾部字段一变它就跟着漂（E3 里它没红就是这个原因）。整段比对兜住了它，但这条单独看不钉位置 | `GameInitialConnectionCodecTests.cs:90` |
| N-3 | `game-frame.md:45`"出站 header 未随版本改变，**见下文**与 `Docs/…`"——本页并无描述 15 字节出站头的下文，悬空引用 | `game-frame.md:45` |
| N-4 | `catalog.md:45` 把 `0x04DD` 插在 `0x0008` 与 `0x000F` 之间，破坏了该表的 type 升序 | `catalog.md:45` |
| N-5 | `ItemPathLiveTests.AnItemDraggedOverASocketLandsInTheDatabase…` 单独跑会以 `EndOfStreamException` 失败，全量跑则通过。**与本改动无关**：我在 `git archive HEAD` 出来的干净基线副本上单独跑，失败形态一模一样。属既有偶发项 | `Server/tests/Dfo.Server.Protocol.Tests/Game/ItemPathLiveTests.cs:204` |
| N-6 | 根契约 line 26 写"455 帧"，子契约 line 36 写"458 帧"。实际 268+190=458 | 根契约 |
| N-7 | `0x04DD.md:56`"请求间隔约十秒"：`packet_log.txt` 不记时间戳，这一条无法从该会话核实；它来自本仓服务端一侧的观察，而那份记录也不可解析（见 A-1/A-3）。数值大概率没错，但目前无依据 | `0x04DD.md:56` |

---

## 四、核对通过的项（明确记下来，避免被重复质疑）

### 服务架构未被改动 —— 通过

- **分层与项目引用图**：没有任何 `.csproj` 改动。
- **切片六类制品边界**：新切片 `Game/ConnectionCheck/` 只含消息 record、payload codec、
  解包器、handler、编码器五类，无命令；单文件 `*Slice.cs` 的写法与既有的
  `ContentsPlayInfoSlice.cs`、`InformNoticeSlice.cs` 一致，不是新形态。
- **handler 四类允许行为**：`ConnectionCheckHandler`（`ConnectionCheckSlice.cs:62-72`）
  只做一件事——`sender.SendCmdAsync(context.Connection.ConnectionId, …)`，
  正是 AGENTS.md line 228 的**第 4 类**（"sender 的单连接或显式收件人发送方法"），
  同时符合 line 180-182 的"没有领域状态或领域决策的纯协议交互可以直接发送"。
  它与既有的 `ContentsPlayInfoHandler` 形态相同。**不经命令总线在这里是被允许的，不是偏差。**
  AGENTS.md line 246-247 要求这类例外写进协议记录的 Implementation mapping——
  `0x04DD.md:81` 的 `Application | — | 无领域语义，不派发命令` 已经写了。
- **命令总线与事务语义**：本改动没有新增或修改任何命令、管线与事务。
- **`ProtocolModule` 唯一接线点**：三处注册全部落在 `ProtocolModule.cs`
  （`:189` handler、`:262` 解包器、`:339` 编码器），`ApplicationModule` 与传输侧未动。
- `ClientMessageType` / `ServerMessageType` 加入 `GameConnectionCheck = 0x0001_04DD`，
  两个枚举内**无取值冲突**（逐一算过）。

### 仓库约定 —— 通过

- **行尾**：`protocol/` 下四个文件（`catalog.md`、`NOTI-0x0001.md`、`game-frame.md`、`0x04DD.md`）
  **100% LF、0 CRLF**；其余 12 个改动文件（含全部 C# 与 `AGENTS.md`、`Docs/`）**100% CRLF**。
- **BOM**：全部改动的 C# 文件**无 BOM**。`protocol/catalog.md` 有 BOM，但基线上就有
  （基线 1451 份 protocol md 中有 19 份带 BOM），非本次引入。
- **`protocol/` 无凭据**：本会话 LOGIN 帧的账号 `10038` 与口令哈希
  `de509f65…8e6c` 在 `protocol/` 下**零命中**；`FrameRequestInfoTests.cs:333-337` 只收录了
  LOGIN 的 14 字节**帧头**，body 未入库，注释也点明了原因。该哈希只出现在既有的
  `ClientHack/tools/*.ps1`（本次未改动）。

### 子契约 SC-4 —— 通过

"末三字节的切分要么有证据判定，要么如实记为未判定且实现不依赖它"：本改动选了前者，
而且证据够（V2）；同时把仍未证实的那一点（高位字节是否参与计数）如实标了出来
（`game-frame.md:39-41`、`GameClientFrameRequestInfo.cs:19-23`、
`Docs/NetworkingFrameProtocol.md:135`、规格 line 111）。**这一条做得干净**，
是"构造上确定"与"统计上推断"分开陈述的正面例子。

---

## 五、要求的动作（按优先级）

1. **A-2**：删掉或改写"探测未被应答之前客户端不发登录命令"与"until it is answered"
   这两条被实测反证的因果（`0x04DD.md:72`、`ConnectionCheckSlice.cs:11-13`、
   `NetworkMessages.cs:27-28`、`game-frame.md:31`）。
2. **A-3**：把本成果依赖的实测字节按 `protocol/evidence.md` 归档进
   `protocol/patterns/data/traces/`（LOGIN body 可去除），并让每一处"实测"引用指向该路径。
3. **A-1**：补上可核实的实机记录，或把 `catalog.md:45,71,72` 的阶段降级，
   并把 `0x04DD.md:89` 与 `E-001` 改写成实际拿到的证据种类。
4. **B-1**：`Docs/NetworkingFrameProtocol.md:280,292,300` 同步到 14 字节。
5. **B-2**：改正 `NOTI-0x0001.md:79-80` 的推导（两个值都是各自枚举的 `END` 哨兵），
   并删掉 `:112` 那条不存在的未知项。
6. **B-3**：加一条把 `0x04DD` 钉在线上的用例（`Networking.Tests` 线级集成，
   或至少断言编码器产出的 type 字节 = `0x04DD`）。判据：E5 的改坏必须变红。
7. **B-4 / B-5**：`Medium` 改成词表内的等级；把 A21 客户端 SHA-256
   `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD` 写进
   承载 A21 结论的三份文档。

修完后需要重新独立评审，重点复核 A-2 的四处改写与 B-3 的新守卫（用同样的改坏实验判定）。

---

## 六、复原核对

- 全部改坏实验在临时目录下的 `baseline/` 与 `mutant/` 两份副本里进行，
  **本工作树未被写入**。
- 评审结束时 `git status --short` 与开始时逐行一致：12 项已跟踪改动 + 4 项未跟踪，
  无残留、无新增文件（除本评审文件本身）。
- 复原后重跑：`dotnet build` **0 警告 0 错误**；`dotnet test … -m:1` **1193 / 2 / 0**。

---

## 七、整改复核

第二轮，同一评审者，全新取证。基线仍为 `0d37e9a`；改动规模由 12 改 + 4 新增
变为 12 改 + **5** 新增（多出 `protocol/patterns/data/traces/a21-world-entry-20260823.log`）。
执行者自查结论一概不采信，八条发现逐条自行验证。产品代码与文档**依然一字未动**，
所有改坏实验都在临时目录的副本 `mutant2/` 里做。

**判决：`passed`。八条阻塞发现全部关闭，无 A 级残留。**

### 7.1 归档件的可解析性、忠实性与凭据（复核要求 1）

先说一句更正我自己：第一轮我从"NetworkHook 在 `plugins-disabled/`、两次开抓包的运行都
fatal 退出"推断"双向帧记录不存在"。**这个推断错了**——记录确实存在，它不是客户端侧抓包，
而是服务端侧的记录型中转日志，当时既没有入库也没有被任何文档引到，所以我找不到它。
A-1 说的"找不到"属实，要求的动作（归档并让引用可解析）也正确，但我不该顺带断定它不存在。

三个声明哈希**逐个实测比对，全部精确相符**：

| 声明位置 | 声明值 | 我算出的值 | 结果 |
|---|---|---|---|
| 归档件 line 6（A21 客户端） | `1F8244D3…B20B00DD` | `DNFClient-A21-protocol/DNF.exe` = `1f8244d3…b20b00dd` | **符** |
| 归档件 line 7（source A） | `76a57c4c…806ad2f5` | `ServerS4A21/…/bin/Debug/packet_log.txt` = 同值 | **符** |
| 归档件 line 9（source B） | `f243d216…47d36cb6` | 我按哈希全盘搜出唯一命中：`…/scratchpad/spec001-raw-frames.log`（27331 B，18:06） | **符** |

**source B 确实是本仓服务端，不是上游。** 判据两条，都是我自己解出来的：它下发的
`NOTI 0x0001` 是 `ch.11` / `127.0.0.1` / channelId `0x0B`（上游那份是 `ch.10` / `127.0.0.2` /
`0x0A`）；它的 LOGIN 响应整帧 68 字节、body 53（上游是 69 / 54，且前缀 `01 12 00 01` 对
`01 14 00 01`）——两套实现的字节不同。其通知的时间戳解出来是 `2026-08-23T09:58:23Z`
= 本地 17:58:23，与 `DNFClient-A21-protocol/ClientHack.log` 的那次运行对得上。

**`[A]`–`[F]` 六节我从 source A 全部重算，逐项复现：**

| 节 | 声明 | 重算结果 |
|---|---|---|
| `[A]` | `byte[11]`/`byte[13]` 恒零；`u16@12` 0..189 且 189/189 加一；`u16@11` 0..48384 且 0/189 | **全部一致**（48384 = 189×256） |
| `[B]` | 前 14 条 `(seq,type)` | **14 项逐项一致** |
| `[C]` | 30 条探测，seq `13,15,…,181,182` | **一致** |
| `[D]` | 15 字节帧头 + 68 字节 body | **83 个字节全部一致** |
| `[E]` | 请求 14 字节 / 响应 16 字节 + body `01` | **一致** |
| `[F]` | LOGIN 帧头 14 字节，TotalLength 87 / BodyLength 73 | **一致**，且 body 确未收录 |

**`[G]` 我做了逐帧机器比对，这是本次最有价值的一项。** 中转日志记的是 TCP 读，会把多条
协议帧并进一条记录（本会话有 4 处），所以我按每帧自报的 `TotalLength` 重新切分，再把
时刻按首帧重定基：

```text
derived protocol frames: 123  (coalesced reads: 4)   archive [G] rows: 123
mismatched rows: 0
```

**123 行对 123 行，时刻、方向、CMD/NOTI 族、type、body 长度五项全中，零处不符。**
`[G]` 不是挑出来的片段，是那条连接的完整时间线。19 个协议名我也逐个对了上游 A21 枚举
（`CmdPacketTypeA21` / `NotiPacketTypeA21`），**19/19 正确**。

**凭据：干净。** 归档件里所有长度 ≥11 的十六进制串只有 5 段，我把每段都转成 ASCII 看过：
NOTI 帧头（15）、NOTI body（68，内容是 `ch.10` 与 `127.0.0.2`）、探测请求头（14）、
探测响应头（15）、LOGIN **帧头**（14）。**没有 body，没有账号名，没有口令哈希。**
`grep -rn "de509f65\|10038\|31 30 30 33 38" protocol/` 全树**零命中**。
对照之下，原件 `spec001-raw-frames.log` 里那条 LOGIN 是**带 body 的**（`10038` 与
`de509f65…8e6c` 都在），所以"剔除 LOGIN body"这一步是真做了，不是原件本来就没有。

顺带确认 RQ-007 / AC-006 / EV-004（第一轮无证据的那条）**现在成立且我独立核实过**：
`[G]` 的 `0.00` 是 68 字节 CHANNELINFO，`0.06` 客户端就发出 LOGIN；原件里 GAME 这条腿
只有 `GAME OPEN`，**自始至终没有 CLOSE、没有 ERROR**，一直跑到 `579.484` 还在收发。
客户端不再断开、并继续发出登录命令，这件事有据了。

### 7.2 四处因果是否改干净（复核要求 2）

四处**都改对了**，而且不是删掉了事，是把被推翻的结论连同推翻它的证据一起留下：

| 位置 | 现状 | 我的核实 |
|---|---|---|
| `0x04DD.md:84-90` | 明写"此前把它写成…**那是错的，并且被本记录自己引的会话直接推翻**"，并指出当时看不到 LOGIN 的真因是 CHANNELINFO 那 8 字节 | 与 `[B]`、`[G]` 一致 |
| `ConnectionCheckSlice.cs:16-20` | "It keeps arriving whether or not it is answered…**in both observed sessions the client sends LOGIN before its first probe**" | 两份会话我都验过：source A LOGIN seq 0 / 探测 seq 13；source B LOGIN `t=0.06` / 探测 `t=1.22` |
| `NetworkMessages.cs:27-29` | "keeps coming for the life of the connection **whether or not it is answered**" | 一致 |
| `game-frame.md` | 原来那句"第一条命令是 `0x04DD`"**已删除**，改用 190 帧的长度自洽论证 | 一致 |

新增的 `E-003` 这条证据行（`0x04DD.md:70`）正是用 `[B]` 反证旧结论，方向对。

**残留一处**，见 R-1。

### 7.3 B-3 那条守卫是不是真守卫（复核要求 3）

我在 `mutant2/` 里重跑了第一轮的 E5，并追加了一次更窄的改坏：

| # | 改了什么 | 结果 |
|---|---|---|
| 基线 | 不改 | Protocol.Tests 211 通过 / 1 失败（既有偶发项 `ItemPathLiveTests`，见 N-5） |
| **M1** | `GameProtocol.cs` `ConnectionCheckType` **0x04DD → 0x04DE**（即第一轮的 E5） | **红**：`ConnectionCheckCodecTests.The_answer_carries_the_wire_type_the_probe_arrived_on` 失败 |
| M2 | 只把 `ProtocolModule` 入站解包器注册键写成字面量 `0x04DE`，**常量不动** | **全绿**（Protocol 212、Networking 51） |

**M1 变红即 B-3 关闭，这是真守卫**：它把编码器实际产出的帧头 type 与一条抄录探测帧里的
type 相比，而不是拿被测常量自己比自己。

M2 说明守卫的**覆盖边界**：它钉住的是**出站**方向。入站方向仍只靠"注册键引用同一个常量"
传递——常量本身现在被钉住了，所以现实中的"常量写错"已被覆盖；只有刻意把注册键写成
偏离常量的字面量才能绕过。这是个窄得多的口子，记为 R-6，不阻塞。

### 7.4 整改是否引入新的错误陈述（复核要求 4）

四处，全部 C 级，**没有一处改变字节、结论或证据链**：

| # | 位置 | 问题 | 我的实测 |
|---|---|---|---|
| **R-1** | `FrameRequestInfoTests.cs:59` | 仍写 "it is **the first thing** the A21 client sends on the Game endpoint"——这正是本次被撤回的那句因果，且与 `ConnectionCheckSlice.cs:19-20`（"the client sends LOGIN before its first probe"）当面矛盾 | 全量 grep 只剩这一处；其余命中都是改正后的撤回句或无关旧文 |
| **R-2** | `game-frame.md` "最短的一条是 `0x04DD`：整帧 14 字节" | 不是"一条"：source A 的 190 条入站帧里有 **40 条**并列最短（14 字节），分属 **7 个 type**（`0x002A 0x003F 0x0078 0x01BA 0x01E4 0x0286 0x04DD`） | 论证方向不受影响——证据其实比声称的更强 |
| **R-3** | `NOTI-0x0001.md` "`1271` 与 `1218` **不是"条目数"**，而是…`END` 哨兵的取值" 与 `Docs/NetworkingFrameProtocol.md:172-173` 仍写"派发表**条目数**" | 两份文档现在对同一字段的说法对不上。而且这是个**假两分**：两个枚举取值都是 `0..END` **连续**的（我验过 contiguous=True），所以 `END` 的值恒等于"除哨兵外的条目数"，两种说法是同一个数 | 更稳妥的写法是只陈述等式：`1271 = CmdPacketType.END = 除 END 外的成员数`，不必否定另一说 |
| **R-4** | `0x04DD.md:42-47`、`ConnectionCheckCodecTests.cs:49-53`、`FrameRequestInfoTests.cs:64-67` 共用的样本 `01 DD 04 0E 00 00 00 B5 9F 81 BD 00 00 00` | 标注"实测 / verbatim off the wire"，但这串字节在**两份归档源里都不存在**（`grep -c "B5 9F 81 BD"` 对 source A、source B 均为 0）。它来自未归档的修前诊断会话 | 帧的**形状**由归档件完全佐证（`[E]` 一条 + source B 里 48 条同形帧），不可解析的只有 checksum 与 seq 这两个无协议含义的逐帧值。最省事的修法：换成 `[E]` 里那条 `01 DD 04 0E 00 00 00 8A D6 51 F4 00 0D 00` |

R-4 是 A-3 的一点尾巴：`protocol/` 里仍有一处 `实测` 样本指不到归档件。因为它承载的
结论已被别的归档证据覆盖，不阻塞；但如果目标是"每条实测都能解析"，这一处该收掉。

### 7.5 八条发现逐条判定

| 发现 | 判定 | 关键证据 |
|---|---|---|
| **A-1** 实机结论无证据 | **关闭** | `catalog.md:64,71,72` 与 `0x04DD.md:15` 阶段已降为 `实现服务端功能`（其定义"已解码为 typed message 并有消费者、parser/codec 与 handler 有回归测试"确实满足）；`0x04DD.md:72-73` 主动写明"没有指令级证据，因此不能进入 `双向验证结束`"；`## Verification` 的 Real-client result 改为归档件 `[G]` 可见的成对往返，并加注"'被应答后停止重发'不成立" |
| **A-2** 因果被自证推翻 | **关闭**（残留 R-1） | 见 7.2 |
| **A-3** 引用不可解析 | **关闭**（残留 R-4） | 归档件入 `protocol/patterns/data/traces/`，三哈希实测相符，`[A]`–`[G]` 逐节复现，`[G]` 123/123 精确一致 |
| **B-1** 帧基线文档只改一半 | **关闭** | `Docs/NetworkingFrameProtocol.md:280,292,300` 三处全部跟上；残余的"13 字节"只剩三处，每处都已显式限定为 A12 |
| **B-2** 派发表大小的错误推导 | **关闭**（措辞见 R-3） | 假谜已删，Unknowns 换成"客户端拿它做什么比较（相等？不小于？）"，是个能做的实验 |
| **B-3** 无线级守卫 | **关闭** | M1 变红；覆盖边界见 R-6 |
| **B-4** `Medium` 不在词表 | **关闭** | 全部改为 `Hypothesis`；全树已无 `Medium` 命中 |
| **B-5** A21 结论绑 A12 哈希 | **关闭** | `game-frame.md`、`NOTI-0x0001.md` 人工区、`0x04DD.md` 三处都加了 `1F8244D3…B20B00DD`，且都写明"本页/机器区其余部分绑定的是 A12"——分节声明比全页改哈希更准确 |
| N-1 / N-3 / N-4 / N-6 | **关闭** | 测试注释已改为"末三字节实测、外围帧头合成"；`game-frame.md` 补上 15 字节出站头字段（与 `GameServerFrameEncoder` 及帧基线表一致）；`0x04DD` 行移到 `0x02C1` 之后（C→S 表升序恢复；S→C 表在 HEAD 上本就无序，不计） |

### 7.6 我这一轮实际跑过的

| # | 做了什么 | 结果 |
|---|---|---|
| C1 | `dotnet build Server/86JP.Server.slnx` | **0 警告 0 错误** |
| C2 | `dotnet test Server/86JP.Server.slnx -m:1`（PVF 环境变量同前） | **1194 通过 / 2 跳过 / 0 失败**（Protocol 由 211 → 212，即新增的守卫）。与执行者声称的数字一致 |
| C3 | 三个声明哈希实测比对 | 3/3 精确相符 |
| C4 | `[A]`–`[F]` 从 source A 重算 | 逐项复现 |
| C5 | `[G]` 对 source B 逐帧机器比对（按 `TotalLength` 重切分并重定基） | **123/123，零处不符** |
| C6 | `[G]` 的 19 个协议名对上游 A21 枚举 | 19/19 正确 |
| C7 | 归档件全部十六进制串转 ASCII 人工过目 + `protocol/` 全树凭据 grep | 无凭据；原件确实含凭据，剔除动作属实 |
| C8 | 改坏 M1（`ConnectionCheckType` → `0x04DE`） | **红 1**，守卫成立 |
| C9 | 改坏 M2（只改入站注册键为字面量） | 全绿 → R-6 |
| C10 | 上游两个枚举的成员数、连续性与 `END` 取值 | 1272/1219，均 `0..END` 连续，`END` = `0x04F7`/`0x04C2` |
| C11 | 17 个改动/新增文件的行尾与 BOM | `protocol/**`（含新归档件）全 LF；其余全 CRLF；C# 无 BOM。**零违规** |
| C12 | 五份文档的相对链接可达性 | **0 处断链** |
| C13 | 撤回句的全量 grep | 只剩 R-1 一处 |
| C14 | `git status --short` 有无 `.csproj` 改动 | 无 → 架构仍未被改动 |

**只读未跑**：我没有再启客户端或服务端取新证——本轮所需的实机事实已由归档件与其原件
（哈希相符）覆盖。执行前确认 7001/10011/17001/20011 无监听、无 `DNF*`/`Dfo*` 进程。

### 7.7 建议（不阻塞，不需要再评审）

R-1 与 R-4 建议合并处理：把 `FrameRequestInfoTests.cs:59` 那句删掉或限定为"修前那次
会话里它是首帧"，同时把三处共用的样本换成归档件 `[E]` 的 `…8A D6 51 F4 00 0D 00`——
一次改动同时收掉撤回残留和不可解析样本。R-3 建议只保留等式、不做否定断言，并把
`Docs/NetworkingFrameProtocol.md:172-173` 的措辞与之统一。R-2 把"最短的一条"改成
"最短的一类（40 条并列，分属 7 个 type）"。四条都是纯措辞，不涉及字节、结论与证据链，
改完不需要重新评审。

### 7.8 复原核对

- 改坏实验全在 `…/scratchpad/mutant2/` 里做，**本工作树未被写入**。
- `git status --short` 与本轮开始时逐行一致：12 项已跟踪改动 + 5 项未跟踪，
  无残留、无新增文件（除本评审文件本身）。
- 复原后重跑：`dotnet build` **0 警告 0 错误**；`dotnet test … -m:1` **1194 / 2 / 0**。
