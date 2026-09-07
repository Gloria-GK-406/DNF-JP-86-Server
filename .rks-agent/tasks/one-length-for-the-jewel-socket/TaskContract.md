---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: 珠宝槽在线上只有一个长度

## Context

### Source Request

所有者从本仓 issue 里挑的下一件，原话：

> 我们先看看现在能执行的9 10 11.下一个任务做什么？

随后在三选一里选了 **#9**（珠宝槽长度收敛）。所有者已知另外两件：#10（装扮栏拖不动）
与 #11（时装穿不上，槽位映射要实机量），并已被告知 #11 的测量必须由所有者实机做。

issue #9 的接受条件（原文，本契约的成功条件由它展开）：

> - NOTI-2 那条路也发 18 字节
> - 两条路共用同一个常量，不再各写各的
> - 有判据钉住「两条路发出的珠宝槽长度相同」，且改坏会红

（**引文照录，不改**。issue #9 说的「两条路」已由 C-1 更正为三条——第三条是邮件附件。
下文一律按三条写。）
> - 字节锚会变，按规矩逐帧指名、解释、给出变化量

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，分支 `main`，HEAD `d94e1841`（工作区干净）。
判据命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
基线（**本轮在 `d94e1841` 实测**）：**2281 通过 / 2 跳过 / 0 失败**，退出码 0。
逐项目：Pvf 60、Networking 52、Protocol 559、Persistence 329、Infrastructure 236(+2 跳过)、Host 6、Application 1039。
那 2 个跳过是 `A_missing_archive_*`，只在没有档案可达时才跑，跳过正是「档案确实加载了」的标志。

**跑判据前必须确认没有 `Dfo.Server.Host` 进程在跑**——运行中的 host 锁住
`Dfo.Server.Host/bin` 下的 `Dfo.Server.Protocol.dll`，构建以 MSB3027 失败，看起来像判据坏了其实不是。
（本轮建契约前已停掉一个残留的 host 进程，当前无该进程。）

既知偶发失败，按「异常类型 + 栈根 + 复跑即绿」判别，**不按用例名**：
`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根
`CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`。

#### 三条路，两个数字

同一份珠宝槽数据，本仓在三条协议路径上写出不同的长度：

| 路径 | 位置 | 线上长度 |
|---|---|---|
| ITEM_LIST 家族（NOTI `0x000D` / `0x000E`） | `GameInventoryDelivery.cs:222` `WireJewelBytes = 18` | **18** |
| 穿戴条目（下称 NOTI-2 条目） | `StateSynchronizationPayloadCodec.Entry.cs:188-189` 硬写 `writer.Write(30)` | **30** |
| 邮件附件 | `StateSynchronizationPayloadCodec.Entry.cs:770-771` 硬写 `writer.Write(30)` | **30** |

第一条由上一个成果 `308ac02c` 改成 18，后两条**没有跟着改**。两个数字各写各的，
仓内没有任何一处把它们联系起来。（第三条是本轮开工前的扫描发现的，见 C-1。）

#### 哪个是对的：18

上游参考服务器 `ServerS4A21` **对三条路都用同一个常量 `A21AvatarJewelBytes = 18`**
（本轮逐行读出）。**常量定义在 `Network/Builders/Inventory/ItemListProtocolWriter.cs`，
三个写入方分居两份文件**——前两个在该文件内，第三个在
`Network/Handlers/MailboxHandler.cs`，跨文件引用它：

```
ItemListProtocolWriter.cs
 89        writer.WriteInt32(A21AvatarJewelBytes);          ← WriteAvatarEntry126（ITEM_LIST）
 90        WriteFixedBytes(writer, avatarDetail?.JewelSocket, A21AvatarJewelBytes);

193            writer.WriteInt32(A21AvatarJewelBytes);      ← WriteNoti2AvatarBlock（穿戴条目）
194            WriteFixedBytes(writer, avatarDetail?.JewelSocket, A21AvatarJewelBytes);

MailboxHandler.cs
1127            writer.WriteInt32(ItemListProtocolWriter.A21AvatarJewelBytes);   ← 邮件附件
1128            WriteFixedMailboxBytes(writer, jewelSocket, ItemListProtocolWriter.A21AvatarJewelBytes);
```

`WriteFixedBytes`（同文件 417 行）从**存储的 30 字节里截前 `length` 个**，不足则补零。
珠宝槽存储结构是 `SocketCount = 5 × EntrySize = 6 = 30`，**A21 只发前 3 个孔**。
存储侧本仓与上游一致（`InventoryModels.cs:154` 的 `byte[] JewelSocket`，
建表 `CHECK(length(avatar_jewel_socket) = 30)`），**差别只在线上长度**。

#### 这条路不是死的：它有两个调用方，其中一个每次进游戏都发

`WriteNoti2EquippedItem`（`Entry.cs:152`）被两处调用，**一次修改覆盖两者**：

| 调用点 | 帧 | 内容 |
|---|---|---|
| `Entry.cs:132` `EncodeSupportCharacter` | 消息类型 `0x019F`（`TAG_CHARACTER_INFO`，助战标签） | 助战角色的穿戴 |
| `Entry.cs:1066` `EncodeExtendedUserState` | **NOTI `0x0002` 子类型 1**（USERINFO） | **玩家自己的穿戴，每次进游戏都发** |

issue #9 把它笼统写作「已穿戴时装那条路」，实际是这两处共用的一个写法。
今天之所以还没有真的把 30 字节发上线，是因为**时装还穿不上**（issue #11：
`PvfEquipmentSlots.ByName` 没有时装的键，`WornSlotForAsync` 返回 null）。
**#11 一旦落地，这条路立刻带着 30 字节上线**——这也是本件排在 #11 前面的理由。

#### 为什么至今没人发现

块是**自描述**的：先写 u32 长度，客户端按这个长度读。所以发 30 也能对齐、也不崩。
而实机测过的那些时装珠宝槽**全是零**，18 个零与 30 个零在屏幕上没有差别。
**判据与实机都区分不了这两个长度**——这一条要记牢，它决定了本件的验收只能靠
「与上游一致」加「三条路互相钉住」，不能靠实机。

#### 现有判据会变的是哪一条

`SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`
（`Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterPayloadCodecTests.cs:173`）
在助战角色的装备槽 2（时装槽）放了一件 `ItemKind == 8`、珠宝槽为 `0,1,2,…,29` 的时装，
并逐偏移断言。该用例的这些断言**正对应本轮被授权的字节变化**，必然要改：

- `Assert.Equal(347, populated.Length)`（:252）
- `Assert.Equal(30, …AsSpan(206))` 与其后 30 字节内容（:206-207 断言，:270-272）
- 该条目之后的一切偏移（颜色 244/246、生物槽 276/277/300）

**除此之外的断言不得改动。**

## Goal

让珠宝槽在**线上只有一个长度**：把穿戴条目与邮件附件那两条路从 30 改成 18，并让三条路读同一个常量、
用同一段写法，使「两处各写各的」这件事在结构上不可能再发生；同时把这处不一致
及其订正写进协议文档，让仓内留下痕迹。

## Scope

### In

- `WriteNoti2EquippedItem` 的珠宝槽运行改为：`u32(18) + 存储 30 字节里的前 18`。
  非时装（`ItemKind != 8`）的时装槽仍写 `u32(0)` 且不跟字节——**这一支不变**，
  与上游 `WriteNoti2AvatarBlock` 的 else 分支一致。
- 颜色运行 `u32(4) + u16 Color1 + u16 Color2` **不变**（本轮已核实三条路都是 u16，
  见 Assumptions）。
- **邮件附件那条路同样改为 18**（`WriteMailboxAvatarDetail`，消息 `0x0061`）。见 C-1。
- **三条路共用**：线上珠宝长度这个数在仓内只存在一处，三条路都读它；
  截断/补零的写法也只存在一处。具体如何提取由 spec/plan 决定，本契约只约束结果。
- 判据：
  - 钉住三条路发出的珠宝运行长度**相同**，且把任一路改回 30 会红；
  - 钉住穿戴条目里时装的珠宝运行是 18 且内容是存储的前 18 字节（用可分辨的填充，
    不能用全零——全零证明不了截断）；
  - 钉住非时装在时装槽仍是 `u32(0)` 且不跟字节。
- 字节锚与既有断言：会变的用例**逐条指名、解释、给出新值**，并证明变化量恰好是
  被授权的 12 字节。
- 协议文档：
  - `protocol/game/server-to-client/NOTI-0x0002.md`「子类型 1 的装备条目是 60 字节」一节
    **今天完全没有记载时装槽的外观块**（那一节的条目布局从 `24 编年史条数` 直接接下去，
    因为参照会话里没有人穿时装）。补上外观块的形状与它插在哪里。
  - `protocol/game/server-to-client/NOTI-0x000D.md:261` 写着那条穿戴路径
    「**本轮未改动**」——这句在本轮之后不再成立，按规矩补记而不是悄悄改掉。

### Out

- **不做 #10（装扮栏拖不动）与 #11（时装槽位映射）**。本件只改线上长度。
- **不改存储侧的 30 字节**。存储与上游一致，只是线上截前 18。
- **不动 `EncodeExtendedUserState` / `EncodeSupportCharacter` 的其余任何字段**。
- **不新增协议帧，不改任何帧的字节布局**（本条外观块除外）。
- **不做实机验证**——见 Assumptions，实机在原理上分辨不了这两个长度，
  跑一趟只会产出一条「不崩」的无信息证据。不得把「不崩」写成本件的证据。
- **不把 `WriteNoti2EquippedItem` 与 `GameInventoryItemEntry.Write` 合并**。
  两条路的条目格式本来就不同（一个 60 字节动态、一个定长 101），本件只共用珠宝这一段。

## Constraints

- **被授权的字节变化恰好两处，都是同一件事**（见 C-1）：
  1. 穿戴条目（NOTI `0x0002` 子类型 1 与消息 `0x019F`）里**时装槽上 `ItemKind == 8` 的条目**；
  2. 邮件附件（消息 `0x0061`）里**时装附件的条目**。
  两处的珠宝运行都由 `u32(30) + 30 字节` 变为 `u32(18) + 18 字节`，每条**缩短 12 字节**。
  其余任何字节变化都要停下报告。
- 既有判据的期望值与断言**不得为迁就实现而改写**；正对应上述授权变化者除外，
  且每处改写要单独说明它对应哪一条。
- `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 必须继续通过，
  判定谓词与具名豁免清单不得放宽或扩充。
- `TreatWarningsAsErrors` 开启，CS9113 是错误。
- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host。
- 切片架构（`AGENTS.md`）：`Protocol/<Endpoint>/<Protocol>/` 一个目录，切片内不跨协议引用；
  要共用须上提到 endpoint 层。（`GameInventoryDelivery.cs` 在 `Protocol/Game/` 即 endpoint 层，
  `StateSynchronization` 切片今天已经在调用它，这条通路是既有的。）
- 每个任务单元结束时判据必须全绿，不允许攒红。
- **不得触碰主检出的 `ClientHack/` 与 `.worktrees/client-debug-lab`**（另一个 agent 在做，只读）。
  也不得触碰 `.worktrees/entries-are-what-the-client-reads`（所有者选择保留的旧 worktree）。
- **禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——
  本仓曾因此丢过未提交的工作。变异核实用文件级备份 + `sha256sum`，每次用完立刻还原并核散列。
- 记载不得写成比证据强：「上游源码读出」「本仓代码读出」「静态反汇编」「实机观测」
  要分别标明；不写读者无法证伪的基数。

## Success Conditions

- **SC-1** 穿戴条目与邮件附件里的时装，珠宝运行都是 `u32(18) + 18 字节`，
  内容是存储 30 字节里的**前 18**；判据用可分辨的填充钉住内容与长度两者，改坏会红。
  邮件那条今天**零判据覆盖**（本轮扫描所得），要新建。
- **SC-2** 线上珠宝长度这个数在仓内**只有一处定义**，三条路都读它；截断/补零的写法
  也只有一处。交付时要给出「仓内搜这个数，命中都在哪里」的结果作为证据，
  并逐条说明每个留下来的 `30` 为什么是对的（存储长度就是 30）。
- **SC-3** 有判据**直接比较三条路发出的珠宝运行长度相同**——不是各自断言 18，
  而是把三条路的输出放在一起比。把任一路单方面改回 30，该判据会红。
- **SC-4** 非时装物品占着时装槽时，穿戴条目仍写 `u32(0)` 且不跟字节；判据钉住。
- **SC-5** 除被授权的变化外，其余帧逐字节不变。`SelectCharacterAcceptedTranscriptTests`
  的 62 帧要逐帧核对：变化的帧被指名、解释、给出新值；未变的帧证明未变。
  （**预期是该字节锚不变**——它的会话里没有人穿时装。若它变了，那是规则实现错了，停下报告。）
- **SC-6** 全量判据不低于基线 **2281 通过 / 2 跳过 / 0 失败**，退出码 0。
- **SC-7** `NOTI-0x0002.md` 记下穿戴条目里时装槽外观块的形状与位置；
  `NOTI-0x000D.md:261` 那句「本轮未改动」有指名它的补记，说明它何时起不再成立。
  两处都要标明证据强度（本件的长度依据是**上游源码读出**，不是实机）。
- **SC-8** 交回一份变化清单，**正向与反向都报**，且**从代码出发独立扫**而不是只核对条目。
  本仓已有五次同类失误（见 `the-container-decides-the-entry-shape` 的 C-1 补充），
  共同错法有三层：只顺着起因往下游追、只搜数字不搜说法、**搜索集本身没被检查**。
  本轮的搜索集与搜法要写进变化清单，并**显式列出本轮推翻的说法有哪些**
  （至少含「那条穿戴路径本轮未改动」）。

## Authorization

覆盖：任务契约的创建与修订、路由、**一个任务 worktree**、配置内的执行与验证、
经完整评审与新鲜验证之后的**一次本地接受提交**、以及一次未接受尝试的精确工作流回滚。
**不含**合并、推送、建 PR、删除分支或 worktree、清理。
本项目已获所有者常驻授权可调用子代理。

## Requires Confirmation

- 若 `SelectCharacterAcceptedTranscriptTests` 的字节锚发生变化，停下报告——
  按 SC-5 它不该变，变了意味着改动波及了不该波及的路径。
- 若发现颜色运行或 else 分支也与上游不一致，**停下报告，不要顺手改**——
  那是新发现，属于另一件，不在本轮授权的字节变化里。
- 若「三条路共用一处」在不违反切片架构的前提下做不到，停下报告说明卡在哪，
  不要为了共用而把切片边界打开。

## Assumptions

- **18 这个数来自上游源码读出**（`ItemListProtocolWriter.cs` 的 `A21AvatarJewelBytes`，
  三条写法各引用一次），**不是实机测得**。
- **实机在原理上分辨不了 18 与 30**：块自描述长度，发 30 也对齐也不崩；
  而已测过的时装珠宝槽全为零，两个长度的字节内容视觉上相同。
  上一个成果的 11 件时装实机通过，**证明的是形状机制成立，不是这个长度正确**。
- 多发 12 字节会落在客户端接收该块的目标缓冲之后——这一条**由静态反汇编量得、未实机验证**，
  是动机不是结论依据。本件的依据是「与上游一致」。
- 颜色是 `ushort`：`InventoryAvatarDetailState`（`InventoryModels.cs:155-156`）声明
  `ushort Color1, ushort Color2`；`Entry.cs` 写 `item.AvatarDetail?.Color1 ?? 0`，
  C# 空合并在 `b` 可隐式转成 `A0` 时取 `A0`，故为 `ushort` 的 2 字节重载。
  **这一条由既有判据独立佐证**：`SelectCharacterPayloadCodecTests` 在 244 与 246
  相隔 2 字节各读一个 `u16` 并通过。
- 上游 `ServerS4A21` 是参考实现，不是本仓的规范；引用它的地方要标明是「上游读出」。
- 时装槽的边界是 `0..11`（`Entry.cs:296` `IsCostumeBarSlot`），本件不改它。

## Contract Amendment C-1（spec 阶段，2026-09-07，经所有者同意）

**珠宝槽在本仓有三条上线路径，不是两条。** revision 1 的 Scope In、Constraints 第一条、
SC-1、SC-2、SC-3 都写的是「两条路」，那是照抄 issue #9 的措辞。

发现经过：写 spec 前按 SC-8 的要求做「哪些 `30` 要改、哪些必须留」的全仓扫描，
在 `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationPayloadCodec.Entry.cs:770-771`
撞见第三处：

```csharp
if (socket.Length != 30) throw new InvalidDataException("Mailbox avatar jewel socket must contain exactly 30 bytes.");
writer.Write(30); writer.Write(socket); writer.Write(4);
```

`WriteMailboxAvatarDetail`，服务于消息 `0x0061`（`MAILBOX_MAIL_LIST`），
邮件附件是时装时写这一段。形状与另外两条完全相同。

**上游三条路用的是同一个常量**（本轮读出）：

| 上游位置 | 常量 |
|---|---|
| `ItemListProtocolWriter.cs:89` `WriteAvatarEntry126`（ITEM_LIST） | `A21AvatarJewelBytes` |
| `ItemListProtocolWriter.cs:193` `WriteNoti2AvatarBlock`（穿戴条目） | `A21AvatarJewelBytes` |
| `MailboxHandler.cs:1127` `WriteA21MailboxItemBlobs`（邮件附件） | `ItemListProtocolWriter.A21AvatarJewelBytes` |

**成本很低**：`WriteMailboxAvatarDetail` 今天**没有任何判据覆盖**——本仓没有任何用例
造过带时装附件的邮件（本轮在 `Server/tests` 全仓搜 `DetailJson` 与邮件相关夹具所得）。
所以改它**不会动到任何既有断言**，只需要补一条新判据。

**为什么一并收**：本件的目标是「珠宝槽在线上只有一个长度」。留着邮件那条发 30，
等于原样重现本件要偿的那笔债，且下一个人要为一行改动再走一遍完整流程。
所有者被告知了两条路线（收进来 / 另开 issue）与各自代价后，选择收进来。

**初版为什么会写成两条**：契约的事实是从 issue #9 抄来的，而 issue #9 是上一个成果的
`behavior-change-list.md` 写的，那份清单的搜索口径是「ITEM_LIST 那条改成 18 之后，
还有谁没跟上」——**从被改动的那一处往外找**，找到了共享同一个"NOTI-2"名字的穿戴条目，
没有找到名字不同的邮件路径。这正是 SC-8 点名的第一层错法（只顺着起因往下游追），
**这一次是在开工前的扫描里被自己抓住的，不是被评审抓住的。**

### C-1 的补充（review 阶段，2026-09-07）

**C-1 初版犯了它自己正在诊断的那个错，这是同一类的第六次。**

C-1 写「revision 1 的 Scope In、Constraints 第一条、SC-1、SC-2、SC-3 都写的是两条路」，
然后据此改了那五处。独立评审（BF-1）指出：

1. **枚举漏了 Goal**。`Goal` 一节写着「把穿戴条目那条路从 30 改成 18，并让两条路读同一个常量」，
   C-1 从头到尾没有提到它。**Goal 是本契约最上层的那句话**，漏它比漏一处判据条款更糟。
2. **被枚举到的 Scope In 只执行了一半**。C-1 点名 Scope In，而 Scope In 有两处说「两条路」：
   共用常量那一条改了，**判据那一条没改**，于是它与同一份文件里已改成「三条」的 SC-3 直接打架。
   这与上一轮 C-1 补充里记的**第三次**（「枚举了却没执行」）是同一个形态。
3. 另有 Context、Constraints 的说明文字、Requires Confirmation、Assumptions 数处，
   以及 `routing-decision.md` 里的两处，同样没跟上。

**评审给出的清单本身也不全。** 它列了契约 7 处加路由 2 处；按它的线索复扫时又找出
契约 `:52`（表下的「前者／后者」，两行表本身要变三行）、`:58`（上游只列了两个写入方，
少了 `MailboxHandler.cs:1127`）、`:217`（Assumptions 的「两条写法各引用一次」）
与路由 `:11`（同上游问题），共 4 处。**所以「按外部清单逐条改完」也不等于改干净了**——
仍然要自己重扫一遍并逐条判定每个剩余命中是对是错。

**改完后的复扫结果。** 口径：`grep -n "两条" TaskContract.md routing-decision.md`，逐行判定。

**这里不报命中总数**：这两份文件每追加一段修正案就会多出若干命中，
任何写下来的数都会在下一次追加时过期（先后写过 13、21，第三轮复审实测 28）。
可复现且不随文档增长而变的说法只有一条：

> **`## Contract Amendment C-1` 及其各节补充之外，
> 没有任何一处规范性陈述仍说「两条路」。**

那几节之内的命中按构造是正确的——它们的职责就是引述被推翻的说法，逐行核过无一是规范性陈述。

**这几节之外 9 行**，逐行判定：

| 行 | 是什么 | 判定 |
|---|---|---|
| `TaskContract.md:23`、`:24` | issue #9 接受条件的引文 | 照录不改，已在 `:26` 就地加注说明已被 C-1 更正 |
| `TaskContract.md:26` | 上面那条加注本身 | 正确 |
| `TaskContract.md:56` | 「后两条**没有跟着改**」——指三行表里的后两行 | 正确指代 |
| `TaskContract.md:120` | Goal 的「穿戴条目与邮件附件那两条路」 | 正确指代（就是被改的那两条） |
| `TaskContract.md:159` | Scope Out「不把那两个写法合并」——指 P1 与 P2 两个具体写入方 | 正确 |
| `routing-decision.md:37` | 记 revision 1 曾照抄 issue #9 写「两条路」 | 历史记载，正确 |
| `routing-decision.md:68`、`:69` | 「两条失败路径」 | 另一个意思，正确 |

**上面这段的初版本身也是错的**，见下面的「补充之二」。

**这次的错法与前五次都不同，值得单独记**：前五次的问题在**搜索**（搜什么、在哪搜）；
这一次搜索集是对的、扫描也真的做了（第三条路径正是它扫出来的），
错在**枚举与执行之间**——找到了、写进清单了，然后改的时候漏掉一部分。
所以「扫得全」不是终点，**改完必须再扫一遍，并对每个剩余命中给出「它为什么可以留」的判定**。
本轮的复扫结果就写在上面一段，供下一个人照做。

**六次里仍然没有一次是自查发现的。**

### C-1 的补充之二（review 第二轮，2026-09-07）

**上面那段整改自己又出了两处错，其中一处是把真话改成了假话。**
由第二轮独立复审发现（BF-3、BF-4）。

#### BF-3：只改了散文，没改它底下的证据

第一轮整改把 Context 的小标题与散文由「两条路」改成「三条路」，**却没动它们底下的两样东西**：

1. **那张表仍然只有两行**（缺邮件附件那一行），于是紧接着的「后两条」在表里找不到落点。
   而 `behavior-change-list.md` 里，**我自己把「那张两行表本身」列进了「评审清单之外我又找出的 4 处」**——
   列了，然后没改。**这与本补充上一节正在诊断的「枚举了却没执行」是同一个形态，
   在同一次整改里又犯了一遍。**
2. **「对两条路都用同一个常量」被改成「三条」，而那句话的主语是
   `ItemListProtocolWriter.cs`——该文件里只有两个写入方（`:89`、`:193`），
   第三个在 `MailboxHandler.cs:1127`。旧句对那个文件为真，新句为假。**
   本仓记过「整改一句假话时写下另一句假话」；这次方向反了，是把**真话**改成了假话。
   已改写为「常量定义在 X，三个写入方分居两份文件」，并把 `MailboxHandler.cs:1127-1128` 补进代码块。

**教训**：`grep` 一个词只命中**散文**。表格、代码块、清单里的同一个事实不含那个词，
所以永远不会被命中。**改一个说法时要连它引用的证据一起看**——散文与它底下的表、
代码块、行号必须同时更新。这是继「搜什么／在哪搜／枚举与执行」之后的**第四层**。

#### BF-4：变异证据的因果归因不实，且那条断言本身是坏的

第一轮整改给 `AnOrdinaryItemInACostumeSlotDeclaresNoJewelsAndGoesStraightToTheColours`
补了两条断言，并在变化清单里写「加固使该判据在 M4 下也会红，红条数 15→16」。

**「15→16」是真的，「谁抓住的」是假的。** 复审实跑证明红的是
`Assert.Equal(-1, IndexOfRun(frame, [(byte)avatarLength, 0, 0, 0]))`，
而那是一次**假红**：`[20,0,0,0]` 有四分之三是零，在一个**根本没有珠宝运行**的帧里被偶然撞上。
**一个在代码正确时也会失败的检查，比没有这个检查更糟。**
（另一条长度差断言对线上长度本就不敏感，也不该敏感——它证的是「else 分支一个字节都没写」。）

**已删掉那条假红断言。** 重跑 M4：红 **15** 条，该判据恢复为绿——**这才是对的**，
它按设计对长度不发表意见。

**这一条恰好是第一轮非阻塞观察 NB-5 提的风险**（「首条断言是宽松模式，理论上可假绿」）。
我按它加固时**把风险从「可能假绿」搬成了「已经假红」**，并且**没有实跑验证归因就写进了清单**。
**变异证据必须报「哪一行真的红了」，不能报「我以为哪一行红了」。**

#### 顺带修正的计数

第一轮补充写的「复扫 13 处命中」并给了 6 类分类，**两个数都不对**：当时实际 14 行，
分类只覆盖 13 行，漏判 `routing-decision.md:37`（第二轮复审指出）。上一节已按可复现口径重写。

**至此同一族失误在本仓共 8 次，仍然没有一次是自查发现的。**
