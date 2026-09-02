# TASK-13 — 补上契约点名却没产出的两份证据

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

验证阶段判 `manual_required`：十三条成功条件里十二条有当前证据，两处欠账都是
**契约 Required evidence 一栏点名要、而至今没产出**的东西。本 Task 补上它们。

- Referenced spec requirements: RQ-002（AC-003 / EV-002）、RQ-001 与 RQ-003（AC-001 / AC-004 / EV-001）
- Referenced plan sections: `plan.md` 的 T-003 与 T-004
- Owned boundary: 仅判据；不改产品代码
- Permitted files or interfaces:

- `Server/tests/` 下与翻牌相关的判据文件
- **不改任何 `Server/src` 下的文件**。若补判据必须改产品代码，**停下来写阻塞报告**。

- Dependencies: TASK-09, TASK-10, TASK-11, TASK-12
- Required dependency outputs: 全部十二个 Task 的产出
- Acceptance conditions:

1. **SC-3 的「重连」一支要有判据。** 契约 Required evidence 逐字要求
   「同一局重复请求与**重连后再问**，同一牌位得到同一件东西的判据」，
   `spec.md` 的 AC-003 也是已批准的验收场景。
   现状：重问、时间推移两支有判据（并被变异 V9 钉住），**重连一支只有结构推论**——
   `End` 全仓恰三个调用点（`DungeonClearSlice.cs:159` / `LeaveDungeonSlice.cs:119` /
   `DungeonSelectionSlice.cs:202`）无一在断线路径，断线观察者 `OnlineUserConnectionObserver`
   不依赖 `IDungeonRunDomainService`。
   **推论很可能为真，但谁日后给断线路径加一句 `runs.End`，SC-3 会静默失效而全量仍绿。**
   判据要走**真实的断线路径**，不是把结论抄成断言。
2. **SC-1 的「与已实证抓包一致」要真的比对过抓包。** 验证员只验了「判据的期望值与代码一致、
   且断言在生效（变异 V7 会红）」，**没有比对抓包原文**。
   去读本课题的取证记录（`card-reward-evidence.md` 一类），把三个应答的期望字节
   与记录里的实测字节逐段对照，**结论如实写**——对得上就写对得上，
   对不上或记录不足以支撑就照实说，**不要为了让 SC-1 成立而调整判据**。
3. 全量判据不低于 **1963 通过 / 0 失败 / 2 跳过**；新增用例要如实计入并对平。

- Task-specific authorization: 根契约 revision 4 覆盖本任务。合并、推送、建 PR、删分支或清理 worktree 不在其内。
- Required methods:

- `test-driven-development`：新判据要**先能红**——重连那条，先确认它在「断线时 End 掉本局」
  的假想实现下会红，否则它没钉住任何东西。
- 变异五步：先记 sha256 → mtime 前进 → 编译失败不算变红 → 还原后用哈希校验 → 每条做完立刻还原。
- 行尾 LF（按字节数核 ``）。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-033 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 不低于 1963 通过、0 失败、2 跳过；新增数对平 |
| CK-034 | `Non-command:` 重连判据在「断线即 End」的变异下变红 | 变红，且是判据红不是编译失败 |
| CK-035 | `Non-command:` 三个应答的期望字节与取证记录逐段对照 | 逐段结论如实记录 |

- Required evidence: 三条检查的输出；重连判据先红的原始输出；抓包对照表。

## 可用基线

`scratchpad/baseline-approved`（69 文件，评审 approved 后的交付态，可逐字节比对）。

## Execution Result — round 1

### 起点核对

- 工作区与 `scratchpad/baseline-approved` 的 **69 个文件逐字节相同**（`cmp` 全数无差异）。
  也就是说下面这些是压在评审 `approved` 的那个交付态上跑出来的。
- CK-033 的**起点**（未加任何新判据时）：退出码 **0**，
  Protocol 479 / Networking 51 / Pvf 60 / Host 4 / Persistence 248 /
  Infrastructure 215（+2 跳过）/ Application 906 = **1963 通过 / 0 失败 / 2 跳过**。

### CK-035 — 三个应答的期望字节 × 取证记录，逐段对照

**取证记录用了哪几份。** 课题的取证汇总是
`.rks-agent/tasks/finishing-and-leaving-a-run/card-reward-evidence.md`（第二节「协议那一半已经定死」），
协议侧是 `protocol/game/client-to-server/0x0045.md` / `0x0046.md` / `0x0047.md`。
这三份都是**转述**。它们转述的抓包原文我这次找到并直接读了：
`E:/Project/DNF/ServerS4A21/Server/DfoServer/bin/Debug/` 下 9 份归档的上游逐帧日志
（`packet_log.txt`、`packet_log.upstream-killexp-20260827.txt`、
`packet_log.upstream-tutorial-20260827.txt`、`packet_log.reference-20260823.txt`、
`packet_log.before-finishquest-20260827-2019.txt` 等）。按 `protocol/evidence.md` 的效力表，
这是**一级「服务端逐帧发送日志」**，比文档里的转述高两级。

全语料里这三个 type 的帧数与**去重**后的取值：

| 帧 | 条数 | 去重后 |
|---|---:|---|
| `SEND CMD 0x0045` | 13 | **只有一种**：`(1B) [01]` |
| `SEND CMD 0x0046` | 13 | **只有一种**：`(17B) [01 01 00 FF FF FF FF FF FF FF FF FF FF FF FF FF FF]` |
| `RECV CMD 0x0047` | 14 | 两种：`(2B) [00 00]` ×10、`(2B) [01 00]` ×4 |
| `SEND CMD 0x0047` | 17 | `(33B)` ×10 全同；`(49B)` ×7（`c=2` 带两对） |

#### 应答一：`CMD 0x0045` 的确认

| | 值 |
|---|---|
| 判据期望 | `ResultScreenTests.The_acknowledgement_carries_the_type_the_scroll_frame_arrived_on`：`Assert.Equal(new byte[] { 0x01 }, frame[HeaderLength..])` |
| 抓包实测 | 13/13 帧 `body=(1B) [01]` |
| 结论 | **逐字节对得上，且抓包无第二种取值。** |

#### 应答二：`CMD 0x0046` 的版式

| | 值 |
|---|---|
| 判据期望 | `CardRewardTests.CapturedLayout` = `01 01 00 FF×14`（17 字节），由 `The_layout_is_the_body_the_capture_carried` 整段比对 |
| 抓包实测 | 13/13 帧 `01 01 00 FF FF FF FF FF FF FF FF FF FF FF FF FF FF` |
| 结论 | **逐字节对得上，且抓包无第二种取值。** |

#### 应答三：`CMD 0x0047` 的翻牌答复

这一条要分段说，因为**本仓是有意与上游不同的**（四个牌位各报各的序号，上游只填第 0 条）。

| 段 | 判据期望 | 抓包实测 | 判 |
|---|---|---|---|
| 首字节 | `0x01` | 17/17 帧首字节 `01` | **对得上** |
| 空脸总长 | `CapturedEmptyAnswerLength = 33` | 10/10 空脸帧 `(33B)` | **对得上** |
| 记录条数 | 8 条 | 33 = 1 + 8×4 | **对得上** |
| 后四条 | `FF FF FF FF` ×4 | 全部 17 帧的末 16 字节均为 `FF`×16 | **对得上** |
| 带货记录的语法 | `a,b,c` + `c×(u32 id, u32 count)` + 尾 `u8`；id/count 小端 | 7 帧 `c=02`：`FF 00 02 \| 00000000 00000000 \| 8F900000 01000000 \| 00`，其后紧接第 1 条 `FF FF 00 00` | **对得上**：变长记录的语法、小端 u32、以及对之后那个尾字节，都被 `c=2` 的实测帧钉死了 |
| 第 1–3 条的取值 | 本仓 `00 <序号> 00 00` | 上游 `FF FF 00 00` | **不同——有意的**，见 `0x0047.md`「本仓与上游的差别」 |
| 第 0 条的 `b` | 本仓写牌位序号（空脸时 `00`） | 上游 `00`（有过付费翻牌时）或 `FF`（没有时） | **抓包分不开这两种读法**：上游只往牌位 0 填货，两种读法在这一格上给的都是 `00`。契约把「`b` 是牌位序号」列为**未实证的假设**，这次比对**没有推翻它，也没有证实它** |
| 第 0 条的 `a` | 本仓：免费 `0x00`、付费 `0x01`（`Each_record_names_the_face_it_belongs_to`） | 免费面 `00`（4 帧）；付费面 `FF`（3 帧） | 免费面**对得上**；**付费面的 `0x01` 在整个语料里一次都没出现过**。这是本仓自己的读法，不是抓包支持的值 |
| 一件货写几对 | 本仓只写物品那一对（`c=1`，一件货时 41 字节） | 上游恒写两对（先一对 `(0,0)`、再物品对），`c=2`、49 字节 | **不同**。`0x0047.md` 已写明「本仓此处不发金币，`id == -1` 的记录一条也不写」；抓包进一步显示上游即使金币为零也照写那一对 |

**结论（如实）**：

1. **`0x0045` 与 `0x0046` 两个应答，判据的期望字节与抓包原文逐字节相同，且抓包无第二种取值。**
   这两条的 SC-1「与已实证抓包一致」**成立，且现在是抓包一级证据，不再只是转述**。
2. **`0x0047` 的应答，"字节形状"一致、部分取值有意不同。** 一致的是：首字节、空脸 33 字节、
   8 条记录、后四条 `FF FF FF FF`、带货记录的 `a/b/c + 小端 (id,count) 对 + 尾字节`语法。
   不同的三处（第 1–3 条的取值、付费面的 `a=0x01`、一件货只写一对）**都是已登记的有意偏离**，
   其中 **`a=0x01` 这一个取值在抓包里没有任何支持**——它按本仓自己的读法写，与契约
   Assumptions 里「付费牌那一条没有抓包互证」相符。
3. **我没有为了让 SC-1 成立而改动任何期望值。** 上面第 2 条里那三处不同，就是原样报告的结果。

#### 顺带查出的两件与契约 Assumptions 有关的事（未改任何判据）

- **契约写「黄金牌的请求形状未实证」，现在有实测了，而且与本仓的读法一致。**
  `packet_log.txt`（上游服务端 2026-08-31 12:19 起的一次真实客户端会话）里有
  **4 帧 `RECV CMD 0x0047 len=16 body=(2B) [01 00]`**，即牌型 1、牌位 0、**两字节**。
  全语料 14 帧 `RECV 0x0047` **没有一帧是一字节**。这与本仓解包器
  （`A_paid_flip_names_the_position_it_clicked` 读 `[0x01, position]`）一致，
  并与「付费走 `0x00D10DFF` 那个只写一字节的构包点」这一静态推断**相左**。
  **这是加强，不是削弱**，因此我没有动任何判据；处置（是否更新契约 Assumptions 与
  `0x0047.md`）留给契约所有者。
- **`a == 0xFF` 那一条的读法。** 3 帧付费应答的第 0 条是 `a=FF` 却**照样带着两对内容**，
  其后客户端确实收到了 `NOTI 0x000E`。这支持「`a==0xFF` 只是那个子调用提前返回」，
  而不是「整条记录被跳过」。本仓从不往 `a` 写 `0xFF`，所以这一点不影响任何判据。


### CK-034 与 SC-3 的「重连」判据

**新判据：** `Server/tests/Dfo.Server.Networking.Tests/Integration/CardRewardReconnectIntegrationTests.cs`
的 `A_clears_cards_survive_the_connection_that_was_shown_them`（1 个用例）。

**为什么放在这里。** 本仓已有的真进程/真 socket 机制就在
`Server/tests/Dfo.Server.Networking.Tests/Integration/` 下
（`GameExitIntegrationTests`、`GameLoginIntegrationTests`）：起一个真 `Host`、装齐
Infrastructure/Persistence/Application/Networking/Protocol 五个模块、开真监听口、用真
`TcpClient` 连上去，再从 `IGameTcpService.GetClients()` 反查连接号。新判据照这个现成写法来，
没有另造机制。

**它走的是真实的断线路径，不是把结论抄成断言。** 顺序是：

1. 真 socket 连上 → 拿到真连接号 `first`；把这条连接的 `SelectedSessionIdentity` 登进
   **产品自己的** `ISelectedSessionDirectory`（断线观察者就是从这里查「刚断的是谁」）。
2. 用产品自己的 `IDungeonRunDomainService`（DI 里的那个单例）`Begin` → `TrySettle` →
   `FixClearRewardCards`，两面各四个牌位，**有货与空位交错**、两面的空位错开、
   四件货的 id/数量/稀有度/耐久互不相同。
3. 把断前的两面按**值**（不是对象引用）拍成快照，连同 `PaidFlipCost` 一起。
4. **关掉 socket**，然后**等**到服务端确实处理完这次断线——
   `!onlineUsers.TryGetByConnection(first, out _)`，这是
   `OnlineUserConnectionObserver.DisconnectedAsync` 的**最后一句**做的事；再等
   `GetClients()` 清空。**这个等待和断言一样承重**：不等就问，等于问了一个还没拆完的连接，
   那样即使服务端真的断线即 End，判据照样绿。
5. 再开一条**新的** socket，断言新连接号与旧的不同，为同一角色登一条新会话（这就是「重连」）。
6. 断言：本局还在、两面逐位与快照**逐值相同**、`PaidFlipCost` 相同；
   再**逐个牌位真的去领**（`TryClaimClearRewardCard`），断言领到的东西就是断线前那个牌位
   声明的那一件（id/数量/稀有度/耐久四项全比）。第 6 步才是
   AC-003「每个牌位给出的东西与断开之前完全相同」的字面。

**CK-034 —— 先让它红（原始输出）。** 变异纪律：先记 sha256
`be4bab6b39930cc7e52116f86913d946c35d624132e89ef77a1db2594e36fe06`
（`Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs`），
施加后 mtime 由 `2026-08-31 20:58:16` 前进到 `2026-09-02 10:29:19`。

变异内容 = **「断线即 End 掉本局」的假想实现**：给 `OnlineUserConnectionObserver` 加一个
末位带默认值的 `IDungeonRunDomainService? runs = null`（带默认值是为了让另外两处**手工构造**
该观察者的既有判据仍然编译得过——`TreatWarningsAsErrors` 下编译失败不算变红），
并在 `DisconnectedAsync` 里 `if (selected is not null)` 分支的第一句写 `runs?.End(selected.CharacterId);`。

```text
  Dfo.Server.Protocol -> ...\Dfo.Server.Protocol.dll
  Dfo.Server.Networking.Tests -> ...\Dfo.Server.Networking.Tests.dll
总共 1 个测试文件与指定模式相匹配。
[xUnit.net 00:00:00.46]     Dfo.Server.Networking.Tests.Integration.CardRewardReconnectIntegrationTests.A_clears_cards_survive_the_connection_that_was_shown_them [FAIL]
  失败 ...A_clears_cards_survive_the_connection_that_was_shown_them [264 ms]
  错误消息:
   Assert.NotNull() Failure: Value is null
  堆栈跟踪:
     at ...CardRewardReconnectIntegrationTests.Snapshot(IDungeonRunDomainService runs) in ...\CardRewardReconnectIntegrationTests.cs:line 192
   at ...CardRewardReconnectIntegrationTests.A_clears_cards_survive_the_connection_that_was_shown_them() in ...\CardRewardReconnectIntegrationTests.cs:line 154

失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1，持续时间: 275 ms
```

三件事要点出来：

- **是判据红，不是编译失败。** 八个项目全部 `->` 输出成功，失败发生在测试执行期，
  报的是 `Assert.NotNull() Failure`。
- **红在第 154 行**，即**重连之后**那次 `Assert.Equal(shown, Snapshot(runs))`；
  断线前的那次 `Snapshot`（第 137 行）是绿的。也就是说红的正是「重连后再问」这一支。
- **顺带证明了这条判据真的走进了断线路径的最深一支。** 变异只写在
  `if (selected is not null)` 里面；它能让判据变红，说明真实断线时那个分支**确实被执行了**
  ——不是靠推理说「它会执行」。

还原：`cp` 回 pristine 后 `sha256sum -c` 报 **OK**（同一串
`be4bab6b…`），mtime `2026-09-02 10:29:48`。做完立刻还原，没有攒。

**稳定性**：还原后单独重跑该用例三次，三次全绿（361 / 363 / 380 ms）。

### CK-033 — 全量

还原变异后，`scratchpad/baseline-approved` 的 **69 个文件与工作区再次逐字节相同**（`cmp` 全数无差异）。

```text
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
EXIT=0
Pvf 60 / Networking 52 / Protocol 479 / Host 4 / Infrastructure 215（+2 跳过）/ Application 906 / Persistence 248
```

**1964 通过 / 0 失败 / 2 跳过。**

**增减对平**：起点 1963 → 现在 1964，**+1**；新增的正好是 1 个用例
（Networking 由 51 → 52，其余六个项目一个不动）。两条跳过仍是原来那两条
`Skip.If(ContentArchive.Available, …)`，档案确实加载了。

## Executor Self-check — round 1

### 判据数字与增减对平

| | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| 起点（未加新判据，工作区 = baseline-approved） | 1963 | 0 | 2 |
| 交付（加了 1 条重连判据） | **1964** | **0** | **2** |

差 **+1**，与新增用例数 **1** 相等，且只落在 Networking 一个项目上（51 → 52）。
不低于契约要求的 1963 / 0 / 2。

### 重连判据先红的原始输出

见上面 CK-034 一节，逐字抄的是 `dotnet test` 的输出：`Assert.NotNull() Failure: Value is null`，
`CardRewardReconnectIntegrationTests.cs:line 192`（由 `line 154` 调入），
`失败: 1，通过: 0`。八个项目全部编译成功，所以**不是编译失败冒充变红**。

### 抓包逐段对照的结论

- **`CMD 0x0045`、`CMD 0x0046` 两个应答：判据期望字节与抓包原文逐字节相同**，
  且 13 帧语料里各自**只有一种取值**，没有反例。这两条现在压的是一级证据（服务端逐帧发送日志），
  不再只是文档转述。
- **`CMD 0x0047` 的应答：形状一致，三处取值有意不同。** 一致的是首字节、空脸 33 字节、
  8 条记录、后四条 `FF FF FF FF`、以及带货记录的 `a/b/c + 小端 (u32 id, u32 count) 对 + 尾字节`
  语法（被 7 帧 `c=2` 的实测帧钉死）。不同的是：第 1–3 条的取值、付费面的 `a=0x01`、
  一件货只写一对。**其中 `a=0x01` 在整个抓包语料里一次都没出现过**——它是本仓自己的读法。
- **我一个期望值都没改。** 上面这三处不同是原样报告，不是把判据往结论上凑。

### 未查明的东西

- **`b` 是「牌位序号」还是「付费面是否被选中」，抓包分不开。** 上游只往牌位 0 填货，
  两种读法在实测的每一帧上给出的都是同一个字节。契约把它列为未实证的假设，
  **这次比对既没证实也没推翻**。要分开只能实机（给牌位 2 填一对，看第 3 张是否亮起）。
- **付费面 `a` 该写什么，没有证据。** 抓包里上游在付费应答的第 0 条写 `FF`；
  本仓写 `0x01`。哪一个是客户端要的，静态与抓包都答不了。
- **上游为什么恒写两对**（先一对 `(0,0)` 再物品对），第一对到底是不是金币位，没查明。
  一帧里那个 `id=0x06CB8082` 的取值也没去核它对应哪件物品。
- **`packet_log.txt` 里那次会话是不是真实客户端驱动的**，我用「`E:/Project/DNF` 下没有任何
  `.py` 探针引用 `0x47/0x0047`」加上会话里有完整登录/选人/副本帧来判断是真实客户端；
  没有更直接的凭据。

### 我不确定的地方

- **新判据把会话直接登进 `ISelectedSessionDirectory`，而不是走完整的登录 + 选人上线流程。**
  断线是真的（真 socket 关闭 + 真观察者跑完），会话登记是夹具。理由写在用例注释里：
  完整流程要种一份角色名单，且证明不了本判据要证明的东西。**代价是**：如果日后有人把
  `runs.End` 加在**登录/选人流程本身**产生的某个额外观察者里，而不是加在断线路径上，
  这条判据不一定盖得住。我认为这不在 SC-3 的射程内，但把它写在这里由人来判。
- **变异用了「末位带默认值的构造器参数」**，因为直接加必填参数会让另外两处手工构造
  `OnlineUserConnectionObserver` 的既有判据编译失败（那样就成了「编译失败冒充变红」）。
  带默认值这一点让变异比真实改动略微人工，但注入是真的（DI 解析到已注册的单例，
  否则 `runs?.End` 会因为 `runs == null` 而什么也不做、判据就不会红——它红了，
  所以注入确实发生了）。
- **`packet_log.txt` 里 4 帧两字节的付费请求 `[01 00]`，与契约 Assumptions 里
  「付费走另一个只写一字节的构包点、形状未实证」相左。** 它**加强**本仓的读法，
  所以我没有动任何判据、也没有改契约（契约一字不动）。是否据此更新契约 Assumptions
  与 `protocol/game/client-to-server/0x0047.md`，留给契约所有者。
- **我没有让重连后的「再问」走完整的 `CMD 0x0047` 线上帧**（新判据在领域缝上问）。
  走线上帧需要再补一份已发布的运行期会话夹具，风险与收益不成比例；而 `0x0047` 这一帧
  本身的字节形状已由 `CardRewardTests` 逐字节钉住。**这是一个我主动划的边界，写在这里备查。**

### 边界

- **没有改任何 `Server/src` 下的文件。** 唯一一次触碰是 CK-034 的变异，做完立刻还原，
  `sha256sum -c` 报 OK；`scratchpad/baseline-approved` 的 69 个文件与工作区逐字节相同
  （施加前、还原后各核一次）。
- 新增文件恰一个：`Server/tests/Dfo.Server.Networking.Tests/Integration/CardRewardReconnectIntegrationTests.cs`
  （13154 字节，CR=0 / LF=291，行尾全 LF）。
- 契约区（本文件 `## Task Unit Contract` 一节）**一字未动**。

## Independent Quick Review — round 1

**Verdict: `passed`。**

我是独立评审员，不继承执行者的结论。下面每一条都是我自己跑出来的；执行者的原文我只当作
「待验证的主张」读。用到的路径一律写全。

### 1. 越界与基线 — 一个文件都没多动

| 检查 | 我怎么做的 | 结果 |
|---|---|---|
| 69 个基线文件逐字节 | 对 `scratchpad/baseline-approved` 下每个文件 `cmp -s` 对工作区同名文件 | **0 处不同**（变异前、每次还原后、全部做完后共核 **3** 次） |
| 有没有新增或漏报的文件 | `git status --porcelain --untracked-files=all` 共 95 条，减去基线的 69 条，再排除 `.rks-agent/` 下的过程文档 | 差集**恰好一条**：`Server/tests/Dfo.Server.Networking.Tests/Integration/CardRewardReconnectIntegrationTests.cs` |
| `Server/src` | 上一行的差集里没有任何 `Server/src` 路径；`Server/src` 下 34 个改动文件全部命中基线且逐字节相同 | **一个文件都没改** |

**这条差集比「我没改」这句自述有力**：它同时排除了「新增一个没登记的产品文件」
与「改了基线之外的判据」两种情形。**结论：边界干净。**

### 2. CK-034「先红」— 我自己重做一次，并额外做了两次执行者没做的

变异五步全程执行。pristine 记录（我自己算的，与执行者写的同串）：

```text
be4bab6b39930cc7e52116f86913d946c35d624132e89ef77a1db2594e36fe06 *Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs
27da0d507833523caf85c22d3d258a7f96eaa13a0d083fa506f9247ae239ebf7 *Server/tests/Dfo.Server.Networking.Tests/Integration/CardRewardReconnectIntegrationTests.cs
```

#### E1 — 复现执行者的变异（`runs?.End` 放在 `if (selected is not null)` 的**第一句**）

`OnlineUserConnectionObserver.cs:24` 加末位默认参数、`:83` 插入 `runs?.End(selected.CharacterId);`，
mtime 由 `10:29:48` 前进到 `10:39:30`。

```text
  Dfo.Server.Protocol -> ...\Dfo.Server.Protocol.dll
  Dfo.Server.Networking.Tests -> ...\Dfo.Server.Networking.Tests.dll
[xUnit.net 00:00:00.51]     ...CardRewardReconnectIntegrationTests.A_clears_cards_survive_the_connection_that_was_shown_them [FAIL]
  错误消息:
   Assert.NotNull() Failure: Value is null
  堆栈跟踪:
     at ...Snapshot(IDungeonRunDomainService runs) in ...\CardRewardReconnectIntegrationTests.cs:line 192
   at ...A_clears_cards_survive_the_connection_that_was_shown_them() in ...\CardRewardReconnectIntegrationTests.cs:line 154
失败!  - 失败:     1，通过:     0，已跳过:     0，总计:     1
```

**与执行者贴的原始输出逐字一致**：同一句 `Assert.NotNull() Failure`、同一对行号
（`:192` 由 `:154` 调入，即**重连之后**那次快照）、九个项目全部 `->` 编译成功，
所以是**判据红，不是编译失败**。**CK-034 属实。**

#### E2 — 「那个等待是不是承重的」：我把它拿掉了

执行者称「不等就问，即使服务端断线即 End，判据照样绿」。我**保留 E1 变异**，
再删掉判据第 140–142 行的两句 `WaitUntilAsync`，跑 **3 次**：

```text
run1 / run2 / run3  EXIT=1 —— 失败: 1，通过: 0（337 / 291 / 362 ms）
Assert.NotNull() Failure: Value is null
  at ...Snapshot(...) in ...:line 189    ← 删掉 3 行后的行号，仍是重连之后那次
  at ...A_clears_cards_...() in ...:line 151
```

**结论：「不等就绿」这句话不成立，3/3 仍然红。** 原因是 `ConnectedIdAsync`
（`CardRewardReconnectIntegrationTests.cs:254-266`）自己就先等 `GetClients().Count() == 1`、
再等新连接的在线记录出现，等于替这条判据把旧连接的拆除等掉了。
**这不是判据的缺陷，是执行者把一次没做过的实验写成了断言**——判据只会比它自述的更强、不会更弱。
那两句等待仍值得留着（把顺序写成显式的，不依赖 `ConnectedIdAsync` 的内部实现），
但**它是防御，不是承重**。记为非阻塞观察 **N-A**。

#### E3 — 执行者没做的：把「断线即 End」挪到 `DisconnectedAsync` 的**最后一句之后**

E2 顺带暴露一个更该问的问题：那句等待钉的是
`onlineUsers.TryDisconnectConnection`（`OnlineUserConnectionObserver.cs:125`，
`DisconnectedAsync` 的最后一句）。**那么把 teardown 加在这一句之后呢？** 等待就够不着了。
我把 `if (selected is not null) { runs?.End(...); }` 整块插到方法末尾（`:128-131`），跑 3 次：

```text
E3-run1 / 2 / 3  EXIT=1 —— 失败: 1，通过: 0（278 / 350 / 362 ms）
  at ...Snapshot(...) in ...:line 192
  at ...A_clears_cards_...() in ...:line 154
```

**3/3 仍然红，且仍红在重连之后那次。** 这条判据**在断线观察者的头、尾两个位置都能钉住**，
覆盖面比执行者自己论证的更宽。这是我给出的、比原报告更强的证据。

#### 还原

两个文件都 `cp` 回 pristine，`sha256sum -c` 两行全 **OK**（同上两串），
mtime `10:41:17` 与 `10:43:02`；随后 69 个基线文件再核一次，**0 处不同**。
还原后单独重跑该用例 **2 次全绿**（317 / 356 ms）。

#### 判据是否真的走真实断线路径 —— 我另外核实的结构事实

- `INetworkConnectionObserver` 在 `Server/src` **只有一个实现**
  （`Server/src/Dfo.Server.Protocol/Game/OnlineUserConnectionObserver.cs:24`），由
  `Server/src/Dfo.Server.Protocol/ProtocolModule.cs:86` 注册为 Singleton。
- 派发在 `Server/src/Dfo.Server.Networking/Runtime/NetworkPlugins.cs:30-41`：
  `OnTcpClosed` 先 `Connections.TryRemove`，再 `foreach` **逐个 `await`** 每个观察者的
  `DisconnectedAsync`。判据关的是真 socket，所以这条路径是**框架自己驱动**跑起来的。
- 变异写在 `if (selected is not null)` **里面**却能让判据红，说明真实断线时那个分支**确实执行了**。

三点合起来：**「走真实断线路径」不是自述，是被实验证住的。**

### 3. CK-035 抓包对照 —— 我自己去日志里抽验

我不看执行者的表，直接在 `E:/Project/DNF` 下把**全部 9 份** `packet_log*.txt`
（8 份在 `E:/Project/DNF/ServerS4A21/Server/DfoServer/bin/Debug/`，
第 9 份是 `E:/Project/DNF/ServerS4A21/_reference-backup-20260826/packet_log.txt`）
按 type 抽出来去重：

| 帧 | 我数到 | 去重后取值 |
|---|---:|---|
| `SEND 0x0045` | **14** | 只有一种：`(1B) [01]` |
| `SEND 0x0046` | **14** | 只有一种：`(17B) [01 01 00 FF×14]` |
| `SEND 0x0047` | **18** | `(33B)` ×11 全同；`(49B)` ×7 |
| `RECV 0x0047` | **14** | `(2B) [00 00]` ×10、`(2B) [01 00]` ×4 |

逐条比对判据里的期望值：

| 期望值出处 | 判据写的 | 抓包实测 | 判 |
|---|---|---|---|
| `Server/tests/Dfo.Server.Protocol.Tests/Game/DungeonClear/ResultScreenTests.cs:228` | `new byte[] { 0x01 }` | 14/14 帧 `[01]` | **逐字节对得上** |
| `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/CardRewardTests.cs:53-57` `CapturedLayout` | `01 01 00` + `FF`×14（17B） | 14/14 帧同串 | **逐字节对得上** |
| 同上 `:73` `CapturedEmptyAnswerLength` | `33` | 11/11 空脸帧 `(33B)` | **对得上** |
| 同上 `:296-299` `Each_record_names_the_face_it_belongs_to` | Free→`0x00`、Paid→**`0x01`** | 全部 18 帧第 0 条的 `a` 只有 `00` 与 `FF` 两值，**`01` 一次都没出现** | **付费面无抓包支持** |

`(49B)` 帧我自己拆过一遍：`01 | a b c=02 | (id,count)×2 | 尾 u8 | (FF FF 00 00)×3 | FF×16` = 49 字节，
`a` 取 `00`（4 帧）或 `FF`（3 帧）、`b` 恒 `00`、id/count 小端 u32、末尾一个 u8，
**与执行者的语法结论一致**。

**「有没有为了让 SC-1 成立而调整期望值」——没有，而且这一点是结构上不可能的**：
承载三个应答期望值的 `ResultScreenTests.cs` 与 `CardRewardTests.cs`
都在 `scratchpad/baseline-approved` 的 69 个文件之内，且**与评审 approved 的交付态逐字节相同**
（见第 1 节，三次核对）。本 Task 只新增了一个文件，一个期望值都碰不到。
**执行者把「`a=0x01` 无抓包支持」这件对自己不利的事如实写了出来，我复核属实。**

一处**非阻塞的数点误差 N-B**：执行者报的是 13 / 13 / 17 / 14，我数到 14 / 14 / 18 / 14。
差在他的检索只覆盖 `bin/Debug/` 那 8 份，漏掉 `_reference-backup-20260826/` 那一份
（内容是其中一次会话的备份）。**去重后的取值集合完全相同，任何一条结论都不受影响。**

### 4. 判据账 —— 我自己对平

| 来源 | Pvf | Networking | Protocol | Host | Persistence | Infrastructure | Application | 合计 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 验证员独立跑的起点 R1 | 60 | **51** | 479 | 4 | 248 | 215（+2 跳过） | 906 | **1963** |
| 我跑的第 1 次（变异前） | 60 | **52** | 479 | 4 | 248 | 215（+2 跳过） | 906 | **1964** |
| 我跑的第 2 次（全部还原后） | 60 | **52** | 479 | 4 | 248 | 215（+2 跳过） | 906 | **1964** |

两次 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
均 **EXIT=0，1964 通过 / 0 失败 / 2 跳过**。

**+1 对得平**：新文件里 `[Fact]` 与 `[Theory]` **恰好 1 个**
（`CardRewardReconnectIntegrationTests.cs:72`），增量只落在 Networking（51→52），
其余六个项目与验证员独立跑出的 R1 **逐项相同**。两条跳过仍是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
（我从日志里读到具名），跳过数停在 2，说明档案确实加载。**满足 CK-033 与 SC-10。**

行尾：新文件 `CR=0 / LF=291 / 13154 字节`，**全 LF**，与执行者自述一致。

### 5. 执行者自认的四条不确定 —— 我逐条判

| # | 自认的不确定 | 我的判定 |
|---|---|---|
| 1 | 会话直接登进 `ISelectedSessionDirectory`，不走完整登录选人 | **不影响结论。** 断线是真的（真 socket + 框架驱动的 `OnTcpClosed` 逐个 await 观察者），而 `INetworkConnectionObserver` **全仓只有一个实现**且已由 `ProtocolModule.cs:86` 注册，因此真实断线时**跑到的观察者集合与线上完全相同**；夹具只替掉了「这条连接属于谁」这一个事实，而 E1 已证明那个分支确实被执行。他自述的残余风险（有人把 `End` 挂在登录选人流程自己产生的额外观察者上）在只有一个观察者实现的前提下不成立；真出现第二个观察者，它一样会被这条判据跑到 |
| 2 | 变异用带默认值的构造器参数 | **不影响结论。** 内置 DI 容器按「参数全可解析」挑构造器、不使用默认值填充，所以 `runs` 是真注入；反证也成立——若为 null，`runs?.End` 什么也不做、判据不会红，而它红了。我 E1 与 E3 用同样手法两次复现 |
| 3 | 重连后的「再问」在领域缝，不是 `0x0047` 线上帧 | **不影响 SC-3，但是一条真实的射程限制。** 契约 Required evidence 的字面是「同一牌位得到同一件东西的判据」，判据第 158–171 行**逐个牌位真的 `TryClaimClearRewardCard`** 并比 id、数量、稀有度、耐久四项，**满足字面**；`0x0047` 帧本身的字节形状由 `CardRewardTests` 另行逐字节钉住。`GameExitIntegrationTests.cs:59-64` 说明线上帧往返在本仓是写得出来的，所以这是**取舍不是不能**——执行者已主动登记，判为可接受的非阻塞 **N-C** |
| 4 | `b` 的两种读法抓包分不开 | **不影响结论，且我复核属实。** 全部 18 帧 `SEND 0x0047` 的第 0 条 `b` 在带货帧里恒为 `00`、空脸帧里为 `FF`；上游只往牌位 0 填货，两种读法在实测的每一帧上给同一个字节。契约把它列为未实证假设，这次比对**既没证实也没推翻**——如实。要分开只能实机 |

### 6. 非阻塞观察（不改变 verdict，供契约所有者处置）

- **N-A**：`CardRewardReconnectIntegrationTests.cs:140-142` 那两句等待，被文件头
  `<remarks>`（`:36-40`）称为「和断言一样承重 / 不等就会绿」，我的 E2 实验证明**并非如此**——
  拿掉它仍 3/3 红。建议把措辞改成「把顺序写成显式的，不依赖 `ConnectedIdAsync` 内部
  先等 `GetClients().Count()==1` 这一实现细节」。**代码不必改，只是注释说过了头。**
- **N-B**：抓包帧数 13/13/17 应为 14/14/18（漏了 `_reference-backup-20260826/packet_log.txt`）。
  **去重取值与全部结论不变。**
- **N-C**：重连后的「再问」在领域缝而非 `0x0047` 线上帧（执行者已自述，见上表第 3 条）。
- **N-D**：`var shown = Snapshot(runs);`（`:137`）取在 `using (var client …)` 块**关闭之后**，
  即 socket 已断。在「断线即 End」的假想实现下，理论上红点可能落在 `:137` 而不是 `:154`。
  我 6 次变异运行里**一次都没发生**（E1 三次 + E3 三次全部红在 `:154`），
  但「红在重连之后」这一点是**时序保证而非结构保证**。不影响「会红」这个判据能力。
- **N-E**（与 TASK-13 无关，登记备查）：仓库根目录有三个 0 字节垃圾文件
  `上游源码**。`、`档案`、`社区文档`。它们**在 `baseline-approved` 里就已存在**，
  说明早于本 Task、不是 TASK-13 引入的；建议由契约所有者清掉。

### 7. Verdict

**`passed`。** 契约点名欠账的两份证据都补上了，且经得起我独立重做：

1. **CK-034**：我自己施加变异，红点、红因、红的位置与执行者所述**逐字一致**；
   我另加的 E3（把 teardown 挪到 `DisconnectedAsync` 末尾）**仍然红 3/3**，
   说明这条判据钉住的范围比原报告论证的更宽。
   **SC-3 的「重连」一支从此有判据，不再是结构推论。**
2. **CK-035**：我自己去 9 份一级抓包日志里抽验，`0x0045` 与 `0x0046` 逐字节相同；
   `0x0047` 形状一致、三处取值有意不同；`a=0x01` 在语料里确实一次都没出现过。
   **没有任何期望值被调整过**——这一点由「69 个基线文件逐字节相同 + 差集只有一个新文件」
   结构性地证死。
3. **CK-033**：**1964 / 0 / 2，EXIT=0，跑了两次**，+1 与新增的 1 个 `[Fact]` 对平。
4. **越界**：`Server/src` 一个文件都没改；变异全部还原并经 `sha256sum -c` 与基线三次逐字节核验。

**没有阻塞性发现。** 上面五条全部非阻塞，其中 N-A 是一句该收敛的措辞、
N-B 是一处不改变结论的数点误差。

## Execution Result — round 2

评审判 `passed`，只留一处散文订正与一处数点订正。两项都做了，**代码一行未动**。

### 一、`<remarks>` 里那句「和断言一样承重」是假的，已改准

**原话（`CardRewardReconnectIntegrationTests.cs` 旧第 35-41 行）**：

> **The wait carries as much weight as the assertion.** … Without that wait this would pass
> against a server that ends the run on disconnect, purely by asking too early.

**评审的反例**：把那两句 `WaitUntilAsync` 删掉重跑，仍 3/3 红。

**我自己复现过，没有抄结论。** 两条实验都重做了一遍：

| 实验 | 变异 | 结果 |
|---|---|---|
| **A** | `runs?.End` 放在 `DisconnectedAsync` **最后一句之后**（两句等待都够不着的位置），判据不动 | **3/3 红**，283 / 339 / 357 ms，均报 `Assert.NotNull() Failure: Value is null`，`:202`（由 `:164` 调入） |
| **B** | `runs?.End` 仍在 `if (selected is not null)` 首句，但**把判据里那两句 `WaitUntilAsync` 整段删掉** | **3/3 红**，287 / 358 / 339 ms，同样报在 `:198`（由 `:160` 调入） |

两次都红在**重连之后**那次 `Snapshot`，断线前那次是绿的；八个项目全部编译成功，不是编译失败冒充变红。

**所以事实是**：真正把「问」排在「拆」后面的，是 `ConnectedIdAsync` 自己头一句
`WaitUntilAsync(() => gameService.GetClients().Count() == 1)`——第二条 socket 已经连上时，
计数要回到 1，旧连接就必须先从服务的客户端表里下去。而且**这道闸没有静悄悄通过的读法**：
如果计数早回到 1 是因为新连接还没登记、旧的还在，`GetClients().Single()` 会把**旧**连接
再命名一次，`Assert.NotEqual(first, second)` 当场红。

**那两句等待是防御，不是承重。判据比我 round 1 自述的更强，不更弱。**

新散文按这个事实重写（第 35-46 行），并**多加一段**（第 47-51 行）记下实验 A 的覆盖面：
`End` 写在断线观察者的**头一支**还是**最后一句之后**，判据都红——钉住的是整条断线路径，
不是其中某一行。

**「别只核你改的那半句」——我把那段 `<remarks>` 其余的话逐句核过了**：

| 原句 | 因这两条事实是否变假 | 处置 |
|---|---|---|
| 「本判据放在真 host / 真 socket 上，因为要测的就是断线路径」 | 否 | 不动 |
| 「重问同答由领域判据管；它们管不了『传输断掉时服务端拆不拆本局』，因为丢个对象引用的单元判据是在断言自己的夹具」 | 否 | 不动 |
| 「连接是照玩家那样断的——socket 没了、框架察觉、每个注册的连接观察者都跑一遍——**然后**判据才问牌面」 | 否。「然后才问」这个时序事实仍成立，变的只是**由谁保证**它，而那正是改写后新段落说的事 | 不动 |
| 「等待与断言一样承重 / 没有它就会因问得太早而通过」 | **是，变假** | **重写** |

`<remarks>` 之外的散文也核了一遍：`Snapshot` 的注释（比值不比引用）、`FreeFace` 的注释
（各位互不相同、空位也要跟着走）、类摘要、以及会话登记处那段行内注释（「这就是断线观察者
用来查『刚断的是谁』的目录」——实验 A、B 都从该目录取到角色，仍成立）。**均不受影响，没有改。**

**改动范围**：只有 `///` 开头的散文行；`old_string` 与 `new_string` 全是 `///` 行，
文件由 291 行增至 301 行（+10 行注释），**可执行代码 211 行一行未变**。

### 二、抓包帧数订正（评审 N-B）

我 round 1 漏了 `E:/Project/DNF/ServerS4A21/_reference-backup-20260826/packet_log.txt`
（我只扫了 `Server/DfoServer/bin/Debug/` 一个目录）。按全部 **9 份**归档日志重数：

| 帧 | round 1 报的 | **实际** | 去重后 |
|---|---:|---:|---|
| `SEND CMD 0x0045` | 13 | **14** | 仍只有一种：`(1B) [01]` |
| `SEND CMD 0x0046` | 13 | **14** | 仍只有一种：`(17B) [01 01 00 FF×14]` |
| `SEND CMD 0x0047` | 17 | **18** | 33B 空脸由 10 → **11** 帧；49B 仍 7 帧，取值一个不多一个不少 |
| `RECV CMD 0x0047` | 14 | 14（**原本就对**） | 仍 `[00 00]`×10、`[01 00]`×4 |

漏掉的那份只贡献三帧（`0x0045` 第 796 行、`0x0046` 第 798 行、`0x0047` 第 822 行），
**三帧的取值都已在去重集合里**。因此 **CK-035 一节的每一条结论都不变**：
`0x0045`/`0x0046` 仍是逐字节相同且全语料无第二种取值；`0x0047` 仍是形状一致、
第 1–3 条取值与付费面 `a=0x01`、一件货只写一对三处有意不同；付费请求仍是 4 帧两字节 `[01 00]`、
全语料无一字节请求。**订正的是分母，不是结论。**

### 三、CK-033 全量（round 2 终态）

变异全部还原后：`sha256sum -c` 对**两个**文件都报 OK
（`be4bab6b…` 观察者、`deac202a…` 判据文件），`scratchpad/baseline-approved` 的
69 个文件与工作区**逐字节相同**。

```text
PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo
EXIT=0
Pvf 60 / Protocol 479 / Networking 52 / Persistence 248 / Host 4 / Infrastructure 215（+2 跳过）/ Application 906
```

**1964 通过 / 0 失败 / 2 跳过**，与 round 1 逐项相同——本轮只改散文，判据数不该动，也确实没动。

## Executor Self-check — round 2

### 判据数字与增减对平

| | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| round 1 交付 | 1964 | 0 | 2 |
| round 2 交付 | **1964** | **0** | **2** |

差 **0**。本轮零新增、零删除，只改 `///` 散文，七个项目逐项相同。

### 变异纪律

两条实验各自：先记 sha256 → 施加使 mtime 前进（观察者由 `2026-08-31 20:58:16` 前进到
`2026-09-02 10:51:56`）→ 跑判据 → **做完立刻还原** → `sha256sum -c` 校验内容。
实验 A 与 B 之间还原并校验过一次，末尾再还原并校验一次，两次都 OK。
实验 B 动了判据文件本身（删两句等待），同样按此还原，`deac202a…` 校验 OK。
无一条因编译失败作废。

### 我改了什么、没改什么

- 改：`CardRewardReconnectIntegrationTests.cs` 的 `<remarks>` 一段散文（8 行 → 18 行）。
- 没改：该文件的任何可执行代码（211 行）、任何 `Server/src` 下的文件、
  本 Task 文件的契约区与 round 1 各节、`TaskContract.md`、`spec.md`、`protocol/` 下任何文档。

### 未查明的东西

- **「计数回到 1」与「`DisconnectedAsync` 跑完」之间是不是硬的先后关系，我没有证明。**
  我证明的是两件较弱但足够的事：(a) 那道计数闸存在，且早回到 1 只会让判据红而不会静悄悄通过；
  (b) 删掉两句显式等待后，判据在两种 `End` 位置下都 3/3 红。散文按这个强度写，
  **没有写成「保证 happens-before」**。若日后断线拆除变慢，删掉等待的版本可能变脆——
  所以那两句留着当防御。
- **实验只覆盖了「`End` 写在断线观察者里」这一形状。** 若 `runs.End` 被加在别的
  `INetworkConnectionObserver` 实现里、或加在登录/选人流程自己产生的某个回调里，
  本判据是否还盖得住，我没有试。round 1 自检里已把这一条列为不确定，round 2 没有改善它。
- round 1 的数点误差说明我当时**只扫了一个目录就报了分母**。这次是按 `ServerS4A21` 整树
  `find -iname "packet_log*"` 重数的，9 份全在内；但我仍无法排除工作树之外还有别处存着抓包。

## Controller Disposition — 评审 `passed`，TASK-13 置 `done`，实现阶段再次结束

控制器复核：全量 **1964 / 0 / 2**，退出码 0；`Server/src` 仍是 20/768/199，一个文件未动。

### 值得记的：执行者没有把评审的结论抄进仓库

评审证伪了「那两句等待和断言一样承重」，执行者**没有直接照抄**，而是自己重跑了两条实验
（把 `End` 挪到 `DisconnectedAsync` 最后一句之后 → 3/3 红；删掉两句等待 → 3/3 红），
然后才把散文改成实验支持的说法。

**要写进仓库的话，作者得自己有证据。** 别人查出来的事实是一条主张，不是免检证明。
本 Spec 一路在要求「不许把别人跑过的结果当自己的证据」，这次是执行者自己做到的。

它顺带把机制找准了：真正把「问」排在「拆」后面的是 `ConnectedIdAsync` 头一句
`GetClients().Count() == 1`；而且这道闸**没有静悄悄通过的读法**——
若计数早回到 1 是因为新连接尚未登记，`Single()` 会把旧连接再命名一次，
`Assert.NotEqual(first, second)` 当场红。

### 它记下了自己证不了的那一步

「计数回到 1」与「`DisconnectedAsync` 跑完」之间是不是硬的 happens-before，**它没有证明**，
于是把散文写成较弱但足够的强度（闸存在 + 早回只会红 + 删等待后仍 3/3 红），
**没写成「保证」**。

这正是本 Spec 三十余处缺陷的反面：那些缺陷的共同形状是
**把一句只在今天成立的话写成结构上成立**。这一次是把一句证不到那个强度的话，
如实写在它够得到的强度上。

### 数点订正

round 1 报的抓包分母 13/13/17 实为 14/14/18——它 round 1 只扫了一个目录就报分母。
**订正的是分母不是结论**，去重取值集合与 CK-035 的每一条结论不变。成因也记了。
