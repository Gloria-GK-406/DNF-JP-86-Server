---
kind: rks-review
verdict: approved
---

# Change Review：珠宝槽在线上只有一个长度

独立复核，fresh context。执行方的自评结论一条未采信；下面每一条都给了我自己跑出来的命令与输出，
或我自己从代码读出的行号。变更集 = `git status` 的 6 个已跟踪文件 + 2 个未跟踪新文件 +
`.rks-agent/tasks/one-length-for-the-jewel-socket/` 下的流程产物。基线 `d94e1841`，未提交。

**结论：代码、判据、变异证据全部成立且经我独立复现；两条阻塞性发现都在记账层——
恰好是本仓历次失败的那一层。**

---

## Blocking Findings

### BF-1 契约修正案 C-1 的枚举不全，且被点名的那一条没有执行完

**错在哪。** C-1（`TaskContract.md:233-234`）写：

> revision 1 的 Scope In、Constraints 第一条、SC-1、SC-2、SC-3 都写的是「两条路」

我逐条核了这五处：Scope In 加了邮件那条与「三条路共用」、Constraints 第一条改成了
「恰好两处」并列出两处、SC-1 加了「与邮件附件」、SC-2 与 SC-3 都改成了「三条路」。
**这五处确实都动过。但**：

1. **C-1 点名的 Scope In 只改了一半。** `TaskContract.md:126`（Scope In → 判据的第一条）
   仍写：

   > 钉住两条路发出的珠宝运行长度**相同**，且把任一路改回 30 会红；

   而同一份文件的 SC-3（:182-183）写的是「**直接比较三条路**发出的珠宝运行长度相同」。
   同一份契约里两条规范性条款对同一件事给出不同的基数，且低的那条正落在 C-1 说要改的段里。
   这就是任务书点名的「枚举了却没执行」。

2. **Goal 从未被枚举，也从未被改。** `TaskContract.md:109-110`：

   > 让珠宝槽在**线上只有一个长度**：把穿戴条目那条路从 30 改成 18，并让两条路读同一个常量、
   > 用同一段写法……

   Goal 是整份契约里最吃重的一句，它今天说的交付物（两条路读同一个常量）比实际交付的
   （三条路）少一条。C-1 的枚举清单里没有它。

3. 另有三处规范性/半规范性文字同样停在两条，C-1 都没枚举：
   `:92`「验收只能靠『与上游一致』加『两条路互相钉住』」；
   `:120`「本轮已核实两条路都是 u16」（颜色现在也是三条路都写）；
   `:211` Requires Confirmation「若『两条路共用一处』在不违反切片架构的前提下做不到」。
   以及描述性的 `:43-49`「#### 两条路，两个数字」小节，其表只有两行，缺 P3。

   计数口径：`grep -n "两条" TaskContract.md` 共 **17 处**命中，我逐条读过并分成三类：
   - **5 处是转述 issue #9 原文或 revision 1 自身的历史文字**（:23、:24、:233、:234、:264）——对；
   - **5 处指的确实是两个东西**（:58 与 :217 上游 `ItemListProtocolWriter` 的两个写法、
     :148 两种条目格式、:246 C-1 里「与另外两条同形」、:262 C-1 给所有者的两条路线）——对；
   - **7 处（:43、:45、:92、:109、:120、:126、:211）指的是「珠宝槽的上线路径条数」，
     全部已被 C-1 推翻**（:43 与 :45 是同一个小节的标题与首句）。

4. **`routing-decision.md` 大体同步了，但残留两处。** 它多处点名第三条与 C-1
   （「要改的位置有两处，覆盖三条帧」「第三条是写 spec 前的扫描新发现的」:11、:36），
   这一份没有重蹈上一轮「整份漏掉」的覆辙。但 `:22`
   （「验收只能靠『与上游一致』加『两条路互相钉住』」，与 `TaskContract.md:92` 是同一句话的复制）
   与 `:54`（Reroute 条件「若『两条路共用一处』……做不到」，与 `TaskContract.md:211` 同源）
   仍停在两条。

**影响哪条要求。** 直接违反 SC-8（「显式列出本轮推翻的说法有哪些」——「珠宝槽有两条上线路径」
正是 `behavior-change-list.md` 第五节自己列为 C-2 的被推翻说法，而它在契约正文里活着）
与契约 Constraints「记载不得写成比证据强 / 不写读者无法证伪的基数」。
实际后果：下一个读契约的人会从 Goal 与 Scope In 读到「两条路」，与 SC-3 冲突，
必须靠 C-1 修正案自行推断哪条作数——这正是本轮要偿的那笔债的形状。

**证据。**

```
$ grep -c "两条" .rks-agent/tasks/one-length-for-the-jewel-socket/TaskContract.md
17
$ grep -n "两条" .rks-agent/tasks/one-length-for-the-jewel-socket/TaskContract.md
23:> - 两条路共用同一个常量，不再各写各的                  ← issue #9 原文，历史，对
24:> - 有判据钉住「两条路发出的珠宝槽长度相同」            ← issue #9 原文，历史，对
43:#### 两条路，两个数字                                  ← 已过期
45:同一份珠宝槽数据，本仓在两条协议路径上写出不同的长度： ← 已过期
58:**对两条路都用同一个常量 A21AvatarJewelBytes = 18**    ← 指上游两个写法，对
92:「与上游一致」加「两条路互相钉住」，不能靠实机          ← 已过期
109:……把穿戴条目那条路从 30 改成 18，并让两条路读同一个常量 ← Goal，已过期，C-1 未枚举
120:（本轮已核实两条路都是 u16）                           ← 已过期
126:  - 钉住两条路发出的珠宝运行长度**相同**               ← Scope In，C-1 点名却未执行
148:两条路的条目格式本来就不同                             ← 对
211:- 若「两条路共用一处」……做不到                         ← 已过期
217:  两条写法各引用一次                                   ← 指上游两个写法，对
233:**……有三条上线路径，不是两条。**                       ← C-1 自述，对
234:SC-1、SC-2、SC-3 都写的是「两条路」                     ← C-1 自述，对
246:形状与另外两条完全相同                                 ← C-1，指另外两条，对
262:所有者被告知了两条路线（收进来 / 另开 issue）           ← C-1 自述，对
264:**初版为什么会写成两条**                               ← C-1 自述，对

$ grep -n "两条" .rks-agent/tasks/one-length-for-the-jewel-socket/routing-decision.md
11:  两条写法（WriteAvatarEntry126:89、WriteNoti2AvatarBlock:193）引用的是同一个常量  ← 对
22:加「两条路互相钉住」。这不是遗漏……                     ← 已过期
36:契约 revision 1 照抄 issue #9 写的「两条路」            ← 历史转述，对
54:  - 若「两条路共用一处」……做不到                        ← 已过期
67:**保护的两条失败路径都不是「代码写错」**                ← 指两条失败路径，对
68:二是「顺手多改了一处既有断言」。挡住这两条……            ← 同上，对
```

**要做什么。** 把 `TaskContract.md` 的 :43、:45（含表补 P3 一行）、:92、:109、:120、:126、:211
与 `routing-decision.md` 的 :22、:54 改成三条，并在 C-1 下补一条修正案补充，如实写明
「C-1 的枚举清单本身不全（漏 Goal、:92、:120、:211），且被枚举的 Scope In 只执行了一半（:126 未改）」。
Goal 与 Scope In 是所有者批准过的条款，改它们要按本仓规矩留痕，不得静默改写。

---

### BF-2 `NOTI-0x0002.md` 新插的日期小节把两段既有记载吞进了它的作用域

**错在哪。** 新小节 `### 2026-09-07：时装槽的条目在放大值之后多一段外观块`
插在 `NOTI-0x0002.md:690`，位置在既有小节
`### 2026-08-26：子类型 1 的装备条目是 60 字节，且三个计数块是动态的`（:655）的**中间**。
该小节原本的最后两段现在落到了新小节名下：

- `:720-722` **「时装槽的边界是 11，不是 10。」**——讲 `IsCostumeBarSlot` 的槽位边界
- `:724-725` **「技能第二页：与第一页相同就写 0。」**——讲 SKILLINFO 第二页，与珠宝毫无关系

**影响哪条要求。** 违反 SC-7 与契约「按规矩补记而不是悄悄改掉」的记法：这两段文字一个字没动，
但它们现在读起来是 2026-09-07 这一轮关于外观块的结论。「技能第二页」那段被挂到一个讲珠宝槽的
日期小节下，来源与日期都错了。这是既有记载被本轮**静默改变了归属**——不是内容错，是溯源错，
而本仓的协议文档正是靠日期小节做溯源的。

**证据。**

```
$ grep -n "^#" protocol/game/server-to-client/NOTI-0x0002.md | awk -F: '$1>600 && $1<780'
655:### 2026-08-26：子类型 1 的装备条目是 60 字节，且三个计数块是动态的
690:### 2026-09-07：时装槽的条目在放大值之后多一段外观块   ← 本轮新增
727:### 2026-08-26：子类型 2 的外观只到槽位 13

$ sed -n '717,726p' protocol/game/server-to-client/NOTI-0x0002.md
717 **本仓 2026-09-07 之前在这里发 30**，与同期已改成 18 的背包家族……   ← 新小节最后一段
718 不一致。三条路径现已共用同一处常量，见 ...AvatarJewelSocketOnTheWire。
719
720 **时装槽的边界是 11，不是 10。** A21 在武器之前插了 AuraSkinAvatar（槽位 11）。 ← 既有，08-26
721 本仓此前用 slot <= 10 判断是否写头像块，那是 A12 的边界。**注意**：A21 参照里槽位 11
722 没有物品，这一条来自上游 EquipmentTypeInfo.IsCostumeBarSlot，不是量出来的。
723
724 **技能第二页：与第一页相同就写 0。** 参照的 SKILLINFO 两页各 6 条……          ← 既有，08-26
725 上游 A21ShouldOmitCopiedPage1 明写此规则。两帧不一致是客户端的规矩……
```

**要做什么。** 把新小节整体移到 `:726` 之后（既有 08-26 小节结束、下一个 `###` 之前），
或把那两段留在原小节里、新小节起点下移。内容本身不必改。

---

## 非阻塞观察

### NB-1 `behavior-change-list.md` 的「共 7 处」与它自己列的 8 处不符

第六节写「生产代码里剩下的每一个珠宝相关的 `30`（**共 7 处**）」，随后逐条列出的是 8 处：
`CharacterDatabaseSchema.cs:199`、`InventoryEntryAssembly.cs:152`、`:394`、`ItemContainers.cs:67`、
`GameInventoryDelivery.cs:137-138`、`Entry.cs:187`、`:780`、`Entry.cs:306`。
我自己扫的结果与那 8 处一致（另有 `Entry.cs:300` 是文档行，未列）：

```
$ grep -rn "30" --include=*.cs Server/src/ | grep -i jewel
（命中 10 行：IEA:152、IEA:394、ItemContainers:67、Schema:199、GID:137、GID:138、
  Entry:187、Entry:300、Entry:306、Entry:780）
```

**语义判定完全正确**——剩下的每一个 `30` 说的都是存储长度，一个都不该改。**只是基数写错了。**
契约明写「不写读者无法证伪的基数」；这个基数可证伪，而且不对。

### NB-2 M1 / M2 / M4 的变异证据只给了红条数，没给失败判据名与错误信息

Spec 的 EV-003 要「三次变异各自的**失败判据名与错误信息**；还原后的 `sha256sum` 与复跑结果」。
清单第六节只给了红条数（13 / 6 / 4 / 15）与收敛判据的红绿，以及一句「三次还原后 `sha256sum -c`
均为 `OK`」——没有判据名、没有错误信息、没有散列值。M3 是唯一给了性质描述的
（「全部是本轮新增的判据」）。我抽核的 M3 与 M4 结果与清单完全一致（见下「F. 变异证据」），
所以这是**证据呈现的欠缺，不是证据不成立**。

### NB-3 `NOTI-0x0061.md` 对既有记载「尚未实现」的定性说过头了一点

新小节写：「下面 A12 区的『服务端实现映射』写着『尚未实现』。**那句话已经不准**」。
但 A12 区原文（`NOTI-0x0061.md:177-179`）并不是光秃秃的「尚未实现」：

> 尚未实现。**注意**：本协议已在实机流量里出现（见上方实测字段表），"尚未实现"指的是
> 服务端侧没有对应 slice，不是"没观察到"。

按它自己给的定义（「服务端侧没有对应 slice」），这句话今天**仍然成立**：`0x0061` 没有自己的
切片目录，`MailboxStateMessage` 与 `EncodeMailbox` 住在 `StateSynchronization` 切片里
（`StateSynchronizationMessages.cs:432` 定义、`:622` 注册）。真正过期的是更弱的一层——
本仓现在确实会发这一帧（`SelectedCharacterStateMessageFactory.cs:110`，我核过）。
新小节只引了前四个字就判「不准」，没有回应原句自带的限定语。

**处置方式本身是对的**：不在 A12 区内原地改、把订正写在 `<!-- rks:a12:begin -->` **之前**、
说明该区将随 A21 重做整体删除——这三点我核了文件位置，都成立（新小节结束于 A12 区起始标记之前）。
建议只把措辞收紧成「该句的『尚未实现』今天读起来比事实弱：本仓已会发这一帧，
只是它没有自己的切片，仍住在 `StateSynchronization` 里」。

### NB-4 `sdd-execution.yaml` 声明的 `spec-result.md` 不存在

`artifacts.result: "./spec-result.md"`，但 `.rks-agent/tasks/one-length-for-the-jewel-socket/`
下没有这个文件，且 `implementation` 阶段没有 `writes:` 条目去产出它。
实际承担这个角色的是 `behavior-change-list.md`。要么删掉这条 artifacts 声明，要么把它指向清单。

### NB-5 两条新判据的定位方式存在可以更稳的写法（都不影响本轮判定）

- `TheColoursFollowTheJewelsUnchangedInEveryFrame`（`JewelSocketHasOneWireLengthTests.cs:153`）
  用 `IndexOfRun(...) + ExpectedWireJewelBytes` 求颜色运行的位置，但**没有先断言
  `IndexOfRun != -1`**。找不到时 `afterJewels` 会变成 `-1 + 18 = 17`，判据仍然会红，
  但红在一个与病因无关的偏移上，读起来会误导。
- `AnOrdinaryItemInACostumeSlotDeclaresNoJewelsAndGoesStraightToTheColours`（:140）的第一条断言
  是「帧里存在 `00 00 00 00 04 00 00 00 00 00 00 00` 这一串」。这是一个 12 字节、其中 11 个是零的
  模式，理论上可能被帧里别处的零区偶然命中，从而**假绿**。
  真正扛事的是第二条 `Assert.Equal(-1, IndexOfRun(frame, [18,0,0,0]))`。
  另外这一支其实**已有既有覆盖**：`SelectCharacterPayloadCodecTests` 里槽位 1 放的是
  `ItemKind == 1` 的物品且 1 落在时装栏 `0..11` 内，`populated[137]` 起的那串偏移断言
  就压在 else 分支的 `u32(0) + u32(4) + 0 + 0` 之后。所以 AC-005 不缺覆盖。

---

## 我自己核过的事项与证据

### A. SC-1..SC-8 逐条

| SC | 判定 | 我自己的证据 |
|---|---|---|
| SC-1 | **达成** | 三条路都经 `AvatarJewelSocketOnTheWire.WriteJewelRun`（`GameInventoryDelivery.cs:192`、`Entry.cs:188`、`Entry.cs:781`），该方法写 `u32(18)` + `stored[..18]`（`AvatarJewelSocketOnTheWire.cs:57-60`）。判据用 `0xA0+index` 的可分辨填充（`JewelSocketHasOneWireLengthTests.cs:56`）钉住长度与内容两者；邮件那条零覆盖已由新判据补上——**M3 变异下红的 4 条全部是本轮新增的，一条既有判据都没红**，这就是零覆盖的直接证明。 |
| SC-2 | **达成** | 见下「只有一处」的独立扫描。 |
| SC-3 | **达成** | `EveryFrameDeclaresTheSameJewelLength`（:94-105）把四个发送方的声明长度放进一个数组比较，不指名任何值。M3（单方面把邮件那条改回 30）下该判据红；M4（三条一起改成 20）下该判据绿。两次我都自己跑了。 |
| SC-4 | **达成** | `Entry.cs:191-193` 的 else 分支仍是 `writer.Write(0)`，其后直接 `writer.Write(4)`。判据 `AnOrdinaryItemInACostumeSlot...`（:136）加既有 `SelectCharacterPayloadCodecTests` 的 `populated[137]` 起偏移串双重钉住。 |
| SC-5 | **达成** | `SelectCharacterAcceptedTranscriptTests.cs` **完全未出现在 `git diff` 里**，其中硬编码 62 个 SHA-256（`AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 逐帧比对），全量判据下通过 → 62 帧逐帧未变。**而且这个锚不是死的**：我在 M4 下看到它变红，说明它对珠宝长度确实敏感，只是参照会话里没人穿时装、也没有带时装附件的邮件，所以本轮真实改动碰不到它。 |
| SC-6 | **达成** | 我自己复跑：**2289 通过 / 2 跳过 / 0 失败，退出码 0**（52+567+60+329+6+236+1039 = 2289）。高于基线 2281/2/0。Protocol.Tests 559 → 567。跑前已确认无 `Dfo.Server.Host` 进程。无偶发失败出现。 |
| SC-7 | **达成，但见 BF-2** | `NOTI-0x0002.md:690-718` 记下了外观块的形状与插入位置（`22 amplifyValue` 之后、`24 编年史条数` 之前），偏移表自洽（`4+18+4+2+2 = 30`，非时装 `4+0+4+2+2 = 12`）；`NOTI-0x000D.md:261` 的「本轮未改动」原句原地留着并加了 `<sup>` 脚注指向文末「补记（2026-09-07，第三次）」，补记说明了它何时起不再成立。两处都标明依据是**上游源码读出**、都明写实机分辨不了。0x0002 那一节的**位置**有问题。 |
| SC-8 | **部分达成 → 见 BF-1** | 清单本身正向反向都报了、搜索集显式列出了、被推翻的说法列了三条。但它列为 C-2 的那条被推翻说法（「两条路」）在契约正文的 7 处仍然活着，其中一处正是 C-1 点名要改的段。 |

### B. 我自己从代码出发的反向扫描

我没有核对清单的条目，而是读完整 diff、读新文件全文、扫产线的 `throw` 与分支：

- **生产 diff 极小且我逐行读过**：`GameInventoryDelivery.cs` 删掉 `private const WireJewelBytes`
  与 4 行内联写法、改调共用方法；`Entry.cs` 两处 `!= 30` → `!= StoredJewelBytes`（同值）、
  两处写法改调共用方法、新增一个 `private const int StoredJewelBytes = 30`。
- **新文件 `AvatarJewelSocketOnTheWire.cs` 全文 62 行，`throw` 零处、分支一处**
  （`stored.Length >= WireJewelBytes ? stored[..WireJewelBytes] : new byte[WireJewelBytes]`）。
- **新增生产拒绝：0 条。被移除的生产拒绝：0 条。被改变条件的拒绝：0 条。**
  三处存储长度校验的条件、错误文字都逐字未变（`GameInventoryDelivery.cs:137-138`、
  `Entry.cs:186-187`、`Entry.cs:780`）；`EntryWriterAvatarShapeTests.cs:71` 仍断言
  `"Avatar jewel socket must contain exactly 30 bytes."` 并通过。
- **P1 逐字节等价**：旧写法 `writer.Write(18); writer.Write(stored.Length >= 18 ? stored.AsSpan(0,18) : new byte[18])`
  与新 `WriteJewelRun` 逻辑完全相同。字节锚 62 帧未变、`EntryWriterContainerShapeTests` 未改也未红，
  两者共同佐证。
- **我没有找到第四条正向变化，也没有找到清单漏报的反向条目。**
- **「全量全绿不等于没有新增路径」这一关我是这么过的**：M3 让邮件那条单独回退，
  红的 4 条**全部**是本轮新增判据 → 证明这条路在本轮之前判据覆盖为零，
  而不是靠「全量绿」推断它没被走到。

### C. 任务书点名的六件事

1. **C-1 的执行度** → **不合格，见 BF-1**。`routing-decision.md` 本身同步得不错
   （多处点名第三条与 C-1），没有重蹈上一轮整份漏掉的覆辙，只残留 `:22` 与 `:54` 两句。
2. **R-1「不可达」是真的** → **成立，且定性偏保守（可接受）**。
   我自己找出全部三个调用点：`GameInventoryDelivery.cs:192`（传 `avatar?.JewelSocket ?? []`，
   短输入**可达**）、`Entry.cs:188`（前置 `:186` 要求恰好 30）、`Entry.cs:781`（前置 `:780` 要求恰好 30）。
   所以 P2/P3 上补零分支确实到不了。**更准确地说，这处容忍度对任何调用方都不是新的**：
   P1 本来就有一模一样的写法，本轮只是把它移了位置。清单把它报成「放宽但不可达」是往严处报，
   该报，定性没错。
3. **被加了又拿掉的 `ArgumentNullException.ThrowIfNull(writer)`** → **判断与留痕都对**。
   当前代码确实没有它（`AvatarJewelSocketOnTheWire.cs:55-61` 无任何 `throw`）。
   三个调用方传的都是本地 `BinaryWriter`，传 null 会在下一行 NRE——加它只是把
   `NullReferenceException` 换成 `ArgumentNullException`，是一条零可达性、未授权的新拒绝路径。
   移除正确，留痕正确。
4. **`GameInventoryDelivery.cs:137` 故意不统一** → **理由站得住**。
   本轮要收敛的是**线上长度**（RQ-001/RQ-002），存储长度不在任何 RQ/SC 的收敛范围里；
   Spec 的 AC-007 还明写这三处入口校验「保留还是删除由实现决定」。
   而且 `StoredJewelBytes` 是 `StateSynchronizationPayloadCodec` 的 `private`，
   `GameInventoryDelivery` 本来也用不了它——要共用得再提一个类型，那才是无谓地扩大改动面。
   `Entry.cs` 提常量是因为**那一个文件里有两处**同样的校验，这个理由自洽。
5. **`NOTI-0x0061.md` 的处置** → **位置与手法对，措辞说过头一点，见 NB-3**。
6. **不得把「不崩 / 11 件时装实机全过」当长度证据** → **全部交付物均已核，无一违反**。
   逐处核过：`AvatarJewelSocketOnTheWire.cs:32`（「**No live test can confirm this number,
   and none should be claimed.**」）与 `:36`（「What that live run proved was the shape of the block,
   not the size of this field.」）；`GameInventoryDelivery.cs:180-182`
   （「though a live check cannot tell eighteen from thirty」）；
   `JewelSocketHasOneWireLengthTests.cs:30`（「It is not measured against a live client,
   and it cannot be」）；`NOTI-0x0002.md:711-713`（「上一个成果『11 件时装实机全过』证明的是
   **外观块的形状机制**成立，不是这个长度正确」）；`NOTI-0x000D.md` 补记（「本轮同样没有、
   也不可能有实机证据」）；`NOTI-0x0061.md:132`（「没有实机证据，也不可能有」）；
   契约 Assumptions（:218-220）、Spec `NA-001`、清单第七节、`routing-decision.md:20-21`。
   命中逐条读过，**没有任何一处把「不崩」或上一轮的实机结果当作本件长度正确的证据。**

### D. 判据质量

- **`EveryFrameDeclaresTheSameJewelLength` 的设计成立。** 它把四个发送方的声明长度收进数组、
  逐个与第一个比，不出现任何字面长度。**「改一条会红」与「三条一起改会绿」我都实跑验证过**
  （M3 红 / M4 绿，输出见 F）。这不是恒真断言：四个元素里只有一个是自比，另外三个是真比较。
- **`DeclaredJewelLength`（:178-191）没有循环论证，也不会误匹配。**
  它对每个偏移试读一个 u32 当长度，接受条件是「该长度落在 `[1, 30]` 且其后正好是**存储填充的
  同长前缀**」。它**没有**预设 18 或 30——30 只作为搜索上界出现，而 30 是夹具本身的长度，
  不是被检验的那个数。**不会误匹配**：填充是 `0xA0, 0xA1, …, 0xBD` 严格递增，
  从运行内部第 k 个字节起的子串首字节是 `0xA0+k ≠ 0xA0`，所以「以 `0xA0` 开头」的位置在帧里
  唯一，长度字段的位置随之唯一。M3 下它对邮件帧返回 30、对另外三帧返回 18，判据据此发红。
  一处**已知边界**（不影响本轮，值得知道）：若将来把线上长度改成 **大于 30** 的值，
  该方法会抛 `InvalidOperationException` 而不是返回那个值，收敛判据会以异常形式发红。
  这仍是红，方向对，只是错误信息不如「四者不等」直白。
- **无恒真断言。** 我逐条看了 8 个 `[Fact]`。
- **判据名与它证明的东西相符。** `TheMailboxAttachmentSendsEighteenOfTheThirtyStoredBytes`
  确实断言 18 且断言内容是前 18；`EveryFrameDeclaresTheSameJewelLength` 确实只断言相等；
  `TheTwelveBytesThatStayBehindReachNoFrame` 确实在整帧里搜那 12 字节。没有发现名不副实。
- **可分辨填充 `0xA0+index` 足以分辨三种错法。**
  发 30 → `DeclaredJewelLength` 返回 30 ≠ 18，且 `TheTwelveBytesThatStayBehindReachNoFrame`
  会在帧里找到 `0xB2..0xBD`；截了后 18（`stored[12..]`）→ `IndexOfRun(frame, stored[..18])` 返回 -1；
  补的零 → 同样返回 -1。三种都红。**M3 下这三条判据同时红，实测确认。**

### E. 既有断言有没有被为迁就实现而改写

`git diff Server/tests/` **只有一个文件、只有两个 hunk**，我逐处核过：

| 改动 | 归因 | 核验 |
|---|---|---|
| `347` → `335` | 缩短 12 | 347 − 12 = 335 ✓ |
| `AsSpan(206)` 期望 `30` → `18` | 长度字段本身 | 偏移未动（在运行之前）✓ |
| `Range(0,30)` / `AsSpan(210,30)` → `Range(0,18)` / `AsSpan(210,18)` | 内容截前 18 | 偏移 210 未动 ✓；期望是存储 `0..29` 的**前 18** ✓ |
| `AsSpan(244)` → `(232)`；`AsSpan(246)` → `(234)` | 偏移下移 12 | 244−12=232 ✓；246−12=234 ✓；期望值 `0x1122`/`0x3344` 未动 ✓ |
| `populated[276]` → `[264]`；`AsSpan(277)` → `(265)`；`AsSpan(300)` → `(288)` | 偏移下移 12 | 276−12=264 ✓；277−12=265 ✓；300−12=288 ✓；期望值 `24`/`444`/`555u` 未动 ✓ |

**每一处都恰好是被授权的 12 字节位移或长度字段本身，没有一处期望值被放宽、删除或改成别的含义。**

**「该条目之前的 14 处断言逐值未动」——逐条核对结果：全部属实。**
清单点名的 14 处依次是 `populated[100]`、`[101]`、`AsSpan(102)`、`AsSpan(113)`、`[137]`、
`AsSpan(138)`、`[150]`、`AsSpan(151)`、`[157]`、`AsSpan(158,5)`、`[182]`、`AsSpan(183)`、
`AsSpan(187)`、`AsSpan(194)`——我在 `SelectCharacterPayloadCodecTests.cs:254-276` 逐行数过，
恰好 14 条，且**全部作为未改动的上下文行出现在 diff 里**。
一处口径补充（不是错）：珠宝运行之前其实还有 2 条更靠前的断言
（`ReadUInt16LittleEndian(populated)` 与 `AsSpan(2)`，:247-248），清单与 Spec 的 AC-009
都没把它们算进那 14 条；它们同样未变，diff 可证。所以「14」这个数对应的是清单列出的那份名单，
不是「该条目之前的全部断言」，读者按名单核对不会出错。

### F. 变异证据（我自己跑的，抽核 M3 与 M4）

方法：文件级备份 + `sha256sum`，**未使用任何 git 还原类命令**，每次用完立刻还原并核散列。

```
$ sha256sum <两个文件>   # 变异前
bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529 *…/StateSynchronizationPayloadCodec.Entry.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9 *…/AvatarJewelSocketOnTheWire.cs
```

**M3**（把邮件那条改回 `writer.Write(30); writer.Write(socket);`，`Entry.cs:781`）：

```
失败!  - 失败: 4，通过: 563，总计: 567 - Dfo.Server.Protocol.Tests.dll
  JewelSocketHasOneWireLengthTests.TheMailboxAttachmentSendsEighteenOfTheThirtyStoredBytes [FAIL]
  JewelSocketHasOneWireLengthTests.TheTwelveBytesThatStayBehindReachNoFrame                [FAIL]
  JewelSocketHasOneWireLengthTests.TheColoursFollowTheJewelsUnchangedInEveryFrame          [FAIL]
  JewelSocketHasOneWireLengthTests.EveryFrameDeclaresTheSameJewelLength                    [FAIL] ← 收敛判据红
其余 6 个测试项目全绿（Networking 52、Pvf 60、Persistence 329、Host 6、Infrastructure 236、Application 1039）
```

**与清单一致：4 红，收敛判据红。而且这 4 条全部是本轮新增的判据、一条既有判据都没红
——「邮件那条今天零判据覆盖」由此独立证实。**

还原并核散列：`sha256sum -c` → 两个文件均 `OK`。

**M4**（把共用常量 `WireJewelBytes` 由 18 改成 20，三条路一起动）：

```
失败!  - 失败: 15，通过: 552，总计: 567 - Dfo.Server.Protocol.Tests.dll
红的 15 条里包含：
  EntryWriterContainerShapeTests × 7
  SelectCharacterPayloadCodecTests.SelectCharacterPayload_InventoryPreservesCommonAndAvatarEntryShapes
  SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrect…
  SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline
  JewelSocketHasOneWireLengthTests 的 4 条数值判据（ItemList / SupportCharacter / UserInfo / Mailbox）
  JewelSocketHasOneWireLengthTests.TheColoursFollowTheJewelsUnchangedInEveryFrame
EveryFrameDeclaresTheSameJewelLength 不在失败名单里 —— **绿**
```

**与清单一致：15 红，收敛判据绿。** 这正面证明了收敛判据不是一条冒充收敛的数值判据。
顺带得到一条清单没写的佐证：字节锚在 M4 下**会红**，说明它对珠宝长度确实敏感，
所以「本轮它没红」是真的没碰到它，而不是它本来就钉不住这件事。

还原并核散列：`sha256sum -c` → 两个文件均 `OK`；`git status --porcelain` 与变异前逐行一致。

（M1、M2 我未复跑；M3/M4 全中，且 M1 所改的 P1 与 M2 所改的 P2 都有既有判据覆盖，
清单报的 13 / 6 与「收敛判据均红」在机理上与 M3 同构，无复核必要。）

### G. 整体是否成立

- **分层**：新类型在 `Dfo.Server.Protocol`，只依赖 `System.IO.BinaryWriter`，没有反向依赖。✓
- **切片架构**：`AGENTS.md:338`「切片内不跨协议引用。要共用就上提到 endpoint 层」。
  `AvatarJewelSocketOnTheWire.cs` 落在 `Protocol/Game/`（endpoint 层），
  被 `Protocol/Game/StateSynchronization/` 切片与同在 endpoint 层的 `GameInventoryDelivery` 引用。
  **正是规则要求的做法**，没有把切片边界打开。✓
- **`CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests`**：
  两个文件**都不在 `git diff` 里**，判定谓词与具名豁免清单一字未动，全量判据下通过。✓
- **新增协议帧**：0 个。`AddNoti<>` 注册表未动。✓
- **别的帧的字节布局**：除授权的两处（NOTI `0x0002` 子类型 1 / 消息 `0x019F` 的时装条目、
  消息 `0x0061` 的时装附件）外无改动——字节锚 62 帧未变可证，
  且 M4 下它会红说明这个证明不是空的。✓
- **`TreatWarningsAsErrors` / CS9113**：全量构建通过、退出码 0 → 无「警告即错误」触发。✓

### 「只有一处」的独立扫描（SC-2 / AC-001 / AC-002）

```
$ grep -rn "WireJewelBytes\|WriteJewelRun" --include=*.cs Server/src/
AvatarJewelSocketOnTheWire.cs:45   internal const int WireJewelBytes = 18;   ← 唯一定义
AvatarJewelSocketOnTheWire.cs:49/57/58/59/60                                 ← 定义处自用与文档
AvatarJewelSocketOnTheWire.cs:55   internal static void WriteJewelRun(...)   ← 唯一实现
GameInventoryDelivery.cs:177                                                 ← 文档 <see cref>
GameInventoryDelivery.cs:192       WriteJewelRun(...)                        ← P1
Entry.cs:188                       WriteJewelRun(...)                        ← P2
Entry.cs:302                                                                 ← 文档 <see cref>
Entry.cs:781                       WriteJewelRun(...)                        ← P3
```

**线上长度定义 1 处、截断/补零实现 1 处、生产调用点 3 处，与三条路一一对应。**

```
$ grep -rn "JewelSocket\|jewel" --include=*.cs Server/tests/ | grep 30
（10 行命中，逐条读过：Application 2 处、Persistence 6 处、Protocol 2 处，全部是存储侧夹具、
 存储长度断言、建表 CHECK 文本或存储长度校验的错误文字。没有一处是线上长度。）

$ grep -rn "珠宝" protocol/ | grep 30
（12 行命中：0x0002.md 2 行、0x000D.md 7 行、0x0061.md 3 行。逐条读过：全部是
 「存储 30 / A12 发 30 / 本仓此前发 30 / 发 30 也不崩」这类正确的对照或历史陈述，
 没有一处仍把 30 说成今天的线上长度。）
```

**没有发现任何被漏掉的、仍然主张线上 30 的记载。**

---

# 第二轮：整改后复审

独立复核，fresh context，**执行方与第一轮评审方的自评结论一条未采信**。
下面每条判定都附我自己跑出的命令与输出，或我自己从文件读出的行号。
scope = change。基线 `d94e1841`，仍未提交。本轮我没有修改除本文件以外的任何文件。

**结论：`changes_required`。代码与判据没有任何问题，且经散列证明自第一轮以来一字未动；
两条阻塞发现全部落在整改本身——整改在修一句错话时写下了另一句错话，
以及被点名要改的两个证据块没有跟着散文一起改。这正是任务书要我重点核的那一类。**

---

## 一、前一轮 7 条发现的处置判定

| 发现 | 判定 | 一句话依据 |
|---|---|---|
| BF-1 C-1 枚举不全、Scope In 只执行一半 | **partially-resolved** | 全部**规范性**条款已改对，`:126` 与 SC-3 的冲突真的消失；但 Context 的两个**证据块**没跟上，且改写引入一句新的不实陈述（见 BF-3） |
| BF-2 新小节吞掉既有两段 | **resolved** | `git diff` 显示该文件是纯 30 行插入、**零删除**，两段回到 `2026-08-26` 名下 |
| NB-1 「共 7 处」与列出的 8 处不符 | **partially-resolved** | 数字 8 对，但新写的计数口径按其自述规则只得 6（见 NB-7） |
| NB-2 变异证据缺判据名与错误信息 | **partially-resolved** | M1 我实跑复现，名单与错误信息逐字相符；M4 总数 16 对，但**列出的只有 15 条**（见 NB-6） |
| NB-3 `NOTI-0x0061.md` 说过头 | **resolved** | 已改写为「一处容易读岔的记载」并明写「原句不改，它没有错」，其事实主张我实扫核过 |
| NB-4 `spec-result.md` 不存在 | **not-resolved（执行方选择不改）** | 理由未回应「没有任何 stage 的 `writes:` 产出它」（见 NB-8），非阻塞 |
| NB-5 两条判据可以更稳 | **resolved（加固属实）/ 但其记载不实** | 两条判据确实只增不减；`Assert.Equal(frame.Length + avatarLength, …)` 成立且非恒真——但它**不是**抓住 M4 的那条（见 BF-4） |

### BF-1 我自己重扫的结果

`grep -n "两条" TaskContract.md routing-decision.md`（我跑的，当前工作区）：
**TaskContract.md 18 行 / routing-decision.md 3 行，共 21 行命中**。
逐条判定后，**前一轮点名的 9 处全部已改**：

- 契约 `:92→:95`、`:109→:112`(Goal)、`:120→:123`、`:126→:129`、`:211→:214` 均已成「三条」；
- `:43→:46` 标题、`:45→:48` 首句均已成「三条」；
- 路由 `:22→:22-23`、`:54→:55` 均已成「三条」。

**`:126` 与 SC-3 的冲突确认消失**：现契约 `:129`「钉住**三条路**发出的珠宝运行长度**相同**」
与 `:185-186` SC-3「直接比较**三条路**」一致。`grep -n "三条" TaskContract.md` 的 18 处命中我逐条读过，
Goal / Scope In / Constraints / SC-1..SC-8 / Requires Confirmation / Assumptions
**内部不再有互相打架的基数**。Constraints `:155`「被授权的字节变化恰好**两处**」
指的是两个代码写入方、覆盖三条帧，与 `routing-decision.md:14` 同义，**不是遗漏**。

`spec.md` 我也扫了：`:14`「两条发 30」与 `:101`「两条帧」都指别的东西，**对**。

**一处剩余命中未被执行方判定过**：`routing-decision.md:37`
（「契约 revision 1 照抄 issue #9 写的『两条路』」）是历史转述，**该留**；
但它不在执行方那份「13 处逐条判定」的任何一类里，见 NB-6 之外的下述计数问题。

---

## 二、新发现（整改本身引入的问题）

### BF-3（阻塞）契约 Context 的两个证据块仍是「两条路」的形状，且改写引入了一句新的不实陈述

**错在哪。** 整改把 Context 里的**散文**从「两条」改成了「三条」，但散文底下紧跟的**两个证据块
一个都没改**，其中一个还因此变成了假话。

**(a) `TaskContract.md:48-56`：说三条，表里只有两行。**

```
$ sed -n '46,56p' .rks-agent/tasks/one-length-for-the-jewel-socket/TaskContract.md
46  #### 三条路，两个数字
48  同一份珠宝槽数据，本仓在三条协议路径上写出不同的长度：
50  | 路径 | 位置 | 线上长度 |
51  |---|---|---|
52  | ITEM_LIST 家族（NOTI `0x000D` / `0x000E`） | … | **18** |
53  | 穿戴条目（下称 NOTI-2 条目） | … 硬写 `writer.Write(30)` | **30** |
55  第一条由上一个成果 `308ac02c` 改成 18，后两条**没有跟着改**。
```

标题与首句都承诺三条，表只给两行，紧接着的 `:55`「**后两条**没有跟着改」在表里**找不到落点**。
缺的正是 P3（邮件附件 / `WriteMailboxAvatarDetail` / 消息 `0x0061` / 30）。

**(b) `TaskContract.md:60-69`：把三个写入方按到了只有两个写入方的那个文件头上。**

```
60  上游参考服务器 `ServerS4A21` 的 `Network/Builders/Inventory/ItemListProtocolWriter.cs`
61  **对三条路都用同一个常量 `A21AvatarJewelBytes = 18`**（本轮逐行读出）：
63-69  ← 代码块里只有 89/90 与 193/194 两处，都在该文件内
```

我在上游实扫（**只读**）：

```
$ grep -rn "A21AvatarJewelBytes" --include=*.cs E:/Project/DNF/ServerS4A21/Server/DfoServer
./Network/Builders/Inventory/ItemListProtocolWriter.cs:13:  internal const int A21AvatarJewelBytes = 18;
./Network/Builders/Inventory/ItemListProtocolWriter.cs:89 / :90 / :193 / :194
./Network/Handlers/MailboxHandler.cs:1127 / :1128
```

`ItemListProtocolWriter.cs` 里**只有两个**写入方，第三个在 `MailboxHandler.cs:1127`。
**旧句「对两条路都用同一个常量」对该文件是真的；改成「三条」之后这句话对该文件是假的**，
而底下的代码块仍然只列两条，读者按它去核会核不出第三条。
（契约 `:251-257` 的 C-1 表格里三行是对的、`MailboxHandler.cs:1127` 也在，
所以这不是事实认知错误，是**改写时只动了散文没动证据**。）

**影响哪条要求。** 违反契约 Constraints `:174-175`「记载不得写成比证据强……
『上游源码读出』要标明」——`:61` 声称「本轮逐行读出」，而它给出的逐行证据不支持它的基数。
也违反前一轮 BF-1 的整改指令，该指令**明写**「把 `:43`、`:45`（**含表补 P3 一行**）……改成三条」。
更要紧的是执行方在 `behavior-change-list.md:263` 里**自己把「那张两行表本身」列进了自查找出的 4 处**，
然后没有执行——这与 C-1 补充自己诊断的「找到了、写进清单了，改的时候漏掉」是**同一个形态**，
发生在专为记录该形态而写的那次整改里。

**证据汇总。** `TaskContract.md:48-56`（表两行）、`:60-69`（散文三条 / 证据两条）；
`behavior-change-list.md:263`（自称已找出该表）；上游 grep 输出见上。

---

### BF-4（阻塞）变异证据的因果归因不实：抓住 M4 的不是新加的那条断言

**错在哪。** `behavior-change-list.md:205-209` 写：

> **M4**（共用常量 18→20，16 条）：……`AnOrdinaryItemInACostumeSlot…` 也红
> （**评审后加固时给它补的「时装帧比非时装帧正好长一个珠宝运行」那条断言抓住了它**——
> 初版这条在 M4 下是绿的，红条数由 15 变 16）

`behavior-change-list.md:286` 的 NB-5 行同样把这条长度差断言说成「这条加固使该判据在 M4 下也会红了」。

**「红条数由 15 变 16」是真的，「是那条长度差断言抓住的」是假的。** 我实跑核实：

```
$ sed -i 's/WireJewelBytes = 18;/WireJewelBytes = 20;/' …/AvatarJewelSocketOnTheWire.cs
$ PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Protocol.Tests/…csproj --nologo \
    --filter "FullyQualifiedName~AnOrdinaryItemInACostumeSlot"
  错误消息:
   Assert.Equal() Failure: Values differ
   Expected: -1
   Actual:   90
  堆栈跟踪:
     … JewelSocketHasOneWireLengthTests.cs:line 147
```

红在 **:147**，不是新加的 **:148**。`:147` 是
`Assert.Equal(-1, IndexOfRun(frame, [(byte)avatarLength, 0, 0, 0]))`。

我再把 `:147` 注释掉、保持 M4 不变，单跑该用例：

```
已通过! - 失败: 0，通过: 1，已跳过: 0，总计: 1
```

**所以 `:148` 在 M4 下是绿的**，即**新加的长度差断言对线上长度完全不敏感**。
道理也是闭合的：时装分支写 `4+L+4+2+2`，非时装分支写 `4+0+4+2+2`，差恒为 `L`；
而 `avatarLength` 就是从时装帧读回来的那个 `L`。等式对**任何** `L` 都成立。
（该断言**成立且不是恒真**——它仍然约束两个分支的相对形状，若 else 分支多写或少写字节会红——
但它**不是**、也不可能是抓住 M4 的那条。）

而 `:147` 之所以在 M4 下红，是因为 `avatarLength` 变成 20 后，
它在一个**根本没有珠宝运行**的非时装帧里、于偏移 90 撞上了一串无关的 `[20,0,0,0]`。
AC-005 要钉的性质（非时装仍写 `u32(0)` 且不跟字节）在 M4 下**并没有被违反**，
这条红是**假红**——恰恰是 NB-5 当初提出的「零/松散模式会偶然命中」那个风险，
只不过这次命中的方向是红而不是绿。

**影响哪条要求。** 违反契约 Constraints `:174-175`「记载不得写成比证据强」，
并直接损害 SC-8 与 Spec 的 `EV-003`：变异证据的用途就是告诉下一个人**哪条断言在守哪件事**。
按现在的记载，下一个人会以为长度差断言守着线上长度，于是可以放心改动 `:147`——
而真正在守的恰恰是 `:147`，尽管它守得并不干净。

---

## 三、非阻塞观察

### NB-6 M4 的判据名单只列了 15 条，与它自己写的 16 条不符；「13 处命中」也不可复现

**(a) M4 名单短一条。** `behavior-change-list.md:205-209` 列出的是：四条逐帧判据 +
`AnOrdinaryItemInACostumeSlot…` + `EntryWriterContainerShapeTests` 7 条 +
两条既有 SelectCharacter 判据 + 字节锚 = **4+1+7+2+1 = 15**，而抬头写的是 16 条。
我实跑 M4 得到的 16 条里，名单缺的那条是 **`TheColoursFollowTheJewelsUnchangedInEveryFrame`**：

```
$ …dotnet test …Dfo.Server.Protocol.Tests.csproj --nologo   # WireJewelBytes = 20
失败!  - 失败: 16，通过: 551，总计: 567
  EntryWriterContainerShapeTests × 7
  JewelSocketHasOneWireLengthTests.AnOrdinaryItemInACostumeSlot…
  JewelSocketHasOneWireLengthTests.TheColoursFollowTheJewelsUnchangedInEveryFrame   ← 名单里没有
  JewelSocketHasOneWireLengthTests.The{ItemList,Mailbox,SupportCharacter,UserInfo}Sends… × 4
  SelectCharacterPayloadCodecTests.SelectCharacterPayload_InventoryPreserves…
  SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwner…
  SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_…
  （EveryFrameDeclaresTheSameJewelLength 不在名单里 → 绿，与设计意图一致）
```

这与 NB-1 指出的「说 7 列 8」是**同一个缺陷**，出现在 NB-1/NB-2 的整改稿里。

**(b) 「13 处命中」按它自己给的命令得不出来。**
`TaskContract.md:294` 与 `behavior-change-list.md:267` 都写
「改完后复扫 `grep -n "两条" TaskContract.md routing-decision.md`，13 处命中**逐条判定**」。
我跑同一条命令得到 **21 行**；即使剔除 C-1 补充节自身（契约 1-272 行）也是 **11+3 = 14 行**。
其分类 `2+1+5+2+1+2` 确实等于 13，但那 13 条对不上文件里的 14 条：
**漏掉的是 `routing-decision.md:37`**（历史转述，该留，但从未被判定）。
所以「逐条判定过每一处」这句不成立，且这个基数**读者一跑就能证伪**，
正是契约 Constraints `:175`「不写读者无法证伪的基数」要挡的东西。

**M1 我一并复跑，与清单完全相符**（这一条是**对**的，记下来作为对照）：

```
$ …dotnet test …Dfo.Server.Protocol.Tests.csproj --nologo   # P1 回退成 writer.Write(30)
失败!  - 失败: 13，通过: 554，总计: 567
  既有 9 条：EntryWriterContainerShapeTests × 7、SelectCharacterPayload_InventoryPreserves…、
             AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline
  新增 4 条：TheItemListSendsEighteen…、TheColoursFollow…、TheTwelveBytesThatStayBehind…、
             EveryFrameDeclaresTheSameJewelLength
$ …--filter "FullyQualifiedName~EveryFrameDeclaresTheSameJewelLength"
   Assert.All() Failure: 3 out of 4 items in the collection did not pass.
```

清单 `:186-192` 写的「既有 9 条」与那句 `Assert.All()` 错误信息**逐字属实**。

### NB-7 「共 8 处」的计数口径按其自述规则只得 6

`behavior-change-list.md:232` 写「**共 8 处**，按下面的逗号分隔项计」，
而下面那句用 `、` 分隔的项是 6 个（`InventoryEntryAssembly.cs:152` **与** `:394`、
`Entry.cs:187` **与** `:780` 各被合并成一项）。
按「不同 `文件:行`」计才是 8，这也正是前一轮 NB-1 数出来的 8。
**语义判定我复核了，完全正确**——10 行 grep 命中里，除 `Entry.cs:300`（文档行，两轮都未列）外
全部是存储长度，一处都不该改：

```
$ grep -rn "30" --include=*.cs Server/src/ | grep -i jewel
InventoryEntryAssembly.cs:152 / :394、ItemContainers.cs:67、CharacterDatabaseSchema.cs:199、
GameInventoryDelivery.cs:137 / :138、Entry.cs:187 / :300 / :306 / :780
```

数字对、口径错，**换成「共 8 处，按不同 `文件:行` 计」即可**。

### NB-8 NB-4 的不改理由没有回应前一轮指出的那一半

执行方答「该产物按流程在终态封存时才写，声明先于存在是 Profile 的正常形态」。
这对 `completion_evidence` 成立——`sdd-execution.yaml:43-52` 的 `verification` 阶段
确实有 `writes: - completion_evidence`。但 `result: "./spec-result.md"`（`:10`）
**没有任何 stage 的 `writes:` 指向它**（`:22-23` 写 `spec`、`:41-42` 写 `change_review`、
`:51-52` 写 `completion_evidence`，`implementation` 阶段根本没有 `writes:`）。
前一轮指的正是这一半。非阻塞。

---

## 四、复核：前一轮已通过的部分有没有被整改破坏

**没有。整改只碰了记载与那一个新判据文件。**

**生产代码逐字未动**——我用第一轮评审自己记下的散列直接对：

```
$ sha256sum Server/src/Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9   ← 与第一轮记录逐字符相同
$ sha256sum …/StateSynchronizationPayloadCodec.Entry.cs
bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529   ← 与第一轮记录逐字符相同
```

- **正向 3 条 / 反向 1 条**：由上面两个散列直接推出，生产行为不可能变。
  我另跑了 `git diff d94e1841 -- Server/src/`，与第一轮描述的改动逐行一致
  （`GameInventoryDelivery` 删常量与 4 行内联、改调共用方法；`Entry.cs` 两处 `!= 30` →
  `!= StoredJewelBytes`、两处改调共用方法、新增 `private const int StoredJewelBytes = 30`）。
  三处存储长度校验的条件与错误文字仍逐字未变。
- **T-1 的既有判据改动未变**：`git diff d94e1841 -- Server/tests/` 仍是**一个文件两个 hunk**，
  `347→335`、`AsSpan(206)` 期望 `30→18`、`Range(0,30)/AsSpan(210,30)→(0,18)/(210,18)`、
  `244/246→232/234`、`276/277/300→264/265/288`。**期望值一个未改，偏移位移全部恰好 12。**
- **新判据文件只增不减**：`TheColoursFollow…:161` 多了 `Assert.NotEqual(-1, jewels)`；
  `AnOrdinaryItemInACostumeSlot…` 多了 `:143`、`:148`，`:147` 由写死 `[18,0,0,0]`
  改为 `[(byte)avatarLength,0,0,0]`（今日等价，`DeclaredJewelLength` 把值限在 `[1,30]`，
  强制转换不会截断）。**没有任何断言被放宽或删除。** 仍是 8 个 `[Fact]`。
- **字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 依旧不在 `git diff` 里**，
  全量绿；而 M1 与 M4 下它都红，说明「本轮它没红」不是它钉不住。

**全量判据我自己复跑**（跑前确认无 `Dfo.Server.Host` 进程）：

```
$ tasklist | grep -i Dfo.Server.Host      → 无
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
    dotnet test Server/86JP.Server.slnx --nologo
Protocol 567、Application 1039、Persistence 329、Infrastructure 236(+2 跳过)、
Pvf 60、Networking 52、Host 6  → 合计 2289 通过 / 2 跳过 / 0 失败，退出码 0
```

**2289 / 2 / 0**，高于基线 2281 / 2 / 0，与执行方所报一致。无偶发失败出现。SC-6 达成。

**变异纪律**：全程文件级备份 + `sha256sum`，**未使用任何 git 还原类命令**。
三个被动过的文件每次用完立刻还原并核散列，`sha256sum -c` 三行全 `OK`，
`git status --short` 与开工前逐行一致（6 个已跟踪修改 + 3 个未跟踪项）。

---

## 五、SC 复核（相对第一轮的增量）

SC-1 / SC-2 / SC-3 / SC-4 / SC-5 / SC-6 我按上面的散列与实跑重新确认，**均仍达成**，
第一轮的结论我复现得到、不是采信。SC-7 因 BF-2 已解决而**由「达成但有位置问题」升为达成**。
**SC-8 仍只是部分达成**：清单的搜索集、正向反向、被推翻的说法都在，但
（a）契约 Context 的两个证据块还停在两条路（BF-3），
（b）清单自己的变异证据有一条因果归因不实（BF-4）、一条名单短一项、
一条复扫基数不可复现（NB-6）、一条计数口径不自洽（NB-7）。

---

## 六、要做什么（按代价排序，都不涉及代码）

1. **BF-3**：`TaskContract.md:50-53` 的表补第三行
   （邮件附件 / `Entry.cs:770-771` 硬写 `writer.Write(30)` / 消息 `0x0061` / **30**）；
   `:60-61` 要么把主语改成「上游三个写入方共用 `ItemListProtocolWriter.A21AvatarJewelBytes`」，
   要么在 `:63-69` 的代码块里补上 `MailboxHandler.cs:1127-1128` 两行。
   并在 C-1 补充里如实记下：这一次仍有两处「枚举了却没执行」，且是自查清单里的条目。
2. **BF-4**：把 `behavior-change-list.md:207` 与 `:286` 改成事实——
   M4 下红的是 `:147`（`IndexOfRun([avatarLength,0,0,0])`），
   且它是在一个没有珠宝运行的帧里偶然命中偏移 90，属**假红**；
   新加的 `:148` 长度差断言对线上长度**不敏感**（差与 `avatarLength` 同步变化），
   它守的是两个分支的相对形状，不是长度。若想让该用例真正对长度敏感，
   `:147` 的需要是一个更长、更少零的模式，或直接断言非时装帧里 `DeclaredJewelLength` 抛异常。
3. **NB-6 / NB-7**：M4 名单补 `TheColoursFollowTheJewelsUnchangedInEveryFrame`；
   复扫基数改成实际可复现的数并把 `routing-decision.md:37` 补进判定；
   `:232` 的口径改成「按不同 `文件:行` 计」。
4. **NB-8**：`sdd-execution.yaml:10` 的 `result` 要么删、要么指向 `behavior-change-list.md`、
   要么给某个 stage 补 `writes: - result`。

**代码与判据不需要任何改动。**

---

## 七、我跑过的命令

```
tasklist | grep -i Dfo.Server.Host
git status --short / git rev-parse --abbrev-ref HEAD / git merge-base HEAD d94e1841
git diff --stat d94e1841
git diff d94e1841 -- protocol/game/server-to-client/NOTI-0x0002.md
git diff d94e1841 -- protocol/game/server-to-client/NOTI-0x0061.md
git diff d94e1841 -- protocol/game/server-to-client/NOTI-0x000D.md
git diff d94e1841 -- Server/src/ Server/tests/
grep -n "两条" TaskContract.md routing-decision.md          → 18 行 + 3 行 = 21
grep -c / -o "两条" …（含/不含 C-1 补充节两种口径）           → 21 行 / 14 行(不含补充节)
grep -n "三条" TaskContract.md                              → 18 处，逐条读
grep -n "两条|两处路|两个写入方" spec.md
grep -n "^#" protocol/game/server-to-client/NOTI-0x0002.md   → 小节归属
sed -n '690,740p' protocol/game/server-to-client/NOTI-0x0002.md
grep -rn "A21AvatarJewelBytes" --include=*.cs E:/Project/DNF/ServerS4A21/Server/DfoServer   （只读）
grep -rn "30" --include=*.cs Server/src/ | grep -i jewel     → 10 行
grep -rn "MailboxStateMessage" Server/src/ --include=*.cs
ls Server/src/Dfo.Server.Protocol/Game/                      → 无邮件切片，NB-3 的主张成立
sha256sum AvatarJewelSocketOnTheWire.cs / Entry.cs / GameInventoryDelivery.cs / JewelSocket…Tests.cs
PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo            → 2289/2/0，退出码 0
（M4）sed -i 's/WireJewelBytes = 18;/= 20;/' … + dotnet test Protocol.Tests → 失败 16
（M4）--filter AnOrdinaryItemInACostumeSlot                                 → 红在 :147，Expected -1 / Actual 90
（M4 + 注释掉 :147）--filter AnOrdinaryItemInACostumeSlot                    → 已通过 1  ← :148 对长度不敏感
（M1）P1 回退成 writer.Write(30) + dotnet test Protocol.Tests               → 失败 13（既有 9 / 新增 4）
（M1）--filter EveryFrameDeclaresTheSameJewelLength                         → Assert.All() 3 out of 4
sha256sum -c baseline.sha256（每次变异后）                                   → 三行全 OK
```

**未使用任何 git 还原类命令。未触碰主检出、`client-debug-lab`、`entries-are-what-the-client-reads`。
`ServerS4A21` 只读。除本文件外未修改任何文件。**

---

# 第三轮：二次整改后复审

独立复核，fresh context，**执行方与前两轮评审方的自评结论一条未采信**。
每条判定都附我自己跑出的命令与输出，或我自己从文件读出的行号。
scope = change。基线 `d94e1841`，仍未提交。本轮我没有修改除本文件以外的任何文件。

**结论：`changes_required`。**
**代码、判据、变异证据、以及关于「协议本身」的全部记载我逐条独立复现，无一处不成立。**
**BF-3 完整解决；BF-4 的实质（删掉假红断言、M4 回到 15）也完整解决。**
**但第二轮点名的两个位置各只改了一个，NB-7 一字未改，而第九节的处置表把这三件都写成「已改」——
一条阻塞发现由此而来：记载现在自相矛盾地同时声称一条判据在 M4 下会红、又声称它是绿的，
而我实跑证明它是绿的。**

---

## 一、第二轮 5 条发现的处置判定

| 发现 | 判定 | 一句话依据 |
|---|---|---|
| BF-3 契约 Context 只改散文没改证据 | **resolved** | 表已成三行且三行的基线行号我逐条对过 `git show d94e1841`；上游那句话改写后我在 `ServerS4A21` 实扫核实**为真**；代码块补全；`routing-decision.md:10-12` 同步 |
| BF-4 变异证据因果归因不实 | **partially-resolved → 见 BF-5** | 假红断言确已删除；M4 我实跑得 **15**，分组 **5+7+3** 与清单逐条相符；但被第二轮点名的**第二个位置**（`behavior-change-list.md:286→:289`）原文未动，假归因仍在 |
| NB-6(a) M4 名单短一条 | **resolved** | 名单现列全 15 条，与我实跑的失败名单逐条一致 |
| NB-6(b) 「13 处命中」不可复现 | **not-resolved** | 新写的 `21 / 12 / 9` 我一跑即得 **28 / 19 / 9**；且 `behavior-change-list.md:270` 的「13 处命中」原文仍在（第二轮点名的两处之一） |
| NB-7 「共 8 处」口径不自洽 | **not-resolved（且被记成已改）** | `:235` 仍是「共 8 处，按下面的逗号分隔项计」，其下 `、` 分隔项数**仍是 6**，一字未动 |
| NB-8（承第一轮 NB-4）`spec-result.md` | **not-resolved（执行方持续拒改）** | 非阻塞，不重复论证 |

---

## 二、BF-3 我自己的复核（判定：解决）

**表。** `TaskContract.md:50-54` 现为三行。三行给的都是**基线**位置，我逐条对过基线内容：

```
$ git show d94e1841:.../GameInventoryDelivery.cs | sed -n '222p'
    private const int WireJewelBytes = 18;                                    ← 表行 1 所指，对
$ git show d94e1841:.../StateSynchronizationPayloadCodec.Entry.cs | sed -n '188,189p'
                writer.Write(30);
                writer.Write(item.AvatarDetail.JewelSocket);                   ← 表行 2「:188-189」，对
$ git show d94e1841:.../StateSynchronizationPayloadCodec.Entry.cs | sed -n '770,771p'
            if (socket.Length != 30) throw new InvalidDataException(...);
            writer.Write(30); writer.Write(socket); writer.Write(4);           ← 表行 3「:770-771」，对
```

`:56`「后两条**没有跟着改**」现在在表里有落点。

**上游那句话。** 现文（`:61-64`）：「**对三条路都用同一个常量 `A21AvatarJewelBytes = 18`**…
**常量定义在 `Network/Builders/Inventory/ItemListProtocolWriter.cs`，三个写入方分居两份文件**——
前两个在该文件内，第三个在 `Network/Handlers/MailboxHandler.cs`，跨文件引用它」。
我在上游实扫（**只读**），逐字为真：

```
$ grep -rn "A21AvatarJewelBytes" --include=*.cs E:/Project/DNF/ServerS4A21/Server/DfoServer
ItemListProtocolWriter.cs:13   internal const int A21AvatarJewelBytes = 18;      ← 唯一定义
ItemListProtocolWriter.cs:15                                                     ← 尺寸计算，不是写入方
ItemListProtocolWriter.cs:89 / :90                                               ← 写入方 1
ItemListProtocolWriter.cs:193 / :194                                             ← 写入方 2
MailboxHandler.cs:1127 / :1128                                                   ← 写入方 3，跨文件引用
SelfTests/A21MailboxProtocolSelfTest.cs:131 / :132                               ← 自检，不是写入方
```

契约代码块里的六行与上游逐字相符（我 `sed` 出原文比对）。方法名也对：
`:89` 在 `WriteAvatarEntry126`、`:193` 在 `WriteNoti2AvatarBlock`、`:1127` 在 `WriteA21MailboxItemBlobs`
（我用 `awk` 扫各自上方的方法签名核出）。`:79` 说的 `WriteFixedBytes`（同文件 417 行）
确实在 417 行，确实是「截前 `length` 个、不足补零」。

**结论：第二轮 BF-3 指出的两处（表缺一行、真话被改成假话）都已如实修好，且新句子经我在上游独立复核为真。**

---

## 三、BF-4 我自己的复核（实质解决，但记载留了一半）

**(a) 假红断言确已删除。** 现 `JewelSocketHasOneWireLengthTests.cs:145-153` 只剩两条断言，
`Assert.Equal(-1, IndexOfRun(frame, [(byte)avatarLength, 0, 0, 0]))` 不复存在。

**(b) M4 我自己重跑，与清单完全一致。** 文件级备份 + `sha256sum`，未用任何 git 还原类命令：

```
$ sha256sum .../AvatarJewelSocketOnTheWire.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9    ← 与第一、二轮记录逐字符相同
$ sed -i 's/WireJewelBytes = 18;/WireJewelBytes = 20;/' ...
$ PVF_ARCHIVE_PATH=... dotnet test .../Dfo.Server.Protocol.Tests.csproj --nologo
失败!  - 失败: 15，通过: 552，总计: 567
  EntryWriterContainerShapeTests × 7
    （AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList、AvatarListEntriesLineUpBackToBack、
      TheEquipmentListCarriesTheBlockOnlyForAnAvatar(kind:8)、
      TheAvatarListCarriesTheBlockWhateverTheItemIs(kind:0/2/8)、TheAvatarListsBlockIsTwoLengthPrefixedRuns）
  JewelSocketHasOneWireLengthTests × 5
    （TheItemList…、TheSupportCharacterFrame…、TheUserInfoFrame…、TheMailboxAttachment…、TheColoursFollow…）
  SelectCharacterPayloadCodecTests × 2 + AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline × 1
$ sha256sum -c（还原后）→ OK；git status --short 与开工前逐行一致
```

**5 + 7 + 3 = 15，与 `behavior-change-list.md:205-210` 逐条相符。**
`AnOrdinaryItemInACostumeSlot…` 与 `EveryFrameDeclaresTheSameJewelLength` 都**不在失败名单里**——
清单 `:212-214` 说它们在 M4 下是绿的，**属实**。字节锚在 M4 下会红，`:216` 属实。

**(c) 删掉那条断言之后，该判据没有弱到不该接受的程度——我用一次新变异证明。**
现存两条断言各能证伪什么：

| 断言 | 能证伪 | 不能证伪 |
|---|---|---|
| `:151` `NotEqual(-1, IndexOfRun(frame, [0,0,0,0, 4,0,0,0, 0,0,0,0]))` | else 分支不再写「`u32(0)` 紧跟 `u32(4)` 紧跟两个零 u16」这个形状 | 线上长度；且 12 字节里 11 个是零，理论上可**假绿** |
| `:152` `Equal(withAvatar.Length - DeclaredJewelLength(withAvatar), frame.Length)` | **else 分支多写了任意字节**（含「写了 L 个零」） | 线上长度是多少（差与 `L` 同步变化，对任何 `L` 恒成立） |

我把 else 分支改成 `writer.Write(0); writer.Write(new byte[WireJewelBytes]);`（**M5**，正是「else 分支写了 L 个零」这个错法），单跑该用例：

```
失败! - 失败: 1
   Assert.Equal() Failure: Values differ   Expected: 180   Actual: 198
   at ...JewelSocketHasOneWireLengthTests.cs:line 152
$ sha256sum -c（还原后）→ 两个文件均 OK；git status 与开工前逐行一致
```

**`:152` 确实抓得住这个错法，且红在正确的行、错误信息直指病因。**
判据的文档注释（`:129-143`）说 `:151` 是「shape check and not the proof」、
`:152` 证的是「this branch wrote none of them… It says nothing about how long that run is,
and is not meant to」——**与我实测出的能力边界逐字相符**。SC-4 仍被钉住。

**(d) 但第二轮 BF-4 点名的两个位置只改了一个。** 详见下面的 BF-5。

---

## 四、非阻塞 3 条的复核

**NB-6(a) M4 名单** → 已补 `TheColoursFollowTheJewelsUnchangedInEveryFrame`，与我实跑名单一致。**解决。**

**NB-6(b) 复扫基数** → **未解决，换了个数字仍然不可复现。** 我跑执行方自述的那条命令：

```
$ grep -n "两条" TaskContract.md routing-decision.md | wc -l
28
$ grep -c "两条" TaskContract.md routing-decision.md
TaskContract.md:25
routing-decision.md:3
```

契约 `:302` 写「**命中 21 行**」，`:304` 写「其中 **12 行**落在 `## Contract Amendment C-1` 及其两节补充之内」。
按行号切（C-1 起于 `:242`）：**修正案各节内 19 行**（`:244 :245 :257 :273 :275 :285 :288 :290 :298
:302 :305 :313 :314 :316 :317 :336 :338 :342 :355`），**各节外 9 行**。**19 + 9 = 28。**

**9 这个数与那张逐行判定表是对的**——表列的正是 `TC:23 :24 :26 :56 :120 :159` 与
`routing:37 :68 :69` 这 9 行，我逐行读过，判定全部正确（含第二轮指出的 `routing-decision.md:37`，
现已入表）。**错的是 21 与 12**，而 `21` 恰好是第二轮评审当时测得的数——
看起来是**照抄了评审的数字而没有在改完之后自己重跑**。
这与「补充之二」刚刚为 BF-4 写下的根因（「没有实跑验证就写进了记载」）是同一个动作。

**NB-7 计数口径** → **一字未改。** `behavior-change-list.md:235` 仍是
「**共 8 处**，按下面的逗号分隔项计」，其下用 `、` 分隔的项我数了两遍，**仍是 6 项**
（`InventoryEntryAssembly.cs:152` 与 `:394` 合为一项、`Entry.cs:187` 与 `:780` 合为一项）。
第二轮给的可行口径「按不同 `文件:行` 计」没有被采用。
**语义判定仍然完全正确**——我自己扫的 10 行命中里，除 `Entry.cs:300`（文档行，三轮都未列）外
全是存储长度，一处都不该改：

```
$ grep -rn "30" --include=*.cs Server/src/ | grep -i jewel
InventoryEntryAssembly.cs:152 / :394、ItemContainers.cs:67、CharacterDatabaseSchema.cs:199、
GameInventoryDelivery.cs:137 / :138、Entry.cs:187 / :300 / :306 / :780
```

---

## 五、新发现（本轮整改自己引入或留下的）

### BF-5（阻塞）第二轮 BF-4 点名的两处只改了一处；留下的那处现在与同一份文件里的另一句直接矛盾

**错在哪。** 第二轮的整改指令写得很具体：

> **BF-4**：把 `behavior-change-list.md:207` 与 `:286` 改成事实

`:207`（现 `:205-214`）**改得很好**，我实跑逐条核过。**`:286`（现 `:289`）原文一字未动**：

```
$ sed -n '289p' .rks-agent/tasks/one-length-for-the-jewel-socket/behavior-change-list.md
| NB-5 两条新判据的定位方式可以更稳 | **已加固**：…；`AnOrdinaryItemInACostumeSlot…` 不再写死 18，
  改为先从时装帧问出长度再断言非时装帧里没有它，并加了「时装帧比非时装帧正好长一个珠宝运行」的
  长度差断言——**这条加固使该判据在 M4 下也会红了** |
```

这一行现在有**两处与交付物不符的陈述**，且都以「**已加固**」的现在完成时呈现：

1. **「改为先从时装帧问出长度再断言非时装帧里没有它」——那条断言已经在本轮被删掉了**，
   现存的两条里没有它。读者按这一行去核判据会核不到。
2. **「这条加固使该判据在 M4 下也会红了」——假。** 我实跑 M4：
   `AnOrdinaryItemInACostumeSlot…` **不在 15 条失败名单里**，是**绿**的。
   而且这正是第二轮的原话：长度差断言对线上长度**不敏感、也不该敏感**。

**它还与同一份文件 `:212` 直接打架**：

```
:212  **`AnOrdinaryItemInACostumeSlot…` 在 M4 下是绿的，这是对的**：它按设计对线上长度不发表意见。
:289  ——**这条加固使该判据在 M4 下也会红了**
```

同一份清单对同一条判据在同一次变异下的红绿，给出了两个相反的答案。

**为什么这是阻塞而不是措辞。** 第二轮 BF-4 的危害论证是：
「下一个人会以为长度差断言守着线上长度，于是可以放心改动 `:147`」。
`:147` 现在没了，但 `:289` 仍在教读者「长度差断言对线上长度敏感」——
**这条错误认识原封不动地活着**，且落在专门用来记「哪条断言在守哪件事」的那张处置表里。
违反契约 Constraints `:182-183`「记载不得写成比证据强」，并使 SC-8 与 Spec `EV-003` 未完全达成。

**这一次的形态，恰好是「补充之二」刚刚命名的第四层。** 契约 `:348-350` 写：
「`grep` 一个词只命中**散文**……改一个说法时要连它引用的证据一起看」。
而第九节 BF-4 的处置写的是「上面表格与叙述里**所有「16」**已改回「15」」——
**按 `16` 这个词去改**，而 `:289` 的假归因整句不含 `16`，于是没被命中。
**新写下的教训，在写下它的同一次整改里被同一个动作绕过。**

---

### BF-6（阻塞）第九节的处置表有三条陈述与文件的实际状态不符

第九节是专门记录第二轮整改的那一节。它的三行处置表（`:336-338`）里有两行不实，
BF-4 一节（`:326-327`）的收尾还有一处不实：

| 位置 | 声称 | 实际 |
|---|---|---|
| `:337` | 「已按可复现口径重写：**21 行**中 **12 行**落在修正案各节内，其余 9 行逐行列表判定」 | 跑同一条命令得 **28 行 / 19 行 / 9 行**。9 与那张表是对的，21 与 12 不是（见第四节） |
| `:337` | 同上，隐含「那条不可复现的记载已被重写」 | 第二轮点名了**两处**（`TaskContract.md:294` 与 `behavior-change-list.md:267`）。契约那处重写了；**`behavior-change-list.md:270-273` 的「13 处命中**逐条判定**」与它 `2+1+5+2+1+2` 的旧分类原文仍在**，且没有像契约 `:319` 那样就地加「本段初版有错，见下文」的指针 |
| `:338` | 「NB-1 计数口径不自洽 → **已改**：直接给出「8 处」的逐项枚举，口径为「按逗号分隔项计」」 | `:235` 与第二轮引用的原文**逐字相同**，一处未动；「按逗号分隔项计」正是第二轮判为不自洽的那个口径，数出来仍是 6 而不是 8。**把未改写成已改** |
| `:326-327` | 「上面表格与叙述里所有「16」已改回「15」」 | 「16」确已全部改掉（我 `grep` 过），但同一段假归因的非「16」表述漏网 → BF-5 |

**为什么这是阻塞。** 单看被漏掉的三件事，它们各自都只是第二轮的**非阻塞**项，留着不改本来不构成阻塞。
**成为阻塞的是「声称已改而未改」本身**：第九节是下一个人（以及下一轮评审）用来判断
「哪些还需要看、哪些可以跳过」的那张表，它现在会让人跳过三处仍然错着的记载。
这与 BF-5 是同一个形态——**被点名的位置只执行了一部分，然后在处置表里报成全部完成**——
也正是契约 C-1 补充 `:290-292` 所记的「**枚举了却没执行**」，这已是它在本件里的第三次发生
（C-1 初版一次、第一轮整改一次、本轮一次）。

---

## 六、非阻塞观察

### NB-9 `behavior-change-list.md:180` 的交叉引用指向了错误的小节

`:179-180`（本轮新增的那段括注）写「经过见**第八节** BF-4」。
BF-4 在**第九节**（`:320`），第八节只有 BF-1 与 BF-2。我 `grep -n "^## \|^### BF"` 核过小节归属。
一处新引入的、读者一跳就落空的指针。

### NB-10 第九节 `:302-303` 对第二轮结论的转述比第二轮实际说的强

写「复审同时确认：**代码与判据**自第一轮以来**逐字符未动**（散列比对）」。
第二轮实际说的是**生产代码**逐字未动（两个散列），而**判据文件**在第一轮整改里
「只增不减」地加过断言——第二轮把这两件事分开写了。这里把它们并成一句，
读起来像是判据文件也一字未动。非阻塞，但这是第九节自己开头的一句。

### NB-11 「只有一处」的证据块有两处行号对不上

`behavior-change-list.md:222-232` 以 `$ grep -rn ...` 的形式给出输出，但：

```
清单写 AvatarJewelSocketOnTheWire.cs:49/58/59/60/61     实际 :49/57/58/59/60
清单写 GameInventoryDelivery.cs:191   ← WriteJewelRun    实际 :192
```

我跑 `grep -rn "WireJewelBytes\|WriteJewelRun" Server/src --include=*.cs` 得实际值。
**结论（定义 1 处、实现 1 处、调用点 3 处）不受影响**，SC-2 仍达成；
只是以「命令输出」形式呈现的东西不是那条命令的输出。三轮都没被查到，记在这里。

### NB-12 「第四层」的层数与 SC-8 自己列的三层对不齐

SC-8（`:205`）列的三层是「只顺着起因往下游追 / 只搜数字不搜说法 / 搜索集本身没被检查」，
三层**全在搜索**。C-1 补充把这三层收成「搜什么／在哪搜」两层、加「枚举与执行」为第三层，
补充之二据此把 BF-3 记为「第四层」。**内部自洽、可追溯**，但与 SC-8 的原始编号不是同一套。
纯编号问题，不影响任何事实主张。

### NB-13 「至此同一族失误在本仓共 8 次」可追溯，但族的边界是自定义的

链条读者可核：前一个成果的 C-1 补充记 5 次（`the-container-decides-the-entry-shape/TaskContract.md:240`
「同一类失误在本轮出现了五次」，我核过）+ C-1 初版（自记第六次）+ BF-3 + BF-4 = 8。
**基数不是不可证伪的**，符合契约要求。只是 BF-4（「没实跑就写下归因」）与前七次
（「枚举／搜索不全」）严格说不是同一形态，把它并进同一族是一个判断而非事实。非阻塞。

### NB-14 承前两轮：`sdd-execution.yaml:10` 的 `spec-result.md` 仍无任何 stage 产出

执行方第三轮未再回应。第一轮 NB-4 / 第二轮 NB-8 的论证不重复。非阻塞。

---

## 七、前两轮已通过的部分有没有被破坏：没有

**生产代码逐字符未动。** 我用前两轮各自记下的散列直接对，三轮同值：

```
$ sha256sum Server/src/Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9   ← 与第一、二轮记录相同
$ sha256sum .../StateSynchronizationPayloadCodec.Entry.cs
bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529   ← 与第一、二轮记录相同
```

- **正向 3 条 / 反向 1 条**：由上面两个散列直接推出，生产行为不可能变。
  我另读了 `git diff` 全文：`GameInventoryDelivery.cs` 删常量与 4 行内联、改调共用方法；
  `Entry.cs` 两处 `!= 30` → `!= StoredJewelBytes`、两处改调共用方法、
  新增 `private const int StoredJewelBytes = 30`。三处存储长度校验的条件与错误文字仍逐字未变
  （`GameInventoryDelivery.cs:137-138`、`Entry.cs:186-187`、`Entry.cs:780`，我逐行读过）。
- **T-1 的既有判据改动未变**：`git diff -- Server/tests/` 仍是**一个文件两个 hunk**，
  `347→335`、`AsSpan(206)` 期望 `30→18`、`Range(0,30)/AsSpan(210,30)→(0,18)/(210,18)`、
  `244/246→232/234`、`276/277/300→264/265/288`。**期望值一个未改，偏移位移全部恰好 12。**
- **判据文件本轮只减一条断言**，且减掉的是一条会在代码正确时失败的假红断言，
  剩下两条的能力我用 M5 实测（见第三节 (c)）。仍是 8 个 `[Fact]`。
- **字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 依旧不在 `git diff` 里**，
  全量绿；而我实跑的 M4 下它会红，说明「本轮它没红」不是它钉不住。
- **`git status --short`** 与开工前逐行一致：6 个已跟踪修改 + 3 个未跟踪项。

**全量判据我自己复跑**（跑前确认无 `Dfo.Server.Host` 进程）：

```
$ tasklist | grep -i Dfo.Server.Host      → 无
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
    dotnet test Server/86JP.Server.slnx --nologo
Protocol 567、Networking 52、Pvf 60、Host 6、Infrastructure 236(+2 跳过)、Application 1039、Persistence 329
→ 2289 通过 / 2 跳过 / 0 失败，退出码 0
```

**2289 / 2 / 0**，高于基线 2281 / 2 / 0。无偶发失败出现。**SC-6 达成。**

**变异纪律**：全程文件级备份 + `sha256sum`，**未使用任何 git 还原类命令**。
两次变异（M4、M5）各自用完立刻还原，`sha256sum -c` 全 `OK`，`git status --short` 与开工前逐行一致。

---

## 八、SC 复核（相对第二轮的增量）

SC-1 / SC-2 / SC-3 / SC-4 / SC-5 / SC-6 / SC-7 我按上面的散列、实跑与独立扫描重新确认，**均达成**。
新增的独立证据两条：M4 名单我实测（15，5+7+3）；`AnOrdinaryItemInACostumeSlot…` 剩余断言的
能力我用 M5 实测（能抓住「else 分支写了 L 个零」）。

**SC-8 仍只是部分达成**，且这是本轮唯一未达成项：
清单的搜索集、正向反向、被推翻的说法、变异证据的**主体**都成立且经我复现；
但 `:289` 仍在教读者一条被实跑证伪的因果关系（BF-5），
第九节的处置表有三条陈述与文件实际状态不符（BF-6）。

---

## 九、关于「记载能不能收敛」的明确表态

**分两层说，因为这两层现在的状态不一样。**

**第一层：关于「事情本身」的记载——已经准确，可以接受。**
我把每一条会被下一个人当作事实引用的陈述都独立核过：
珠宝槽存 30 发 18、三条上线路径各是哪一帧哪一行、18 的依据是上游哪三个写入方哪几行、
上游常量定义在哪个文件而三个写入方分居两份文件、实机为什么在原理上分辨不了两个长度、
哪条判据守长度哪条守收敛哪条守 else 分支、四次变异各红了哪些、字节锚为什么没红但确实敏感、
剩下的每一个 `30` 为什么是存储长度。**没有一条不成立。**
上游我在 `ServerS4A21` 实扫、代码我逐行读、M4/M5 我实跑。
协议文档三份（`NOTI-0x0002.md` / `0x000D.md` / `0x0061.md`）我读了全部新增内容，
证据强度标注、「不得把不崩当证据」、A12 区处置手法、日期小节归属，**都成立**。
**这一层我认为已经收敛，不需要再改。**

**第二层：关于「前两轮评审整改本身」的记载（第八、九节与两处基数）——仍有实质性不实。**
不是措辞问题，是三条可被一条命令或一次实跑直接证伪的陈述：

1. `:289` 说一条判据在 M4 下会红 —— 我跑 M4，它是**绿**的；同一份文件 `:212` 也说它是绿的。
2. `:289` 把一条**已被删除**的断言描述成现存。
3. `:337` / `:338` 把两件**未做**的整改写成「已改」。

判定标准我按任务书给的：**读者会不会据此做出错误判断**。会。
下一个动这条判据的人，会从 `:289` 得到「长度差断言守着线上长度」这个已被实跑证伪的认识——
**而这恰恰是第二轮 BF-4 论证的那条危害，一字未减地活着**；
下一个读第九节的人，会跳过三处仍然错着的记载。
所以我判 `changes_required`，**但只为这一层**。

**我明确不把下面这些升格为阻塞**（它们是「可以更好」）：
`21/12` 这两个数本身、`按逗号分隔项计` 的口径、`第八节 BF-4` 的错指针、
证据块的两处行号偏差、「第四层」的编号、「共 8 次」的族边界。
它们都被我列成非阻塞观察。**若只剩这些，我会判 approved。**

**我认为它能收敛，而且这一轮就该收敛完**——要改的是四句话，不涉及代码、判据或任何事实认定。
但收敛的前提是**换一种改法**：前三轮的失败都出在「按外部清单给的词去改」
（改「两条」、改「16」、改被点名的那一行行号）。
这一次要改的四处里有三处**不含**任何可以被 `grep` 的关键词，
只能靠**把第八、九两节从头读一遍，逐句问「这句话今天还成立吗」**。
清单自己在契约 `:348-350` 写下的第四层教训，说的正是这件事。

**给下一轮的具体动作（都不涉及代码与判据）：**

1. `behavior-change-list.md:289` 的 NB-5 行：删去或改写「改为先从时装帧问出长度……」
   与「这条加固使该判据在 M4 下也会红了」，如实写成
   「该断言后被证明是假红，已在第二轮整改中删除，见第九节 BF-4」。
2. `behavior-change-list.md:270-273`：像契约 `:319` 那样就地加一句
   「本段的『13 处命中』与其分类是错的，见第九节与契约补充之二」——**不必改动原文**。
3. `TaskContract.md:302` / `:304` 与 `behavior-change-list.md:337`：
   **重新跑一次那条命令**再写数（我跑出来是 28 / 19 / 9），或者干脆只写那个稳定的部分
   （「修正案各节之外命中 9 行，逐行判定如下」），避免写一个每次编辑都会变的自指基数。
4. `behavior-change-list.md:235` 与 `:338`：口径改成「按不同 `文件:行` 计」，
   或把 `:338` 的「已改」改成「未改，理由是……」。
5. 顺带：`:180` 的「第八节 BF-4」改成「第九节 BF-4」。

**代码、判据、协议文档不需要任何改动。**

---

## 十、我跑过的命令

```
tasklist | grep -i Dfo.Server.Host                                  → 无
git status --short / git rev-parse --abbrev-ref HEAD / rev-parse HEAD
git diff --stat / git diff（全文逐行读）
git show d94e1841:.../GameInventoryDelivery.cs | sed -n '218,224p'
git show d94e1841:.../StateSynchronizationPayloadCodec.Entry.cs | sed -n '185,192p' / '768,773p'
grep -n "两条" TaskContract.md routing-decision.md                  → 28 行（TC 25 / routing 3）
grep -c "两条" TaskContract.md routing-decision.md
grep -n "三条\|两处" spec.md
grep -rn "A21AvatarJewelBytes" --include=*.cs E:/Project/DNF/ServerS4A21/Server/DfoServer   （只读）
sed -n '410,435p' ServerS4A21/.../ItemListProtocolWriter.cs                                （只读）
awk '…方法签名…' ServerS4A21/.../ItemListProtocolWriter.cs / MailboxHandler.cs             （只读）
grep -rn "WireJewelBytes\|WriteJewelRun" Server/src --include=*.cs
grep -rn "30" --include=*.cs Server/src/ | grep -i jewel            → 10 行
grep -rn "StoredJewelBytes" Server/src --include=*.cs
grep -n "^#" protocol/game/server-to-client/NOTI-0x0002.md          → 小节归属（BF-2 仍解决）
sed -n '715,732p' protocol/game/server-to-client/NOTI-0x0002.md
grep -n "^## \|^### BF" behavior-change-list.md                     → BF-4 在第九节
grep -n "五次" the-container-decides-the-entry-shape/TaskContract.md
cat -n TaskContract.md / behavior-change-list.md / routing-decision.md / JewelSocketHasOneWireLengthTests.cs（全文读）
sha256sum AvatarJewelSocketOnTheWire.cs / StateSynchronizationPayloadCodec.Entry.cs
PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo      → 2289/2/0，退出码 0
（M4）sed -i 's/WireJewelBytes = 18;/= 20;/' + dotnet test Protocol.Tests  → 失败 15（5+7+3）
（M5）else 分支改写 u32(0)+18 个零 + --filter AnOrdinaryItemInACostumeSlot → 失败 1，红在 :152
sha256sum -c（每次变异后）                                           → 全 OK
git status --short | diff -（每次变异后）                            → 与开工前逐行一致
```

**未使用任何 git 还原类命令。未触碰主检出、`client-debug-lab`、`entries-are-what-the-client-reads`。
`ServerS4A21` 只读。除本文件外未修改任何文件。**

---

# 第四轮：三次整改后复审

独立复核，fresh context，**执行方与前三轮评审方的自评结论一条未采信**。
每条判定都附我自己跑出的命令与输出，或我自己从文件读出的行号。scope = change。
基线 `d94e1841`，仍未提交。本轮我没有修改除本文件以外的任何文件。

**结论：`approved`。**
**代码、判据、变异证据、以及关于「事情本身」的记载，我按第三轮划的那条界重新抽核，无一处不成立。**
**第三轮的 BF-5、BF-6 与它点名的 6 条非阻塞，我逐条实核：BF-5 解决、BF-6 解决、6 条里 4 条解决、
2 条按第三轮自己的定性留作非阻塞。**
**新发现 0 条阻塞、4 条非阻塞观察，全部落在「自述整改过程」这一层，
没有一条会让读者对代码、判据、协议或那条不变式做出错误判断。**

---

## 一、第三轮 2 条阻塞的处置判定

| 发现 | 判定 | 我自己的依据 |
|---|---|---|
| BF-5 第八节 NB-5 那行仍在教一条被证伪的因果 | **resolved** | `behavior-change-list.md:297` 现文三句话我逐句核：① `TheColoursFollow…` 的 `Assert.NotEqual(-1, jewels)` 前置**在**（`JewelSocketHasOneWireLengthTests.cs:165`）；② `Assert.Equal(-1, IndexOfRun(frame, [(byte)avatarLength, 0, 0, 0]))` **不在**（我扫 `IndexOfRun(frame` 得 `:151`、`:164`、`:178`，无此式）；③ **我自己跑 M4**，`AnOrdinaryItemInACostumeSlot…` 不在 15 条失败名单里，**是绿的**——与该行现在的说法一致，与它的初版相反 |
| BF-6 第九节处置表把没做的事写成「已改」 | **resolved** | `:346` / `:347` 两行现文都是如实陈述：`:347` 说「第二轮报的『已改』不实、第三轮已改为『按代码位置计』」——我核了 `:235-236` 的口径确已改且与其枚举自洽；`:346` 说「第二轮只改了契约里的一处、漏了本文件第八节的同一句」，与第三轮的实测一致，不再把未做的事写成已做。**但它新写的「第三轮改法：两处都不再报总数」对契约那一处不成立**——见 NB-16 |

**BF-5 的核心是一条可实跑证伪的因果关系。我实跑了。**

```
$ sha256sum Server/src/Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9   ← 与第一、二、三轮记录逐字符相同
$ sed -i 's/WireJewelBytes = 18;/WireJewelBytes = 20;/' …            ← M4
$ PVF_ARCHIVE_PATH=… dotnet test .../Dfo.Server.Protocol.Tests.csproj --nologo
失败!  - 失败: 15，通过: 552，总计: 567
  EntryWriterContainerShapeTests × 7
    AnItemWithNoAvatarFactsStillWritesTheBlockInTheAvatarList、AvatarListEntriesLineUpBackToBack、
    TheEquipmentListCarriesTheBlockOnlyForAnAvatar(kind:8)、
    TheAvatarListCarriesTheBlockWhateverTheItemIs(kind:0/8/2)、TheAvatarListsBlockIsTwoLengthPrefixedRuns
  JewelSocketHasOneWireLengthTests × 5
    TheItemList…、TheSupportCharacterFrame…、TheUserInfo…、TheMailboxAttachment…、TheColoursFollow…
  SelectCharacterPayloadCodecTests × 2（…InventoryPreservesCommonAndAvatarEntryShapes、
    …SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty）
  + 字节锚 AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline × 1
$ sha256sum -c（还原后）→ OK；git status --short 与开工前逐行一致
```

**5 + 7 + 3 = 15，与清单 `:205-210` 逐条相符。**
`AnOrdinaryItemInACostumeSlot…`、`EveryFrameDeclaresTheSameJewelLength`、
`TheTwelveBytesThatStayBehindReachNoFrame` **都不在失败名单里**——清单 `:212-214` 说前两条在 M4 下是绿的，
**属实**；字节锚在 M4 下会红，`:216` 属实。

---

## 二、第三轮 6 条非阻塞的处置判定（逐条实核）

| 非阻塞项 | 判定 | 我自己的依据 |
|---|---|---|
| `21/12` 这两个数会过期 | **partially-resolved** | `behavior-change-list.md` 侧**已解决**：`:272-274` 只把 13/21/28 当**历史**列出，并明写「这里不再报命中总数」。**契约侧未解决**：`TaskContract.md:302` 仍写「命中 **21 行**」、`:304` 仍写「**12 行**落在…之内」，我跑同一条命令得 **28 行**（`TaskContract.md` 25 + `routing-decision.md` 3），修正案各节内 19、节外 9。第三轮已明确把 `21/12` 定性为非阻塞，我维持该定性；由它派生的新问题见 NB-16 |
| `按逗号分隔项计` 口径不自洽 | **resolved** | `:235-236` 现为「共 8 处，**按代码位置计**——下面有两项各含两个位置，逗号分隔项只有 6 个」。我数了：顿号分隔项确为 6（`CharacterDatabaseSchema.cs:199`／`InventoryEntryAssembly.cs:152` 与 `:394`／`ItemContainers.cs:67`／`GameInventoryDelivery.cs:137-138`／`Entry.cs:187` 与 `:780`／`Entry.cs:306`），其中两项各含两个位置，6+2 = 8。**算术与枚举自洽。** 语义判定我另行独立复核，见下 |
| 指针写「见第八节 BF-4」而 BF-4 在第九节 | **resolved** | `:180` 现为「经过见**第九节** BF-4。」；扫标题核得 BF-4 在第九节（`:329`） |
| 证据块两处行号偏差 | **resolved** | 我跑 `grep -rn "WireJewelBytes\|WriteJewelRun" Server/src --include=*.cs`：`AvatarJewelSocketOnTheWire.cs` 命中 `:45 :49 :57 :58 :59 :60`，`GameInventoryDelivery.cs:177`（文档）；`WriteJewelRun` 命中 `AvatarJewelSocketOnTheWire.cs:55`、`GameInventoryDelivery.cs:192`、`Entry.cs:188`、`Entry.cs:302`（文档）、`Entry.cs:781`。**与清单 `:221-233` 现文逐个相符。** |
| 「第四层」编号与 SC-8 三层不对齐 | **未改，仍为非阻塞** | `TaskContract.md:350` 仍写「第四层」。纯编号，不影响任何事实主张 |
| 「共 8 次」的族边界是自定义的 | **未改，仍为非阻塞** | `TaskContract.md:374` 仍写「共 8 次」。链条可追溯，基数可证伪 |

### 「共 8 处」的语义我自己重扫了一遍

```
$ grep -rn "30" --include=*.cs Server/src/ | grep -i jewel     → 10 行
InventoryEntryAssembly.cs:152 / :394、ItemContainers.cs:67、CharacterDatabaseSchema.cs:199、
GameInventoryDelivery.cs:137 / :138、Entry.cs:187 / :300 / :306 / :780
```

我逐行读过：**10 行全部说的是存储长度，一处都不该改**。清单枚举覆盖其中 9 行
（`137-138` 合为一项），未列 `Entry.cs:300`（文档行里引述建表 `CHECK`）。
**未列的那一行也是存储长度，所以「剩下的每一个都是存储长度」这句话仍然为真**，
读者不会因此判错；只是枚举与 `grep` 行数对不上，记为 NB-15。

---

## 三、那条「不变式」我自己验了：成立，且可复现

清单 `:277-278` 写：

> 两份文件里，`## Contract Amendment C-1` 及其各节补充之外，
> 没有任何一处规范性陈述仍说「两条路」。

**边界是确定的**：扫标题可见 `## Contract Amendment C-1` 在 `TaskContract.md:242`，
是全文最后一个 `##`，其后 `:281`（补充）、`:329`（补充之二）及其 `####` 全部嵌在它之下——
所以「各节补充」= `:242` 到文末，**按标题结构可机械切分，不需要判断**。

**节外 9 行我逐行读过并各自判定：**

| 行 | 原文要点 | 我的判定 |
|---|---|---|
| `TaskContract.md:23`、`:24` | issue #9 接受条件的引文（引用块内） | 引文照录，非规范性陈述 |
| `TaskContract.md:26` | 「issue #9 说的『两条路』已由 C-1 更正为三条」 | 更正陈述本身，正确 |
| `TaskContract.md:56` | 「第一条…**后两条**没有跟着改」 | 指三行表的后两行，正确指代 |
| `TaskContract.md:120` | Goal「把穿戴条目与邮件附件**那两条路**从 30 改成 18，并让**三条路**读同一个常量」 | 指被改的那两条，同句已写「三条路」，正确 |
| `TaskContract.md:159` | Scope Out「不把 `WriteNoti2EquippedItem` 与 `GameInventoryItemEntry.Write` 合并。**两条路**的条目格式本来就不同」 | 指上一行点名的那两个写入方，正确 |
| `routing-decision.md:37` | 记 revision 1 曾照抄 issue #9 写「两条路」 | 历史记载，正确 |
| `routing-decision.md:68`、`:69` | 「保护的**两条**失败路径」 | 另一个意思，正确 |

**不变式成立。** 我另外抽核了它**盖不住**的那一类（第四层教训自己说的那类）：
契约 Context 的表（`:50-54`）现为**三行**、上游代码块（`:71-81`）现列**三个写入方**
（`ItemListProtocolWriter.cs:89/193` 与 `MailboxHandler.cs:1127`），**都不含「两条」这个词**，
但内容是对的。

### 对这条不变式表述本身的两点意见（都不影响它成立）

1. **「规范性陈述」没有界定。** 它把判断留给读者，靠的是契约「C-1 的补充」里那张逐行表兜底。
   我认为这是可接受的：9 行都摆在表里，读者一分钟能自己判完。
2. **它只锚在「两条」这一个词上。** 一处用表格、用「P1 与 P2」、用「两处路径」写出来的
   同一个错误说法不会被这条 `grep` 命中——**这恰恰是本件第四层教训说的那件事**。
   我替它补扫了上面那两个不含该词的证据块，结论是干净的；但**不变式本身不能证明这一点**，
   它证明的是「这个词的每一次出现都正当」，不是「没有别的写法说了同一句错话」。记为 NB-17。

---

## 四、第十节逐条核：转述忠实，M5 我自己重跑了一遍

| 第十节的陈述 | 判定 |
|---|---|
| `:358`「第三轮判 `changes_required`，2 条新阻塞，全部落在前几轮整改本身的记载上」 | **属实**，与第三轮「五、新发现」的 BF-5 / BF-6 一致 |
| `:361-366` 对第三轮「两层」表态的转述 | **实质忠实**（第一层已收敛不需再改、第二层仍有实质性不实；枚举的事项是第三轮原列的子集）。**但它以引用块出现、前一句写「必须原样记下」，而文字是压缩改写、不是逐字**——记为 NB-18 |
| `:370-373` BF-5 的复述（两个副本、只改了第六节、实测为绿、与第六节矛盾） | **属实**，与第三轮原文一致 |
| `:375-377` 根因（按「16」grep 着改，那句不含 16） | **属实**，与第三轮原文一致 |
| `:384-389` BF-6 的复述（两行不实：13 处命中那行、计数口径那行） | **属实**。第三轮标题写「三条陈述」、正文是两行表 + BF-4 收尾一处；第十节按「两行」复述，且第四处已并入 BF-5，**不构成误导** |
| `:398` 两处行号的前后值 | **属实**，我实测现值为 `:49/57/58/59/60` 与 `:192` |
| `:402-413` M5 的设计、数字、归属、结论 | **属实，我自己重跑复现**（见下） |
| `:417-418`「第三轮零判据改动」 | **与我的观察一致**：`JewelSocketHasOneWireLengthTests.cs` 仍是 8 个 `[Fact]`，`:145-153` 的结构与第三轮引述逐字相符，`Server/tests/` 的 `git diff` 仍是一个文件两个 hunk |

**M5 我自己跑了（文件级备份 + `sha256sum`，未用任何 git 还原类命令）：**

```
$ sha256sum .../StateSynchronizationPayloadCodec.Entry.cs
bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529   ← 与第一、二、三轮记录相同
$ sed -i '192s/…/writer.Write(0); writer.Write(new byte[AvatarJewelSocketOnTheWire.WireJewelBytes]);/' …
$ PVF_ARCHIVE_PATH=… dotnet test … --filter "FullyQualifiedName~AnOrdinaryItemInACostumeSlot"
失败! - 失败: 1
   Expected: 180   Actual: 198
   at …JewelSocketHasOneWireLengthTests.cs:line 152
$ sha256sum -c（还原后）→ OK；git status --short 与开工前逐行一致
```

**`Expected 180 / Actual 198`、红在 `:152`、归属标注「这条证据是复审做的，不是执行方做的」——
三项全部与第十节写的一致。** 第十节把 `new byte[WireJewelBytes]` 写成 `new byte[18]`，同值，不构成偏差。

---

## 五、新发现（本轮整改自己引入或留下的）

**阻塞 0 条。** 下面 4 条全部是非阻塞观察，且全部落在「自述整改过程」这一层。

### NB-15 「共 8 处」的枚举漏了 `Entry.cs:300`，与 `grep` 行数仍对不上

口径「按代码位置计」现在与它自己的枚举自洽（6 项 + 2 = 8），**第三轮的 NB-7 已被解决**。
但读者若按上面那条 `grep` 去数会得到 10 行；枚举把 `137` 与 `138` 并成一项，
又整个略去 `Entry.cs:300`（文档行）。**该行也是存储长度，所以「剩下的每一个 30 都是存储长度」
这句正面主张仍然为真**，SC-2 不受影响。三轮都没被列进去，记在这里备查。

### NB-16 第九节 `:346` 新写的「两处都不再报总数」，对契约那一处不成立

`:346` 的上文点名的「两处」是**契约里的那一处**与**本文件第八节的同一句**。第八节确已改
（`:272` 只把总数当历史列）；**契约那一处没改**：

```
$ sed -n '302p;304p' TaskContract.md
**改完后的复扫结果。** 口径：`grep -n "两条" TaskContract.md routing-decision.md`，**命中 21 行**。
其中 **12 行落在 `## Contract Amendment C-1` 及其两节补充之内**……
$ grep -n "两条" TaskContract.md routing-decision.md | wc -l
28
```

契约 `:372` 还写着「上一节已按可复现口径重写」——而「21 行」按其自述命令跑出来是 28。

**为什么我不把它判为阻塞。** 判据只有一条：读者会不会据此做出错误判断。
① `21/12` 本身第三轮已明确定性为非阻塞（「若只剩这些，我会判 approved」），我维持该定性；
② `:346` **在同一行里就告诉了读者这两个数会过期、第三轮实测是 28**，
所以读者不会把 21 当成今天的真值；
③ 契约 `:302` 之后紧跟的**那张 9 行逐行判定表是对的**——我逐行核过——
而那张表才是这段的实质内容，总数只是它的引子；
④ 它不会让任何人对代码、判据、协议或那条不变式判错。
**它的错法确实与第三轮 BF-6 同族（点名两处只改一处、然后报成两处都改了），
这一点必须记下来**；但它遮住的是一个**已被公开标为会过期的自指计数**，
而 BF-6 当时遮住的是一条**在教读者错误技术事实**的句子。两者的后果不在一个量级。

### NB-17 那条不变式只锚在「两条」这一个词上

见第三节末尾。它证明的是「这个词的每次出现都正当」，不是「没有别的写法说了同一句错话」。
我替它补扫了两个不含该词的证据块（三行表、三个写入方的代码块），干净；
但这一步是我做的，不在不变式的保证范围内。

### NB-18 第十节的引用块写着「原样记下」，实际是压缩改写

`:359` 写「复审同时明确表态，这一点必须**原样记下**」，随后的引用块把第三轮的
「第一层：……已经准确，可以接受」与它段末的「这一层我认为已经收敛，不需要再改」
并成了一句，枚举项也做了删减。**实质忠实、无一处曲解**，但形式上不是逐字引用。

### 承前几轮、仍未处置的两条（都不是本轮引入）

- **NB-10（第三轮）** 第九节 `:311`「代码**与判据**自第一轮以来逐字符未动」仍在。
  第二轮实际说的是**生产代码**逐字未动；判据文件在第一轮整改里加过断言。仍为非阻塞。
- **NB-4 / NB-8 / NB-14** `sdd-execution.yaml:10` 声明的 `spec-result.md` 仍不存在
  （`ls` 实核：本任务目录下无该文件，`verification/` 亦空）。执行方四轮持续拒改，
  理由是终态封存时才写。非阻塞，不重复论证。

---

## 六、前三轮已通过的部分有没有被破坏：没有

**生产代码逐字符未动，四轮同值。**

```
$ sha256sum Server/src/Dfo.Server.Protocol/Game/AvatarJewelSocketOnTheWire.cs
36c4ef2746f85904d9f6e2f9eb1049c0a2fa65f2c85f9e06b17d0009ea52d7f9   ← 与第一、二、三轮记录相同
$ sha256sum .../StateSynchronizationPayloadCodec.Entry.cs
bf002d32f63ac75b113a6a748e951aa888b8277cdd3027e386efb1edaa08e529   ← 与第一、二、三轮记录相同
```

- **正向 3 条 / 反向 1 条**：由上面两个散列直接推出，生产行为不可能变。
  我另读了 `git diff` 与三处存储长度校验的现文
  （`GameInventoryDelivery.cs:137-138`、`Entry.cs:186-187`、`Entry.cs:780`）——条件与错误文字逐字未变。
- **T-1 的既有判据改动未变**：`git diff -- Server/tests/` 仍是**一个文件两个 hunk**，
  `347→335`、`AsSpan(206)` 期望 `30→18`、`Range(0,30)/AsSpan(210,30)→(0,18)/(210,18)`、
  `244/246→232/234`、`276/277/300→264/265/288`。**没有一处期望值被放宽，位移全部恰好 12。**
- **判据文件未动**：仍是 8 个 `[Fact]`；`AnOrdinaryItemInACostumeSlot…` 仍只有 `:151`／`:152` 两条断言。
- **字节锚 `SelectCharacterAcceptedTranscriptTests.cs` 依旧不在 `git diff` 里**，
  而我实跑的 M4 下它会红——「本轮它没红」不是它钉不住。
- **BF-2 仍解决**：`NOTI-0x0002.md` 的 `### 2026-09-07`（`:697`）仍在
  「时装槽的边界是 11」与「技能第二页」两段**之后**，两段仍挂在 `### 2026-08-26`（`:655`）名下。
- **`git status --short`** 与开工前逐行一致：6 个已跟踪修改 + 3 个未跟踪项。

**全量判据我自己复跑**（跑前 `tasklist | grep -i Dfo.Server.Host` → 无进程）：

```
$ PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf \
    dotnet test Server/86JP.Server.slnx --nologo
Protocol 567、Networking 52、Pvf 60、Persistence 329、Host 6、Infrastructure 236（+2 跳过）、Application 1039
→ 2289 通过 / 2 跳过 / 0 失败，退出码 0
```

**2289 / 2 / 0**，高于基线 2281 / 2 / 0，与执行方所报一致。无偶发失败出现。**SC-6 达成。**
两次变异（M4、M5）用完各自立刻还原，`sha256sum -c` 全 `OK`，还原后
`Dfo.Server.Protocol.Tests` 复跑 **567 通过 / 0 失败**。

---

## 七、SC 复核

SC-1 / SC-2 / SC-3 / SC-4 / SC-5 / SC-6 / SC-7：**均达成**，依据是上面的散列、实跑、独立扫描
与我自己重跑的 M4 / M5。

**SC-8 我判为达成。** 第三轮把它卡在两件事上：`:289` 教了一条被实跑证伪的因果、
第九节处置表把未做写成已做。**两件都已如实修正，我各自实核。**
剩余的非阻塞观察都不含「被实跑或一条命令直接证伪的技术事实」，
其中最重的一条（NB-16）在它自己那一行里就把过期风险交代给了读者。

---

## 八、对任务书第 5 点三问的明确回答

### A. 现在还有没有「读者会据此做出错误判断」的实质性不实？

**没有。**

我按这条判据把清单里每一类会被下一个人当作事实引用的陈述都过了一遍：
三条路径各是哪一帧哪一行、18 的依据、常量与写法在哪、每条判据守什么、
四次变异各红了哪些、剩下的每个 `30` 为何是存储长度、那条不变式、M5 的数字与归属——
**没有一条会把读者带偏。** 上一轮那条真正有害的陈述（教人「长度差断言守着线上长度」）
已被删除并如实改写，我用实跑 M4 确认了改写后的说法是对的。

NB-16 是本轮唯一一条「声称已改而未改」，但它遮住的是一个**在同一行里已被声明会过期的自指计数**，
且那段的实质内容（9 行逐行判定表）是对的。读者据此不会对代码、判据、协议或不变式判错。

### B. 判定

**`approved`。** 剩余的全部是措辞、编号、可过期计数与元记载瑕疵，已列为 NB-15..NB-18
及承前的 NB-10 / NB-14。

### C. 第八、九、十节是不是已经超出了它应有的价值？

**是。我认为继续打磨它的边际收益已经为负，它本身已经成为新的错误来源，建议就此定稿。**

这不是客套，是从这份文件自己的记录里读出来的：

1. **四轮里所有阻塞发现的来源分布是压倒性的。**
   第一轮 2 条阻塞里 1 条在契约、1 条在协议文档；
   **第二、三轮各 2 条，全部在第八／九节；本轮的唯一新发现 NB-16，仍在第九节。**
   而代码、判据、协议记载**三轮零发现、四轮逐字符未动**。
   这几节现在是这份产物里**唯一还在产生缺陷的部位**。

2. **它的缺陷率没有随打磨下降，形态还在自我复制。**
   第二轮 BF-4 → 第三轮 BF-5（同一条假归因的第二个副本）→ 本轮 NB-16
   （同一条「点名两处只改一处」的第三次）。每修一轮，就在这几节新增一层
   「我上一轮说我改了但没改」的自述，而那层自述本身又成为下一轮的核查对象。
   **这是一个自指的增长过程：记账层每长一段，可被证伪的陈述就多一批。**
   第八节 `:272-274` 现在要用三句话讲「13 → 21 → 28」这个数字的三次修订史，
   而这个数字对交付物本身没有任何用处。

3. **它挤占的正是最该被读的那部分。**
   `behavior-change-list.md` 共 418 行，第八、九、十节占 `:256-418`，**近四成**。
   下一个接手的人真正需要的是第一到第七节：三条路径、18 的依据、三条正向一条反向、
   判据守什么、四次变异、已知局限。这七节 250 行、三轮零发现，
   却被一段越来越长的、只关于「前几轮评审来回」的自述压在下面。

4. **它的读者其实已经不存在了。** 第八、九、十节的唯一读者是**下一轮评审**——
   而本轮之后不应该再有下一轮。对将来读这份清单的人（做 #11、或改这段外观块的人）来说，
   「第二轮点名两处只改了一处」这件事没有任何可操作价值。
   **有价值的是那几条教训**（`grep` 一个词只命中散文；枚举了要执行；变异证据必须报实际红的那一行），
   而这三条**已经被写进 `TaskContract.md` 的「C-1 的补充」与「补充之二」了**，
   那才是它们该在的地方——契约会被下一个任务继承，清单不会。

**我的建议（按优先级）：**

- **首选：就此定稿，一个字都不再改。** 这几节今天的状态是「实质准确、边缘有瑕」，
  而每一次为消除边缘瑕疵的编辑，本仓的历史记录显示都有相当概率引入一处新的自述不实。
  **在这里，「不改」比「改对」的期望收益更高。**
- **次选（若一定要动）：把第八、九、十节整体收敛成一段不超过 15 行的总结**，
  只保留三件事：整改了几轮、每轮的阻塞落在哪一层（代码零、记载 N）、
  以及那三条教训指向契约的哪一节。**删掉所有「我上一轮说我改了什么」的自述**——
  它们的证据价值已经由这份评审文件的四轮记录完整承担了，不需要在清单里再存一份副本。
  **但这个改动本身也要被复审**，所以只在有人另有理由重开这份文件时顺带做，不要为它专门开一轮。
- **不建议**：再为 NB-15..NB-18 中的任何一条单独开一轮整改。它们加起来的信息价值
  低于「再编辑一次这几节」的预期风险。

---

## 九、我跑过的命令

```
tasklist | grep -i Dfo.Server.Host                                   → 无
git status --short / git log --oneline -1 / git diff --stat d94e1841
git diff -- Server/tests/（全文读）
PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo      → 2289/2/0，退出码 0
sha256sum AvatarJewelSocketOnTheWire.cs / StateSynchronizationPayloadCodec.Entry.cs
        / GameInventoryDelivery.cs / JewelSocketHasOneWireLengthTests.cs
grep -rn "WireJewelBytes" Server/src --include=*.cs                  → 45/49/57/58/59/60 + GID:177
grep -rn "WriteJewelRun" Server/src --include=*.cs                   → 55 / GID:192 / Entry:188,302,781
grep -rn "30" --include=*.cs Server/src/ | grep -i jewel             → 10 行，逐行读
grep -n "\[Fact\]|NotEqual|IndexOfRun(frame" JewelSocketHasOneWireLengthTests.cs
sed -n '100,175p' JewelSocketHasOneWireLengthTests.cs                （断言与文档注释全文读）
grep -n "两条" TaskContract.md routing-decision.md | wc -l            → 28
grep -n "两条" TaskContract.md（节外 6 行）/ routing-decision.md（3 行）→ 逐行读并判定
grep -n "^#{1,4} " TaskContract.md / routing-decision.md              → 修正案边界（:242 起至文末）
sed -n '46,83p;118,132p;150,165p;281,330p;329,375p' TaskContract.md   （全文读相关节）
grep -n "21 行|12 行|命中" TaskContract.md                            → :302 / :304 仍为 21 / 12
cat behavior-change-list.md（全文读，逐句核第八、九、十节）
sed -n '780,1202p' reviews/change-review.md                           （第三轮原文，用于核转述）
grep -n "^#{2,4} " protocol/.../NOTI-0x0002.md + sed -n '676,760p'    → BF-2 仍解决
cat sdd-execution.yaml / ls -la（task 目录）                          → spec-result.md 仍不存在
（M4）cp 备份 + sha256sum + sed 's/WireJewelBytes = 18;/= 20;/' + dotnet test Protocol.Tests
                                                                      → 失败 15（5+7+3），逐条列出
（M5）cp 备份 + sha256sum + sed '192s/…writer.Write(new byte[WireJewelBytes])…/'
      + dotnet test --filter AnOrdinaryItemInACostumeSlot             → 失败 1，Expected 180 / Actual 198，:152
sha256sum -c（每次变异后）                                            → 全 OK
dotnet test Protocol.Tests（两次还原后）                              → 567 通过 / 0 失败
```

**未使用任何 git 还原类命令。未触碰主检出、`.worktrees/client-debug-lab`、
`.worktrees/entries-are-what-the-client-reads`。`ServerS4A21` 本轮未访问。
除本文件外未修改任何文件。**
