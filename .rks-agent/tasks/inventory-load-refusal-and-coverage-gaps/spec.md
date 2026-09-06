---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 装载侧的重复指名拒绝，与三处「没有判据撑着」的取值校验的覆盖

## Why and Outcome

本仓有一类反复出现的缺陷：**一条结论是对的，但撑着它的既不是代码也不是判据**。AGENTS.md 为此专门列了两节（「三处『结论对，但撑着它的不是代码』」与「已知缺陷与覆盖缺口」），并记下过教训——一条被记作「有覆盖」而实际不覆盖的规则，比一条明记「没覆盖」的规则更危险。

本次要消掉其中四条，验收口径统一是**可证伪**：

1. **装载侧缺一条拒绝。** 世界表／位置表分家之后，「同一件东西被两个位置同时指名」成为一种新的坏形状。保存侧已经拒绝它；装载侧没有对称的拒绝，于是一个已经坏掉的存档能被读进内存，玩家会看到两份同一件东西，并且能把两份都搬走。
2. **三条取值校验没有任何判据。** 删掉它们，全量判据仍然全绿——这三条今天是「写着但没人证明它在」。
3. **一条判据靠无关的事实通过。** `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 断言的六个时装值之所以对，是因为那一行**根本没被删过**；「六个值被搬到一个新建的世界行上」这条真正要紧的路径今天零覆盖。

**结果**：这四条各自获得它缺的那一半，且**正常数据上的行为逐值不变**。

## Scope

**In**

- `InventoryEntryAssembly` 的两条装载路径新增「同一个世界行被两个位置指名」的拒绝。
- 四条新判据：装载侧拒绝、`GameInventoryItemEntry` 两条、`InventoryEntryAssembly.WithAvatarDetail` 的畸形细节一条。
- 一条新判据钉住「时装六值落到新建的世界行上」。
- 把 `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 改名为描述它**实际**证明的东西（幸存行不经检索也保住自己的列）；**不动它的任何断言与期望值**。
- AGENTS.md 相关记载按代码实况更新。

**Out**

- 不改任何帧的字节布局，不新增协议帧，不改 `GameInventoryDelivery` 在正常数据上写出的字节。
- 不改 schema，不改 `Abstractions` 中任何 public 契约的形状。
- 不给 `SqliteWorldItemTable` 补守卫（AGENTS.md 明记为有意留下、生产到不了的缺口）。
- 不碰契约兽、时装槽号表、孤儿世界行清扫。

## Definitions

- **世界行身份**：`items.item_uid`，在位置表 `item_slots` 上以 `core_uid` 列被指名。装载侧读到的是 `CharacterItemRow.ItemUid` / `AccountCargoItemRow.ItemUid`。
- **指名**：一条 `item_slots` 行通过 `core_uid` 指向一条 `items` 行。「同一件东西被两个位置指名」即两条位置行携带同一个 `core_uid`。
- **正常数据**：不含重复 `core_uid`、不含重复 (容器, 槽)、时装行的六列齐备且取值在范围内的存储状态。

## Constraints

- `TreatWarningsAsErrors` 开启；CS9113（未读的主构造器参数）是错误。
- 分层由项目引用强制，取证不得反向引用。
- 禁止一切 git 还原类命令；变异核实用文件级备份 ＋ `sha256sum` 还原并核散列，用完立刻还原。
- 每个执行单元结束时全量判据全绿。
- 偶发失败只按「`ObjectDisposedException: 'SQLitePCL.sqlite3'` ＋ 栈根在 AGENTS.md 三条清单内 ＋ 复跑即绿」三者同时成立判别，不按用例名。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 角色装载路径读到两条位置行指名同一个世界行时，拒绝装载，不返回部分结果。 |
| RQ-002 | 账号货柜装载路径在同样的情形下同样拒绝。 |
| RQ-003 | 该拒绝按**世界行身份**判定，因而对堆叠物与具名物一视同仁——与保存侧 `SaveAsync` 的 `claimed.Count != settled.Count` 同口径。 |
| RQ-004 | 正常数据在两条装载路径上的结果与 HEAD `25dccec6` 逐值相同。 |
| RQ-005 | `GameInventoryItemEntry.Write` 对「非时装却带着时装细节」的拒绝，有判据钉住。 |
| RQ-006 | `GameInventoryItemEntry.Write` 对「时装珠宝槽不是恰好 30 字节」的拒绝，有判据钉住。 |
| RQ-007 | `InventoryEntryAssembly.WithAvatarDetail` 对畸形时装细节的拒绝，其**三个分支**（珠宝槽长度、`Color1` 越界、`Color2` 越界）各自有判据钉住。 |
| RQ-008 | 「一次搬迁导致世界行被重建时，时装的六个值落到那条新行上」有正向判据；该判据必须真的走重建路径，而非幸存路径。 |
| RQ-009 | `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 的名字描述它实际证明的东西；其断言、期望值与用例数不变。 |
| RQ-010 | AGENTS.md 中关于装载侧校验、以及这四条覆盖状态的记载，与代码实况一致。 |
| RQ-011 | 帧字节与架构判据不变：入场 62 帧字节锚逐字节相同，`CommandHandlerArchitectureTests` 通过且其判定谓词与豁免清单未放宽。 |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | **Given** 一个角色的 `item_slots` 有两条行（不同 `list_type`/`slot_index`）携带同一个 `core_uid`，**When** 调用 `LoadCharacterAsync`，**Then** 抛 `InvalidDataException`，消息指名那个重复的世界行身份。 |
| AC-002 | RQ-002 | **Given** 一个账号的货柜有两条位置行携带同一个 `core_uid`，**When** 调用 `LoadAccountAsync`，**Then** 抛 `InvalidDataException`。 |
| AC-003 | RQ-003 | 重复指名的那件东西是**堆叠物**（`ItemWorldClass.Interchangeable`，因而 `IdentityOfAsync` 答 `null`）时，AC-001 仍然成立。这一条单独存在，因为按 `ItemIdentity` 去重的实现会在此处静默放行。 |
| AC-004 | RQ-004 | 无重复的存储状态经 `LoadCharacterAsync` / `LoadAccountAsync` 装载成功，返回的物品条数、槽号与 core 取值与新增拒绝之前相同；HEAD 已有的全部装载相关判据未改动期望值且继续通过。 |
| AC-005 | RQ-001,RQ-002,RQ-003 | **变异核实**：把新增的拒绝从生产代码中删去，AC-001..AC-003 对应的判据变红；还原后全绿。 |
| AC-006 | RQ-005 | **Given** 一条 `ItemKind != 8` 却携带非空 `AvatarDetail` 的 `InventoryItemState`，**When** 经 `GameInventoryItemEntry.Write` 写出，**Then** 抛 `InvalidDataException`，消息为 `Non-avatar inventory item carries avatar detail.`。 |
| AC-007 | RQ-006 | **Given** 一条 `ItemKind == 8` 且 `AvatarDetail.JewelSocket.Length != 30` 的 `InventoryItemState`，**When** 经 `GameInventoryItemEntry.Write` 写出，**Then** 抛 `InvalidDataException`，消息为 `Avatar jewel socket must contain exactly 30 bytes.`。 |
| AC-008 | RQ-005,RQ-006 | **变异核实**：分别删去这两条生产校验，AC-006 / AC-007 对应的判据各自变红；还原后全绿。 |
| AC-009 | RQ-007 | 三条判据分别令 `ICharacterItemTable.ReadByCharacterAsync` 交出一条 `ItemKind == 8` 的行，其 `AvatarDetailRow` 依次为：珠宝槽长度 ≠ 30、`Color1 > ushort.MaxValue`、`Color2 > ushort.MaxValue`；每条都使 `LoadCharacterAsync` 抛 `InvalidDataException`（而非 `OverflowException`），消息含 `is malformed or has the wrong owner`。 |
| AC-010 | RQ-007 | **变异核实**：删去该条件的三个子句中的**任意一个**，对应的那条判据变红；还原后全绿。 |
| AC-011 | RQ-008 | 见下「AC-011 时装六值落到新建的世界行上」。 |
| AC-012 | RQ-008 | **变异核实**：令 `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 恒返回空字典，AC-011 的判据变红；还原后全绿。（对照：HEAD 上同一变异只红 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 一条。） |
| AC-013 | RQ-009 | 改名后的用例其 `Assert` 语句、字面期望值与所在文件的用例总数与 HEAD 相同；差异仅为方法名与其文档注释。 |
| AC-014 | RQ-010 | AGENTS.md 中「已知缺陷与覆盖缺口」一节的相关条目可与 `InventoryEntryAssembly`、`GameInventoryDelivery`、`SqliteItemDomainStoreTests` 及新判据逐句核对；不再声称已被本次消掉的缺口仍然存在，也不声称未被消掉的缺口已消掉。 |
| AC-015 | RQ-011 | `Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs` 与 `CommandHandlerArchitectureTests.cs` 相对 HEAD `25dccec6` 的 `git diff` 为空。 |
| AC-016 | RQ-004,RQ-011 | 全量判据 `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` 退出码 0；跳过数仍为 2；通过数 = 2237 ＋ 本次新增判据数。 |

### AC-011 时装六值落到新建的世界行上

**Given** 一条时装世界行（`ItemKind == 8`，core 的 Value 位携带时装 UID）站在某个槽上，其六个时装列已填好；
**When** 在一次结算里把它清出原槽、写到另一个槽，**且写下的 core 字节与原行不同**（因而 `Survivors` 的三趟匹配——`InPlace && SameContent`、`SameContent`、`InPlace && SameThing`——一趟都不认领它，原行不进 `spared`，被 `EmptyAsync` 删除，落点走 `FillAsync` 新建一行）；
**Then** 结算成功，且：

- 新槽上那条世界行的 `item_uid` **不等于**原行的 `item_uid`（证明这确实是重建，不是幸存）；
- 该新行的六个时装列（`avatar_expire_date`、`avatar_clear_id`、`avatar_jewel_socket`、`avatar_color1`、`avatar_color2`、`avatar_delete_date`）**逐个**等于原行的值。

**六个值逐个断言，不做整体比较**——同文件 `AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` 已经写下这条理由：「五个绑上、一个留 null」正是绑定写漏时的形状，宽松的比较会放它过去。

「`item_uid` 不等于原行」这一条不是附带断言，而是这条判据成立的前提：没有它，一条落到幸存路径上的判据会与 HEAD 那条一样靠「行没被删」通过，本条就白补了。

## Success Evidence

| ID | Covers | Evidence |
|---|---|---|
| EV-001 | AC-001..AC-004, AC-006..AC-009, AC-011, AC-016 | 全量判据一次落盘输出（`--logger "console;verbosity=detailed"`），含退出码与逐项目读数。 |
| EV-002 | AC-005, AC-008, AC-010, AC-012 | 每次变异核实留下三样：变异内容、变异后变红的用例名与断言、还原后 `sha256sum` 与变异前一致的证明。 |
| EV-003 | AC-013, AC-015 | `git diff` 输出（对应文件为空差异；改名文件的差异仅方法名与注释）。 |
| EV-004 | AC-014 | AGENTS.md 改动段落与其所述代码位置的对照。 |

## Assumptions

- `InventoryItemCore.Encode` 会把 `Durability`（或另一个非 Value 位字段）写进 core 字节，因而改动它足以让 `SameContent` 不成立而 `substr(item_core, 6, 4)` 不变。实现时先验证这一点；若不成立，改用另一个同样不落在偏移 5..8 的字段。
- `ICharacterItemTable` 的 7 个成员中，`LoadCharacterAsync` 在抛出前只会用到 `ReadByCharacterAsync`，其余成员在假件里可以拒绝调用。实现时以实际调用为准。
- 装载侧新增拒绝落在两个读方法取到行之后、逐行处理之前。这只会改变「一个存档同时坏了两处」时先报哪一条，不影响正常数据。
