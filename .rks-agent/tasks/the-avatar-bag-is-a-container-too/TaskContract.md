---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 装扮栏也是一个容器

## Context

### Source Request

所有者从本仓 issue 里挑的下一件。先是问排序：

> 先做10吧，看起来10的内容很小，并且可控？还是10 11一起做？我应该能直接拖动让客户端发送装备的指令了吧。
> 这样可以直接知道装扮的11槽是哪些。你怎么看

在被告知「测量今天就能做、不必等 #10」并选择「先量槽位，再单干 #10 → #11」之后，
槽位已实机量全（见下）。随后：

> 可以，直接开干吧

本件是 **issue #10**。issue 的接受条件（原文）：

> - 装扮栏（`InventoryContainerType.Avatar`）进出的移动被 `MoveItemSpace` 服务
> - 拒绝路径上**先读到物品再拒**，让日志能记下 `{ItemId}`——现在的 `item 0` 对排查没有用
> - 判据覆盖：装扮栏内换位、装扮栏 ↔ 主背包、以及仍应拒绝的情形

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，分支 `main`，HEAD `7147d2e9`（工作区干净）。
判据命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
基线（**本轮在 `7147d2e9` 合并后实测**）：**2289 通过 / 2 跳过 / 0 失败**，退出码 0。
逐项目：Pvf 60、Networking 52、Protocol 567、Persistence 329、Infrastructure 236(+2 跳过)、Host 6、Application 1039。
那 2 个跳过是 `A_missing_archive_*`，只在没有档案可达时才跑，跳过正是「档案确实加载了」的标志。

**跑判据前必须确认没有 `Dfo.Server.Host` 进程在跑**——运行中的 host 锁住
`Dfo.Server.Host/bin` 下的 `Dfo.Server.Protocol.dll`，构建以 MSB3027 失败，看着像判据坏了其实不是。

既知偶发失败，按「异常类型 + 栈根 + 复跑即绿」判别，**不按用例名**：
`ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根
`CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`。

#### 根因：装扮栏这个容器从来没有被绑定

`SelectCharacterEntryInventoryDomainService.BindContainers`（`:56-87`）在入场时**只绑两个**：

```csharp
foreach (var container in (InventoryContainerState[])[main, equipment])
```

而 `MoveItemSpaceCommandHandler`（`MoveItemSpaceSlice.cs:244`）只服务三种列表对：
主背包→主背包、主背包→装备栏、装备栏→主背包。**凡是沾 `Avatar`(=1) 的一律在
`:280` 被拒为 `ListNotServed`**，那一句在**读到物品之前**执行，所以日志里 `{ItemId}` 是 0。

**这与 2026-08-26 的事故是同一形态，而那次的教训就写在同一个方法的注释里**
（`SelectCharacterEntryInventoryDomainService.cs:63-68`）：装备栏当时没绑，玩家把刚捡的护肩
拖到肩甲槽，被拒为「这个列表本服务器不服务」。今天装扮栏正处在完全相同的处境。

#### 上游怎么做的（`ServerS4A21`，只读参照，本轮逐行读出）

`Game/Inventory/Services/ItemSlotBoundService.cs`：

| 列表 | 槽位区间 | 该列表的槽位只接受 |
|---|---|---|
| Main | `3..351` | 按 band 分（`TryGetMainItemKindBySlot`） |
| **Avatar** | **`GetAvatarOpenRange(0)` = `0..209`** | **`ItemCore.KindAvatar`**（`:137-139`） |
| Equipment | `0..29` | 按槽位分（`TryGetBodyItemKindBySlot`） |
| Pet | `0..239` | — |

`Network/Handlers/InventoryHandler.Move.cs`：上游**服务**装扮栏↔装备栏的移动。
它有一条 `IsAvatarEquipMutation` 的拦截（错误码 `0xB4`），但那条**只在**
「助战限制不允许该角色改外观」时才触发（`:45-47` 的三段合取），普通角色照常走。

#### 本轮已实机量得：客户端认为每件时装该进哪个装扮槽

2026-09-07，11 件骑士时装放进主背包后逐件拖到装扮槽，服务器逐条记下拒绝：

| 槽 | itemId | 部位 | | 槽 | itemId | 部位 |
|---:|---|---|---|---:|---|---|
| 0 | 413550000 | 帽子 | | 6 | 413520000 | 胸部 |
| 1 | 413560000 | 头发 | | 7 | 413530000 | 腰带 |
| 2 | 413570000 | 脸 | | 8 | 413580000 | 皮肤 |
| 3 | 413500000 | 上衣 | | 9 | 413590000 | 光环 |
| 4 | 413510000 | 下装 | | 10 | 413600000 | 武器 |
| 5 | 413540000 | 鞋 | | | | |

11 件 → 11 个互不相同的槽，`0..10` 连续无缺口。
上衣 `413500000 → 3` 是**第三次**独立吻合（此前主背包 `6->3`、装扮栏 `0->3`，本次 `185->3`，
三次源格互不相同），说明目标槽确由客户端按物品判定，不是从请求里抄的。
**槽 11 没有量到**——没有对应物品；文档推测它是 `AuraSkinAvatar`，**那是推断不是实测**。

**这张表是 issue #11 的输入，不是本件的。** 本件不动 `PvfEquipmentSlots.ByName`。

## Goal

让装扮栏和别的容器一样：它被绑定，进出它的移动被 `MoveItemSpace` 服务，
拒绝发生在读到物品之后因而日志说得出是哪一件。

## Scope

### In

- **绑定装扮栏容器**：`BindContainers` 除 main / equipment 外一并绑 `Avatar`，
  形状取入场投影里该容器的 `Parameter`。
- **`MoveItemSpaceCommandHandler` 服务涉及装扮栏的列表对**：
  - 装扮栏 ↔ 装扮栏（栏内换位）
  - 装扮栏 ↔ 主背包（进出）
  - 装扮栏 ↔ 装备栏（穿上 / 脱下）
- **装扮栏槽位的准入规则**：具体形态由 spec 决定，但**默认取上游的读数**——
  区间 `0..209`，只接受时装（`ItemKind == 8`）。若 spec 决定偏离上游，
  必须写明偏离的理由与证据，不得默默偏离。
- **拒绝路径上先读到物品再拒**：对本轮新服务的列表对成立。
  对仍然不服务的列表对（个人货柜、宠物、账号金库…），`{ItemId}` 仍为 0，
  交付时**如实说明**这一点，不得写成「所有拒绝都带 itemId」。
- 判据：栏内换位、装扮栏↔主背包、装扮栏↔装备栏、以及**仍应拒绝的情形**各有判据；
  「拿掉规则就会红」的变异验证。

### Out

- **不做 issue #11**：不给 `PvfEquipmentSlots.ByName` 补任何时装的键。
  因此**本件做完之后，把时装拖到装扮槽仍会被拒为 `NotWearable`**——
  这是预期结果，不是本件失败。它与 `ListNotServed` 的区别正是本件的成果：
  拒绝从「这个列表我不服务」变成「我读到了这一件，但不知道它该进哪个槽」。
- **不做助战的外观限制**（上游 `IsAvatarEquipMutation` + `CanMutateAppearance`）。
  本仓没有这个概念，凭空造一条限制会拦住普通玩家。
- **不改任何协议帧的字节布局，不新增协议帧。**
- **不动装扮栏的 `listParam16`**（issue #12，未清，本轮不碰）。
- **不做装扮栏的容量/扩容判定**：上游 `GetAvatarOpenRange` 忽略参数恒返回 `0..209`，
  本轮照此，不引入扩容概念。
- **不改主背包的 band 规则**（`IItemSlotRuleService.CanOccupyMainSlot`），
  除非装扮栏↔主背包这条路强制要求，且那时要单独说明。

## Constraints

- **被授权改变的可观察行为，只有「涉及装扮栏的移动请求不再被拒为 `ListNotServed`」这一件**，
  以及它带来的成功/拒绝码。其余任何行为变化都要停下报告。
- 既有判据的期望值与断言**不得为迁就实现而改写**；正对应上述授权变化者除外，
  且每处改写要单独说明它对应哪一条。
- `CommandHandlerArchitectureTests` / `NotificationHandlerArchitectureTests` 必须继续通过，
  判定谓词与具名豁免清单不得放宽或扩充。
- **凡能触达持久写的命令一律 `Transactional`**（`AGENTS.md`）。移动是持久写。
- `ErrorCodeFor` 的枚举**必须继续逐条列举**，不得改成 fallthrough——
  那条规矩存在的理由写在它自己的注释里。
- `TreatWarningsAsErrors` 开启，CS9113 是错误。
- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host。
- 切片架构（`AGENTS.md`）：`Protocol/<Endpoint>/<Protocol>/` 一个目录，切片内不跨协议引用；
  新协议只改 `ProtocolModule` 一处接线。
- 每个任务单元结束时判据必须全绿，不允许攒红。
- **不得触碰**主检出的 `ClientHack/`、`.worktrees/client-debug-lab`（另一个 agent 在做，只读）、
  `.worktrees/entries-are-what-the-client-reads`（所有者保留的旧 worktree）。
- **禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——
  本仓曾因此丢过未提交的工作。变异核实用文件级备份 + `sha256sum`，每次用完立刻还原并核散列。
- 记载不得写成比证据强：「上游源码读出」「本仓代码读出」「实机观测」要分别标明；
  不写读者无法证伪的基数。

## Success Conditions

- **SC-1** 入场后装扮栏是一个已绑定的容器；判据钉住，拿掉绑定会红。
- **SC-2** 装扮栏内换位被服务并真的落库；判据覆盖空格→空格、有物品→空格、以及交换两格。
- **SC-3** 装扮栏 ↔ 主背包双向被服务；判据钉住两个方向。
- **SC-4** 装扮栏 ↔ 装备栏双向被服务，即请求能走到 `PutOnAsync` / `TakeOffAsync`。
  **穿戴仍会因 `NotWearable` 被拒**（issue #11 未做），判据要**明确钉住这一点**——
  钉的是「拒绝理由从 `ListNotServed` 变成了 `NotWearable`」，
  并且**日志此时带得出 `{ItemId}`**，这正是 issue #10 第二条接受条件。
- **SC-5** 装扮栏槽位的准入规则被判据钉住：区间外的槽被拒；
  不被接受的物品种类被拒。规则本身与上游一致，或写明偏离理由。
- **SC-6** 仍然不服务的列表对继续被拒为 `ListNotServed`；判据钉住至少两种
  （个人货柜、宠物或账号金库），**防止「顺手把所有列表都放行」**。
- **SC-7** 全量判据不低于基线 **2289 通过 / 2 跳过 / 0 失败**，退出码 0；
  字节锚 `SelectCharacterAcceptedTranscriptTests` 的 62 帧**逐帧不变**
  （本件不碰编码器，若它变了说明改动波及了不该波及的地方，停下报告）。
- **SC-8** 交回一份变化清单，**正向与反向都报**，且**从代码出发独立扫**。
  上一件的教训要照用，且要覆盖到第四层：
  1. 不要只顺着改动的**起因**往下游追，要反过来搜；
  2. 搜**说法**而不只是搜数字或标识符；
  3. **显式列出搜索集**，含 `.rks-agent/tasks/` 下本轮的全部产物；
  4. **`grep` 一个词只命中散文**——表格、代码块、行号里的同一事实不含那个词，
     改一个说法时要连它引用的证据一起看。
  见 `.rks-agent/tasks/one-length-for-the-jewel-socket/TaskContract.md` 的
  `C-1 的补充` 与 `补充之二`（同一族失误在本仓已 8 次，无一次由自查发现）。

## Authorization

覆盖：任务契约的创建与修订、路由、**一个任务 worktree**、配置内的执行与验证、
经完整评审与新鲜验证之后的**一次本地接受提交**、以及一次未接受尝试的精确工作流回滚。
**不含**合并、推送、建 PR、删除分支或 worktree、清理。
本项目已获所有者常驻授权可调用子代理。

## Requires Confirmation

- 若绑定装扮栏之后**别的路径**开始行为不同（例如某处遍历已绑定容器），停下报告，
  不要顺手改那条路径。
- 若装扮栏↔主背包这条路要求改动主背包的 band 规则，停下报告——
  那会影响所有物品的移动，超出本件授权的行为变化。
- 若发现本仓存在助战外观限制的对应概念，停下报告；**不得凭空造一条限制**。
- 若为了服务这些列表对，必须把 `MoveItemSpaceCommandHandler` 改成「任意容器到任意容器」，
  停下报告并说明——那是比本件宽得多的变化，需要单独决定。

## Assumptions

- 装扮栏的槽位区间与准入种类**读自上游** `ItemSlotBoundService`
  （`GetAvatarOpenRange` 恒返回 `0..209` 并忽略参数；`TryGetItemKindBySlot` 对该列表恒答
  `KindAvatar`），**不是实机测得**。
- 上游服务装扮栏↔装备栏的移动，其 `IsAvatarEquipMutation` 拦截**只对受限的助战角色生效**——
  这是从上游三段合取条件读出的。
- `SnapshotAsync` 对非主背包容器抛 `NotSupportedException`，但**生产里没有调用方**
  （本轮全仓扫描：`Server/src` 内除 `ItemContainers.cs` 与接口定义外零命中），
  所以绑定装扮栏不会踩到它。`GetContainers` 同样零生产调用方。
- 客户端允许时装待在主背包并从那里拖向装扮槽——**实机观测**（本轮 11 件全部如此）。
  这不意味着客户端会提供「主背包↔装扮栏」的拖拽；那一条**未观测**。
- 上游 `ServerS4A21` 是参考实现，不是本仓的规范；引用它的地方要标明是「上游读出」。
