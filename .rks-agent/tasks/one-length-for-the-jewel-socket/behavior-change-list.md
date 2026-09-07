# 变化清单：珠宝槽在线上只有一个长度

契约 `./TaskContract.md` revision 2（含 C-1）／Spec `./spec.md`（RQ-001..RQ-010）。
基线 `d94e1841`，未提交。

本清单**正向与反向都报**，且**从代码出发独立扫**，不是核对 Spec 的条目。

---

## 一、扫法（先说搜索集，因为本仓的历次遗漏都出在这里）

本仓在上一个成果里同一类失误连犯五次，诊断出三层错法：只顺着改动的**起因**往下游追；
只搜**数字**不搜**说法**；**搜索集本身没被检查**。本轮按这三层各做一遍。

### 1. 搜数字（不是搜「哪里被我改了」）

```
grep -rn "30" --include=*.cs Server/src Server/tests | grep -i "jewel\|socket"
grep -rn "avatar_jewel_socket\|jewel_socket" --include=*.cs Server/
grep -rn "珠宝" protocol/ Docs/ Document/
```

**这一步就是本轮最大的一次收获**：它找出了第三条发 30 的路径（邮件附件，
`WriteMailboxAvatarDetail`），而 issue #9 与契约 revision 1 都只知道两条。
上一轮找不到它，是因为上一轮的口径是「背包家族改成 18 之后还有谁没跟上」——
从被改动处往外找，只找到了共享 "NOTI-2" 这个名字的穿戴条目。见契约 C-1。

### 2. 搜说法（被本轮推翻的话，里面往往没有数字）

见下面第五节。至少一条（`NOTI-0x000D.md` 的「本轮未改动」）**完全不含数字**，
只能靠列举「本轮推翻了哪些说法」才找得到。

### 3. 搜索集（显式列出，因为上一轮漏的是一整份文件）

| 范围 | 含 |
|---|---|
| `Server/src/` | 全部 `.cs` |
| `Server/tests/` | 全部 `.cs` |
| `protocol/` | 全部 `.md` |
| `Docs/`、`Document/` | 全部 `.md` |
| **`.rks-agent/tasks/one-length-for-the-jewel-socket/`** | **本轮四份产物：TaskContract、routing-decision、spec、本清单** |
| `E:/Project/DNF/ServerS4A21/` | 只读参照，只查不改 |

**最后一行是上一轮漏掉的那一类**：上一轮的两次扫描口径分别是「契约里现存的 42」与
「`Server/` 下的 143/42」，两个都不含 `routing-decision.md`，于是一个被推翻的数字活到第五轮。
本轮的 `routing-decision.md` 与 `TaskContract.md` 在 C-1 里已按「三条路」同步改过。

**未纳入**：`.worktrees/client-debug-lab`（另一个 agent 在做，只读）、
`.worktrees/entries-are-what-the-client-reads`（所有者保留的旧 worktree）、
以及主检出 —— 三者按契约都不得触碰。它们里面若有过期记载，本轮不处置。

---

## 二、正向：可观察的字节变化，共 3 条

| # | 帧 | 条件 | 变化 |
|---|---|---|---|
| F-1 | NOTI `0x0002` 子类型 1（USERINFO，玩家自己的穿戴） | 时装槽 `0..11` 上 `ItemKind == 8` 的条目 | 珠宝运行 `u32(30)+30` → `u32(18)+18`，**每条缩短 12 字节** |
| F-2 | 消息 `0x019F`（`TAG_CHARACTER_INFO`，助战角色的穿戴） | 同上 | 同上 |
| F-3 | 消息 `0x0061`（`MAILBOX_MAIL_LIST`） | 附件 `ItemKind == "avatar"` | 同上 |

F-1 与 F-2 共用一个写法（`WriteNoti2EquippedItem`），所以是一处改动两条帧。
判据对**两个调用方各测一次**，没有让一个代表另一个。

**没有第四条**。NOTI `0x000D` / `0x000E` 这条路 `308ac02c` 起就发 18，本轮的改动对它
是**逐字节等价的重构**：被替换掉的四行与新的 `WriteJewelRun` 逻辑完全相同
（`>= 18 ? 前18 : 18个零`）。变异 M1 也间接证明了这点——把它改回 30 时红了 13 条，
其中 **9 条**是本轮之前就存在的既有判据（含字节锚），逐条见第六节。

### 这三条今天都发不出去吗？

不是。**F-3 今天就在线上**：`0x0061` 在入场序列里（`SelectedCharacterStateMessageFactory.cs:110`），
且在字节锚的 62 帧里（`SelectCharacterAcceptedTranscriptTests.cs:142`）——只要有一封带时装附件的邮件就会发。
**F-1 / F-2 今天发不出带时装的条目**，因为时装还穿不上（issue #11：`PvfEquipmentSlots.ByName`
没有时装的键）。#11 一旦落地，它立刻带着这段上线——这正是本件排在 #11 前面的理由。

---

## 三、反向：新增或改变的拒绝与放行路径，共 1 条（且不可观察）

**从代码出发逐处扫产线的 `throw` / 分支，而不是核对上面的正向表。**

| # | 位置 | 是什么 |
|---|---|---|
| R-1 | `AvatarJewelSocketOnTheWire.WriteJewelRun` | **一处内部容忍度被放宽，但不可达。** 它对短于 18 字节的输入补零而不是抛。P2（`Entry.cs:186`）与 P3（`Entry.cs:780`）在调用它**之前**仍各自要求存储恰好 30 字节，所以在这两条路上补零分支到不了。P1 本来就走这个逻辑，不变。 |

**新增的生产拒绝：0 条。** 实现过程中一度在 `WriteJewelRun` 里加了
`ArgumentNullException.ThrowIfNull(writer)`，那是一条**没有被授权、也没有用处**的新拒绝路径
（三个调用方传的都是本地 writer，传 null 也会在下一行 NRE）。**已在提交前移除**，
写在这里是因为「新增了又拿掉」也该留痕。

**被移除的生产拒绝：0 条。**
**被改变条件的拒绝：0 条**——三处存储长度校验的条件与错误文字都逐字未变，只是把字面量
`30` 换成了具名的 `StoredJewelBytes`（同值同语义）。

---

## 四、非行为改动（结构与记载）

| # | 内容 |
|---|---|
| S-1 | 新增 `Server/src/Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs`：`internal const int WireJewelBytes = 18` 与 `internal static void WriteJewelRun(...)`。无 DI、无接线、无新协议帧。 |
| S-2 | 可见性放宽：线上长度常量原为 `GameInventoryItemEntry` 的 `private const`，现为新类型的 `internal const`。范围仍在 `Dfo.Server.Protocol` 程序集内。 |
| S-3 | `StateSynchronizationPayloadCodec` 新增 `private const int StoredJewelBytes = 30`，替换该文件里两处字面量。 |
| S-4 | 判据：新增 8 条（`JewelSocketHasOneWireLengthTests`）；修改 1 条（见下 T-1）。 |
| S-5 | 文档：`NOTI-0x0002.md`、`NOTI-0x000D.md`、`NOTI-0x0061.md`。 |

### 一处**故意没有**统一的地方

`GameInventoryDelivery.cs:137` 的存储长度校验仍是字面量 `30`，没有跟着提取成常量。
理由：本轮要收敛的是**线上长度**；存储长度由数据库 `CHECK` 与 Application 层校验各自钉住，
它不是本轮的漂移源。多提一个共享常量会把改动面扩大到无关文件。
`StoredJewelBytes` 只在 `Entry.cs` 内出现，是因为那一个文件里有**两处**同样的校验。

### T-1：唯一被修改的既有判据

`SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`

| 改动 | 归因 |
|---|---|
| `347` → `335` | F-2，缩短 12 |
| `Assert.Equal(30, …AsSpan(206))` → `18` | F-2，长度字段本身 |
| `Range(0,30) … AsSpan(210,30)` → `Range(0,18) … AsSpan(210,18)` | F-2，内容 |
| 颜色 `AsSpan(244)/(246)` → `(232)/(234)` | F-2，偏移下移 12 |
| 生物槽 `[276]`、`AsSpan(277)`、`AsSpan(300)` → `[264]`、`(265)`、`(288)` | F-2，偏移下移 12 |

**该条目之前的 14 处断言逐值未动**（`populated[100]`、`[101]`、`AsSpan(102)`、`AsSpan(113)`、
`[137]`、`AsSpan(138)`、`[150]`、`AsSpan(151)`、`[157]`、`AsSpan(158,5)`、
以及条目头部的 `[182]`、`AsSpan(183)`、`AsSpan(187)`、`AsSpan(194)`）——珠宝运行在它们之后。
**没有任何断言被放宽或删除**，也没有任何期望值是为迁就实现而改的：每一处都对应 F-2 的一个字节位移。

---

## 五、本轮推翻的说法（不含数字，只能靠列举找到）

| # | 说法 | 出处 | 处置 |
|---|---|---|---|
| C-1 | 「穿戴中的时装……那条路本轮未改动」 | `NOTI-0x000D.md`（原「一处仍然存在的功能缺口」一节） | **原句原地留着**，加脚注指向文末新增的「补记（2026-09-07，第三次）」，说明它何时起不再成立 |
| C-2 | 「珠宝槽有两条上线路径」 | issue #9、契约 revision 1、routing-decision 初版 | 由契约 **C-1 修正案**改为三条；routing-decision 同步 |
| C-3 | `0x0061`「服务端实现映射：尚未实现」 | `NOTI-0x0061.md` 的 A12 区 | **不在原处改**——那一整区按迁移规则将在 A21 重做时删除。在 A12 区**之外**新增的 A21 小节里点名它已过期，并给出现在的实现位置 |

**C-3 的判断依据**：`MailboxStateMessage`（类型 `0x0061`）在
`SelectedCharacterStateMessageFactory.cs:110` 被加进入场序列，且出现在字节锚的 62 帧里。

**没有找到第四条。** 搜法：把本轮认定为「过期」的三件事各自的**主题词**
（穿戴路径、路径条数、邮件是否实现）在第一节的搜索集里各扫一遍。

---

## 六、证据

### 判据

| | 值 |
|---|---|
| 基线（`d94e1841`，主检出与本 worktree 各实测一次，一致） | **2281 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 现在 | **2289 通过 / 2 跳过 / 0 失败**，退出码 0 |
| 差额 | **+8**，全部在 `Dfo.Server.Protocol.Tests`（559 → 567） |

字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧**全部未变**——这与预期一致：
参照会话里没有人穿时装，也没有带时装附件的邮件。

### 变异验证（文件级备份 + `sha256sum`，每次用完立刻还原并核散列；未使用任何 git 还原类命令）

| 变异 | 做了什么 | 红了几条 | 收敛判据 `EveryFrameDeclaresTheSameJewelLength` |
|---|---|---:|---|
| M1 | 背包家族那条改回 `writer.Write(30)` | 13 | **红** |
| M2 | 穿戴条目那条改回 `writer.Write(30)` | 6 | **红** |
| M3 | 邮件附件那条改回 `writer.Write(30)` | **4** | **红** |
| M4 | 把共用常量 `18` 整体改成 `20`（三条一起动） | 15 | **绿** |

三点值得记：

1. **M3 只红了 4 条，且全部是本轮新增的判据**——这就是「邮件那条今天零判据覆盖」的直接证明。
2. **M4 让收敛判据保持绿**，正是设计意图：它不认 18，只认「四个发送方彼此相等」。
   若它在 M4 里也红了，说明它其实是在断言数值，那就是一条冒充收敛判据的重复判据。
3. **两类判据缺一不可**：M1/M2/M3 靠收敛判据兜住，M4 靠逐帧判据兜住。

（M4 的红条数在第一轮整改期间一度是 16，那多出来的一条是**假红**，已在第二轮整改里删掉；
数字回到 15。经过见第九节 BF-4。）

每次还原后 `sha256sum -c` 均为 `OK`，复跑全绿。

#### 各次变异红了哪些判据（judged 阶段补齐——初版只报了条数，不满足 Spec 的 `EV-003`）

**M1**（背包家族回退，13 条）：本轮新增 4 条
（`TheItemListSendsEighteenOfTheThirtyStoredBytes`、`TheColoursFollowTheJewelsUnchangedInEveryFrame`、
`TheTwelveBytesThatStayBehindReachNoFrame`、`EveryFrameDeclaresTheSameJewelLength`），
既有 9 条（`EntryWriterContainerShapeTests` 7 条、`SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、
**字节锚 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**）。
收敛判据报 `Assert.All() Failure: 3 out of 4 items in the collection did not pass.`

**M2**（穿戴条目回退，6 条）：
`TheSupportCharacterFrameSendsEighteenOfTheThirtyStoredBytes` 与
`TheUserInfoFrameSendsEighteenOfTheThirtyStoredBytes` 均报 `Expected: 18 / Actual: 30`；
`TheTwelveBytesThatStayBehindReachNoFrame` 报 `Expected: -1 / Actual: 147`（那 12 字节真的出现在帧内偏移 147）；
`TheColoursFollowTheJewelsUnchangedInEveryFrame` 报 `Expected: 4 / Actual: -1246448718`（颜色长度位读到了珠宝数据）；
收敛判据报 `2 out of 4 items ... did not pass`；
既有的 `SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty` 报 `Expected: 335 / Actual: 347`。

**M3**（邮件附件回退，4 条）：`TheMailboxAttachmentSendsEighteenOfTheThirtyStoredBytes`、
`TheColoursFollowTheJewelsUnchangedInEveryFrame`、`TheTwelveBytesThatStayBehindReachNoFrame`、
`EveryFrameDeclaresTheSameJewelLength`。**一条既有判据都没红**——这就是「邮件那条零覆盖」的直接证明。

**M4**（共用常量 18→20，**15 条**，第二轮整改后重跑所得，逐条列全）：
本轮新增 4 条逐帧判据全红（`TheItemList…`、`TheSupportCharacterFrame…`、`TheUserInfoFrame…`、
`TheMailboxAttachment…`）加 `TheColoursFollowTheJewelsUnchangedInEveryFrame`，共 5 条；
`EntryWriterContainerShapeTests` 7 条；`SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes`、
`SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`、
以及**字节锚 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**，共 3 条。5+7+3 = 15。

**`AnOrdinaryItemInACostumeSlot…` 在 M4 下是绿的，这是对的**：它按设计对线上长度不发表意见。
**收敛判据 `EveryFrameDeclaresTheSameJewelLength` 也绿**，单独复跑确认
（`--filter FullyQualifiedName~EveryFrameDeclaresTheSameJewelLength` → `已通过! 通过: 1`）。

**字节锚在 M1 与 M4 下都会红。** 这一条是评审独立发现并补上的，很重要：
它说明「本轮字节锚未变」不是因为它钉不住这段，而是因为参照会话里既没人穿时装、也没有带时装附件的邮件。

### 「只有一处」的证据

```
$ grep -rn "WireJewelBytes" Server/src --include=*.cs
AvatarJewelSocketOnTheWire.cs:45    internal const int WireJewelBytes = 18;   ← 唯一定义
AvatarJewelSocketOnTheWire.cs:49/57/58/59/60                                  ← 定义处自用与文档引用
GameInventoryDelivery.cs:177                                                  ← 文档 <see cref>

$ grep -rn "WriteJewelRun" Server/src --include=*.cs
AvatarJewelSocketOnTheWire.cs:55   ← 唯一实现
GameInventoryDelivery.cs:192       ← P1
Entry.cs:188                       ← P2（F-1 / F-2）
Entry.cs:781                       ← P3（F-3）
Entry.cs:302                       ← 文档 <see cref>
```

生产代码里剩下的每一个珠宝相关的 `30`（**共 8 处**，**按代码位置计**——
下面有两项各含两个位置，逗号分隔项只有 6 个）**全部说的是存储长度**，逐条已核，均未改动：
`CharacterDatabaseSchema.cs:199`（建表 CHECK）、`InventoryEntryAssembly.cs:152` 与 `:394`（装配层校验）、
`ItemContainers.cs:67`（新建时装的零填充长度）、`GameInventoryDelivery.cs:137-138`（P1 入口校验）、
`Entry.cs:187` 与 `:780`（P2/P3 入口校验的错误文字）、`Entry.cs:306`（`StoredJewelBytes` 定义）。

---

## 七、本清单的已知局限

- **18 这个值本身没有实机证据，本轮也没有产生任何实机证据。** 依据只有一条：
  上游三个写入方共用 `A21AvatarJewelBytes = 18`（源码读出）。
  实机在原理上分辨不了 18 与 30，理由见 Spec 的 `NA-001`。**本清单不把「不崩」当证据。**
- **F-1 / F-2 的真实上线尚未发生**，因为时装还穿不上（issue #11）。
  所以这两条的正确性今天只由判据保证，没有任何线级样本。
- **`0x0061` 的完整布局仍未做**。本轮只记了附件条目里外观块这一小段，
  该协议其余偏移一个都没有确证——`NOTI-0x0061.md` 的新小节里已写明这一点。


---

## 八、评审后的整改（2026-09-07）

独立评审判 `changes_required`，2 条阻塞、5 条非阻塞。逐条处置如下。

### BF-1（阻塞）：契约修正案 C-1 枚举不全，且被枚举到的条款只执行了一半

**属实。** 详见契约的「C-1 的补充（review 阶段）」，那里记了这是同一类失误的**第六次**、
以及这一次的错法与前五次都不同（前五次错在**搜索**，这一次搜索是对的，错在**枚举与执行之间**）。

**复核时又发现评审给出的清单本身也不全**：它列了 9 处，按其线索复扫又找出 4 处
（契约 `:52` 表下的「前者／后者」与那张两行表本身、`:58` 上游只列了两个写入方少了
`MailboxHandler.cs:1127`、`:217` Assumptions；路由 `:11` 同上游问题）。
**所以「按外部清单逐条改完」也不等于改干净了。**

改完后按 `grep -n "两条" TaskContract.md routing-decision.md` 复扫并**逐行判定**。

**这里不再报命中总数。** 初版报「13 处」，实际是 14 且分类只覆盖 13（漏 `routing-decision.md:37`）；
第二轮整改改报「21 处」，而这两份文件每被追加一段修正案就会多出若干命中，
**任何写下来的总数都会在下一次追加时过期**（第三轮复审实测已是 28）。
可复现且不随文档增长而变的说法只有一条：

> **两份文件里，`## Contract Amendment C-1` 及其各节补充之外，
> 没有任何一处规范性陈述仍说「两条路」。**

那几节之内的命中按构造是正确的——它们的职责就是引述被推翻的说法。
节外命中的逐行判定见契约「C-1 的补充」里的那张表。

### BF-2（阻塞）：`NOTI-0x0002.md` 新小节把既有小节的最后两段改挂到了新日期名下

**属实。** 新增的 `### 2026-09-07` 插在 `### 2026-08-26：……60 字节` 中间，
把「时装槽的边界是 11」与「技能第二页」两段挪到了新标题之下，后者与珠宝毫无关系。
**已把新小节整体下移到那两段之后**，两段归位到 `2026-08-26` 名下。

### 非阻塞 5 条

| | 处置 |
|---|---|
| NB-1 清单说「共 7 处」而列了 8 处 | **已改为 8 处**，口径「按代码位置计」（初次写的口径是「按逗号分隔项计」，那个规则只得 6，由第三轮复审指出后改正） |
| NB-2 M1/M2/M4 只报了条数，缺 `EV-003` 要的判据名与错误信息 | **已补齐**，见第六节新增小节；补的过程中顺带记下评审独立发现的「字节锚在 M1/M4 下会红」 |
| NB-3 `NOTI-0x0061.md` 判「尚未实现」不准时，没回应原句自带的限定 | **属实，是把话说过头了。** 原句自带「指的是服务端侧没有对应 slice」，而 `Protocol/Game/` 下**确实没有邮件切片**（本轮实扫零命中），**按它自己的定义那句话仍然成立**。已改写为「一处容易读岔的记载」，只说明本帧确实在发，并明写「原句不改，它没有错」 |
| NB-4 `sdd-execution.yaml` 声明的 `spec-result.md` 不存在 | **不改**：该产物按流程在终态封存时才写，声明先于存在是 Profile 的正常形态 |
| NB-5 两条新判据的定位方式可以更稳 | **部分加固成立，另一半是坏的、已在第二轮删除。** 成立的一半：`TheColoursFollowTheJewelsUnchangedInEveryFrame` 补了 `Assert.NotEqual(-1, jewels)` 前置（原先找不到时会以偏移 17 读下去），这条保留至今。坏的一半：给 `AnOrdinaryItemInACostumeSlot…` 加的 `Assert.Equal(-1, IndexOfRun(frame, [(byte)avatarLength, 0, 0, 0]))` 会**假红**，已删，经过见第九节 BF-4。**本行初版写的「这条加固使该判据在 M4 下也会红了」是错的**——该判据在 M4 下是**绿**的，且那句话把一条已删除的断言描述成现存 |

### 第一轮整改带来的判据变化

无新增或删除判据，仍是新增 8 条、修改既有 1 条。
**本节说的是第一轮当时的状态。** 第二轮又删掉了其中一条断言（那条会假红的），
净结果见第九节末尾。


---

## 九、第二轮复审后的整改（2026-09-07）

第一轮整改被复审判 `changes_required`，2 条新阻塞——**都出在整改本身，不在代码**。
复审同时确认：代码与判据自第一轮以来**逐字符未动**（散列比对），
前一轮已通过的部分没有被破坏。

### BF-3（阻塞）：只改了散文，没改它底下的表与代码块

**属实。** 契约 Context 的小标题与散文改成了「三条路」，但底下那张表仍是两行、
上游那段代码块仍只有两个写入方；更糟的是「对两条路都用同一个常量」被改成「三条」之后，
因为那句话的主语是 `ItemListProtocolWriter.cs`（该文件只有两个写入方），
**一句真话被改成了假话**。

**已改**：表补第三行（邮件附件，`Entry.cs:770-771`）；那句话改写为
「常量定义在 `ItemListProtocolWriter.cs`，三个写入方分居两份文件」，代码块补上
`MailboxHandler.cs:1127-1128`。

**这条是本清单自己的责任**：第一节「扫法」写的三层错法全是**搜索**层面的，
而 `grep` 一个词**只命中散文**——表格、代码块、行号里的同一个事实不含那个词。
**这是第四层，已补进契约的「C-1 的补充之二」。**

### BF-4（阻塞）：变异证据的因果归因不实

**属实，而且比归因错更严重**：被归功的那条断言在 M4 下红，是因为
`[20,0,0,0]` 这个四分之三是零的模式，在一个**根本没有珠宝运行**的帧里被偶然撞上——
**假红**。一个在代码正确时也会失败的检查比没有更糟。

**已删掉那条断言**，M4 重跑得 15 条并逐条列全（见第六节）。
上面表格与叙述里所有「16」已改回「15」。

**根因**：加固判据时**没有实跑验证归因就写进了清单**——凭「我加了一条，红条数多了一条」
推断因果。变异证据必须报**实际红的那一行**。

### 非阻塞 3 条

| | 处置 |
|---|---|
| M4 名单短一条（缺 `TheColoursFollow…`） | **已补**，并按 5+7+3=15 给出分组计数 |
| 「13 处命中」按其自述命令不可复现（实为 21 行，且当时的分类漏判 `routing-decision.md:37`） | **第二轮只改了契约里的那一处，漏了本文件第八节里的同一句**（第三轮复审 NB-6(b) 指出）。而且改后写的「21 行 / 12 行」**同样会过期**——第三轮实测已是 28。**第三轮改法**：两处都不再报总数，改报一条不随文档增长而变的不变式 |
| NB-1 计数口径不自洽 | **第二轮报的「已改」不实**——「8 处」是对的，但当时给的口径「按逗号分隔项计」按其规则只得 6，口径本身没被修好（第三轮复审 NB-7 指出）。**第三轮已改为「按代码位置计」**并说明为何与逗号项数不同 |

### 第二轮整改带来的判据变化

删除断言 1 条（假红），无新增、无删除判据。仍是新增 8 条、修改既有 1 条。
删掉的是一条**会在代码正确时失败**的断言，因此这是加强而非放宽。

---

## 十、第三轮复审后的整改（2026-09-07）

第三轮复审判 `changes_required`，**2 条新阻塞，仍然全部落在「前几轮整改本身的记载」上**。
复审同时明确表态，这一点必须原样记下：

> **第一层（关于事情本身的记载）：已经准确，可以接受，不需要再改。**
> 三条路径各是哪一帧哪一行、18 的依据是上游哪三个写入方哪几行、常量定义在哪个文件、
> 实机为何分辨不了、每条判据守什么、四次变异各红了哪些、剩下的每个 `30` 为何是存储长度、
> 三份协议文档的证据强度标注与日期小节归属——**没有一条不成立**。
>
> **第二层（关于前两轮整改本身的记载）：仍有实质性不实。**

### BF-5（阻塞）：第二轮点名两处，只改了一处

第二轮 BF-4 点名的假归因有**两个副本**：第六节的叙述与第八节 NB-5 那一行。
**只改了第六节。** 第八节那行原样活着，说「这条加固使该判据在 M4 下也会红了」——
实测是**绿**，且它把一条**已被第二轮删除**的断言描述成现存，
还与同一份文件第六节改后的说法直接矛盾。

**根因**：第二轮的改法是**按「16」这个词去 grep 着改**，而第八节那句假归因**不含 `16`**。
**这正是「补充之二」刚写下的第四层教训（`grep` 一个词只命中散文），
被写下它的同一次整改绕过去了。**

**已改**：第八节 NB-5 那行重写，如实写明「加固的一半成立、另一半是坏的已删」，
并点名「本行初版那句话是错的」。

### BF-6（阻塞）：第九节处置表把没做的事写成「已改」

第九节是下一个人用来判断「哪些可以跳过」的那张表，**它有两行不实**：

- 「13 处命中」那行写「已按可复现口径重写」，实际**只改了契约里的一处，漏了本文件第八节的同一句**；
  且新写的「21 行 / 12 行」同样会过期（第三轮实测 28）。
- 「NB-1 计数口径不自洽」那行写「已改」，实际**口径本身一字未动**——
  「按逗号分隔项计」按其规则只得 6，而枚举有 8 个位置。

**已改**：两行都重写为如实陈述，并写明第三轮的实际动作。

### 非阻塞项一并处理

| | 处置 |
|---|---|
| 复扫命中总数会随文档增长而过期 | **不再报总数**。改报一条不变式：「修正案各节之外，没有任何一处规范性陈述仍说两条路」——它不随追加而变，且可复现 |
| 第六节证据块两处行号偏差 | **已改**：`AvatarJewelSocketOnTheWire.cs:49/58/59/60/61` → `:49/57/58/59/60`；`GameInventoryDelivery.cs:191` → `:192` |
| 第六节指针写「见第八节 BF-4」而 BF-4 在第九节 | **已改** |
| 第八节收尾「没有任何断言被放宽」已被第二轮改变 | **已加限定**：该节说的是第一轮当时的状态，净结果见第九节末尾 |

### 复审独立做的一次变异（M5），收进本清单

第三轮复审自己设计并跑了一次本清单没有的变异，用来回答
「删掉那条假红断言之后，`AnOrdinaryItemInACostumeSlot…` 是不是弱到不该接受」：

**M5**：把 else 分支改成 `writer.Write(0); writer.Write(new byte[18])`，
即模拟「else 分支写了 L 个零」这个正是该判据要挡的错法。
结果：该判据**红**在长度差那一行（`Expected 180 / Actual 198`）。

**所以删掉那条断言没有让它失去证伪能力**，SC-4 仍被钉住；
且它文档注释里对「两条断言各能证伪什么、不能证伪什么」的描述与实测逐字相符。
**这条证据是复审做的，不是执行方做的**，如实标注。

### 第三轮整改带来的判据变化

**零。** 本轮只改记载，一行判据代码都没有动。
判据的净状态仍是：新增 8 条、修改既有 1 条；第二轮删掉的那条会假红的断言不再计入。
