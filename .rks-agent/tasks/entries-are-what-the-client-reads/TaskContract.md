---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 物品记录写成客户端真正读的样子，并且不再能把客户端打崩

## Context

### Source Request

所有者原话：

> 可以，这边我们先处理能处理的。后面另一个agent有结果了在和你说

指的是我提出的这一件：**按 `vmp1-9.md` 第三节的权威字段表，把 101 字节物品记录的编码收口。**
所有者同时确认：装扮栏（listType 1）那条线**不在本轮**，它的修法取决于另一个 agent 还在反的
格式化调用，服务器侧在那之前不动 type=1 的装填路径。

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，分支 `main`，HEAD `445cb5f7`（只含文档，代码与
`bce6f3a8` 相同）。工作区除另一个 agent 的 `ClientHack/tools/crossmodule-relocs-a21.csv`
（客户端重建产物，**不属于本任务，不得触碰**）外干净。

判据命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
基线（在 `445cb5f7` 实测）：**2250 通过 / 2 跳过 / 0 失败**，退出码 0。逐项目：
Pvf 60、Networking 52、Protocol 528、Persistence 329、Host 6、Infrastructure 236(+2 跳过)、Application 1039。
那 2 个跳过是 `A_missing_archive_*`，只在**没有**档案可达时才跑，所以它们跳过正是「档案确实加载了」的标志。

既知偶发失败，见到不要当成自己引入的回归——按「异常类型 + 栈根 + 复跑即绿」判别，**不按用例名**：
`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根 `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`。

**跑判据前必须停掉服务器进程**：运行中的 `Dfo.Server.Host` 会锁住 `Dfo.Server.Host/bin` 下的
`Dfo.Server.Protocol.dll`，构建以 MSB3027 失败，看起来像判据坏了其实不是。

#### 权威来源

`.worktrees/client-debug-lab/Docs/a21-vmp1/vmp1-9.md` 第三节，由另一个 agent 从 A21 客户端
**解密后的调试字符串**逐字段回绑得出（客户端自己的 `%s` 格式串按顺序打印字段名，把打印源操作数
绑回记录偏移）。它是「客户端把哪个偏移当什么字段」这件事目前唯一的一手证据。

#### 已就地核对的事实（读代码 + 逐字段算偏移得出）

把 `GameInventoryItemEntry.Write`（`Server/src/Dfo.Server.Protocol/Game/GameInventoryDelivery.cs`
第 39–120 行）按写入顺序累加宽度，与权威表并排，**21 个具名字段的偏移一个不差**：

| 偏移 | 权威表字段名 | 本仓写的东西 | 偏移 |
|---|---|---|---|
| `+0x00` i16 | SlotIndex | `item.SlotIndex` | ✓ |
| `+0x02` u32 | itemIndex | `core.ItemId` | ✓ |
| `+0x06` i32 | Data | `core.Value` / 时装剩余秒数 | ✓ |
| `+0x0A` u8 | ext_data1 | `core.Attribute` | ✓ |
| `+0x0B` u16 | Durability | `core.Durability` | ✓ |
| `+0x0D` u8 | isSealed | `core.SealFlag` | ✓ |
| `+0x0E` i32 | enchantIndex | `core.EnchantCardId` | ✓ |
| `+0x12` u8 | enchantUpgrade | `core.EnchantUpgradeCount` | ✓ |
| `+0x13` u8 | amplify_type | `core.AmplifyType` | ✓ |
| `+0x14` u16 | amplify_value | `core.AmplifyValue` | ✓ |
| `+0x16` (表未命名) | — | `core.Marker16` i32 | — |
| `+0x1A` i8 | **珠宝/纹章条目数** | `PopulatedCount(编年史两个 Id)` | ✓ 位置 |
| `+0x1B..0x2A` | 珠宝数组（dword@+0x1B stride 4；属性字节@+0x23/0x25/0x27/0x29） | 编年史两组 | ✓ 位置 |
| `+0x2B` i32 | **period** | `core.ExpireTime` | ✓ |
| `+0x2F` i8 | **附加选项条目数（致命计数）** | `core.EmblemSocketCount` | ✓ 位置 |
| `+0x30..0x49` | 附加选项 dword 数组 | EmblemId1/EmblemId2/Rune/随机选项 | ✓ 位置 |
| `+0x4A` i8 | genuineUpgrade | `core.GenuineUpgrade` | ✓ |
| `+0x4B` i8 | emancipateEquipmentLevel | `core.EmancipateEquipmentLevel` | ✓ |
| `+0x4C` i8 | tradeRestriction | `core.TradeRestriction` | ✓ |
| `+0x4D..0x53` | （表说记录被用满至 `+0x64`） | TailUnknown0-3 / RemainUseCount / SortLockFlag | 部分 |
| `+0x54..0x64` | 客户端确实按字段读（含 `+0x61` dword） | 17 字节零填充 | 未写 |

**布局本来就是对的。** 真正错的是两件，其中第二件此前无人发现：

**一、时装记录不是 101 字节。** 第 110–119 行在定长区之后追加 42 字节
（`4 + 30(JewelSocket) + 4 + 2 + 2`），使时装条目变成 143 字节。客户端按定长 101 跨步读，
于是这一条之后的每一条都错位。实机已证：把这个追加块去掉，主背包不再
`STATUS_STACK_BUFFER_OVERRUN (0xC0000409)`；不去掉就崩。

**二、`+0x2F` 写的是 `core.EmblemSocketCount`，直接来自数据库且无上界。**
它在客户端是那条**无上界 `rep movsd` 的计数**（`vmp1-9.md` 第五节）：

| NOTI | 处理器 | 目标缓冲 | 溢出阈值 |
|---|---|---|---|
| `0x0D` ITEM_LIST | `0x0117C670` @`0x0117D1F0` | `[ebp-0x38]` = 40B | > 10 dword |
| `0x0E` UPDATE | `0x0117D720` @`0x0117E038` | `[ebp-0xd4]` = 196B | > 49 dword |

两处都只 `jle` 挡 `<= 0`，`1..127` 均可打崩。本仓的取值链是
`item_core[45]`（`Server/src/Dfo.Server.Persistence/Inventory/InventoryItemCore.cs:36`）
→ `core.EmblemSocketCount`（`byte`，0..255）→ 原样写进 `+0x2F`。
**因此本服务器今天就带着一个活的崩溃向量：任何一件 `EmblemSocketCount > 10` 的物品，
会把每一个收到它的客户端打崩。** 装备那条路早已在夹
（`StateSynchronizationPayloadCodec.Entry.cs:222` 的 `Math.Min(2, ...)`），只有背包这条没夹。

#### 一处必须如实记下的证据冲突

权威表说 `+0x2B` 是 `period`。但实机上，时装的剩余时间是从 `+0x06`（`Data`）显示出来的：
本仓对 `ItemKind == 8` 在 `+0x06` 写 `ExpireDate - now`，所有者在客户端里读到
「剩余 4153 天」，而 `2147483647 - now ≈ 358796511`，`÷ 86400 ≈ 4152.7`。同时 `+0x2B`
（`core.ExpireTime`）对那件时装是 0。**所以 `+0x06` 承载剩余秒数这件事有实机证据，
不得因为权威表把 `+0x2B` 叫 `period` 就去改 `+0x06`。**

#### 一处刚发生过的事故，写进契约以免重演

本轮准备阶段，另一个 agent 在**同一个主检出**上按错误理解改了
`GameInventoryDelivery.cs` 与 `EntryWriterAvatarShapeTests.cs`，判据一度红 5 条；
所有者已回退。本任务因此**必须在独立的任务 worktree 内进行**，不在主检出上写产物。

## Goal

让物品记录在线上的样子与客户端真正读它的方式一致：每一条都是定长 101 字节，
两个驱动客户端定长栈缓冲的计数字段由服务器夹住上界，使服务器**不可能**再交出一条
能把客户端打崩的记录；并把「这些偏移各是什么」这件事的记载改到与客户端自己的调试串一致，
其中已被证伪的旧说法补记修正案而不是悄悄改掉。

## Scope

### In

- `GameInventoryItemEntry.Write` 去掉第 110–119 行的 42 字节追加块，使**时装条目也是定长 101 字节**。
- 时装的 `JewelSocket` / `Color1` / `Color2` 的去向按证据处置：若本轮能取得证据证明它们落在
  `+0x1A` 计数子数组，就写进去；**若不能，就写零并明说没写、为什么没写**，不得靠旧客户端布局猜。
- `+0x2F` 与 `+0x1A` 两个计数字段由服务器夹住上界，取值与理由都要写下来
  （`0x0D` 的 40 字节缓冲给出 10 dword 的硬上界；`0x0E` 给出 49；记录余量 `(0x64-0x30)/4 = 13`）。
- 判据：夹紧必须有一条「拿掉夹紧就会红」的判据；定长 101 必须有一条钉住时装条目长度的判据。
- 记载：`GameInventoryItemEntry` 的类型注释、`EntryWriterAvatarShapeTests` 的说明、
  以及 `protocol/game/server-to-client/NOTI-0x000D.md`，改到与权威表一致；
  其中「那 17 个字节位置不可读」「时装条目在定长区后追加珠宝块」两句已被证伪，按仓库规矩补记修正案。
- 若上述记载与 `AGENTS.md` 现有说法冲突，按已查明的事实修正 `AGENTS.md` 并补记修正案。

### Out

- **装扮栏（listType 1）非空必崩**：另一个根因（`read_bytes(buf,0x65)` 内的失控 memcpy，
  长度 `0x30303030`），正由另一个 agent 反。本轮不动 type=1 的装填路径，也不改容器容量
  `GetDefaultParameter`。
- **改客户端**：`vmp1-9.md` 第五节给的是给客户端打补丁的建议。本轮只做服务器侧的规避，
  不产出任何客户端补丁。
- **把 `EmblemSocketCount` 等字段改名**成客户端调试串里的名字（`附加选项条目数` 等）：
  那是跨 Data/Persistence/Protocol 三层的重命名，血缘面大，值得单独一件。本轮只要求**记载**
  说清偏移的真实含义，不要求符号改名。
- **`+0x54..0x64` 那 17 个字节按字段写出**：客户端确实按字段读它们，但每个字段是什么尚未反出来，
  参照会话里全为零。本轮维持零填充，只把「这是未知而非补零」记下来。
- 时装能不能穿上、装扮槽位映射 0..11：那是装扮栏那条线的事。

## Constraints

- **正常数据上逐值不变**，唯二被授权的字节变化是：时装条目不再有 42 字节尾块；
  计数字段被夹时的取值。除此之外任何字节变化都要停下来报告。
- 入场序列字节锚 `SelectCharacterAcceptedTranscriptTests` 中**不含时装、且计数字段未越界**的帧，
  必须与 HEAD 逐字节相同。会变的帧要指名是哪一帧、为什么变、变成什么。
- 既有判据的期望值与断言**不得为迁就实现而改写**；正对应上述两处被授权变化者除外，
  且每一处改写都要单独说明它对应哪一条授权变化。
- 不新增协议帧。
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，
  判定谓词与具名豁免清单不得放宽或扩充。
- `TreatWarningsAsErrors` 开启，CS9113 是错误。
- 分层由项目引用强制：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host。
- 切片架构（`AGENTS.md`）：`Protocol/<Endpoint>/<Protocol>/` 一个目录，切片内不跨协议引用。
- 每个任务单元结束时判据必须全绿，**不允许攒红**。
- **不得触碰主检出的 `ClientHack/` 与 `.worktrees/client-debug-lab`**——都是另一个 agent 正在做的。
- **禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）。
  要做变异核实就用文件级备份 + `sha256sum`，每次用完立刻还原并核散列。
- 记载不得写成比证据强：凡「代码读出、未实机」与「实机观测」要分别标明。

## Success Conditions

- **SC-1** `GameInventoryItemEntry.Write` 对任何 `ItemKind`（含 8）都只写 101 字节；
  有一条判据钉住时装条目长度等于非时装条目长度，且拿掉这个性质会红。
- **SC-2** `+0x2F` 写出的值恒 `<= ` 所选上界；有一条判据在夹紧被移除时会红，
  且该判据指名的是「服务器交出了一个会越界的计数」而不是别的。
- **SC-3** `+0x1A` 写出的值同样有上界，且该上界有写下来的依据。
- **SC-4** 时装的 `JewelSocket`/`Color1`/`Color2` 要么落在有证据支持的位置，
  要么明确不写并把理由写下来；两种情形都不得留下「按旧客户端布局猜」的痕迹。
- **SC-5** 字节锚：不含时装、计数未越界的帧与 HEAD 逐字节相同；会变的帧被逐帧指名并解释。
- **SC-6** 全量判据不低于基线计数，退出码 0。
- **SC-7** `GameInventoryItemEntry` 的注释、`EntryWriterAvatarShapeTests` 的说明与
  `protocol/game/server-to-client/NOTI-0x000D.md` 与权威表一致；两句已证伪的旧说法各有修正案。
- **SC-8** 交回一份变化清单，**正向与反向都报**：本轮新增或改变的每一条拒绝、夹紧与放行路径。
  上一个成果正是在这一关被拦下（清单漏了 13 条新增拒绝），根因是只从授权变化的起因往下游追、
  再加一次全量判据，而全量判据全绿恰恰意味着没有用例走到那些新路径上。**本轮请特别留意同一失败模式。**

## Authorization

所有者授权覆盖：任务契约的创建与修订、路由、**一个任务 worktree**、配置内的执行与验证、
经完整评审与新鲜验证之后的**一次本地接受提交**、以及一次未接受尝试的精确工作流回滚。

**不含**：合并、推送、建 PR、删除分支或 worktree、清理工作区。这些要所有者单独选择。
本项目已获所有者常驻授权可调用子代理。

## Requires Confirmation

- 若发现 SC-1 的定长化会改变**不含时装**的帧的字节，停下报告——那意味着我对布局的读法有错。
- 若 `+0x2F` 的夹紧上界在 `0x0D` 与 `0x0E` 两个处理器之间无法取同一个值而不丢真实数据，
  停下报告，不要自行决定牺牲哪一边。

## Assumptions

- `vmp1-9.md` 第三节的字段表来自客户端自己的解密调试串，作为「客户端怎么读」的一手证据采信；
  但它对珠宝条目**字节语义**（颜色/槽位）自称推断，本轮不得把推断当已核实。
- 参照会话从未发送过带非零 `+0x2F` 计数的物品，所以「客户端收到非零计数会怎样」只有静态证据
  （无上界的 `rep movsd`）而无实机证据。夹紧是按静态证据做的防御，这一点要如实记下。
- 时装剩余时间由 `+0x06` 承载，有实机证据（4153 天）。
