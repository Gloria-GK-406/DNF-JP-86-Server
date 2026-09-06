---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 装载侧拒绝与四处覆盖缺口

## Approach

五个执行单元，**每个单元自己跑完全量判据并全绿之后才进下一个**（契约硬约束「不允许攒红」）。

只有 T-001 改生产代码；T-002..T-004 只加判据，生产代码一字不动——这正是「补上缺失的判据」的定义：**判据是新的，被钉住的行为是旧的**。因此这三个单元不能用「先红后绿」的常规 TDD 循环（新判据一写出来就该是绿的），它们的红必须靠**变异核实**取得：把被钉的那条生产校验删掉、看判据变红、再按散列还原。这一步不是可选的补充证据，而是这三个单元**唯一**能证明自己不是空判据的手段，所以写进每个单元的 Check 里。

T-001 反过来，走标准 TDD：先写会红的判据，再加拒绝。

变异核实的操作规程（禁用一切 git 还原命令，全程只用文件级备份）：

```
cp <file> <scratchpad>/<file>.bak
sha256sum <file> > <scratchpad>/<file>.sha
# 编辑 <file>，跑目标判据，记录变红的用例名与断言
cp <scratchpad>/<file>.bak <file>
sha256sum -c <scratchpad>/<file>.sha     # 必须 OK，且立刻做，不拖到单元末尾
```

## Decisions Before Work

- **DP-001 拒绝按 `core_uid` 判定，不按 `ItemIdentity`。** 保存侧的 `claimed` 集合取的是**落位后的世界行身份**，堆叠物也在内；`IdentityOfAsync` 对堆叠物答 `null`，用它去重会静默漏掉堆叠物。装载侧因此取 `CharacterItemRow.ItemUid` / `AccountCargoItemRow.ItemUid`（两者都有该字段，已核）。AC-003 就是钉这一条的。
- **DP-002 拒绝落在两个读方法内、取到行之后、逐行处理之前。** `InventoryStorageItem` 不携带 `ItemUid`（`Abstractions/Inventory/IInventoryEntryServices.cs:15`），所以不能像 `RequireUniqueSlots` 那样放到 `LoadCharacterAsync` 末尾——除非改 public 契约，而那在 Out 之外。形状对齐 `RequireUniqueSlots`：一个静态私有辅助 ＋ 一个 `source` 标签。
- **DP-003 `WithAvatarDetail` 的三个分支用假 `ICharacterItemTable` 覆盖，不用真库。** schema 上 `avatar_jewel_socket BLOB CHECK (… length(…) = 30)`（`CharacterDatabaseSchema.cs:199`）使「珠宝槽长度 ≠ 30」在真库上**不可达**——真库只能覆盖两个颜色分支。而 `WithAvatarDetail` 是 Application 层的守卫，它要挡的是「表交上来的行」，与哪种存储无关；用假件是它本来的测法。`ICharacterItemTable` 是 public（`InventoryTables.cs:295`）、7 个成员，假件只实现 `ReadByCharacterAsync`，其余抛 `NotSupportedException`。
- **DP-004 重建路径靠改 `Durability` 触发。** `InventoryItemCore.Encode` 把 `Durability` 写在偏移 10（`InventoryItemCore.cs:27`），`Value`（时装 UID）写在偏移 5（同文件第 25 行）。改 `Durability` 使 `SameContent` 不成立、`substr(item_core, 6, 4)` 不变，于是 `Survivors` 三趟全不认领（第一、三趟要 `InPlace`，落点换了槽所以不成立；第二趟要 `SameContent`），原行被删、落点走 `FillAsync` 新建。**已按代码核算，非猜测。**

## Work and Validation

| ID | Covers | Work and boundary | Check and expected result |
|---|---|---|---|
| T-001 | RQ-001..RQ-004 | **先判据后生产。** 新增 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/` 下一个用例文件（与 `ClassificationRefusalTests.cs`、`AvatarCarrierRulesTests.cs` 同族），用 `Support/InventorySide.cs` 的真库harness 播下两条 `item_slots` 行携带同一 `core_uid`，分别覆盖角色路径、账号货柜路径、以及堆叠物情形（AC-001/002/003），再加一条正常数据装载成功（AC-004）。判据变红后，在 `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` 的 `ReadCharacterItemsAsync` 与 `ReadCargoItemsAsync` 内加 DP-002 的拒绝。 | 加拒绝前：三条新判据红、正常数据那条绿。加拒绝后：全量判据绿，跳过仍为 2。CK-001＝AC-005 的变异核实（删拒绝→三条红→还原核散列）。 |
| T-002 | RQ-005,RQ-006 | 在 `Server/tests/Dfo.Server.Protocol.Tests/Game/StateSynchronization/InventoryDeltaLiftTests.cs` 所在目录内补两条判据，经 `GameInventoryItemEntry.Write`（`Dfo.Server.Protocol` 已对 `Dfo.Server.Protocol.Tests` 开 `InternalsVisibleTo`）分别送入「kind≠8 带细节」与「kind=8 且珠宝槽 29 字节」。**不动生产代码。** | 两条新判据直接绿。CK-002＝AC-008：分别删去 `GameInventoryDelivery.cs` 第 51 行与第 113 行的 `throw`，对应判据各自变红；两次都还原并核散列。 |
| T-003 | RQ-007 | 在 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/AvatarCarrierRulesTests.cs`（其类文档已自陈是「只活在代码里的时装规则」之处，两条同方法的兄弟拒绝已在此）补三条判据，各用一个假 `ICharacterItemTable`（DP-003）交出畸形 `AvatarDetailRow`：珠宝槽长度≠30、`Color1 = 65536`、`Color2 = 65536`。断言 `InvalidDataException` 且消息含 `is malformed or has the wrong owner`——**断言异常类型很要紧**：守卫若被删，颜色分支会在随后的 `checked((ushort)…)` 上抛 `OverflowException`，类型断言才能把「守卫在」与「守卫没了但恰好也炸了」分开。**不动生产代码。** | 三条新判据直接绿。CK-003＝AC-010：分三次各删该条件的一个子句，对应的那一条变红（另两条仍绿）；每次还原并核散列。 |
| T-004 | RQ-008,RQ-009 | 在 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/SqliteItemDomainStoreTests.cs` 内：(a) 按 AC-011 加一条重建路径判据，需要一个新的读 `item_uid` 的私有 helper（现有 `ReadSlotsAsync` 只答槽号/模板号/Value）；(b) 把 `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 改名为描述「幸存行不经检索保住自己的列」，**只改方法名与其文档注释，断言与期望值一字不动**。 | 新判据绿；改名后该文件用例数不变。CK-004＝AC-012：令 `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 恒返回空字典，**新判据必须变红**（对照 HEAD 上同一变异只红 `TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` 一条）；还原并核散列。CK-005＝AC-013：`git diff` 该文件，确认改名处之外无断言改动。 |
| T-005 | RQ-010 | 按代码实况改 `AGENTS.md`「已知缺陷与覆盖缺口」一节：装载侧那条按本次复核结论重写（`RequireUniqueSlots` 确在两条装载路径上，`core_uid` 原先无检查、现已有）；三条取值校验与缺口三的覆盖状态如实更新。遵循「过期的记载直接改掉，不另起修正案」「不指向 `.rks-agent/`」「不写过程」「一个数要么写出加法要么不写」。 | CK-006＝AC-014：改动段落逐句与代码位置对照。CK-007＝AC-015：`git diff` 确认 `SelectCharacterAcceptedTranscriptTests.cs` 与 `CommandHandlerArchitectureTests.cs` 零差异。 |

### T-001 边界说明

BD-001：本单元是唯一改变生产行为的地方，也是「正常数据逐值不变」唯一可能被破坏的地方。因此它排在最前——之后每个单元都会再跑一次全量判据，等于对它反复复核。

DP-005：新拒绝的消息沿用本文件既有装载拒绝的语气（`$"Inventory row {uid} has invalid character ownership."` / `$"The {source} contains duplicate slot …"`），指名重复的那个世界行身份与 `source` 标签。确切措辞留给实现。

## Choices During Implementation

- 四个新判据文件放新建文件还是并入既有文件，按各目录既有惯例就近决定；`AvatarCarrierRulesTests.cs` 与 `SqliteItemDomainStoreTests.cs` 已由 T-003/T-004 点名并入。
- 假 `ICharacterItemTable` 放在用例文件内私有嵌套类，还是 `Support/`：只有一处用，先放用例文件内；若 T-001 也需要它再提到 `Support/`。
- 新判据的命名沿用各文件既有风格（Persistence.Tests 多为整句式 `TheWorldRefusesASecondRow…`，Protocol.Tests 亦然）。
- 播「两条位置行同一个 `core_uid`」时，`item_slots` 上的三条具名 CHECK（`count > 0`、世界槽 `count = 1`、世界槽 `core_uid = id`）会限制可用形状：世界槽要求 `core_uid = id`，两条世界槽行携带同一 `core_uid` 就必须携带同一 `id`，而 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 只管槽不管 id，所以这是播得出来的。堆叠物那条走 `kind='template'`，`core_uid` 与 `id` 可以不等。**实现时先确认这两种形状都能落库**，落不了再调整播种方式（例如用两个不同 `list_type`）。

## Risks

- **R-1：新拒绝误伤正常数据。** 最可能的走形是把「两个容器各有一条位置行指向同一世界行」当成合法。它不合法——世界行只有一个位置。缓解：AC-004 的正常数据判据 ＋ 每单元全量判据。
- **R-2：新判据写成空判据。** 缓解：五处变异核实（CK-001..CK-004）逐条要求「变红」，这是本任务唯一能证伪空判据的手段。
- **R-3：还原不彻底。** 禁用 git 还原命令后，唯一保障是散列核对。缓解：每次变异**用完立刻还原并 `sha256sum -c`**，不攒到单元末尾。
