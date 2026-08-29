---
kind: rks-change-review
decision: changes_required
---

# 变更评审：spec-001-boss-die-check

## 评审范围

只评 spec-001 的成果物，即原始委派清单里的这些文件：

- 新增 `Server/src/Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs`
- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs`
- 新增 `protocol/game/client-to-server/0x0075.md`
- 修改 `protocol/game/client-to-server/0x0045.md`（人工区）
- 修改 `Server/src/Dfo.Server.Protocol/Game/GameProtocol.cs`（只看 `BossDieCheckType` / `ScoreScrollStateType` 两个常量）
- 修改 `Server/src/Dfo.Server.Data/Networking/NetworkMessages.cs`（只看 `GameBossDieCheck` / `GameScoreScrollState` 三个成员）
- 修改 `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`（只看 BossDieCheck / ScoreScrollState 的三处注册）
- 修改 `Server/tests/Dfo.Server.Host.Tests/ChannelHostStartupTests.cs` 与 `Docs/HostStartup.md`（非本成果，但在同一次变更里，见 NB-9）

**明确排除**：评审期间同一工作树里出现的 `LeaveDungeonSlice.cs`、`TownReturnProjection.cs`、
`FloorPieces.cs`、`DungeonEntrySlice.cs`、`PvfDungeonRoomDropSource.cs`、`LeaveDungeonTests.cs`、
`GameProtocol.EplpCommandType`、`ProtocolModule` 里的 Eplp 三处注册、以及 `0x0045.md` 里
「上游的应答是两帧」之后新增的 `CMD 0x0048` 段落 —— 属其他成果，未评。

对照的判据：根契约 `finishing-and-leaving-a-run/TaskContract.md`（rev 1）、子契约
`spec-001-boss-die-check/TaskContract.md`（rev 2）、`spec.md`。

行号为**近似**（评审期间工作树在被并发修改，行号可能已漂移几行）。

---

## Blocking

### B-1 `BossDieCheckDecoder` 抛异常会掐断连接，与 AC-005「本局仍可正常结算」直接冲突

**文件**：`Server/src/Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` 约 39–44 行
（`if (body.Length < MinimumBodyLength) throw new InvalidDataException(...)`）；
配套测试 `ResultScreenTests.cs` 约 74–79 行
（`A_confirmation_too_short_to_carry_its_two_numbers_is_refused`）。

**问题**：这条抛出不是「拒绝这一帧」，是「拒绝这条连接」。
`Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs` 约 220–236 行的 `Decode` 里，
decoder 抛出的异常被记一条 `Game decoder ... threw` 之后 **原样 rethrow**，而同一段的注释
自己写明了后果：

> "Rethrown, not swallowed -- a frame this server cannot read is not one it can continue a
> session on, so the connection still drops."

同一段注释还记着这个失败模式已经真实发生过一次：A21 的 `CHANGE_TUTORIAL_FLAG` 长度判错，
「every tutorial entry ended with the client reporting a lost connection」。

于是三条判据同时不成立：

| 判据 | 原文 | 实际 |
|---|---|---|
| 子契约 SC-3 | 「帧不可解……**不会卡死**」 | 玩家掉线，本局作废 |
| spec RQ-005 | 「体不可解……**本局不卡死**」 | 同上 |
| spec AC-005 | 「被拒绝，**本局仍可正常结算**」 | 结算不了了 |

另外这与 seam 自己的契约也不一致：`Server/src/Dfo.Server.Abstractions/Networking/ClientMessageDecoding.cs`
约 14–17 行写着「a decoder signals a body it does not recognise by **returning null and never by
throwing**」。

**测试为什么没抓住**：`A_confirmation_too_short_to_carry_its_two_numbers_is_refused` 只断言
`Assert.Throws<InvalidDataException>`。它把违反 AC-005 的那个行为**锁成了绿灯**——这正是「测试
通过的理由是错的」那一类。

**修法**：`body.Length < 4` 时 `return null`。decline 路径会打一条
`Game decoder ... declined` 然后只丢这一帧，连接与 run 都活着，正好等于 AC-005 要的「被拒绝，
本局仍可正常结算」。handler 本身是纯日志，什么都不会少。测试相应改成断言 `Decode` 返回 null，
并补一条「decline 之后 run 状态不变」。（顺带：上游 `DungeonCombatHandler.HandleBossDieCheck`
在 `TryParse` 失败时也是静默 `return;`——本仓在这一点上比上游更硬，而不是更稳。）

---

### B-2 「应答的 body 写什么都行，写多长也都行」/「The byte is convention, not requirement」与本仓已确认的 CMD 结果信封相矛盾

**文件**：
- `protocol/game/client-to-server/0x0045.md` 约 79 行：
  「**所以应答的 body 写什么都行，写多长也都行；重要的是它到了。**」
- `Server/src/Dfo.Server.Protocol/Game/DungeonClear/ResultScreenSlice.cs` 约 138–144 行：
  「The byte is convention, not requirement. The client's handler for this reply reads nothing out
  of it -- there is no read primitive anywhere in its call graph -- so what carries the answer is
  its arrival.」

**问题**：本仓已经把这件事定过案，而且是花了四个缺陷定的案。
`protocol/patterns/game-receive.md` 的「CMD body 的两字节结果信封（**每一条 CMD 都有**）」：

```text
CMD body = u8 ok      ; 非 0 = 成功
         + u8 reason  ; 仅当 ok == 0 才存在
         + <载荷>
```

`protocol/patterns/character-lifecycle.md` 约 26–59 行进一步写明「首字节属于全体 CMD 共有的
结果信封，由客户端接收函数在分发之前读掉，**handler 根本看不到它**」，并列了四个由此misread
造成的缺陷，其中一条正是：「`0x0003` 退出失败发 `00` 裸码 → 第二个 `ReadU8` **吃掉下一帧首
字节** → 此后每帧错位」。

代码注释里那句推理（"the handler reads nothing out of it, so what carries the answer is its
arrival"）**就是那条被记录过的误读本身**：读这个字节的不是 handler，是分发之前的接收函数。

`game-receive.md` 绑定的是 A12 客户端哈希 `F78F0AEA…`，所以我在 A21 dump 上另行复核过：
- RVA `0x00D0E720`（S2C CMD `0x0006`）与 RVA `0x00D05CC0`（S2C CMD `0x002A`）的函数开头都是
  `cmp byte ptr [ebp+0xC], 0` / `je <退出>`，`[ebp+0xC]` 即接收函数从 body 里剥出来的 `ok`。
  **A21 同样有这条信封。**
- 上游 `CardRewardNotificationSender.SendLayoutAsync` 对 `0x0045` 发的也正是 `[01]`（1 字节），
  同样是这个 ok 字节，不是巧合。

**具体后果**：本次发出去的字节是对的（`0x01`），坏的是写下来的结论。下一个人若照这句话给
`0x0045` 补一条「拒绝」分支发 `00`，或者干脆发 0 字节 body，就会静默打乱客户端此后每一帧的
切分——不报错，只是全错。子契约的约束「结论必须写下来……下一个人不该重新推一遍」在这里被
反向满足了：写下来的是一条会把人带沟里的结论。

**修法**：两处都改成——首字节是 CMD 族共有的 `ok` 信封，**必须非零**，本仓发 `01`；并链到
`protocol/patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有`。
代码注释里「reads nothing out of it」这句要限定为「**它的 handler** 不读」，而不是「这个字节
没人读」。

---

### B-3 本次变更的核心行为没有任何测试：把 `0x0045` 的 decoder 注册到错误的 wire key，全套测试仍然全绿

**文件**：`Server/src/Dfo.Server.Protocol/ProtocolModule.cs` 约 374–375 行
（`services.AddGameCmdMessageDecoder<ScoreScrollStateDecoder>(GameProtocol.ScoreScrollStateType, ...)`）。

**实验（已做，已还原）**：只把那一行的第一个实参从 `GameProtocol.ScoreScrollStateType` 换成
`0x0099`，其余一字不动，跑 `dotnet test Server/86JP.Server.slnx`（带构建）：**七个测试程序集
0 失败**。在那个状态下服务端根本不会解码客户端的 CMD `0x0045`，本 spec 要修的缺陷原封不动，
而没有一条测试会说话。还原后重跑，仍全绿。

**为什么现有防线挡不住**：
- `ResultScreenTests.cs` 约 52–60 行的 `ObservedScrollType = 0x0045` 抄写常量，护的是
  **`GameProtocol.ScoreScrollStateType` 这个常量**——这一层确实有效（把常量改成 `0x0046` 会让
  `The_acknowledgement_carries_the_type_the_scroll_frame_arrived_on` 变红，Expected 69 / Actual 70）。
  但它护不到 `ProtocolModule` 里那一行**注册**用了哪个常量。
- `SelectCharacterAcceptedTranscriptTests.RegisteredGameClientDecoders_AreCmdOnly` 只断言
  「selector 都是 CMD」与「wire key 互不重复」，从不断言哪个 type 对应哪个 decoder。

**具体后果**：任何一次改动或合并动了那一个实参，就原样复现本 spec 要修的缺陷，且静默。
从「实机日志里 `0x0045` 未被应答」这条现象出发，**注册**才是承重的那一环，而它恰恰裸着。

**修法**：加一条从 `ProtocolModule` 构容器的测试，断言
`GameMessageDecoderRegistration(0x01, 0x0045) → ScoreScrollStateDecoder` 与
`(0x01, 0x0075) → BossDieCheckDecoder`，其中 type 用抄写字面量（理由与 `ObservedScrollType`
的注释相同），不要从 `GameProtocol` 读。

---

### B-4 `0x0075.md` 用一条关于本仓自己普查数据的错误事实，去论证「普查有盲区」

**文件**：`protocol/game/client-to-server/0x0075.md` 约 42 行：

> `0x0075` 找不到，`0x002E SET_PLAY_RESULT` 也找不到，而后者客户端确实每局都发。

**问题**：后半句是假的。

```
awk -F'\t' '$1=="C2S" && $2=="CMD" && $3=="0x002E"' protocol/patterns/data/protocol-identity.tsv
→ C2S  CMD  0x002E  1  0x01541EBF    text  0x01541EBF
```

`protocol/game/client-to-server/0x002E.md` 的「定位」表里也写着同一个构包点 `0x01541EBF`。
即：普查**找得到** `SET_PLAY_RESULT`，新记录与既有记录当场互相矛盾。

结论本身（盲区存在）不受影响——`0x0075` 自己就是证据：普查找不到它的构包点，而实机日志里
它就在那儿。坏的只是被拿来当例子的那一条，而它是这一节唯一的支撑事实。
`protocol/evidence.md` 的「错了就删」正是针对这种情况：「同时存在真假两份证据比只有真的更糟」。

**修法**：删掉 `0x002E` 那一句；盲区用 `0x0075` 自身论证即可（找不到构包点 + 实机日志里它确实
发了）。若要保留一个正例，先用同样一条 `awk` 核过再写。

---

### B-5 引了一堆 RVA 却没有客户端哈希，而 `evidence.md` 的默认「当前客户端」是**另一个**二进制

**文件**：`protocol/game/client-to-server/0x0075.md`（**全文人工区，没有机器区，因此全篇没有任何
哈希**）；`0x0045.md` 人工区（约 65–95 行）同样只写地址不写哈希，只是它的机器区恰好带了
`1F8244D3…`，情形轻一些。

**问题**：`protocol/evidence.md`「反汇编 → 客户端哈希 + 地址段」一节是加粗的：

> **只写地址段是不够的。** RVA 只在同一个二进制上稳定，而客户端会被重建。少了哈希，客户端一变
> 引用就**静默地指向不同的字节**——不报错，只是变错。

而同一页给出的「当前客户端完整哈希」是 `F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`
——按 `protocol/patterns/game-frame.md` 那是 **A12**；本次这些 RVA 属于 **A21**
（`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`）。

**具体后果**：读者按本仓默认口径去解析 `0x00D0E410` / `0x021EE7DE`，打开的是错的二进制，读到的
是不相干的字节，而且不会有任何报错提示他走错了。

**修法**：两份文档的人工区各加一行「本节地址绑定客户端 SHA-256 `1F8244D3…`（A21，
`DNFClient-A21-protocol/DNF.exe`）」。

---

## Non-blocking

### NB-1 两个字段的语义依据既没归档也无法复核，而解码与「不许照抄」的上游 parser 逐字节相同

**文件**：`ResultScreenSlice.cs` 约 14–17 行（"That much is measured … the run it belonged to had
reported actor 3817 -- `0x0EE9` -- dead moments earlier under wire user 1"）；
`ResultScreenTests.cs` 约 32–36 行（"which is what makes the two leading numbers **more than a
reading borrowed from the reference server**"）。

- 那条对应关系（3817 刚被 `DIE_MONSTER` 报死、wire user = 1）在整个仓库里**找不到第二处**：
  `protocol/patterns/data/traces/` 里有 `a21-*-2026082{3,5,6,7}`，唯独没有 08-29 那次会话；
  子契约的 Current Situation 只引了 `Unhandled Game frame` 和 `boss room 55611 ... is clear` 两行，
  两行都不含 3817。
- 两份新文档都复述了「样本按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`」，但**没有存**——
  `protocol/game/client-to-server/samples/` 里没有 `0x0075-2026-08-29-01.hex`。
- 与此同时，解码与上游 `ServerS4A21/Server/DfoServer/Network/Parsers/Dungeon/BossDieCheckRequest.cs`
  **完全一致**：同样 offset 0 / 2 两个 LE u16，同样 `body.Length < 4` 下界。而子契约 Assumptions
  写的是「解码要自己核，**不照抄上游的 `BossDieCheckRequest.TryParse`**」。

`0x0075.md` 的字段表本身很诚实（Confidence 写的是 Probable，并说明升级到 Confirmed 需要同一局
两个不同 actor 序号的差分）——问题在代码注释与测试注释把同一件事说成了 "measured" 与
「不只是照抄上游」。

**修法**：把 08-29 那次会话的相关日志行归档进 `protocol/patterns/data/traces/`、body 存进
`samples/`，然后引路径；或者把代码/测试注释降级到与文档一致的 Probable 口径。

### NB-2 「与结算界面无关（Confirmed）」把推论贴上了确认标签

**文件**：`protocol/game/client-to-server/0x0075.md` 约 92–96 行。

「实机日志里它未被应答，客户端照样发出了 `SET_PLAY_RESULT`、收下了结算帧、画完了卷轴」——这是
观察，成立。但同一段的落点「**卡住的是再往后的 `0x0045`**」是推论，而 spec 自己的 Assumptions
写着「应答 `0x0045` 之后客户端是直接收尾还是转去翻牌，**静态分析断不了**」，实机确认按子契约
rev 2 已经归还给根契约 SC-1。`(Confirmed)` 挂在整节标题上，把两者一起盖住了。

`evidence.md` 的「比假证据更阴的一类：真实观察夹带未被支撑的推论」正是这一条：
「**「我看到了什么」和「因此我认为什么」分开写**」。

**修法**：拆成两段，`(Confirmed)` 只留给观察那一半。

### NB-3 「`GIVEUP_GAME` is answered unconditionally」是错的

**文件**：`ResultScreenSlice.cs` 约 174 行；`ResultScreenTests.cs` 约 186 行
（"The same choice `GIVEUP_GAME` makes."）。

`GiveUpGameHandler`（`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`
约 129–137 行）在**没有 published session** 时是 `logger.LogWarning("... no published session,
ignoring"); return;`——**不发应答**。而「没有 published session 仍然应答」恰恰是这里要论证的那个
选择，被引来当先例的代码做的正好相反。`GIVEUP_GAME` 的「unconditional」指的是「有没有 run 都答」，
不是「有没有会话都答」。

行为本身可辩（这条应答不发奖、不读体、不改状态，确实没什么可拒的），坏的是理由。

**修法**：要么把注释改成据实说理（并说明与 `GIVEUP_GAME` 在无会话时的差异是有意的），要么
干脆照 `GiveUpGameHandler` 提前返回——真实流程里结算时必然有 session，改了不影响链路。

### NB-4 `The_confirmation_handler_cannot_answer_at_all` 自陈的局限说小了

**文件**：`ResultScreenTests.cs` 约 96–105 行，注释在约 92 行：
「Nothing in this codebase sends without that seam」。

不成立：`Server/src/Dfo.Server.Protocol/Channel/` 下有四个 handler 直接注入
`INetworkMessageSender`（`ChannelConnectHandler`、`ChannelInfoHandler`、`ChannelScriptHandler`、
`ChannelScriptVersionHandler`）。一个长出 `INetworkMessageSender` 依赖的 `BossDieCheckHandler`
会让这条断言照样绿。

这条测试本来就是签名断言而非行为断言（它自己也承认），但既然写了局限说明，说明就该是对的。

**修法**：断言「构造参数里没有任何可赋给 `IGameMessageSender` 或 `INetworkMessageSender` 的类型」，
或者改成行为断言（用容器解析 handler + 录制型 `INetworkMessageSender`，断言零帧出站）。

### NB-5 AC-003 的 Given（「本局已经结算过」）从未被构造出来

**文件**：`ResultScreenTests.cs` 约 257–290 行的 `OneRun` 夹具。

`DungeonRunState` 有 `IsSettled` 标志（`Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs`
约 22 行），而 `OneRun.Find` 永远返回默认的 `IsSettled = false`。
`The_confirmation_changes_nothing_about_the_run(1|3)` 覆盖的是**重放**，不是**已结算**。

风险很低（handler 无论如何都只 `Find` 一次并写日志，夹具对所有写操作抛 `NotSupportedException`），
属于「判据与断言对不上」的账面问题。

**修法**：夹具加一个 `IsSettled: true` 的分支再跑一遍，或在 spec 里写明这两种情形在本 handler 上
构造性等价、只保留一条。

### NB-6 同一份文档里「构包点」用了两种约定，其中一种与自己的机器区打架

**文件**：`protocol/game/client-to-server/0x0045.md`：机器区约 39 行写 `| 构包点 | 0x021EE7E5 |`，
人工区约 65 行写「构包点 `0x021EE7DE`（RVA）」。

我在 dump 上看过这一段：

```
0x021EE7DA  push 0
0x021EE7DC  push 0x45
0x021EE7DE  call 0x234ca50    ; GetWriter
0x021EE7E3  mov  ecx, eax
0x021EE7E5  call 0x234d1e0    ; MakeHeader   ← 生成器记的是这一条
```

生成器的约定是 MakeHeader 那一条（另核 `0x0025`：机器区记 `0x011527EB`，而 `0x011527E4` 才是
GetWriter）。而同一段人工区在写 `0x0046` 时用的又是 MakeHeader 约定（`0x00D0E4D1`）——即一页里
两种约定并存，其中一种和自己上面五行的机器区数值对不上。

**修法**：人工区改用 `0x021EE7E5`，或明写「此处给的是 GetWriter 调用点」。

### NB-7 「整个调用图里没有任何取字段原语调用」在这里静态不可判定

**文件**：`protocol/game/client-to-server/0x0045.md` 约 67 行。

handler `0x00D0E410` 内部有一次间接调用：

```
0x00D0E4BF  mov eax, dword ptr [edx + 0xC]
0x00D0E4C2  push 1
0x00D0E4C4  call eax          ← 虚调用
```

有虚调用就走不完调用图。`evidence.md` 自己也写着这类否定判断「**这是否证，不是证明**」。
我实际核到的、也足够支撑结论的是更窄的那句：**handler 自身的函数体内没有任何
`ReadU8`/`ReadU16`/`ReadU32`/`ReadBytes` 调用**（`0x00D0E410`–`0x00D0E507` 逐条看过）。

**修法**：把「整个调用图」收窄成「handler 自身函数体」，并注明存在一处虚调用故不作全图断言。

### NB-8 引用了一份上游 `bin/Debug` 里的易失日志，既无路径也无哈希

**文件**：`protocol/game/client-to-server/0x0045.md` 约 93 行：
「一次完整通关的上游抓包（`packet_log.upstream-killexp-20260827.txt` 第 748 行起）」。

该文件实际位于 `/d/project/DNF/ServerS4A21/Server/DfoServer/bin/Debug/packet_log.upstream-killexp-20260827.txt`
——上游服务端的构建输出目录，每跑一局就被覆盖。`evidence.md`：「**日志会被下一次运行覆盖**，
所以要先归档再引用——否则引用在下一次跑客户端之后就静默失效」，归档去处是
`patterns/data/traces/`，且要连源文件哈希一起写进产物头部。

**修法**：归档到 `protocol/patterns/data/traces/` 并带哈希，再引路径 + 行号。

### NB-9 变更里夹带了两处与本成果无关的改动

**文件**：`Server/tests/Dfo.Server.Host.Tests/ChannelHostStartupTests.cs` 约 105–134 行
（30 秒 → 3 分钟，并在超时时打印 host 已有输出）；`Docs/HostStartup.md` 约 42–55 行（待办说明）。

两处本身我核过、都对：`ChannelScriptStartupService` 确实是 `IHostedService`
（`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs` 约 247 行），`Script.pvf` 确实
72,894,573 字节 ≈ 70 MB。问题只在归属：子契约 Scope/In 四条里没有它，Scope/Out 也没提它，
根契约的 Constraints 同样不覆盖。它是另一件事的成果，不该混在 spec-001 的验收里。

**修法**：单独成一次改动，或在子契约里补一条 Scope 说明为什么它必须跟着走。

### NB-10 报出来的测试证据是陈旧的（1595 不含新增的 11 条）

`ResultScreenTests` 恰好是 11 条用例（含 `[Theory]` 的两行）。我在本工作树里第一次跑
`dotnet build … && dotnet test … --no-build` 得到 `Dfo.Server.Protocol.Tests = 343`，而实际重编
之后是 `354`：**343 + 11 = 354**，即那次 `--no-build` 跑的是不含新测试的旧程序集。
我另外复现过一次同样的陈旧：一处确实会让 `The_acknowledgement_carries_the_type_the_scroll_frame_arrived_on`
变红的常量改动，在 `--no-build` 下照样报全绿，单独重跑该测试才失败（Expected 69 / Actual 70）。

也就是说，作为 SC-5 证据的那份 `dotnet test` 输出，很可能根本没有执行过本成果新增的任何一条测试。

**修法**：SC-5 的证据用 `dotnet test`（让它自己构建）重跑一次，并核对
`Dfo.Server.Protocol.Tests` 的用例数确实包含了新增的 11 条。

---

## 我独立复核了什么

| 主张 | 结论 | 依据 |
|---|---|---|
| 「A21 客户端为 671 个 CMD type 注册了应答 handler，`0x0075` 不在其中，表从 `0x0071` 直接跳到 `0x0076`」 | **成立** | `protocol/patterns/data/protocol-identity.tsv` 筛 `S2C/CMD` 恰好 671 行；`0x0075` 零行；相邻序列 `… 0x006F 0x0071 0x0076 0x0077 …` 与文档一字不差 |
| 上一条不是「普查漏了」 | **成立** | 我把注册辅助函数 `0x0123C4A0` 的全部 943 个 `E8` 调用点逐个反解出 type 立即数：唯一压 `0x75` 的站点是 `0x00D9AA3C`，其 handler VA `0x2231720`（RVA `0x01E31720`）正是 TSV 里 **NOTI** `0x0075 GUILD_WAR_START` 的 primary。CMD 表里确实没有 `0x0075` |
| 「`0x0045` 的应答 handler 在 `0x00D0E410`，不读任何字段，条件满足时构包 CMD `0x0046`（0 字节）发出」 | **成立** | 逐条反汇编 `0x00D0E410`–`0x00D0E507`：无 `0x23C5230/52A0/5310/5380` 调用；`0x00D0E4C6 push 0 / 0x00D0E4C8 push 0x46 / 0x00D0E4CA call GetWriter / 0x00D0E4D1 call MakeHeader / 0x00D0E4D6 call Finalize` |
| 文档列的五个分支条件与两条出口 | **成立**（见 NB-6/NB-7 的口径问题） | `0x0131C570 = lea eax,[ecx+0x140]`（故 `[obj+0x174]` = 反汇编里的 `[eax+0x34]`，`[obj+0x698]` = `cmp byte [eax+0x558],1`）；`0x01F00CE0 = mov eax,[ecx+0x8FA0]`、`0x01F00C00 = mov eax,[ecx+0x8F68]`，与文档的 `->[0x8FA0]` / `->[0x8F68]` 对上 |
| 「客户端构包 `0x0045` 的条件是 `[esi+0x658] == 0`，body 长度 0」 | **成立** | `0x021EE7D1 cmp byte [esi+0x658],0 / jne / push 0 / push 0x45 / call GetWriter` |
| 捕获体的抄写（子契约 Assumptions / `0x0075.md` / 测试数组三处） | **三处逐字节相同，均 39 字节**；`u16@0 = 1`，`u16@2 = 0x0EE9 = 3817` | 直接做字节比对 |
| A21 上是否存在 CMD 两字节结果信封（B-2 的前提） | **存在** | `0x00D0E720`（CMD `0x0006`）与 `0x00D05CC0`（CMD `0x002A`）两个 handler 开头都是 `cmp byte ptr [ebp+0xC],0 / je <退出>` |
| 上游 `CardRewardNotificationSender.SendLayoutAsync` 对 `0x0045` 发 1 字节 `01` | **成立** | `ServerS4A21/Server/DfoServer/Network/Handlers/Dungeon/CardRewardNotificationSender.cs` 约 29–37 行 |
| 上游确实为 `0x0045` 注册了处理器 | **成立** | `GameProtocolHandler.cs:664` `d[0x0045] = _dungeonHandler.Handle_CARD_START_REQUEST` |
| 上游 `BossDieCheckRequest.TryParse` 的读法 | **与本仓解码逐字段相同**（见 NB-1） | `Network/Parsers/Dungeon/BossDieCheckRequest.cs` |
| 枚举与常量有没有撞号 | **没有** | `ClientMessageType` 61 个成员、`ServerMessageType` 53 个成员，各自无重复值；`GameProtocol` 79 个 `ushort` 常量里，新增的 `0x0075`/`0x0045` 不与任何既有常量同值（既有 9 组同值全是 CMD/NOTI 分属两族的既定情形） |
| 三处注册齐不齐、会不会重复 | **齐、不重复** | handler / decoder / encoder 各一处；`GameMessageDecoderRegistry` 与 `MessageHandlerRegistry` 在建容器时分别做重复 wire key 与「produce 了却没 handler」的检查，均通过 |
| 错 wire key 会不会被测试抓住 | **不会**（B-3） | 把 `ScoreScrollStateDecoder` 注册到 `0x0099`，`dotnet test Server/86JP.Server.slnx` 七个程序集 0 失败；已还原并重跑全绿 |
| 常量本身有没有被护住 | **有** | 把 `GameProtocol.ScoreScrollStateType` 改成 `0x0046`，`The_acknowledgement_carries_the_type_the_scroll_frame_arrived_on` 变红（Expected 69 / Actual 70）；已还原 |
| decoder 抛异常会怎样 | **连接掉线**（B-1） | `NetworkPlugins.cs` 约 220–236 行 log 后 `throw;`，其注释自陈「the connection still drops」 |
| `0x002E` 在不在 C2S 普查里 | **在**（B-4） | TSV 有 `C2S CMD 0x002E … 0x01541EBF`，`0x002E.md` 记同一地址 |
| `NOTI 0x0073` 交叉引用 | **正确** | `protocol/game/server-to-client/NOTI-0x0073.md` 确实叫 `BOSS_DIE_CHECK`（另有 `NOTI-0x0075 = GUILD_WAR_START`，与本帧同号不同族，文档没有混淆） |
| 根契约「已验证的线级字节不得改变」 | **未违反** | 三份源文件的改动全是新增：两个常量、三个枚举成员、三处注册，无一处触及既有帧的编解码 |
| 构建与测试 | **0 警告 0 错误**；还原后全套绿（Protocol 357 / Networking 51 / Pvf 60 / Host 4 / Application 737 / Infrastructure 160+2 skip / Persistence 240） | 见 NB-10 关于计数漂移的说明 |

## 我没能复核的

1. **实机行为。** 应答 `0x0045` 之后修理商与回城入口是否真的出现——这是根契约 SC-1，子契约
   rev 2 已经明确把它归还给根，本次评审也不代它下结论。同样无法断定的是应答之后客户端走
   收尾还是走翻牌（`[obj+0x174]` 查的是哪张表未定，文档已如实标注）。
2. **2026-08-29 那次会话本身。** 日志与 39 字节样本都没有归档进仓库，所以「3817 刚被报死」
   「wire user = 1」这两条对应关系我只能读到转述，无法核对（NB-1）。
3. **`0x0075` 的客户端构包点。** 普查找不到，我也没有去 dump 里把它挖出来，因此 body 后 35 字节
   的含义、以及前 4 字节的语义，都只有「值恰好对得上」这一层证据，不是构造上确定的。
4. **A21 接收函数里信封读取的确切 RVA。** `game-receive.md` 给的地址属 A12 客户端，在 A21 上
   对不上；我改用 handler 侧证据（`[ebp+0xC]` 门控）确认了信封在 A21 同样存在，但没有把 A21 的
   接收函数本身定位出来。这不影响 B-2 的结论。
5. **`Docs/HostStartup.md` 里「排在此前每一个 hosted service 之后」这一句的顺序断言**——只核到
   `ChannelScriptStartupService` 确是 `IHostedService`、`Script.pvf` 确是 ~70MB，没有去核注册顺序。
   属 NB-9 那件另案。

## 一条评审完整性说明

评审进行期间，本工作树被并发写入了 spec-002 / spec-003 的成果（`LeaveDungeonSlice.cs`、
`TownReturnProjection.cs`、`FloorPieces.cs` 等）。B-3 的两次变异实验因此是在共享树上做的：我改动
的只有 `ProtocolModule.cs` 与 `GameProtocol.cs` 各一处，均从改动前的备份逐字节还原，还原后
`dotnet build` 0 警告 0 错误、全套测试通过，且还原后的 `ProtocolModule.cs` 仍包含 spec-002 的
Eplp 三处注册。spec-001 的四份成果文件（`ResultScreenSlice.cs`、`ResultScreenTests.cs`、
`0x0075.md`、`0x0045.md`）我自始至终没有修改过。

另外提请注意（非本次判据）：**根契约的表格把 `CMD 0x0045` 记成 `CARD_START_REQUEST`**，而上游
`PacketTypesA21.cs:1300` 与本次文档都作 `SCORE_SCROLL_STATE`（`CARD_START_REQUEST` 是上游
**处理器**的名字，不是 type 名）。本次成果订正了它但没回头修根契约，后面三个成果仍会读到那一行。
