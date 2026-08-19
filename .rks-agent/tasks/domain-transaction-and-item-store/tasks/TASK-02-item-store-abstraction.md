# TASK-02 — 物品存储抽象与两种实现，并对寻址充分性下结论

## Task Unit Contract

- Contract revision: 2（见 `## Contract Amendment A1`，增列 `IItemContainer.cs` 的 `ContainerId` 加法）
- Goal and deliverables: 交付以「容器与槽」寻址的物品存储抽象与会话契约，及其持久与仅存内存两种实现；并对「容器与槽是否足以表达移动、交换、合并、拆堆、消耗五种操作」下结论。
- Referenced spec requirements: RQ-010
- Referenced plan sections: `../plan.md` 的 `### T-002` 全节；BD-004、BD-005、BD-006、BD-015、BD-016
- Owned boundary: BD-004, BD-005, BD-006, BD-015, BD-016
- Permitted files or interfaces:
  - `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs`（新增）
  - `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs`（新增）
  - `Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs`（新增）
  - `Server/src/Dfo.Server.Application/ApplicationModule.cs`、`Server/src/Dfo.Server.Infrastructure/InfrastructureModule.cs`（注册）
  - `Server/tests/Dfo.Server.Infrastructure.Tests/`、`Server/tests/Dfo.Server.Application.Tests/`（新增存储用例）
- Dependencies: TASK-01
- Required dependency outputs: TASK-01 的参与者契约——存储会话即参与者，其持久性声明供协调器判定单一可失败参与者。
- Acceptance conditions:
  1. 五种操作（移动、交换、合并、拆堆、消耗）均以容器与槽表达并得到预期结果；表达用例先于实现写出。
  2. 同一会话内的读能看见该会话尚未提交的写。
  3. 持久实现复用既有已建立的表，未新增任何数据表定义。
  4. 仅存内存实现的提交是不可失败的状态替换，其准备阶段完成全部分配与校验。
  5. 寻址充分性结论落纸。若任一操作无法表达，本任务即停，记录该事实并回到契约层重议，不得引入替代的实例身份。
- Task-specific authorization: 沿用根 TaskContract 的 `task` 授权。收窄：不得新建任何数据表；若寻址充分性被证伪，不得自行发明替代的实例身份，须停止并交由控制器处置。
- Required methods: `test-driven-development`：五种操作的表达用例先于两种实现写出（plan T-002 步骤 3）。
- Required checks: CK-005、CK-006、CK-007（见 `../plan.md` 对应任务的 Validation 表，命令与预期结果以该表为准）
- Required evidence: CK-005 的 RED 与 GREEN 证据；CK-006 的建表语句检索结果；CK-007 的结论记录。

## Execution Result 1

### 交付物

- `Server/src/Dfo.Server.Abstractions/Inventory/IItemStore.cs`（新增）：`IItemStore`（存储身份、持久性声明、按事务开会话）、`IItemStoreSession`（`ReadAllAsync` / `ReadAsync` / `Write` / `Clear`）、`StoredItem(int SlotIndex, InventoryItemCoreState Core)`。词汇中没有任何物品身份，只有容器与槽。
- `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`（改）：`ContainerId` 增加成对的铸造与解析 `ForCharacter` / `TryGetCharacterOwner`。持久存储要从标识符走到行，二者放在一起才不会漂移。
- `Server/src/Dfo.Server.Infrastructure/Inventory/SqliteItemStore.cs`（新增）：持久实现。会话暂存写，`PrepareAsync` 开数据库事务并落全部暂存写，`CommitAsync` 只提交该事务——这是一次用例中唯一仍可能失败的一步，故 `CommitCanFail => true`。
- `Server/src/Dfo.Server.Application/Inventory/MemoryItemStore.cs`（新增）：仅存内存实现。`PrepareAsync` 算出每个被触及容器的整份替换内容，`CommitAsync` 只做赋值，故 `CommitCanFail => false`。
- 注册（BD-015）：`InfrastructureModule` 加 `SqliteItemStore` 单例（复用既有 `databasePath`），`ApplicationModule` 加 `MemoryItemStore` 单例。容器改用存储属 TASK-03，本任务只让两者在真实组合根中可构造。
- 用例：`Server/tests/Dfo.Server.Infrastructure.Tests/Inventory/SqliteItemStoreTests.cs`（11 条）、`Server/tests/Dfo.Server.Application.Tests/Inventory/MemoryItemStoreTests.cs`（12 条）。

### TDD 次序（plan T-002 步骤 3）

五种操作的表达用例先于实现写出。RED 证据：实现为 `NotImplementedException` 骨架时运行 CK-005，得 `失败: 10，通过: 1，总计: 11`，十条全部止于 `System.NotImplementedException`——即行为缺失而非构建失败；唯一通过的 `Session_TakesPartOnceHoweverOftenItsStoreIsAsked` 只触及登记去重，不触及任何未实现方法。GREEN 证据见 CK-005。

### 边界外的一处改动

`Server/src/Dfo.Server.Infrastructure/Inventory/SqliteInventoryEntryRepository.cs`：`EncodeItemCore` 由 `private static` 改为 `internal static`。该文件不在本任务的 Permitted files 内。

- 为什么必须动：持久存储要把 `InventoryItemCoreState` 写回 82 字节 blob。另一条路是在 `SqliteItemStore.cs` 里复制一份编码器，那正是会让帧字节漂移的重复。
- 为什么是最小改动：只是同程序集内的可见性放宽，无任何行为变化；同文件的 `DecodeItemCore` 早已因同一理由是 `internal static`（被 `SqliteEntryInventoryMutationRepository.cs:239` 使用），本改动与既有先例一致。
- 留待变更评审确认。

## Executor Self-check 1

| Acceptance condition | 结果 | 证据 |
|---|---|---|
| 1. 五种操作均以容器与槽表达并得到预期结果；表达用例先于实现写出 | 通过 | CK-005 两侧全绿；RED 证据见上 |
| 2. 同一会话内的读能看见该会话尚未提交的写 | 通过 | `Read_SeesThisSessionsOwnUncommittedWrites`（两侧各一条），同时断言此刻库内/存储内仍是旧值 |
| 3. 持久实现复用既有已建立的表，未新增任何数据表定义 | 通过 | CK-006 |
| 4. 仅存内存实现的提交是不可失败的状态替换，其准备阶段完成全部分配与校验 | 通过 | `Commit_OnlySwapsInWhatPrepareAlreadyWorkedOut`：未经 prepare 直接 commit 不改动存储，证明活都在 prepare 里 |
| 5. 寻址充分性结论落纸 | 通过 | 见 CK-007 |

自查发现并已处理：`ContainerId.ForCharacter` 引用 `CharacterId` 而该文件缺 `using Dfo.Server.Data.Characters;`，构建失败；已补。测试夹具只建了物品表未建 `characters` 表，`SqliteCharacterRepository` 才是其属主；已在夹具中补建。

### Validation

| ID | 命令 | 结果 |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | `已通过! - 失败: 0，通过: 11` |
| CK-005（内存侧） | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~ItemStore` | `已通过! - 失败: 0，通过: 12` |
| CK-006 | 在两种实现与抽象中检索 `CREATE TABLE|CREATE INDEX|ALTER TABLE|DROP TABLE` | 零命中。持久实现只指名 `character_new_items` 一张表，其属主是既有的 `SqliteInventoryEntryRepository`。运行期另有 `Store_DefinesNoTableOfItsOwn` 断言一次完整读—写—提交前后 `sqlite_master` 的表名集合不变 |
| CK-007 | 结论记录 | 见下节 |

## CK-007 — 寻址充分性结论

**结论：五种操作全部可以「容器与槽」表达。不引入任何替代的实例身份。** RQ-010 成立，计划照常推进到 TASK-03。

| 操作 | 表达 |
|---|---|
| 移动 | 一次 `Clear(源槽)` + 一次 `Write(目标槽, 原 core)` |
| 拆堆 | 两次 `Write`：源槽写回减量后的 core，目标槽写入新堆 |
| 交换 | 两次 `Write`，各自写入对方的 core |
| 合并 | 一次 `Write(目标槽, 合并后的 core)` + 一次 `Clear(源槽)` |
| 消耗 | 部分消耗为一次 `Write`；消耗到空为一次 `Clear` |

### 两处本可证伪该前提、经核查均不成立

**其一：化妆装细节存在第二张表。** 若 `character_avatar_detail` 以背包行号为键，则 clear+write 会把细节孤儿化，寻址就必须携带行身份。核查结果相反：该表的 `item_uid` 绑定的是 `item.Core.Value`（core 内携带的 avatar UID），读写两侧一致——写在 `SqliteInventoryEntryRepository.cs:200`，读在同文件 `:401`（`WHERE item_uid=@avatarUid`，参数为 `item.Core.Value`）。因此细节随 core 走，与附魔、随机属性一样是 core 内的事实。`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 把这一条钉住。

**其二：clear+write 会改变 `item_uid`。** 若有任何一处握着旧行号并在之后据其写回，行号变动就会造成不一致。核查结果：`character_new_items.item_uid` 的唯一卫星表是上述化妆装细节表（已确认不受影响）；而入场租约的精确写回入口 `TryMarkDirtyExact` **没有任何生产调用方**——全库只有接口声明（`IInventoryEntryServices.cs:273`）、一处实现（`SelectCharacterEntryInventoryDomainService.cs:132`）、一条测试与一个测试替身。故不存在据旧行号写回的路径。

### 一处顺带证实的收益

交换是决定性的用例。行寻址下槽位是唯一键的一部分，两端无法直写对穿——被替换的实现要把一行暂泊到 `slot_index = -1` 熬过事务。槽寻址下无需绕行：每个槽直接被告知它现在装什么。`Swap_IsExpressedAsTwoWritesWithNoParkedSlot` 的注释记录了这一点。

### 回归

`dotnet test Server/86JP.Server.slnx -m:1`：`885 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 394、Host 4、Infrastructure 181+2跳过、Networking 46、Protocol 200）。基线 848 + TASK-01 新增 14 + TASK-02 新增 23 = 885，无既有用例语义改动。

自查补记：`IItemStore.cs` 在一次改写后行尾退回 LF（`.editorconfig` 要求 CRLF），已改回并对本任务全部触及的 `.cs` 文件逐一核对，现零处孤立 LF。

## Contract Amendment A1

- Amends: Permitted files or interfaces
- 时点：Independent Quick Review 1 判 failed，唯一 blocking 项为「`IItemContainer.cs` 越界且未申报」。

**增列 `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 为本任务的 Permitted file，仅限对 `ContainerId` 的加法**：`ForCharacter` 与 `TryGetCharacterOwner` 及其所需 `using`。

为什么这一改动必须落在 TASK-02：持久存储要从容器标识符走到它命名的行，这需要解析；解析必须与铸造成对，否则两处格式会各自漂移。而 `ContainerId` 定义在 `IItemContainer.cs`——该文件的属主是 BD-008，plan 把 BD-008 派给 T-003 与 T-004。除此之外只剩三条路，都更差：把 `ContainerId` 拆成 partial 分散到两个文件（为迁就边界而伤设计）；让存储自建一份「容器→角色」登记表（把按角色的生命周期塞进单例，且那个事实标识符里已经写着）；或在存储侧另写一份解析（正是会漂移的重复）。

为什么不越出必要范围：本次只对 `ContainerId` 做加法，**不触碰** BD-008 交给 T-003/T-004 的那部分——`IItemContainer` 上的存储行标识与 `ContainerMovePlan` 等移动计划类型原样保留，删除它们仍是后续任务的事。

授权：属根 TaskContract `task` 授权范围内的边界记录，不扩大对外可观察行为，留待 change review 确认。

## Remediation 1 (post-verdict)

Independent Quick Review 1 判 **failed**，1 项 blocking、12 项 non-blocking。处置如下。

### Blocking

| findings | 处置 |
|---|---|
| `IItemContainer.cs` 越界且未申报（BD-008 属 T-003/T-004） | 已按 `## Contract Amendment A1` 增列为 Permitted file，并限定为仅对 `ContainerId` 的加法。评审同时确认「**代码本身是对的**」，故未改写实现。原 Execution Result 的 `### 边界外的一处改动` 只申报了 `EncodeItemCore`，漏报了这一处——这是自查的疏失，非评审误判 |

### 已修正的实现缺陷

| findings | 修改 |
|---|---|
| `OwnerOf` 接受 `AccountCargo` 并映射到 `character_new_items`，而账号仓库的行在另一张按账号键的表里；写入会造成角色下次入场被拒 | `SqliteItemStore.OwnerOf` 现只接受该表确实承载的五种（Main / Avatar / PersonalCargo / Equipment / Pet），其余抛出。原文档注释宣称「不能安放的容器会被拒绝」，此前对这一条恰恰是假的 |
| `EmptyAsync` 的 DELETE 谓词窄于它要清空的唯一键：`character_id IS NULL` 的行会存活并在插入时冲突 | DELETE 改为与 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 逐字对齐，去掉多余的 `character_id` 条件 |
| `RollbackAsync` 未用 `try/finally` 包住 `CloseAsync`；回滚抛出时连接泄漏 | 提交与回滚都改为 `try/finally`，收尾统一到新的 `SettleAsync`（释放事务、清暂存、关连接） |
| 两种实现都不校验槽号，`Write(slot: -1)` 可落库，而 `ValidateSlot` 要到角色下次入场才拒 | 两种实现的 `Write` / `Clear` 均拒绝负槽号。CK-007 的叙事正建立在旧实现用 `slot_index = -1` 暂泊上，这个值更不该还能写进去 |
| 未 prepare 直接 commit 时静默丢弃全部暂存写；而该行为还被当作验收条件 4 的证据 | 两种实现改为**拒绝**：有暂存改动却未经 prepare 的提交抛出。理由写进注释——在这里补做那些活会让提交成为用例中第二个可能失败的动作，而静默丢弃会表现为道具凭空消失而不是异常。两侧的 `Commit_OnlySwapsInWhatPrepareAlreadyWorkedOut` 相应改写为 `Commit_RefusesToStandInForAPrepareThatNeverRan`，仍然证明「活都在 prepare 里」，但改由可见的失败来证明 |
| `TryGetCharacterOwner` 的接受面远宽于 `ForCharacter` 的铸造面：`"character:60:7"`、`"character:60:Main, Avatar"`（枚举解析会把逗号列表相加，即便不是 flags）、填充空白、`+60`、前导零，都能解析成同一个容器的**不相等**标识符。两种实现都以标识符原文为键，于是一次会话经甲写、经乙读将读不到自己的写——正是验收条件 2 会被打破 | 解析改为只接受 `ForCharacter` 会铸出的那一种拼法：数字段须 `NumberStyles.None` 且往返回原文（排除符号、空白、前导零），类型段须 `Enum.GetName` 等于原文（排除数字别名与逗号列表）。新增 `ContainerIdTests`（4 组共 17 例）把每一种别名钉成拒绝 |

### 已补的覆盖

| findings | 新增用例 |
|---|---|
| 无用例断言 `SqliteItemStore.IsDurable` / `CommitCanFail`——而「最多一个可失败参与者」正挂在这一条上 | `Store_IsTheDurableParticipantWhoseCommitCanFail` |
| 两侧全部用例都只用一个容器，而 `PrepareAsync` 的「先全删后全插」两趟正是为跨容器而设 | 两侧各一条 `TwoContainersExchangeTheirContentsInOneSettlement` |
| `OwnerOf` 拒绝非角色容器是有文档的行为，却无覆盖 | `AContainerThisStoreKeepsNoRowsForIsRefused`（含 `AccountCargo` 与非角色标识符两种） |
| 同槽先 `Write` 后 `Clear` 未覆盖（先 `Clear` 后 `Write` 仅内存侧有） | 两侧各一条 `ClearingASlotThisSessionJustWroteLeavesItEmpty` |
| 负槽号无覆盖 | 两侧各一条 `ANegativeSlotIsRefused` |

### 已更正的记述

- **RED 证据的范围**。原文让人以为两侧的表达用例都先于实现写出。准确说法：plan T-002 步骤 3 要求的那一套五操作表达用例（持久侧，11 条）确实先于**两种实现**写出并留有 RED 证据；内存侧的 12 条是实现之后补的同形对照，其价值在于证明同一套词汇在两种载体上说得通，不是 TDD 证据。验收条件 1 由前者满足。
- **注册的实际效果**。原文称「让两者在真实组合根中可构造」。准确说法：两者都以 internal 具体类型注册，尚无任何消费者解析它们，故 `MemoryItemStore` 实际不会被构造。注册只是让 BD-015 落位并证实 `SqliteItemStore` 所需的 `databasePath` 在组合根可得；接上消费者是 TASK-03 的事。
- **CK-007 的卫星表枚举**。原结论只枚举了以 `item_uid` 为键的卫星表。另有一张以**槽**为键的：`character_item_locks`（`SqliteInventoryEntryRepository.cs:986-994`）键为 `(character_id, equipment_lock_id)`，并把 `inventory_list_type` 与 `slot` 作为数据携带。clear+write 到另一个槽后，该行的槽会变陈旧。**这不是本次引入的回归**——旧的按行移动有完全相同的缺口，且 `EquipmentLockId` 本身就在 core 的第 81 字节里随物品走。但寻址充分性的结论应当把它记上，而不是只说「唯一的卫星表是化妆装细节」。结论本身不变：五种操作仍全部可以容器与槽表达。

### 移交 TASK-03 的两项

1. `SelectCharacterEntryInventoryDomainService.cs:59` 仍在手写 `$"character:{characterId.Value}:{main.ContainerType}"` 这一格式，未走 `ContainerId.ForCharacter`。两者今天一致，但「铸造与解析放在一起就不会漂移」的说法要等这一处也改用 `ForCharacter` 才真正成立。该文件属 BD-007，是 TASK-03 的范围。
2. `character_item_locks` 的槽字段在物品移动后会陈旧（见上）。既有缺口，非本次引入；容器改由存储承载时应一并判断是否要跟随。

### 复验

| ID | 命令 | 结果 |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | `已通过! - 失败: 0，通过: 17`（原 11 + 新增 6） |
| CK-005（内存侧） | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~ItemStore|FullyQualifiedName~ContainerId` | `已通过! - 失败: 0，通过: 32`（内存 15 + 标识符 17） |
| 回归 | `dotnet test Server/86JP.Server.slnx -m:1` | `911 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 414、Host 4、Infrastructure 187+2跳过、Networking 46、Protocol 200）。较上一轮 885 增 26 条，全部为本次补的覆盖；无既有用例语义改动 |

评审复跑的全套结果（885 通过）与本次一致，其判定的 CK-006、CK-007 两条结论均为 SOUND，故寻址充分性结论不变。

## Independent Quick Review 2

复评（另一位评审，未参与首评）判 **passed-with-findings**，**无 blocking**。首评的唯一 blocking 项经复核确认已解决：`IItemContainer.cs` 的改动为 +49/-0，只有两条 `using` 与 `ContainerId` 上的两个新成员；BD-008 留给 T-003/T-004 的 `StorageEntryId`、`ContainerMoveKind`、`ContainerMovePlan`、`ApplyMoveAsync`、`ConsumeAsync` 与基线逐字节一致，确实停在 A1 声明的范围内。

复评独立复算并确认的关键结论：

- `OwnerOf` 的白名单与 `ParseContainerType(..., allowAccountCargo: false)` 逐项一致，是精确修正而非防御性收紧。
- 去掉 `character_id` 后的 DELETE 谓词恰为唯一键本身，最多命中一行，且必是随后 INSERT 会撞上的那一行；`owner_scope = 'character'` 是字面量，账号域的行不可能被误删。
- `ContainerId` 的接受串已是 `(owner, type)` 的全函数——`parts[1]` 由往返约束唯一确定，`parts[2]` 由 `Enum.GetName` 唯一确定，枚举无重复值——故一个容器的两种拼法不可能同时被接受。评审尝试构造别名未果。
- **CK-007 的结论在补记 `character_item_locks` 之后仍然成立**，理由比原记述更强：该表把 `(inventory_list_type, slot)` 作为**数据**携带，它指向的正是「容器与槽」而不是行；引入实例身份并不能修好它，跟随物品所需的恰是一次容器与槽的更新，仍在同一套词汇内。且全库对该表只有 CREATE 与一处 SELECT，无任何写入，旧的按行移动同样让它陈旧。

## Remediation 2 (post-verdict)

复评为 passed-with-findings，本可直接接受。以下四项虽非阻断，但修正代价极小且确属正确，故一并闭合。

| findings | 修改 |
|---|---|
| `SqliteItemStoreSession.PrepareAsync` 在**动手之前**置 `prepared = true`，而内存侧在**做完之后**置；两者对「标记该放在准备的哪一端」意见相左。前者是 fail-open：若准备中途抛出，之后的提交会提交半批而不是拒绝 | 改为做完后再置。经 `DomainTransaction` 时该路径不可达（准备失败一律走回滚），但不应由调用方的正确性来兜底。注释写明理由 |
| `SettleAsync` 自身不是异常安全的：未完成的事务在 `DisposeAsync` 时会真的执行 ROLLBACK，连接已损坏时可能抛出，于是 `CloseAsync` 被跳过、连接泄漏——正是上一轮想关掉的那类泄漏的更窄形态 | `SettleAsync` 内部改用 `try/finally`，无论释放事务是否抛出都关连接。注释写明「连收尾都做不到的会话，恰恰是那个绝不能把句柄留着的会话」 |
| `TryGetCharacterOwner` 对 `default(ContainerId)` 抛 `NullReferenceException`，而 `Try*` 应当答 false | 加空值判断，并新增 `AnIdentifierThatWasNeverMintedIsRefused` |
| `PrepareAsync` 的注释宣称两趟「先全空后全填」是交换所必需，实则不然：`Write` 以 `item.SlotIndex` 为键，每次插入的槽必然是本批刚删过的那个，逐槽交错也不会撞；且唯一键含 `list_type`，两个容器之间本就无从相撞。测试 `TwoContainersExchangeTheirContentsInOneSettlement` 的注释同样把它说成在约束顺序 | 两处注释改为陈述真实的不变量：每次插入落在本批已清空的槽上，两趟只是让这一点无条件成立而不依赖暂存的形状；测试注释改为它实际约束的东西——一次会话触及多个容器时作为一批落定 |

### 记录在案、不予修改的三项

| findings | 判断 |
|---|---|
| 读仍带 `AND character_id = @owner` 而 DELETE 不带：`character_id IS NULL` 的行对读不可见却会被写删除 | 保持。该行对其属主仓储同样不可见（`SqliteInventoryEntryRepository.cs:300`），删它不丢任何活数据；另一条路是唯一键冲突并让整个用例失败，更糟。此处已由复评记录，留待 change review |
| `ForCharacter` 对负数角色号铸出的标识符会被自己的解析拒绝，非全函数往返 | 保持。`character_id` 是 `INTEGER PRIMARY KEY AUTOINCREMENT`，负号今天不可达；`NumberStyles.None` 排除符号是刻意的——角色号为负本身就该被拒 |
| RED 证据无法由交付物独立复现（无提交、未留运行产物） | 属本任务「全程不产生任务级提交」这一约束的必然结果（plan `## Risks and Rollback`）。复评已核对其内部自洽：原 11 条中恰有一条不触及任何 store 方法，与记录的 `失败 10 / 通过 1` 相符 |

### 移交 TASK-03 的第三项

`plan.md` 的 BD-008 仍把 `IItemContainer.cs` 整体派给 T-003/T-004，而 A1 已就 `ContainerId` 作了切分。已批准的 plan 不在实现阶段改动，故该切分记在 TASK-03 的任务文件上，避免 T-003 的执行者只读 plan 而不知情。

### 复验

| ID | 命令 | 结果 |
|---|---|---|
| CK-005 | `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests -m:1 --filter FullyQualifiedName~ItemStore` | `已通过! - 失败: 0，通过: 17` |
| CK-005（内存侧） | `dotnet test Server/tests/Dfo.Server.Application.Tests -m:1 --filter FullyQualifiedName~ItemStore\|FullyQualifiedName~ContainerId` | `已通过! - 失败: 0，通过: 33` |
| 回归 | `dotnet test Server/86JP.Server.slnx -m:1` | `912 通过 / 2 跳过 / 0 失败`（Pvf 60、Application 415、Host 4、Infrastructure 187+2跳过、Networking 46、Protocol 200）。较上一轮增 1 条，为本轮新增的空标识符用例 |

复评亦独立复跑三条并逐字复现了上一轮的 17 / 32 / 911，`ChannelHostStartupTests` 未假红。

## 结论

TASK-02 达成其全部五条验收条件，CK-005、CK-006、CK-007 均通过，寻址充分性结论为**肯定**：移动、交换、合并、拆堆、消耗五种操作全部可以「容器与槽」表达，未引入任何替代的实例身份。RQ-010 成立，计划照常推进至 TASK-03。
