# TASK-05 — 服务器说不出一件时装该穿在哪一格时，把客户端点名的那一格记下来

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

让穿戴被拒的结果带上该物品的模板号，并让既有的那条拒绝记录一并说出它。
目标槽号那一项**已经在记**，不改它。这条记录是实机量出槽号映射的量具。

- Referenced spec requirements: RQ-007
- Referenced plan sections: plan.md 的 T-005（Steps 1–3，CK-013..CK-015）；Owned Boundaries 的 BD-005、BD-008
- Owned boundary:

BD-005 `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`
BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`
- `Server/tests/Dfo.Server.Protocol.Tests/` 下的判据

- Dependencies: TASK-02
- Required dependency outputs: TASK-02 交付的时装新种类（使被拒的物品确实是一件档案判定的时装）
- Acceptance conditions:

1. 穿戴被拒的结果带上该物品的模板号——两个拒绝点手上都已经有它。
2. 既有的那条拒绝记录一并说出模板号；**目标槽号那一项不改**。
3. **不新建日志通路、不新建类型、不改协议、不扩充任何架构判据的豁免清单。**

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

若发现要让拒绝结果带上模板号必须改动协议帧或响应的字节布局，停下并提交阻塞报告——那超出本轮边界。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-013 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Protocol.Tests --nologo` | 新判据断言：一件时装被请求穿到某一格而遭拒时，留下的记录含该物品的模板号与请求点名的那一格 |
| CK-014 | Non-command：备份并核散列后，把记录里的模板号换成常量，重跑 CK-013，再还原并核散列 | 该判据变红；还原后恢复绿 |
| CK-015 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；命令处理器与通知处理器的注入面判据仍然通过 |

- Required evidence:

- 三条检查的实际输出与退出码。
- CK-014 变异前后的散列与红/绿结果。
- 记录的实际文本样例一条，含模板号与槽号两项。

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113（未读的主构造器参数）是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单**不得放宽或扩充**
- 领域层 `store` 只指事务结算的真源
- 既有断言的期望值**不得为迁就实现而改写**；唯一例外是正对应 `spec.md` 列出的某条行为变化，且须逐条归因
- 不裁定契约兽槽位 24 还是 25；**不给 `PvfEquipmentSlots.ByName` 补任何键**
- 注释是参考材料不是铁律，判断以代码设计为准；但**改已被证伪的记载要补记修正案**，改「要求」必须停下来问

## 执行结果与自查（追加，不改上文）

### 改了什么

改动 2 个文件。计数口径：`git status --porcelain` 中本任务新增或修改的条目，不含 TASK-02..04 留下的条目。

**1. `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`（修改，BD-005）**

按成员名列出，3 处：

- `ItemMoveResult.Refused` —— 新增一个双参重载 `Refused(ItemMoveStatus status, int itemId)`，写入 `new(status, 0, itemId)`。原单参重载 `Refused(ItemMoveStatus status)` 一字未动，仍写 `new(status, 0, 0)`。没有新建类型，`ItemMoveResult` 的位置参数表未变。
- `MoveItemSpaceCommandHandler.PutOnAsync` —— 该方法中在 `piece` 读出之后的两个拒绝返回改为传 `piece.ItemId`：`slot is null` 那条（`ItemMoveStatus.NotWearable`）与 `slot.Value != command.DestinationSlotIndex` 那条（`ItemMoveStatus.ItemCannotOccupySlot`）。`TakeOffAsync` 未改（契约点名的是 `PutOnAsync` 里的两处）。
- `MoveItemSpaceHandler.ResolveAsync` —— 拒绝分支那条既有的 `LogInformation`：模板串由
  `"Move item space {ConnectionId} character={CharacterId}: {From}->{To} refused as {Status}"`
  改为
  `"Move item space {ConnectionId} character={CharacterId}: item {ItemId} {From}->{To} refused as {Status}"`，实参表在 `session.CharacterId.Value` 之后插入 `result.ItemId`。**`{To}` 那一项仍是 `message.DestinationSlotIndex`，位置与取值都未动。** 同方法的成功分支未动。

不新建日志通路（用的就是 `MoveItemSpaceHandler` 已注入的 `ILogger<MoveItemSpaceHandler>`）；不新建类型；`MoveItemSpaceRequest` / `MoveItemSpaceResponse` / `MoveItemSpacePayloadCodec.Encode` 一字未动，帧的字节布局无变化；未触碰任何架构判据的豁免清单。

**2. `Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs`（新增，BD-008）**

新增 3 条判据（计数口径：该文件中 `[Fact]` 标注的方法数），分别是：

- `The_archive_calls_this_template_an_avatar_and_names_no_worn_slot_for_it` —— 用生产的 `InfrastructureModule` 注册在实档案上问模板 101500690：`ItemPlacement.WorldClass` 为 `ItemWorldClass.Avatar`，`IEquipmentSlotDefinitionSource.GetAsync` 答 null。
- `An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` —— 把 101500690 放进背包 9 号格，请求穿到装备表 2 号格，比对记录整句与响应错误码。
- `A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` —— 把 10018（档案在 `[coat]` 下声明，`PvfEquipmentSlots.ByName` 给该名字 14）请求穿到 15 号格，比对记录整句。

同文件另有一个 `ArchiveContentFixture`（`IClassFixture`），只为让三条判据共用一次档案打开。`Dfo.Server.Protocol.Tests.csproj` 未改：`Dfo.Server.Infrastructure` 的 `InternalsVisibleTo` 已含本测试程序集，`ItemPathLiveTests` 也已在同一程序集内打开同一档案。

模板号的来路：在暂存区写了一个只引用 `Dfo.Pvf` 的一次性探针，读 `equipment/equipment.lst` 并按 `[equipment type]` 归类，打印出十一个以 `avatar]` 结尾的名字各自最靠前的三个标识（`[coat avatar]` 是 101500690 / 101500691 / 101500692），以及 `[coat]` 的 10018 / 10019 / 10020。探针留在暂存区，未进工作区。

### 三条检查的实测

| ID | 命令 | 退出码 | 结果 |
|---|---|---|---|
| CK-013 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Protocol.Tests --nologo` | 0 | `已通过! - 失败: 0，通过: 524，已跳过: 0，总计: 524` |
| CK-014 | 见下「变异实测」A 条 | 变异时 1，还原后 0 | 变异时 `失败: 2，通过: 522`；还原并核散列后回到 524 全绿 |
| CK-015 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 0 | 零失败，见下表 |

CK-015 逐项目实测与相对基线 2227 的归因（基线各项按契约给出的 521+52+60+6+320+236+1032）：

| 测试程序集 | 基线通过 | 本次通过 | 增量 | 归因 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 524 | +3 | 本任务新增的三条判据 |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | 未触及 |
| Dfo.Pvf.Tests | 60 | 60 | 0 | 未触及 |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | 未触及 |
| Dfo.Server.Persistence.Tests | 320 | 320 | 0 | 未触及 |
| Dfo.Server.Infrastructure.Tests | 236 | 236 | 0 | 未触及（另有 2 跳过，与基线的 2 跳过一致，是缺档案时的两条 `SkippableFact`） |
| Dfo.Server.Application.Tests | 1032 | 1032 | 0 | 未触及 |
| 合计 | 2227 | 2230 | +3 | |

注入面判据另单独跑过一次：`dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~ArchitectureTests"` → `失败: 0，通过: 29`，含 `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests`（两个文件均未被本任务改动，`git status` 中不出现）。

### 变异实测

交付态四个文件的散列（每次变异前后都以 `sha256sum -c` 核对，还原一律用文件级备份复制，全程未用任何 git 还原类命令）：

```
d96cdb0127c277610a30b672567053317ebade93367a2685d0aa7ecc7b9550d9  Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs
dd9c3bf6743317d909b196afb13b814bb83e10b925806bc7473bbc7426450d80  Server/src/Dfo.Server.Infrastructure/Content/PvfEquipmentSlots.cs
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da  Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
5d77b507356855f4db43988dc8817a1c404bbe9e168a20eb8b34f8641ed68272  Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs
```

六次变异，逐条都是「变异后重跑 CK-013 → 红 → 复制备份还原 → `sha256sum -c` 全 OK」。

| 编号 | 变异 | 变异后散列 | 红的判据 | 红在哪一句 |
|---|---|---|---|---|
| A（CK-014） | `ResolveAsync` 拒绝日志实参 `result.ItemId` 换成常量 `0` | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | 两条记录判据（失败 2 / 通过 522） | 各自的 `Assert.Equal(整句期望, Assert.Single(logger.Entries).Message)`：`Expected …item 101500690 9->2…` / `Actual …item 0 9->2…`；`Expected …item 10018 9->15…` / `Actual …item 0 9->15…` |
| B | `PutOnAsync` 的 `NotWearable` 拒绝改回单参 `Refused(ItemMoveStatus.NotWearable)` | `1308ad14e620007adaa6a5edd3022e4f8f5837142ca892b524e4fe943e810c58` | `An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot`（失败 1 / 通过 523） | 同上整句比对：`Actual …item 0 9->2 refused as Not…` |
| C | `PutOnAsync` 的 `ItemCannotOccupySlot` 拒绝改回单参 | `17ecabfeb72050aadc6ae7908188223e00083194cf4f75f691e94046e79a43f3` | `A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot`（失败 1 / 通过 523） | 整句比对：`Actual …item 0 9->15 refused as It…` |
| D | 拒绝日志把 `{To}` 的实参换成 `message.SourceSlotIndex` | `b22a323fc321fbd6e6e3883962933b7f406907eec4c6de7d0e936a1804d4f8da` | 两条记录判据（失败 2 / 通过 522） | 整句比对：`Expected …9->15…` / `Actual …9->9…`；`Expected …9->2…` / `Actual …9->9…` |
| E | `PvfEquipmentSlots.ByName` 临时加一个 `["[coat avatar]"] = 2` | `PvfEquipmentSlots.cs` = `d12c0dfb6b09f1acb0182a3a40305fa558e9a398209aa9090fb2696cc2bc20a5` | 前提判据与头一条记录判据（失败 2 / 通过 522） | 前提判据红在 `Assert.Null(await archive.EquipmentSlots.GetAsync(...))`：`Expected null / Actual 2`；记录判据红在整句比对，`Actual` 变成成功分支写的 `…item 101500690 9->2 count=1` |
| F | `PvfEntryLifecycleContentSource.ClassifyEquipmentType` 去掉 `avatar]` 后缀分支，恒答 `ItemWorldClass.Equipment` | `PvfEntryLifecycleContentSource.cs` = `467d894b0d8dbcad965e78b2aacf371757cc176b5862d47c3119d5bfc2a2b161` | 前提判据（失败 1 / 通过 523） | `Assert.Equal(ItemWorldClass.Avatar, placement.WorldClass)`：`Expected Avatar / Actual Equipment` |

E、F 两次变异动的是 BD-005 之外的文件，只作测量，改完即以备份复制还原并核散列；交付态不含这两处改动 —— 这两个文件的散列与本节开头列出的交付态散列相同，`git status` 中 `PvfEquipmentSlots.cs` 不出现。

关于「恒真陷阱」：A–D 说明两条记录判据在实现被改坏时会红，且模板号那一半与槽号那一半各自都能单独把它拉红（B、C 只动模板号，D 只动槽号，红的句子不同）。E、F 说明前提判据里的两句断言各自都能单独被拉红。

### 记录的实际文本样例

判据用的是整句相等比对且为绿，所以下面两句就是运行时写出的那两条记录（连接标识与角色号由判据里的替身给出）：

```
Move item space game-avatar-refusal character=60: item 101500690 9->2 refused as NotWearable
Move item space game-avatar-refusal character=60: item 10018 9->15 refused as ItemCannotOccupySlot
```

头一句里 `101500690` 是档案在 `[coat avatar]` 下声明的模板号，`2` 是请求点名的那一格。

### 逐条对照验收条件

1. **穿戴被拒的结果带上该物品的模板号。** 已做：`PutOnAsync` 的两个拒绝点都传 `piece.ItemId`。凭据是变异 B 与变异 C —— 各自改回单参重载后，对应判据的整句比对出现 `item 0`，红。
2. **既有的那条拒绝记录一并说出模板号；目标槽号那一项不改。** 已做：改的是 `ResolveAsync` 拒绝分支那条既有的 `LogInformation`，没有新增第二条日志调用。凭据：`git diff` 显示该 `LogInformation` 是被修改而非新增，且 `message.DestinationSlotIndex` 那一实参未动；变异 D 把它换掉后两条判据都红，说明记录里读出的那个槽号确实是它。
3. **不新建日志通路、不新建类型、不改协议、不扩充任何架构判据的豁免清单。** 未新增 `ILogger` 注入或别的日志出口（`MoveItemSpaceHandler` 的构造参数表未动）；未新增类型（新增的是 `ItemMoveResult.Refused` 的一个重载）；`MoveItemSpaceRequest`、`MoveItemSpaceResponse`、`MoveItemSpacePayloadCodec` 三处未动，帧字节布局无变化；`CommandHandlerArchitectureTests.cs` 与 `NotificationHandlerArchitectureTests.cs` 未被改动（`git status` 中不出现），且单独跑过 29 条全绿。

### 阻塞条件

未触发。让拒绝结果带上模板号只动了 `ItemMoveResult` 这个进程内记录类型与它的两个构造点，`MoveItemSpaceResponse` 与 `MoveItemSpacePayloadCodec.Encode` 未动，协议帧与响应的字节布局都未改。

### 这一轮没有确立的事

- 这些实验不足以确定任何一个时装名字对应哪一格。判据里的 2 和 15 是判据自己写进请求的数，不是实机测出来的。真实客户端为十一种时装各写什么槽号，要由所有者拖一轮时装、读这条记录才知道。
- 这些实验不足以确定 `TakeOffAsync` 里的拒绝点在记录中说出模板号 —— 本轮按契约只改了 `PutOnAsync` 里点名的两处，走 `TakeOffAsync` 拒绝路径时这条记录里的 `{ItemId}` 仍是单参 `Refused` 写下的 0。
- 这些实验不足以确定实机上会不会出现别的、判据没走到的拒绝路径。
- 未给 `PvfEquipmentSlots.ByName` 补任何键；未裁定契约兽是 24 还是 25。

## Independent Quick Review — 第 1 轮（QR-05-A）

- 判定：**passed**
- 被评审的契约修订：Contract revision 1（无修正案）
- 评审者：独立快审，fresh context，与执行者非同一上下文
- 拥有的改动身份（自行复核，未采信派工给出的数）：`git status --porcelain` 共 **16 条 = 11 改 + 5 新**；本任务增量 **2 项，分别是**：
  `Server/src/Dfo.Server.Protocol/Game/MoveItemSpace/MoveItemSpaceSlice.cs`（改，`git diff --stat` 记 32 行）与
  `Server/tests/Dfo.Server.Protocol.Tests/Game/MoveItemSpace/RefusedEquipIsRecordedWithItsTemplateTests.cs`（新）。
  计数口径：`git status --porcelain` 全部条目；增量的认定另以修改时间佐证——这两个文件是全部 15 个代码条目里最新的两个（11:56 与 12:06），其余条目均早于 11:23。

### 本轮实际跑过的命令与结果

| 检查 | 命令 | 实测 |
|---|---|---|
| CK-013 基线 | `PVF_ARCHIVE_PATH=… dotnet test Server/tests/Dfo.Server.Protocol.Tests --nologo` | `失败: 0，通过: 524，已跳过: 0`，EXIT=0 |
| CK-015 全量 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 七个程序集零失败，EXIT=0；524+60+52+320+6+236+1032 = **2230 通过 / 2 跳过 / 0 失败**（2 跳过全在 Infrastructure.Tests，总计 238） |
| 注入面 | `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~ArchitectureTests" --nologo` | `失败: 0，通过: 29`，EXIT=0 |

逐项目数与执行者表中的七个数**逐一相符**，增量 +3 全在 Protocol.Tests（521→524），其余六项目零增量。未观察到既知的 `ObjectDisposedException: 'SQLitePCL.sqlite3'` 偶发。

### 变异复现（自做 A 与 D）

还原一律用 `cp` 文件级备份 + `sha256sum -c` 核对，**全程未使用 git 还原类命令**。备份置于会话暂存区。

| 编号 | 变异 | 我算出的变异后散列 | 与执行者所报是否一致 | 实测 |
|---|---|---|---|---|
| A | `ResolveAsync` 拒绝分支日志的第三个实参 `result.ItemId` → 常量 `0`（成员名索引：`MoveItemSpaceHandler.ResolveAsync` 拒绝分支的 `LogInformation`） | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | **逐字符相同** | `失败: 2，通过: 522`，EXIT=1。红的两条是 `An_avatar_refused_as_unwearable_…`（`Expected …item 101500690 9->2…` / `Actual …item 0 9->2…`）与 `A_piece_aimed_at_the_wrong_slot_…`（`Expected …item 10018 9->15…` / `Actual …item 0 9->15…`），红在各自的 `Assert.Equal(整句, Assert.Single(logger.Entries).Message)` |
| D | 同一 `LogInformation` 的 `{To}` 实参 `message.DestinationSlotIndex` → `message.SourceSlotIndex` | `b22a323fc321fbd6e6e3883962933b7f406907eec4c6de7d0e936a1804d4f8da` | **逐字符相同** | `失败: 2，通过: 522`，EXIT=1。红的是同两条，`Expected …9->2…` / `Actual …9->9…` 与 `Expected …9->15…` / `Actual …9->9…`，同样红在整句比对 |

两次还原后 `sha256sum -c` 四个文件全 OK，工作区回到 16 条。执行者所报的交付态四散列，我独立算出的值**逐一相同**：

```
d96cdb0127c277610a30b672567053317ebade93367a2685d0aa7ecc7b9550d9  MoveItemSpaceSlice.cs
dd9c3bf6743317d909b196afb13b814bb83e10b925806bc7473bbc7426450d80  PvfEquipmentSlots.cs
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da  PvfEntryLifecycleContentSource.cs
5d77b507356855f4db43988dc8817a1c404bbe9e168a20eb8b34f8641ed68272  RefusedEquipIsRecordedWithItsTemplateTests.cs
```

`PvfEquipmentSlots.cs` 不在 `git status` 中（E 已还原）；`PvfEntryLifecycleContentSource.cs` 在 `git status` 中，但其 diff 是 TASK-02 的 `AvatarEquipmentSuffix` 分类，与本轮无关，F 已还原。

### 量具的两项是否可靠（本轮的要害）

- **`{ItemId}` 取的是被拒物品的模板号。** `PutOnAsync` 中 `piece` 来自 `bag.ReadSlotAsync(command.SourceSlotIndex)`，即玩家拖动的那一件；目标槽里那件是 `displaced`，只在成功分支之后才被读，两个拒绝点在它之前返回。传入的是 `piece.ItemId`。变异 B/C（执行者做）与 A（本轮复现）三者一致地把它拉成 `item 0`。**属实。**
- **`{To}` 取的是客户端点名的槽。** 日志实参是 `message.DestinationSlotIndex`，`message` 是 `MoveItemSpaceRequest`，该字段由 `MoveItemSpaceDecoder` 从 `body[12..]` 直接读出，中间无重算。判据 `A_piece_aimed_at_the_wrong_slot_…` 恰是能分辨二者的一次观测：档案给 `[coat]` 的槽是 14，请求写的是 15，记录读出 **15**——即记的不是服务器算出的那个。变异 D 复现为红。**属实。**
- **判据用的是真档案。** `ArchiveContentFixture` 走生产的 `new InfrastructureModule().ConfigureServices(...)` 取 `IItemPlacementDefinitionSource` 与 `IEquipmentSlotDefinitionSource`，判定链 `ItemPlacementRuleService(archive.Placements, archive.EquipmentSlots)` 用的就是它们。夹具里 `SlotDictionaryContainer.Seed` 写的 `ItemWorldClass.Equipment` 不参与判定——`PutOnAsync` 只取 `piece.ItemId` 再问档案，该字段在本路径上不被读。**属实，未见伪造分类。**
- 路由为生产路由：`MainListType = (byte)InventoryContainerType.Main = 0`、`EquipmentListType = (byte)InventoryContainerType.Equipment = 3`，判据请求写的正是 0→3，与 `PutOnAsync` 注释里那条实机帧（`… 03 0F 00`）同形。
- 独立复核了源码注释与判据注释里那句全称断言「`PvfEquipmentSlots.ByName` 中没有以 `avatar]` 结尾的名字」：该表是逐条字面量，共 **16 条**，以 `avatar]` 结尾者 **0 条**，`["[coat]"] = 14`。该表有穷且可枚举，**该句为真且可证伪**。

### 边界

- `ItemMoveResult.Refused` 的**原单参重载**：方法行 `public static ItemMoveResult Refused(ItemMoveStatus status) => new(status, 0, 0);` 逐字符未变，其上新增了一行 `/// <summary>` 文档注释。行为与签名未动。
- `MoveItemSpaceRequest`、`MoveItemSpaceResponse`、`MoveItemSpacePayloadCodec`（`Encode` 与 `Decode`）**在本轮 diff 中无任何 hunk**，帧字节布局无变化。阻塞条件未触发，属实。
- 那条 `LogInformation` 是**既有那条被修改**，不是新起的一条：diff 中它以 `-`/`+` 成对出现于同一处，`ResolveAsync` 内 `logger.LogInformation` 仍是两处（成功分支与拒绝分支），未新增日志调用，`MoveItemSpaceHandler` 构造参数表未动——不新建日志通路，属实。
- `{To}` 那一项：占位符顺序 `ConnectionId, CharacterId, ItemId, From, To, Status` 与实参顺序一一对应，`{To}` 仍是 `message.DestinationSlotIndex`，位置在 `{From}` 之后未动。
- 未新建类型（新增的是既有 record 上的一个静态工厂重载，位置参数表未变）。
- `CommandHandlerArchitectureTests.cs` 与 `NotificationHandlerArchitectureTests.cs` 不在 `git status` 中，豁免清单未扩充。

### 既有断言零改写

`git status` 中 Protocol 相关条目只有上述两条；`Server/tests/Dfo.Server.Protocol.Tests/` 下**无任何既有文件被修改**。另两个被改的判据文件（`ItemClassificationTests.cs`、`SqliteItemDomainStoreTests.cs`）修改时间为 08:39 与 08:10，早于本轮两文件（11:56 / 12:06），属 TASK-02/03 的既有条目，不在本轮增量内。

### 记法口径

- 逐句读过「执行结果与自查」全文，**未发现支撑不足的全称句**。带「都」「逐条」「全」的句子均由紧邻的枚举界定（「两个拒绝点」逐点核实为真；「六次变异」界定为表中六行；「全 OK」界定为四个散列）。「未触碰任何架构判据的豁免清单」是可证伪的全称否定，已核为真。
- 「这些实验不足以确定 `TakeOffAsync` 的拒绝点说出模板号，`{ItemId}` 仍是 0」：**属实**。`TakeOffAsync` 的三个拒绝返回（`ItemUnknown`→`NotWearable`、`SlotNotAllowed`→`ItemCannotOccupySlot`、`DisplacedCannotOccupySlot`）连同其前的 `NoBoundContainer` / `BothSlotsEmpty` 全部走单参 `Refused`，`ItemId` 为 0。
- 该小节实际有**三条**「这些实验不足以确定…」加一条「未给…补任何键；未裁定…」，多于派工描述的两条；多出来的一条覆盖面更宽，非缺陷。

### 非阻断观察（交下游完整变更评审）

1. **实机读数的一个已知缺口，对所有者的计划有影响。** 若真实客户端把时装拖拽的目标列写成 `InventoryContainerType.Avatar`（=1）而非 `Equipment`（=3），`MoveItemSpaceCommandHandler.HandleAsync` 会在到达 `PutOnAsync` 之前于 `ListNotServed` 处返回单参 `Refused`，那一行将读作 `item 0 …->N refused as ListNotServed`——槽号仍在，模板号丢失。执行者第三条「不足以确定实机上会不会出现别的、判据没走到的拒绝路径」在形式上已覆盖，故非虚假记载；但这是所有者拖第一轮时最可能先撞上的一条，建议 T-006 的反向扫把它单列。
2. `Refused` 单参重载上方新增了一行文档注释，与自查中「一字未动」的措辞相比略欠精确；重载本身的签名与实现确实逐字符未变，无行为影响。
3. 前提判据 `The_archive_calls_this_template_an_avatar_…` 实有三句 `Assert`（含一句 `Assert.NotNull(placement)` 守卫），自查称「两句断言」——变异 E/F 覆盖的是其中两句实质断言，`Assert.NotNull` 那句未单独做过变异。
4. 「全程未用任何 git 还原类命令」是读者无法直接证伪的过程性陈述；其结果可证伪的那一半（交付态四散列完好）本轮已独立核实为真。

### 需要的整改

无。


## Controller Disposition D-05 — 接受 QR-05-A 的 `passed`，并把一处实机风险显著记下

**判定**：接受。执行结果、自查、QR-05-A 的 `passed` 与被拥有的增量
（`MoveItemSpaceSlice.cs` 修改 ＋ `RefusedEquipIsRecordedWithItsTemplateTests.cs` 新增）指向同一轮。
TASK-05 置 `done`。契约验收三条均由实测支撑，六次变异全部红且评审复现了其中两次
（变异后散列逐字符相同）。

### 一处不扩范围、但必须显著记下的实机风险

评审指出：**若真实客户端把时装拖拽的目标列写成 `InventoryContainerType.Avatar`（=1）
而非 `Equipment`（=3）**，请求会在到达 `PutOnAsync` **之前**于 `ListNotServed` 处走单参
`Refused`，那行记录读作 `item 0 …->N refused as ListNotServed`——
**槽号仍在，模板号丢失**。评审判断这是拖第一轮最可能先撞上的一条。

**不扩本任务范围补它，三条理由**：
1. 契约要的两处（`PutOnAsync` 的 `NotWearable` 与 `ItemCannotOccupySlot`）已做到并有实测。
2. **客户端到底写哪个列号，正是实机要回答的问题之一**——现在补一条针对推测路径的实现，
   是在没有观测的情况下加代码。
3. **量具在那条路径上并未失效**：`{To}` 仍然记下客户端点名的格子，
   而槽号才是要量的东西；所有者一次拖一件，模板号由操作上下文可知。

**去向**：转 **TASK-06** 反向扫单列一条；并转 **TASK-07**，
写进给所有者看的那份记载——**实机时看到 `item 0` 不是缺陷，是走了另一条拒绝路径**。

### 已知限制，一并转 TASK-07

`TakeOffAsync` 的拒绝点仍不会说出模板号（该方法的三个拒绝返回全走单参 `Refused`），
走那条路径时 `{ItemId}` 是 0。评审核实属实。
对实机影响不大——取下时装走背包自己的规则、本来就允许；量映射靠的是拖上去那一次。
