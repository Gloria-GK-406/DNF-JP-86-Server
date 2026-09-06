# TASK-02 — 档案说是时装时，发放通路造出一件完整的时装

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

让档案判定为时装的模板经发放通路入库之后是完整的：种类、身份、六列事实齐备，且能被重新装载、再次存盘、再次装载。

- Referenced spec requirements: RQ-001, RQ-002, RQ-003
- Referenced plan sections: plan.md 的 T-002（Steps 1–4，CK-004..CK-007）；Owned Boundaries 的 BD-003、BD-004、BD-008
- Owned boundary:

BD-003 `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`
BD-004 `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`
- `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
- `Server/tests/Dfo.Server.Application.Tests/` 与 `Server/tests/Dfo.Server.Infrastructure.Tests/` 下的判据

- Dependencies: TASK-01
- Required dependency outputs: TASK-01 交付的「写入可自带时装事实」契约与其结算实现
- Acceptance conditions:

1. 档案元数据对判定为时装的模板给出**时装那个种类**（与服务器各处判定时装所用的相同）。
2. 发放通路对时装造出时装形状：一个**抽取得到的、大于零的**身份值，加六列默认事实——珠宝槽 30 个零字节，两个颜色为零，清除标识为零，删除日期为零，**到期日取 32 位有符号秒数的上界**。
3. 到期日那个取值在代码里**写明它是占位裁定**：本仓语义中该列非空表示「这一行是时装」而非「它会过期」，线上那一格写的是剩余秒数；上界是当前可辩护的「不过期」表达，实机会检验客户端如何渲染它。
4. **非时装模板的落地路径一字不动。**
5. 「发放 → 装载 → 存盘 → 再装载」全程无拒绝无回滚，物品仍在，身份与事实与入库时相同。
6. `PvfEntryLifecycleContentSource` 里「时装被有意留着不动」那句已不再成立的记载已更正，**按仓规补记修正案**。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**若为时装造出「时装形状」需要动到 `IItemContainer.AddAsync` 的签名、或需要新建第二条发放通路，停下并提交阻塞报告。**
计划的中心做法是「只扩展已有的写入契约」；`ItemContainers.cs` 里那段记载明确反对多条发放路径。

**计划已否掉的三条替代不得重新采用**：不要让结算把行的世界身份回填进核心；不要给写入契约加「预支身份」的动词；不要放宽「身份必须大于零」那三处强制。后两条是改**明文要求**，仓规要求先停下来问。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；用例数相对 CK-001 的增量全部来自新增判据 |
| CK-005 | Non-command：对真实档案取一个时装模板，检查发放通路交给结算的种类 | 与服务器各处判定时装所用的种类相同 |
| CK-006 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 新判据走通「发放 → 装载 → 存盘 → 再装载」，全程无拒绝无回滚 |
| CK-007 | Non-command：备份并核散列后，把发放通路造出的事实改成空，重跑 CK-006 的判据，再由备份还原并核散列 | 该判据变红；还原后恢复绿 |

- Required evidence:

- 四条检查的实际输出与退出码。
- CK-007 变异前后的散列与红/绿结果。
- 被更正记载的前后对照。
- 用例数增量的逐条归因。

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113（未读的主构造器参数）是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单**不得放宽或扩充**
- 领域层 `store` 只指事务结算的真源
- 既有断言的期望值**不得为迁就实现而改写**；唯一例外是正对应 `spec.md` 列出的某条行为变化，且须逐条归因
- 不裁定契约兽槽位 24 还是 25；**不给 `PvfEquipmentSlots.ByName` 补任何键**
- 注释是参考材料不是铁律，判断以代码设计为准；但**改已被证伪的记载要补记修正案**，改「要求」必须停下来问

## 执行结果

### 改了哪些文件，每处改了什么（按成员名）

**边界内 — 行为变化（2 个文件）**

1. `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs`
   - 新增 `AvatarItemKind`（值 8）常量，带 doc：说明这是存储侧每个读者与写者判定时装所用的号，与 `CreatureItemKind` 并列，理由相同。
   - `WornPieceMetadata` 的第二个参数由三元式改为 `switch`：`Creature => CreatureItemKind`、`Avatar => AvatarItemKind`、其余 `=> WornPieceItemKind`。堆叠上限、槽段（9..64）、`WorldClass` 一字未动。
   - `WornPieceMetadata` 的 `<remarks>` 中「时装被有意留着不动」那段按仓规补记修正案（前后对照见下）。

2. `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
   - 新增 `internal static class ArrivingAvatar`，三个成员：
     - `ItemKind`（8）——落地分支据以判定的号，与存储侧同一个号；
     - `NewIdentity()` —— `Random.Shared.Next(1, int.MaxValue)`，抽取不排序，doc 照抄 `CharacterRosterService.EquipmentCore` 的理由（「a sequence would need a row of its own to stay distinct across restarts」）；
     - `Facts()` —— 六列默认事实：珠宝槽 `new byte[30]`、两色 0、清除标识 0、删除日期 0、到期日 `int.MaxValue`（私有常量 `NeverExpires`）。`NeverExpires` 的 doc 明写它是**占位裁定**：本仓 `avatar_expire_date` 非空表示「这一行是时装」而非「会过期」，线上那一格写的是 `max(0, ExpireDate - now)` 的剩余秒数，上界是当前可辩护的「不过期」表达，实机会检验客户端如何渲染它。
   - `StoreBackedItemContainer.PutAsync` 拆成两层：公开签名不变，转调新增的私有 `WriteOneAsync(slotIndex, core, avatarDetail, ct)`；分类与 `session.Write` 仍只有这一处。**`IItemContainer.AddAsync` 与 `PutAsync` 的签名一字未动，未新建第二条发放通路。**
   - `StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支：先造 `landing` core，`isAvatar = itemKind == ArrivingAvatar.ItemKind`；是时装则把 core 的 `Value` 换成 `ArrivingAvatar.NewIdentity()` 并带上 `ArrivingAvatar.Facts()`，否则原样调 `WriteOneAsync(..., null, ...)`。`OntoExistingPile` 分支、`itemKind == 0` 的拒绝、`NoRoom`、`default` 全部未动。
   - `PutAsync` 的 `<remarks>` 中「the kind byte the core carries and the archive's answer disagree today」补记修正案（前后对照见下）。

**边界内 — 只新增判据（1 个文件，新建）**

3. `Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs`
   - `The_archive_and_storage_answer_the_same_kind_for_one_avatar`
   - `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`

   两条都用运营者本机档案（`SkippableFact`）与真实 SQLite 库，不打桩。

**跨边界 — 只改被证伪的记载，零行为（4 个文件）**

本轮把「档案给时装 1、存储记 8」这条记载变成假的，仓规（本 Task 契约 Constraints 末条）要求**改已被证伪的记载并补记修正案**。除契约点名的那一处外，仓里另有 **3 处**载有同一条已被证伪的断言，**分别是**（计数口径：`grep -rn "kind 8" Server/src Server/tests --include=*.cs` 去掉 `obj/`、`bin/` 后，语义上断言「档案与存储对时装不一致」的注释；按成员名索引）：

4. `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs` —— `ItemWorldClass` 的 `<remarks>`
5. `Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs` —— `ClassifyAsync` 的 `<remarks>`
6. `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` —— `ClassifyAsync` 的 `<remarks>`
7. `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` —— 类级 `<remarks>`

四处都是**注释文本**，一行可执行代码未改，**一条既有断言的期望值未改**。第 7 处在契约允许的目录内但属「只新增判据」之外，同样按修正案格式处理。**若评审认为跨边界改注释越界，撤掉 4/5/6（及 7 的注释段）不影响任何检查的红绿。**

### 四条检查的实际输出与退出码

**基线（改动前，同一工作区）**：退出码 0

```
失败: 0，通过:   521 - Dfo.Server.Protocol.Tests
失败: 0，通过:    52 - Dfo.Server.Networking.Tests
失败: 0，通过:    60 - Dfo.Pvf.Tests
失败: 0，通过:     6 - Dfo.Server.Host.Tests
失败: 0，通过:   317 - Dfo.Server.Persistence.Tests
失败: 0，通过:   236，已跳过: 2 - Dfo.Server.Infrastructure.Tests
失败: 0，通过:  1025 - Dfo.Server.Application.Tests
```

合计 2217 通过 / 2 跳过 / 0 失败，与契约给的基线一致。

**CK-004** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` → **退出码 0**

```
已通过! - 失败: 0，通过:     6，已跳过: 0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败: 0，通过:    52，已跳过: 0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败: 0，通过:    60，已跳过: 0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败: 0，通过:   236，已跳过: 2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败: 0，通过:   317，已跳过: 0，总计:   317 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败: 0，通过:   521，已跳过: 0，总计:   521 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败: 0，通过:  1027，已跳过: 0，总计:  1027 - Dfo.Server.Application.Tests.dll (net10.0)
```

合计 **2219 通过 / 2 跳过 / 0 失败**。

**CK-005**（Non-command）对真实档案的时装模板 `101550507`（`equipment.lst` 里的时装，`PvfDropRateTableSourceTests.No_avatar_declares_itself_creatable` 已在用同一批标识符）：

- 档案交给结算的种类：`ItemPlacementRuleService.PlacementForAsync(101550507).ItemKind == 8`，且 `ClassifyAsync(101550507) == ItemWorldClass.Avatar`；
- 存储侧判定时装的种类：8（`InventoryEntryAssembly.WithAvatarDetail` 与 `SaveAsync` 的时装扫描、`GameInventoryDelivery`、`StateSynchronizationPayloadCodec.Entry` 都以 8 判）；
- 且发放通路实写下去的那一行读回来也是 8。

判据 `The_archive_and_storage_answer_the_same_kind_for_one_avatar` 逐条断言以上三者，**通过**：

```
已通过 Dfo.Server.Application.Tests.Inventory.GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar [551 ms]
```

判据里的 8 写成字面量而不是从发放通路借常量，所以「发放通路自己跟自己一致」不足以让它变绿。

**CK-006** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo`

第一次 → **退出码 1**，唯一失败为**既知偶发**，按「异常类型 + 栈根 + 复跑即绿」判别（不按用例名）：

```
System.ObjectDisposedException : Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'.
   at Dfo.Server.Persistence.Schema.CharacterDatabaseSchema.KeepWritersAndReadersApart(...)
   at Microsoft.Data.Sqlite.SqliteCommand.ExecuteScalar()
失败!  - 失败: 1，通过: 1026，已跳过: 0，总计: 1027
```

（本次落在 `OneDatabaseOneParticipantTests.TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant`，与本轮改动无关：该用例不碰时装、不碰档案。）

复跑 → **退出码 0**

```
已通过! - 失败: 0，通过: 1027，已跳过: 0，总计: 1027，持续时间: 10 s - Dfo.Server.Application.Tests.dll (net10.0)
```

往返判据本身：

```
已通过 Dfo.Server.Application.Tests.Inventory.GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts [449 ms]
```

它走的是「发放（`IItemContainer.AddAsync`，四个 use case 中的第一个）→ 装载（`CharacterInventoryEntryDomainService.LoadAsync`，跨 `InventoryEntryAssembly.WithAvatarDetail` 的三条硬校验）→ 存盘（`InventoryEntryAssembly.SaveAsync`，跨 kind 8 的三条硬要求：`Core.Value > 0`、事实非空、珠宝槽恰 30 字节）→ 再装载」，全程无异常、无回滚，物品仍在原槽，身份（`Identity` 与 `Core.Value`）与六列事实与入库时逐字段相同。

**CK-007**（Non-command）变异实测，全程文件级备份 + `sha256sum`，**未用任何 git 还原类命令**。

散列（绿态，备份即此态）：

```
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da *Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
```

变异 A（先红后绿的那一步，把种类改回改动前的答案）：`WornPieceMetadata` 里 `ItemWorldClass.Avatar => AvatarItemKind,` 改成 `=> WornPieceItemKind,` → **两条新判据同时变红**：

```
GrantedAvatarIsWholeTests.The_archive_and_storage_answer_the_same_kind_for_one_avatar [FAIL]
   Assert.Equal() Failure: Values differ  Expected: 8  Actual: 1
GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts [FAIL]
   Assert.Equal() Failure: Values differ  Expected: 8  Actual: 1
失败!  - 失败: 2，通过: 0，总计: 2
```

由备份还原 → `sha256sum -c` 两个文件均 `OK`。

变异 B（CK-007 本体，把发放通路造出的事实改成空）：`LandAsync` 里 `isAvatar ? ArrivingAvatar.Facts() : null,` 改成 `null,`（身份仍照造，所以变红只能归因于「事实」这一半）

变异态散列：

```
4c29c7ef4c9bebd4728c43bba7c94b6c40f3240b87f2540eb6374f38f4c64506 *Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
```

→ 往返判据 **变红**，**退出码 1**：

```
GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts [FAIL]
   System.IO.InvalidDataException : Avatar detail 74044469 is unavailable.
     at InventoryEntryAssembly.WithAvatarDetail(...) InventoryEntryAssembly.cs:line 386
     at InventoryEntryAssembly.ReadCharacterItemsAsync(...)
     at InventoryEntryAssembly.LoadCharacterAsync(...)
失败!  - 失败: 1，通过: 1，总计: 2
```

（种类那条仍绿，符合预期：它管的是种类不是事实。）

由备份还原 → `sha256sum -c` 两个文件均 `OK`，复跑 → **退出码 0**：

```
已通过! - 失败: 0，通过: 2，已跳过: 0，总计: 2 - Dfo.Server.Application.Tests.dll (net10.0)
```

交付态散列与绿态备份一致（见上面两行），已复核。

### 用例数增量的逐项目归因

| 项目 | 基线 | CK-004 实测 | 增量 | 归因 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 521 | 0 | — |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | — |
| Dfo.Pvf.Tests | 60 | 60 | 0 | — |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | — |
| Dfo.Server.Persistence.Tests | 317 | 317 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 236（+2 跳过） | 236（+2 跳过） | 0 | — |
| Dfo.Server.Application.Tests | 1025 | 1027 | **+2** | 全部来自新建的 `GrantedAvatarIsWholeTests`：`The_archive_and_storage_answer_the_same_kind_for_one_avatar`（1 条）与 `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`（1 条） |
| **合计** | **2217 / 2 跳过** | **2219 / 2 跳过** | **+2** | 逐条如上 |

### 被更正记载的前后对照

**（一）`PvfEntryLifecycleContentSource.WornPieceMetadata` 的 `<remarks>`**（契约点名的那一处）

改前：

> **The avatar is deliberately left alone.** Storage records an avatar as kind 8 and this still answers 1 for one, so that pair still disagrees. It is the same defect and it is not this outcome's to fix; naming it here keeps the remaining disagreement visible rather than letting the creature's repair read as if every kind now agreed.

改后（保留原文引用，标注 amended）：

> **Amended; what this paragraph said before is quoted at the end of it.** The avatar was the second half of that same defect and it is answered here now: storage records an avatar as kind 8, every reader and writer of an avatar row looks for that 8, and this gives an avatar template the 8 rather than the 1. So the pair no longer disagrees, and an avatar that arrives through this reading is banked as the avatar it is.
>
> What this paragraph said before: "…（原文全文引用）…" It was true of the outcome that repaired the creature, and it stopped being true here. It is corrected rather than deleted because what it was for — keeping the disagreement visible instead of letting a partial repair read as a whole one — is the reason there is a paragraph here at all.

**（二）`ItemContainers.StoreBackedItemContainer.PutAsync` 的 `<remarks>`**

改前：「The class is asked of the template rather than read off the core: **the kind byte the core carries and the archive's answer disagree today**, and the point of the classification is that the archive wins.」

改后：改为「a caller is free to hand over a core whose kind byte says something the archive does not」，并加一段 amended 引用原句、说明两对（兽的与时装的）现已一致，因此「问模板不问行」的理由换成了「调用方仍可递任意 core」。

**（三）`ItemPlacementContent.ItemWorldClass` 的 `<remarks>`**（跨边界）

改前：「the two still disagree -- an avatar is stored as kind 8 while the archive's equipment index gives it kind 1. … That one is now answered by the archive as the 5 it is; **the avatar's is the same defect left standing**.」

改后：正文改成「a caller is free to hand a write a core saying anything at all」，另起 amended 段全文引用原句，说明时装那一半也已站不住。

**（四）`IItemPlacementRuleService.ClassifyAsync` 的 `<remarks>`**（跨边界）

改前：「Those two still disagree -- an avatar is stored as kind 8 and the archive's equipment index gives it kind 1 -- …」

改后：同样式 amended，引用原文并说明现已一致。

**（五）`ItemPlacementRuleService.ClassifyAsync` 的 `<remarks>`**（跨边界）

改前：「the byte and the archive still disagree about an avatar -- stored as kind 8, given kind 1 by the equipment index -- …」

改后：同样式 amended，引用原文并说明现已一致；守卫本身与签名一字未动。

**（六）`ItemClassificationTests` 的类级 `<remarks>`**（跨边界，测试注释）

改前：「an avatar is stored as kind 8 while the archive's equipment index gives it kind 1」以及「The contract creature was a second such pair and is no longer one…」

改后：同样式 amended，引用原文并说明两对现已一致；**该文件的两条用例、其断言与期望值一字未改**。

### 既有判据的期望值是否被改

**无。一条也没有。**

逐项核对：`git diff --stat` 里唯一被改的既有测试文件是 `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs`，改动全部落在类级 `<remarks>` 的散文里（见上「（六）」），其 `EquipmentKindByte = 1`、`CreatureKindByte = 5`、`ConsumableKindByte = 2` 三个常量与两条用例的每一句 `Assert` 均未改动。`SqliteItemDomainStoreTests.cs` 的改动属 TASK-01，不在本轮。

特别检查了可能因「种类由 1 变 8」而受影响的既有判据，**结论是没有一条需要改期望值**：

- `PvfEntryLifecycleContentSourceTests.DailyRefill_RealArchiveProducesAuthoritativeRulesWithStackLimits` 断言 `rule.ItemKind` 属于 `{1,2,3,4,7,9,10,11}`，且逐项等于 `{2680738:4, 2749110:4, 10002520:4, 10093393:4, 10003235:4, 690001556:4}` —— 真实档案的每日补给表里全是任务物（kind 4），没有时装，所以这条不受影响，实测仍绿；
- `PvfEntryLifecycleContentSourceTests.EquipmentType_IsReadAsTheAvatarTheCreatureOrOrdinaryGear` 断言的是 `ItemWorldClass` 不是 kind，未受影响；
- `ItemSlotRuleServiceTests`、`ClassificationRefusalTests`、`AvatarCarrierRulesTests`、`AvatarDetailFollowsTheIdentityTests` 都直接用 8，本来就与新答案一致。

CK-004 全绿即是这一条的实测支撑。

### 执行者自查（逐条对照契约验收条件）

1. **档案元数据对判定为时装的模板给出时装那个种类。** 达成。`WornPieceMetadata` 的 switch 对 `ItemWorldClass.Avatar` 返回 `AvatarItemKind = 8`；凭据是 CK-005 的判据对真实档案模板 `101550507` 实测 `placement.ItemKind == 8`，且变异 A 把这一行改回去后该判据立刻变红（`Expected: 8 Actual: 1`）——不是 grep 断言，是变异实测。

2. **发放通路对时装造出时装形状：抽取的、大于零的身份值 + 六列默认事实。** 达成。`ArrivingAvatar.NewIdentity()` 用 `Random.Shared.Next(1, int.MaxValue)`（抽取，不排序，下界 1）；`ArrivingAvatar.Facts()` 给出珠宝槽 30 个零字节、两色 0、清除标识 0、删除日期 0、到期日 `int.MaxValue`。凭据是往返判据逐字段断言这六个值加 `Core.Value > 0`，且变异 B 把事实改空后它变红。

3. **到期日那个取值在代码里写明是占位裁定。** 达成。`ArrivingAvatar.NeverExpires` 的 `<remarks>` 以 “A placeholder ruling, and it is named as one.” 开头，写明：本仓 `avatar_expire_date` 非空的含义是「这一行是时装」而非「会过期」；线上那一格写的是 `max(0, ExpireDate - now)` 的剩余秒数；上界是当前可辩护的「不过期」表达；并明说「Nobody has yet watched a client draw an avatar whose remaining seconds are two billion… Live play is what settles it」，把实机检验记为待办而不是既成结论。

4. **非时装模板的落地路径一字不动。** 达成。`LandAsync` 里非时装分支仍是 `StackableItemCore.Create(itemKind, itemId, opened.Count)` 原样写下去，`avatarDetail` 传 `null`；`OntoExistingPile`、`itemKind == 0` 的拒绝、`NoRoom` 与 `default` 四处未改。`PutAsync` 的公开签名与语义不变（转调 `WriteOneAsync(..., null, ...)`）。凭据是 CK-004 全绿：既有 2217 条里包含发放、补给、移动、拆分、合并、消耗、卡片直放的判据，无一变红，且用例数增量精确为 +2。

5. **「发放 → 装载 → 存盘 → 再装载」全程无拒绝无回滚，物品仍在，身份与事实与入库时相同。** 达成。由 `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 走通，四步各是一个独立 use case（各自 `Begin` + `CommitAsync`），中间跨过 `WithAvatarDetail` 的三条校验与 `SaveAsync` 的三条硬要求。断言比对的是两次装载读回来的 `Identity`、`Core.ItemId`、`Core.ItemKind`、`Core.Value` 与六列事实逐字段相等——不是相信发放通路自称写了什么。

6. **`PvfEntryLifecycleContentSource` 那句已不再成立的记载已按仓规补记修正案。** 达成。见「被更正记载的前后对照（一）」：原文全文引用保留，标注 amended，说明它此前说的是什么、何时因何停止成立、为什么更正而不是删除。另外 5 处同类记载一并按同样格式处理（其中 3 处跨边界，已在上文单列并说明可撤）。

**阻塞条件核对**：未触发。造出「时装形状」既没有动 `IItemContainer.AddAsync` 的签名，也没有新建第二条发放通路——`session.Write` 仍只有 `WriteOneAsync` 这一个调用点，`PutAsync` 只是它的一个入口，`ItemContainers.cs` 里「The one place that does…」那段记载仍然成立。

**被否掉的三条替代**：均未采用。结算没有把 `item_uid` 回填进 core（身份是发放通路抽的，写进 `Core.Value`）；写入契约没有新增「预支身份」的动词（仍是 TASK-01 那一个 `AvatarDetail` 属性）；「身份必须大于零」那三处强制一字未放宽——变异 B 的红正是靠其中之一（装载侧）打出来的。

**硬约束核对**：分层未变（Infrastructure 只改自己的文件，Application 只改自己的文件，新判据所在的 Application.Tests 早已引用 Infrastructure/Persistence 且其 csproj 注释写明这是测试项目的允许）；`dotnet build` 0 警告 0 错误（`TreatWarningsAsErrors` 开启，无 CS9113）；未新增协议帧、未改任何帧的字节布局；`CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 随 CK-004 全绿且未被改动；未给 `PvfEquipmentSlots.ByName` 补任何键；未裁定契约兽槽位 24/25；未建 Git 检查点、未提交；未触碰主检出与 `.worktrees/client-debug-lab`；变异验证全程文件级备份 + `sha256sum`，未使用任何 git 还原类命令。

## Independent Quick Review — QR-TASK02-R1

- 评审者：独立快审（fresh context，非执行者）
- 评审的契约版本：Contract revision 1（无修正案）
- **判定：`changes_required`**

### 被评审的 owned diff 身份

工作区 `.worktrees/server-agrees-what-an-avatar-is`，未提交。本轮认定为 TASK-02 所有的是 **7 个文件**，
分别是（计数口径：`git status --porcelain` 的 11 项，去掉 `.rks-agent/` 目录一项，
再去掉 TASK-01 已接受的三个 `IItemDomainStore.cs`／`SqliteItemDomainStore.cs`／`SqliteItemDomainStoreTests.cs`）：

| 文件 | numstat | 性质 |
|---|---|---|
| `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` | 32/5 | 行为 + 记载 |
| `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | 133/8 | 行为 + 记载 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs` | 新建 | 只新增判据 |
| `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs` | 14/6 | 纯注释 |
| `Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs` | 9/2 | 纯注释 |
| `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` | 11/3 | 纯注释 |
| `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` | 14/8 | 纯注释 |

交付态散列（评审者自测，与执行者所报一致）：

```
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da *PvfEntryLifecycleContentSource.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *ItemContainers.cs
b196b87a198999d115284ed758725e4232ad07cd71cf3e80d2b2a33ad0abdb07 *GrantedAvatarIsWholeTests.cs
```

### 评审者自跑的检查与实测

1. `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → **退出码 0**，Protocol 521 / Networking 52 / Pvf 60 / Host 6 / Persistence 317 /
   Infrastructure 236（跳过 2）/ Application **1027**，合计 **2219 通过 / 2 跳过 / 0 失败**。
   **首跑即绿，未遇偶发。** 与执行者所报 CK-004 逐项目一致。
2. 变异实测（评审者自做，文件级备份 + `sha256sum`，**未用任何 git 还原类命令**）：见 F-1。
   还原后 `sha256sum -c` 输出 `OK`，`git status --porcelain` 与变异前逐项相同。
3. 逐字读了七个文件的 `git diff`、`GrantedAvatarIsWholeTests.cs` 全文、
   `InventorySide.cs`、`InventoryEntryAssembly.WithAvatarDetail`／`SaveAsync`、
   `SqliteItemDomainStore.FillAsync`／`CarriedAvatarsAsync`、
   `PvfItemPlacementDefinitionSource.GetAsync`、`ResolveItemMetadata`／`ResolveStackableItemKind`、
   `GameInventoryDelivery.WriteItem`、`plan.md` 的 T-002、`AGENTS.md` 第 595–625 行与 676–700 行。

---

### F-1（阻塞）「抽取得到的、大于零的身份值」这一半没有任何判据钉住，而自查称它已被凭据支撑

**文件与成员**：`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`
的 `StoreBackedItemContainer.LandAsync`（`ArrivingAvatar.NewIdentity()` 那一处），
判据侧 `GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`。

**变异实测**（这是证据，不是推演）：

- 备份并记散列：`8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *ItemContainers.cs`
- 变异 C（本轮评审新增，执行者未做）：`LandAsync` 里
  `isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,` 改成 `landing,`
  ——即**把身份抽取整个拿掉**，事实照给。
- `dotnet test Server/tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~GrantedAvatarIsWholeTests"`
  → **退出码 0，两条新判据全绿**。
- 再跑全量 `Server/86JP.Server.slnx` → **退出码 0，2219 通过 / 2 跳过 / 0 失败，全仓一条不红**。
- 由备份还原，`sha256sum -c` → `OK`。

**为什么会这样**：非时装分支的 `StackableItemCore.Create(itemKind, itemId, opened.Count)`
已经把 `Value` 置成堆叠数 `1`。于是——

- `Assert.True(first.Core.Value > 0)`：`1 > 0`，恒真。**这正是契约点名的那类陷阱：
  断言的值恰好等于默认落值。**
- `Assert.Equal(first.Core.Value, second.Core.Value)`：`1 == 1`，往返也不红。
- `InventoryEntryAssembly.WithAvatarDetail` 的 `Core.Value <= 0` 守卫：`1` 过关。
- `SqliteItemDomainStore.FillAsync` 的唯一性守卫只在**第二件**时装撞号时才发作，
  而判据只发放**一件**。

**与契约的冲突**：Acceptance conditions 第 2 条明写要「一个**抽取得到的、大于零的**身份值」；
Required methods 明写「覆盖主张必须由**变异实测**支撑」。执行结果「执行者自查」第 2 条称该条
「达成……凭据是往返判据逐字段断言这六个值加 `Core.Value > 0`，且变异 B 把事实改空后它变红」——
变异 B 只证明了**事实**那一半；**身份那一半的凭据是不成立的**，且执行者未做对应变异。
这也正是仓里「判据名不再描述它证明的东西」那条先例：判据类级 `<remarks>` 写着
「It has to carry **an identity of its own** above zero」，而「its own」恰恰是零覆盖的那一点。

**生产代码本身没错**——`NewIdentity()` 的写法与 `CharacterRosterService.EquipmentCore` 同形，理由也照抄无误。
**缺的是凭据与如实记载。**

**需要的补救（二选一，都是本地的）**：

- (a) 在 `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 里补一条
  能把「抽取的身份」与「堆叠数 1」分开的断言（例如断言两件分别发放的时装身份互异，
  或至少断言身份不等于落地计数），并对该断言做变异实测；**或**
- (b) 明确把「抽取的身份」这一半记为**本任务未覆盖、由 T-003 CK-008 第 1 步承接**，
  并把「执行者自查」第 2 条中已被证伪的凭据句改掉。

按计划 T-003 Steps 1 的确写着「加判据证明两件分别发放的时装拿到互不相同的身份值」，
所以 (b) 是合规的最小补救；但**不能**让当前这句「凭据是……`Core.Value > 0`」留在记录里。

---

### F-2（阻塞，记载类）「仓里另有 3 处载有同一条已被证伪的断言」这个基数是错的

**执行者给的口径**：`grep -rn "kind 8" Server/src Server/tests --include=*.cs` 去 `obj/`、`bin/` 后，
「**语义上**断言『档案与存储对时装不一致』的注释」。

**问题**：所声明的**语义**判准与实际使用的**字面量** grep 不等价。至少还有 **2 处**符合该语义判准、
却因不含字面 `kind 8` 而被 grep 漏掉，分别是（按成员名索引）：

1. `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` —— `IdentityOfAsync` 的 `<remarks>`：
   "Asked of the archive out of the one place that classifies, not read off the kind byte the
   row carries -- **those two disagree**, which is why the classification exists at all."

   现在时断言「那两者不一致」。本轮之后生产档案里**已不存在**两者不一致的模板（见 P-1 的证明），
   与执行者已订正的 `ItemContainers.PutAsync` 那一句（"the kind byte the core carries and the
   archive's answer disagree today"）**是同一句话的同一种失效**。

2. `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` —— `Write` 的 `<remarks>`：
   "there is no second reading of the row it could fall back on: the kind byte a core
   carries and the archive's answer **disagree**, which is the whole reason the classification exists."

   同上。（该文件属 TASK-01 已接受边界，本轮不必改它，但**基数必须算上它**。）

**不计入的一处**，说明为什么：`Server/tests/Dfo.Server.Persistence.Tests/Inventory/ClassificationRefusalTests.cs`
的 `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` 的 `<remarks>`
虽有 "The kind byte and the archive disagreeing"，但下一句立刻说明那是**该判据自己构造出来的**
分歧（"kind 2, an ordinary consumable, on a template the archive calls an avatar"），仍然成立。

**旁证**：`AGENTS.md` 第 610–617 行本身就有一份权威普查，明写「**四处生产注释**点着它——
数的是**点名 8／1 这一对**的生产注释」并列出那四处，还列了「不计入」清单。
执行者订正的正是这四处 + `ItemClassificationTests`（AGENTS.md 说它「点名，但那不是生产注释」）
+ `ItemContainers.PutAsync`（不点名 8／1，故不在那四处内）。**订正的选择本身没问题**；
错的是执行结果里那句自造的「另有 **3 处**」基数——按其自述的语义判准，
上面两处也符合，而 AGENTS.md 的「不计入」清单点的是
`SqliteItemDomainStore.CarriedAvatarsAsync` 与 `InventoryEntryAssembly.WithAvatarDetail`，
**不是** `SqliteItemDomainStore.Write` 与 `InventoryEntryAssembly.IdentityOfAsync`，
所以这两处既没被 AGENTS.md 排除、也没被本轮处理。

**需要的补救**：改「执行结果 → 跨边界」那一段的基数与口径，写清这 2 处也载有同一条已失效的断言、
本轮**未**动它们（一处在 TASK-01 边界、一处在本任务边界外），并说明它们何时由谁订正。
**不要求**本轮去改那两个文件——本任务边界不含它们。

---

### 核过并**通过**的各点

**P-1 `placement.ItemKind == 8` 而非 `WorldClass == Avatar` 的设计判断：不冲突，理由站得住。**

- **区分成立。** 仓规那条原则的原文是「nothing that decides it may be handed **a stored row**」
  （`ItemPlacementContent.ItemWorldClass` 的 `<remarks>`，本轮未改这一句）。
  `LandAsync` 的 `byte itemKind` 形参只有两个供给方：`AddAsync` 传 `placement.ItemKind`
  （来自 `placementRules.PlacementForAsync` → `PvfItemPlacementDefinitionSource` → 档案），
  `RefillAsync` 传 `refill.ItemKind`（来自 `DailyRefillRule`，同样出自
  `PvfEntryLifecycleContentSource.ResolveItemMetadata`）。**两者都是档案答案，都不是存储行的字节。**
  `LandAsync` 手上确实有存储行（`held`），但它只被拿去算堆叠，从未流进 `itemKind`。所以不冲突。
- **今天不会分歧，且这是可证的，不是推测。** 两样都出自同一个 `RefillItemMetadata`：
  `WornPieceMetadata(worldClass)` 用**同一个** `worldClass` 实参同时决定第 2 位（`ItemKind`：
  `Creature => 5`、`Avatar => 8`、`_ => 1`）与第 5 位（`WorldClass`）；
  非装备一路走 `ResolveStackableItemKind`，其返回值穷举为 `{2,3,4,7,9,10,11}`——**永不为 8**——
  且一律配 `ItemWorldClass.Interchangeable`。故对 `PvfItemPlacementDefinitionSource` 能回答的每个模板，
  `ItemKind == 8` 与 `WorldClass == Avatar` **互为充要**。
- **万一将来分歧也不会静默。** `InventoryEntryAssembly.SaveAsync` 已有
  `if (worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8) throw`。
- **「the kind is what every one of those readers uses」属实。** 逐个核过：
  `InventoryEntryAssembly` 第 150、183 行与 `WithAvatarDetail`、`SqliteItemDomainStore.AvatarKind`
  （第 441、580 行两处用）、`GameInventoryDelivery` 第 48、55、110 行、
  `ItemSlotRuleService.KindAvatar`——**全部按 8 判，无一按 `WorldClass` 判**。
- **补充一条执行者没写但支持其选择的事实**：`DailyRefillRule`（`EntryLifecycleContent.cs:9`）
  只有 `ItemKind`，**没有** `WorldClass` 字段。改用 `WorldClass` 会逼着补给通路要么加字段、
  要么多问一次档案——这正是那句「a second question would be a second answer to keep in step」说的事。
  计划 BD-004 的 Inputs 栏写的是 `placement.WorldClass`，但那是提示不是要求，且 `LandAsync` 的
  第二个调用方够不到它。**偏离可辩护。**

**P-2 四处跨边界纯注释改动：逐一核过，全部合规。**

- 四处**都是纯注释**：`git diff -U0` 下，四个文件被删的行**全部**以 `///` 开头
  （`ItemClassificationTests.cs` 的 8 行删除，`grep -c "^-///"` = 8，与去掉 `---` 后的 `grep -c "^-"` = 8 相等）。
- 四处**都逐字引出了原文**并说明哪一半不再成立：`ItemPlacementContent` 引了两整句、
  `IItemPlacementRuleService` 引了整段、`ItemPlacementRuleService` 引了那个从句、
  `ItemClassificationTests` 引了两句，四处均带 "Amended; what … said before is quoted next" 与
  "corrected rather than deleted because …"。
- **没有一处悄悄改了要求。** 四处的**规范句**逐字保留：
  `ItemPlacementContent`「nothing that decides it may be handed a stored row」原样；
  `IItemPlacementRuleService`「It takes a template identifier and nothing else」原样；
  `ItemPlacementRuleService`「A stored kind byte is not an input and cannot become one without
  changing the signature, which is the whole guard」原样。被替换的只有**理由从句**
  （由「两者今天不一致」换成「调用方仍可递任意 core」），而新理由属实（`Write`／`PutAsync`
  的签名确实接受任意 core）。
- `ItemClassificationTests.cs`：`EquipmentKindByte = 1`、`CreatureKindByte = 5`、
  `ConsumableKindByte = 2` 三个常量与两条用例的全部 `Assert`（第 83、84、85、88、136、137、138、139 行）
  **一字未动**——`git diff` 只有一个 hunk，落在类级 `<remarks>` 内。

**P-3 既有断言零改写：属实，自核确认。**

`Server/tests/` 下本轮唯一被改的既有文件是 `ItemClassificationTests.cs`，纯注释（见 P-2）。
`SqliteItemDomainStoreTests.cs` 是 TASK-01 的，且 numstat `37/0` 为纯新增，也无改写。
特别核了「种类由 1 变 8 后本该变红却被调期望值」的可能：
`PvfEntryLifecycleContentSourceTests` **完全未被改动**（不在 `git status` 里），
其 `DailyRefill_...WithStackLimits` 断言的 `ItemKind` 集合 `{1,2,3,4,7,9,10,11}` 原样保留且仍绿
——即真实档案每日补给表里确实没有时装。**没有任何期望值被迁就实现。**

**P-4 往返判据确实跨过了存盘那一侧。**

`side.Leases` 直通 `InventoryEntryAssembly`（`InventorySide.Leases => this.Assembly`），
`side.CharacterEntries` 同理，四步各包一层 `InUseCaseAsync`（`Begin` + `CommitAsync`），
是四个独立 use case。存盘那一侧的三条硬要求确实被跨过：
`SaveAsync` 第 149–156 行对 `ItemKind == 8` 的行检 `Core.Value <= 0 || AvatarDetail is null ||
JewelSocket.Length != 30`；装载侧 `WithAvatarDetail` 的三条同样跨过。
六列的值断言**不是**恒真：`ExpireDate` 断的是 `int.MaxValue`（不是任何默认落值，改成 0 即红），
`JewelSocket` 断 30 个零字节（长度不对在装载侧就炸）。
执行者的变异 B（事实改空）我复核了其逻辑成立。
**唯一的恒真陷阱是身份那一条，已单列为 F-1。**

**P-5 计数与归因：自跑复现，一致。**

评审者自跑 CK-004 得 2219 / 2 跳过 / 0 失败，逐项目与执行者所报**完全相同**；
Application.Tests 1027，其余六项目与其所报基线相同。`+2` 归因可自验：
本轮 `Server/tests/` 下只新建了 `GrantedAvatarIsWholeTests.cs` 一个文件，内含且仅含
两个 `[SkippableFact]`；另一被改的既有测试文件为纯注释，不增用例。

**P-6 偶发失败的判别成立。**

对照 `AGENTS.md` 第 676–695 行的三条判准：
异常类型 `ObjectDisposedException … 'SQLitePCL.sqlite3'` ✓ 命中第 1 条；
栈根 `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`
✓ **在已记录的三条栈根表内（第一行）**；复跑即绿 ✓。
执行者明写「不按用例名」，而 AGENTS.md 恰恰规定「**用例名不是判别依据**——第一条栈根已经在三个
不同用例名上出现过」。`OneDatabaseOneParticipantTests.TwoKindsOfStateInOneDatabaseTakePartAsOneParticipant`
是该栈根的**第四个**用例名，按规矩不构成反例。**判别正确。**
（可选的记载改善，非本任务义务：把这个新用例名补进那张表的「见于」栏；AGENTS.md 属 BD-007，不在本任务边界。）

**P-7 占位裁定写清楚了。**

`ArrivingAvatar.NeverExpires` 的 `<remarks>` 以 "**A placeholder ruling, and it is named as one.**" 起头，
四件都写到了：① 本仓语义——非空的 `avatar_expire_date` 表示「这一行是时装」而非「会过期」；
② 线上那一格写的是 `max(0, ExpireDate - now)` 的剩余秒数；
③ 上界是「当前可辩护的『不过期』表达」；
④ 实机会检验什么——"Nobody has yet watched a client draw an avatar whose remaining seconds are two
billion, and a client that renders that as a date, or refuses it, would be saying this bound is the
wrong placeholder. **Live play is what settles it.**"
第 ② 条我核过属实：`GameInventoryDelivery.WriteItem` 第 55–56 行确为
`Math.Max(0, avatar!.ExpireDate - checked((int)preparedAt.ToUnixTimeSeconds()))`。
**没有把推测写成结论。**

**P-8 阻塞条件与被否替代：均未触发／均未采用（自核）。**

`IItemContainer.AddAsync` 与 `PutAsync` 的公开签名逐字未动（`PutAsync` 改为转调私有
`WriteOneAsync`，签名不变）；`session.Write` 全类只有 `WriteOneAsync` 一个调用点，未建第二条发放通路；
结算未把 `item_uid` 回填进 core；写入契约未加「预支身份」的动词；
`Core.Value > 0` 三处强制一字未放宽。

---

### 需要的补救（汇总）

| ID | 补救 | 范围 |
|---|---|---|
| F-1 | 要么补一条能区分「抽取的身份」与「堆叠数 1」的断言并做变异实测；要么明确记为未覆盖、由 T-003 CK-008 承接。**无论选哪条，都必须改掉「执行者自查」第 2 条里那句已被证伪的凭据。** | 判据文件 + Task 文件记录 |
| F-2 | 改「跨边界」那段的基数与口径，写明另有 2 处（`InventoryEntryAssembly.IdentityOfAsync`、`SqliteItemDomainStore.Write`）载有同一条已失效的断言、本轮未动及原因。 | Task 文件记录 |

两条都不需要改本轮的生产代码。F-1 的 (b) 路线与 F-2 都只改 Task 文件记录。

### 本轮评审的边界声明

评审者未改动任何生产代码或判据。变异 C 全程文件级备份 + `sha256sum` 还原并核散列（`OK`），
**未使用任何 git 还原类命令**，未提交、未建检查点，未触碰主检出与 `.worktrees/client-debug-lab`。


## Controller Disposition D-02 — `task-rework`，只补 QR-TASK02-R1 证明了的两条

**判定**：`task-rework`。TASK-02 保持 `in_progress`。
两条发现都**有直接证据**，都是本任务边界内可局部修正的，approved Spec 与计划的中心做法未被推翻。
**不得借这次返工扩大范围。**

### F-1 是真的，而且它正是本仓反复吃亏的那种

评审者做了执行者没做的变异：把身份抽取整个拿掉，两条新判据**全绿**，全量 2219/2/0 **一条不红**。
根因是非时装分支的 `StackableItemCore.Create(..., opened.Count)` 已把 `Value` 置为 `1`，于是
`Value > 0` 恒真、两件比较是 `1 == 1`、装载侧守卫照过、撞号守卫只在第二件时发作而判据只发一件。

**要补的**：
1. 一条能把「**抽取得到的身份**」与「**堆叠数量**」区分开的断言，
   并**用变异实测证明它在抽取被拿掉时会红**。
2. **删掉那句已被证伪的自证**（自查第 2 条称凭据是「`Core.Value > 0` 加变异 B」）。
   按本仓上一轮的记载，**一句写在自证位置上、自己却没被核对过的话，比没有自证更坏**。
   订正时要写明它此前说的是什么、错在哪。

**不接受**「记为由 T-003 CK-008 承接」这条出路：契约验收第 2 条是**本任务**的，
把它推给下游等于让本任务在一条无覆盖的主张上通过。

### F-2 是口径与基数不符

执行者声明的是**语义**判准（「载有『档案给时装 1、存储记 8』这条被证伪的记载」），
实际用的是字面 `grep "kind 8"`，因而漏掉两处同样以现在时断言二者不一致的记载，分别是：
`InventoryEntryAssembly.IdentityOfAsync` 的 `<remarks>`（"those two disagree"）与
`SqliteItemDomainStore.Write` 的 `<remarks>`（"...disagree, which is the whole reason..."）。

**要补的**：
1. 按**声明的语义判准**重扫一遍，给出订正后的基数与**计数口径**，并列出构成它的每一项。
2. 那两处记载**一并订正**。它们在本任务的声明边界外，但本仓规矩是
   「**改已被证伪的记载可以越任务边界，但要补记修正案**」——
   本轮已按此处理过四处，同一条规矩对这两处同样适用。
   留着两处现在时的假记载，比越界订正更坏。

### 不返工的部分

评审逐条核过并通过的七项——`ItemKind == 8` 的设计判断、四处纯注释改动的合规性、
既有断言零改写、往返判据真跨存盘侧、计数自跑复现、偶发失败判别、占位裁定的写法——
**全部保留，不要重做**。

## 执行结果（第二轮）— 只补 D-02 点名的 F-1 与 F-2

**范围声明**：本轮只做 D-02 要求的两条补救，未扩大范围。
评审 QR-TASK02-R1 逐条核过并通过的七项（P-1..P-8）**一律保留，未重做、未改动**。
起点是上一轮留在工作区里的改动，起点散列与评审自测所报**逐字节相同**：

```
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
b196b87a198999d115284ed758725e4232ad07cd71cf3e80d2b2a33ad0abdb07 *Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da *Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
```

**本轮改了 3 个文件**，分别是（计数口径：本轮相对上一轮交付态发生内容变化的文件，
按 `sha256sum` 前后不等判定；`.rks-agent/` 的 Task 记录不计入）：

| 文件 | 本轮改了什么 | 性质 |
|---|---|---|
| `Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs` | F-1 的判据补强：既有往返判据加 1 条断言，新增 1 条用例 | 只新增判据 |
| `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | F-2 的记载订正：`IdentityOfAsync` 的 `<remarks>` | 纯注释 |
| `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` | F-2 的记载订正：`Write` 的 `<remarks>` | 纯注释 |

**本轮一行生产逻辑未改**：`ItemContainers.cs` 与 `PvfEntryLifecycleContentSource.cs` 的散列
在本轮结束时与起点**完全相同**（见文末交付态散列），两处 F-2 订正的 diff 每一行都以 `///` 开头。

---

### F-1 — 补了什么断言

评审证明的陷阱是：非时装分支的 `StackableItemCore.Create(itemKind, itemId, opened.Count)`
已把 `Value` 置成堆叠数 `1`，所以「大于零」这句话**恒真**，把身份抽取整个拿掉也不会有任何判据变红。
要区分「抽取得到的身份」与「堆叠数量」，靠一件时装、靠一个下界，都做不到。本轮补两处：

**（一）既有往返判据 `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 加一条断言**

在原有的 `Assert.True(first.Core.Value > 0);` 之后加：

```csharp
// Its own, and not the count it landed as. Above zero cannot say that by itself: the
// field the identity goes in is the same field a pile counts itself in, and a slot
// opened for one of anything already carries a one there. So a delivery that drew no
// identity at all would leave "above zero" green, and this is the assertion that would
// not be.
Assert.NotEqual(landed.Count, first.Core.Value);
```

比的是**发放通路自己报回来的落地计数**（`ItemPlacementOutcome.PutInEmptySlot.Count`）
与装载读回来的 `Core.Value`。抽取被拿掉时两者都是 `1`，这一条**确定性地**变红——
不依赖任何随机取值，也不依赖第二件时装。

**（二）新增一条用例 `Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`**

一条断言只能说「不是计数」，说不了「是抽取的」。两件时装可以：
**两个落地计数是同一个数，两次抽取不是。** 该用例连发两件时装，装载读回两行，断言：

```csharp
// Drawn: neither identity is the count its own grant put down.
Assert.NotEqual(earlier.Count, one.Core.Value);
Assert.NotEqual(later.Count, other.Core.Value);

// Its own: the two avatars are not each other.
Assert.NotEqual(one.Core.Value, other.Core.Value);
Assert.NotEqual(one.Identity, other.Identity);

// And still above zero, which is what the load, the save and both projections demand.
Assert.True(one.Core.Value > 0);
Assert.True(other.Core.Value > 0);
```

比的仍然是**装载读回来的**两行，不是发放通路自称写了什么。

**未采用 D-02 排除的那条出路**：没有把这一半推给 T-003 CK-008 承接，
契约验收第 2 条的「抽取得到的」这一半在**本任务内**由上面两处判据钉住。

---

### F-1 — 变异实测：变异前后的散列与红/绿结果

全程**文件级备份 + `sha256sum` 还原并核散列**，**未使用任何 git 还原类命令**
（`git checkout --` / `git restore` / `git stash` 一次都没有出现）。
备份落在会话 scratchpad：`.../scratchpad/ItemContainers.cs.green`。

**第 1 步 — 绿态，备份并记散列**

```
$ sha256sum Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs \
            Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs \
            <scratchpad>/ItemContainers.cs.green
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *ItemContainers.cs
e99bc395ffd28be46df4154393298667d16f0fba3f864ee5b0c34ae21edb7fb4 *GrantedAvatarIsWholeTests.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *<scratchpad>/ItemContainers.cs.green
```

绿态下三条判据全绿，**退出码 0**：

```
已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3 - Dfo.Server.Application.Tests.dll (net10.0)
```

**第 2 步 — 变异 C（与评审所做的同一处、同一改法）**

`StoreBackedItemContainer.LandAsync` 的 `IntoEmptySlot` 分支里
`isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,` 改成 `landing,`
——身份抽取整个拿掉，事实照给。变异态：

```
                var landing = StackableItemCore.Create(itemKind, itemId, opened.Count);
                var isAvatar = itemKind == ArrivingAvatar.ItemKind;
                await this.WriteOneAsync(
                    opened.SlotIndex,
                    landing,
                    isAvatar ? ArrivingAvatar.Facts() : null,
```

变异态散列：

```
cd388e911834331b48da322e29085511307647ac7be3e9e9725c412d381ee46b *ItemContainers.cs
```

**第 3 步 — 变异下重跑，退出码 1，两条都红，且红在新加的东西上**

```
失败!  - 失败:     2，通过:     1，已跳过:     0，总计:     3 - Dfo.Server.Application.Tests.dll (net10.0)
```

红之一，**正是本轮新加的那句断言**（往返判据，`GrantedAvatarIsWholeTests.cs:146`）：

```
失败 GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts
  错误消息:
   Assert.NotEqual() Failure: Values are equal
   Expected: Not 1
   Actual:       1
  堆栈跟踪:
     at ...<A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts>b__5_0>d.MoveNext()
        in ...\GrantedAvatarIsWholeTests.cs:line 146
```

**这就是评审所指的那句「恒真」的反面**：同一条判据里 `Assert.True(first.Core.Value > 0)`
在变异下仍然是绿的（`1 > 0`），红的是新加的 `Assert.NotEqual(landed.Count, first.Core.Value)`。
上一轮判据的全部内容在这个变异下都通过了，本轮加的这一句没有。

红之二，本轮新增的用例（发第二件时装时，撞号守卫发作，红在**第二次发放**上）：

```
失败 GrantedAvatarIsWholeTests.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts
  错误消息:
   System.InvalidOperationException : The world took the item and named no identity.
  堆栈跟踪:
     at SqliteItemDomainStoreSession.FillAsync(...) SqliteItemDomainStore.cs:line 609
     at SqliteItemDomainStoreSession.FlushAsync(...) SqliteItemDomainStore.cs:line 249
     at StoreBackedItemContainer.LandAsync(...) ItemContainers.cs:line 493
     at StoreBackedItemContainer.AddAsync(...) ItemContainers.cs:line 421
     at GrantedAvatarIsWholeTests.GrantAsync(...) GrantedAvatarIsWholeTests.cs:line 250
     at ...<Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts>b__6_0>d.MoveNext()
        in ...\GrantedAvatarIsWholeTests.cs:line 227
```

**如实记载**：这一条的红是 `FillAsync` 的唯一性守卫先发作，抛在第二次 `AddAsync` 上，
所以该用例里那四句 `Assert.NotEqual` **没有走到**。它仍然是本轮新增的用例带来的红——
上一轮不存在这条用例，评审的变异 C 因此全绿——但它不是「断言比较失败」式的红。
**「新加的断言本身变红」这一条，由红之一承担，那是纯断言失败，无异常、确定性。**

**第 4 步 — 由备份还原并核散列**

```
$ cp <scratchpad>/ItemContainers.cs.green Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
$ echo "8e7f085d...240da *Server/src/.../ItemContainers.cs" | sha256sum -c -
Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs: OK
```

**第 5 步 — 还原后恢复绿，退出码 0**

```
已通过! - 失败:     0，通过:     3，已跳过:     0，总计:     3 - Dfo.Server.Application.Tests.dll (net10.0)
```

---

### F-2 — 重扫的判准原文、订正后的基数、构成它的每一项

**判准原文（本轮按声明的语义判准写死，不再用字面量 grep 代替）**：

> 一条注释，若**以现在时**断言「档案对某模板给出的种类」与「存储判定时装所用的种类」
> 二者**不一致**（即「档案给时装 1、存储记 8」这条已被本轮证伪的记载），即计入。
> 生产注释与判据注释**都算**。

**计数口径**：全仓 `Server/src` 与 `Server/tests`（`--include=*.cs`，排除 `obj/`、`bin/`），
用三组语义近义词扫描而不是单个字面量：
`disagree|do not agree|does not agree|out of step|at odds|contradict|inconsistent`、
`kind 8|kind 1|the 8|an 8|equipment index`、`kind byte`；
再对每一条命中**逐条读上下文**判定它是否落在上面那条判准里。**按成员名索引，不按行号。**
上一轮声明的是同一条语义判准，实际只跑了 `grep -rn "kind 8"`，两者不等价——这正是 F-2。

**订正后的基数：8 处，分别是**（前 6 处上一轮已订正，后 2 处本轮订正）：

| # | 文件 | 成员 | 何时订正 |
|---|---|---|---|
| 1 | `Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` | `WornPieceMetadata` 的 `<remarks>` | 第一轮 |
| 2 | `Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs` | `StoreBackedItemContainer.PutAsync` 的 `<remarks>` | 第一轮 |
| 3 | `Server/src/Dfo.Server.Abstractions/Content/ItemPlacementContent.cs` | `ItemWorldClass` 的 `<remarks>` | 第一轮 |
| 4 | `Server/src/Dfo.Server.Abstractions/Inventory/IItemPlacementRuleService.cs` | `ClassifyAsync` 的 `<remarks>` | 第一轮 |
| 5 | `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` | `ClassifyAsync` 的 `<remarks>` | 第一轮 |
| 6 | `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` | 类级 `<remarks>` | 第一轮 |
| 7 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | `IdentityOfAsync` 的 `<remarks>` | **本轮** |
| 8 | `Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs` | `Write` 的 `<remarks>` | **本轮** |

上一轮那句「除契约点名的那一处外，仓里另有 **3 处**」按此判准是**错的**：
它数的是**跨边界的生产注释**（第 3、4、5 项），既没有把边界内的第 2 项与判据侧的第 6 项算进同一个基数，
也漏掉了第 7、8 项。**订正后的说法是：符合该判准的记载共 8 处，全部已订正。**
重扫**没有**发现第九、第十处。

**逐条扫过但不计入的，及其理由**（写出来是为了让基数可被证伪）：

- `Server/tests/Dfo.Server.Persistence.Tests/Inventory/ClassificationRefusalTests.cs` 的
  `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` 的 `<remarks>` —— 有
  "The kind byte and the archive disagreeing"，但下一句写明那是**该判据自己构造的**分歧
  （"kind 2, an ordinary consumable, on a template the archive calls an avatar"）。仍然成立。
- `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` 的
  `SaveAsync` 里那条行内注释（"the silent disagreement this classification exists to make audible"）
  —— 说的是**某个调用方可能**递来的行与档案不符，不是断言两个来源今天不一致。仍然成立。
- `Server/src/Dfo.Server.Abstractions/Inventory/IItemDomainStore.cs` 的 `StoredItem` 的 `<remarks>`
  与 `Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs` 的 `ContainerSlotItem` 的
  `<remarks>` —— "the reading this classification exists to replace"，讲的是设计理由
  （行的字节由调用方填），没有断言两个来源的取值不同。仍然成立。
- `Server/tests/Dfo.Server.Application.Tests/Inventory/ItemClassificationTests.cs` 的
  `A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries` 的 `<remarks>`
  —— 同样是判据自造的分歧。仍然成立。
- `Server/src/Dfo.Server.Application/Characters/CharacterCreationRuleService.cs` 的 `EquipmentKind`
  与 `Server/src/Dfo.Server.Application/Inventory/ItemArrivalRuleService.cs` 的 `EquipmentKind`
  —— 两处 `<summary>` 都写「档案的装备索引让**每一件**穿戴物是 1」。这是一句**泛化**，
  不是「两个来源不一致」的断言，因此不在判准内；但它的泛化面确实在收窄
  （契约兽的 5 由更早那个成果造成，时装的 8 由本成果造成）。
  **本轮未动它们**，两点理由并列记下：其一，它不载有本判准所指的那条记载；
  其二，实测行为不受影响——`ItemArrivalRuleService.BandsFor` 的判断是
  `placement.ItemKind == EquipmentKind || !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)`，
  时装的 8 走第二个析取支进同一个分支，全量判据也全绿。
  **这一条留作后续成果的记载债，在此点名而不是留在暗处。**

**越界声明**：第 7、8 项都在 TASK-02 的 Owned boundary（BD-003/BD-004/BD-008）之外——
第 7 项属 Application 的 entry assembly，第 8 项属 TASK-01 已接受的边界。
按仓规（本 Task 契约 Constraints 末条）**改已被证伪的记载可以越任务边界，但要补记修正案**，
本轮两处都逐字引出原文并说明哪一半不再成立，**不是抹去**。D-02 明文授权了这一步。
两处**都是纯注释**：diff 里被改、被加的每一行都以 `///` 开头，无一行可执行代码。

---

### F-2 — 每处订正的前后对照

**（七）`InventoryEntryAssembly.IdentityOfAsync` 的 `<remarks>`**

改前（逐字）：

> Asked of the archive out of the one place that classifies, not read off the kind byte the
> row carries -- **those two disagree**, which is why the classification exists at all.

改后（正文换掉失效的理由从句，另起一段 amended 全文引用原句）：

> Asked of the archive out of the one place that classifies, not read off the kind byte the
> row carries -- **a row is written by callers free to put anything in that byte**, which is why
> the classification exists at all.
>
> **Amended; what the clause above said before is quoted next.** It said the archive is
> asked "not read off the kind byte the row carries -- *those two disagree*, which is why
> the classification exists at all". The half that has stopped being true is "those two
> disagree": it named the two standing pairs where the archive's equipment index handed out
> the constant 1 while storage recorded a 5 for a contract creature and an 8 for an avatar,
> and both are answered by the archive now -- the creature's by the outcome that repaired it,
> the avatar's by the one that made a granted avatar whole. No template is left that the two
> sources answer differently. The half that still stands is the conclusion: the archive is
> still the only thing asked here, because a write is handed a core by a caller and nothing
> checks that byte before it arrives. It is corrected rather than deleted because a reader who
> found only the conclusion would have no way to tell whether asking the archive was ever load
> bearing.

**哪一半不再成立**："those two disagree"。**哪一半仍成立**：结论——这里仍然只问档案，
因为核心的那个字节是调用方填的，到达之前没有任何东西核过它。
`IdentityOfAsync` 的签名与函数体**一字未动**。

**（八）`SqliteItemDomainStore.Write` 的 `<remarks>`**

改前（逐字）：

> The class the write carries is required rather than optional. Settlement decides on it
> whether the thing being written keeps an identity of its own or is a pile counted in its
> slot, and there is no second reading of the row it could fall back on: **the kind byte a core
> carries and the archive's answer disagree, which is the whole reason the classification
> exists.** A write that did not say would therefore have to be guessed at, and a guess of
> "pile" silently throws away the identity of everything it is wrong about.

改后：

> ... and there is no second reading of the row it could fall back on: **the kind byte a core
> carries is put there by the caller and the archive's answer is not**, which is the whole
> reason the classification exists. A write that did not say would therefore have to be
> guessed at, and a guess of "pile" silently throws away the identity of everything it is
> wrong about.
>
> **Amended; what the sentence above said before is quoted next.** It said there is no
> second reading to fall back on because "the kind byte a core carries and the archive's
> answer *disagree*, which is the whole reason the classification exists". The
> disagreement it named was two standing pairs -- the archive's equipment index handing out
> the constant 1 while storage recorded a 5 for a contract creature and an 8 for an avatar --
> and neither pair stands any more: the creature's was answered by the outcome that repaired
> it, the avatar's by the one that made a granted avatar whole, so no template is left that
> the two sources answer differently. What has not changed is why this method still refuses a
> write that says nothing: the byte is whatever a caller put in it, this class has no archive
> to ask, and settlement reads the class off nothing else. It is corrected rather than deleted
> because a reader who found only the refusal would not know it was ever answering a real
> divergence rather than a hypothetical one.

**哪一半不再成立**："disagree"。**哪一半仍成立**：`Write` 仍然拒绝一个不声明 `WorldClass`
的写入。`Write` 的签名、`ArgumentException` 的守卫与消息**一字未动**。

---

### 那句被证伪的自证的订正前后对照

**位置**：上一轮「### 执行者自查（逐条对照契约验收条件）」的第 2 条。
（按 D-02「不要重写或删改任何既有小节」的要求，**上一轮那一段原样保留**，订正记在这里。）

**它此前说的是**（逐字）：

> 2. **发放通路对时装造出时装形状：抽取的、大于零的身份值 + 六列默认事实。** 达成。
>    `ArrivingAvatar.NewIdentity()` 用 `Random.Shared.Next(1, int.MaxValue)`（抽取，不排序，下界 1）；
>    `ArrivingAvatar.Facts()` 给出珠宝槽 30 个零字节、两色 0、清除标识 0、删除日期 0、到期日 `int.MaxValue`。
>    **凭据是往返判据逐字段断言这六个值加 `Core.Value > 0`，且变异 B 把事实改空后它变红。**

**错在哪**：错在最后那句凭据，而且是**两处**错：

1. **`Core.Value > 0` 根本不是凭据。** 非时装分支的 `StackableItemCore.Create(..., opened.Count)`
   已把 `Value` 置成堆叠数 `1`，所以对一件时装而言 `1 > 0` **恒真**。
   它在身份抽取被整个拿掉时仍然是绿的——这不是推演，是本轮第 3 步实测出来的
   （变异下该断言通过，红的是本轮新加的那句）。
2. **变异 B 只覆盖了「事实」那一半。** 变异 B 改的是 `isAvatar ? ArrivingAvatar.Facts() : null,`，
   身份照抽，所以它的红只能归因于事实缺失（异常原文即 `Avatar detail ... is unavailable.`）。
   把它写成同时支撑「身份」那一半，是**把一个变异的结论安到它没有触及的那一半上**。
   契约验收第 2 条要的「抽取得到的」这半个主张，在上一轮**零覆盖**。

**订正后的说法**（本轮自查第 2 条即以此为准）：
「抽取得到的」这一半的凭据是**本轮**新加的两处断言与**本轮**做的变异 C，
见上文「F-1 — 变异实测」的第 1–5 步；`Core.Value > 0` 只是三处守卫的下界复述，**不作为凭据**。

**为什么这一条要单独订正而不是改掉了事**：上一轮的教训原话是
**「一句写在自证位置上、自己却没被核对过的话，比没有自证更坏」**。
把它连同它错在哪一起留在记录里，才让下一个读者能看出这类错法长什么样。

---

### 全量判据的实际输出与退出码

**CK-004** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
→ **退出码 0，首跑即绿，未遇偶发**

```
已通过! - 失败:     0，通过:   521，已跳过:     0，总计:   521 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   317，已跳过:     0，总计:   317 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:  1028，已跳过:     0，总计:  1028 - Dfo.Server.Application.Tests.dll (net10.0)
```

合计 **2220 通过 / 2 跳过 / 0 失败**（两条跳过仍是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`）。

**CK-006** `PVF_ARCHIVE_PATH=... dotnet test Server/tests/Dfo.Server.Application.Tests --nologo`
→ **退出码 0，首跑即绿**

```
已通过! - 失败:     0，通过:  1028，已跳过:     0，总计:  1028，持续时间: 10 s - Dfo.Server.Application.Tests.dll (net10.0)
```

`TreatWarningsAsErrors` 开启，两次全量与三次过滤跑的构建**均无警告无错误**（无 CS9113）。

### 用例数变化的逐项目归因

| 项目 | 上一轮交付（契约基线） | 本轮实测 | 增量 | 归因 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 521 | 0 | — |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | — |
| Dfo.Pvf.Tests | 60 | 60 | 0 | — |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | — |
| Dfo.Server.Persistence.Tests | 317 | 317 | 0 | — |
| Dfo.Server.Infrastructure.Tests | 236（+2 跳过） | 236（+2 跳过） | 0 | — |
| Dfo.Server.Application.Tests | 1027 | 1028 | **+1** | 全部来自 F-1 新增的 `GrantedAvatarIsWholeTests.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`（1 条）。往返判据加的那句 `Assert.NotEqual` 在**既有用例内**，不增用例数 |
| **合计** | **2219 / 2 跳过** | **2220 / 2 跳过** | **+1** | 逐条如上 |

本轮 `Server/tests/` 下唯一被改的文件是 `GrantedAvatarIsWholeTests.cs`（本轮之前它就是新建的），
另两个被改的是生产文件且改动全为 `///` 注释，**不增不减任何用例**。

### 既有断言的期望值是否被改（本轮）

**无。一条也没有。** 本轮对 `GrantedAvatarIsWholeTests.cs` 只做了两件事：
在既有用例里**追加**一句 `Assert.NotEqual`，以及**追加**一条新用例。
既有的 `Assert.True(first.Core.Value > 0)`、六列事实的每一句 `Assert.Equal`、
往返的每一句比较，逐字保留、期望值未动。其余文件本轮只改注释。

---

### 执行者自查（第二轮，逐条对照契约验收条件）

**每条的凭据都是本轮真的跑过的实测**，不沿用上一轮那句已被证伪的说法。

1. **档案元数据对判定为时装的模板给出时装那个种类。** 达成。
   凭据：本轮 CK-004／CK-006 首跑即绿，其中
   `The_archive_and_storage_answer_the_same_kind_for_one_avatar` 对真实档案模板 `101550507`
   实测 `PlacementForAsync(...).ItemKind == 8`（判据里的 8 写成字面量，不借发放通路的常量）。
   该条的变异凭据是上一轮的变异 A，本轮**未改动**该判据、也未改动 `WornPieceMetadata`
   （`PvfEntryLifecycleContentSource.cs` 散列本轮前后相同：`2c388060…`），
   评审 P-1 亦已独立核过并通过，故不重做。

2. **发放通路对时装造出时装形状：一个抽取得到的、大于零的身份值，加六列默认事实。** 达成，
   **两半各有本轮的凭据**：
   - 「**抽取得到的**」这一半 —— 凭据是**本轮**新加的
     `Assert.NotEqual(landed.Count, first.Core.Value)` 与新增用例
     `Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`，
     并由**本轮**的变异 C（把 `isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,`
     改成 `landing,`）实测证明：变异下前者以
     `Assert.NotEqual() Failure: Values are equal / Expected: Not 1 / Actual: 1` **纯断言变红**，
     后者也红；还原后 `sha256sum -c` 为 `OK` 且三条恢复全绿。散列与输出见上文 F-1 五步。
   - 「**大于零**」这一半 —— `Assert.True(one.Core.Value > 0)` 三处。
     **本轮明确记下它不是凭据而是复述**：它在变异 C 下仍然是绿的（`1 > 0`），
     单靠它不能支撑任何覆盖主张。
   - 「**六列默认事实**」这一半 —— 凭据是往返判据逐字段的六个 `Assert.Equal`，
     加上一轮的变异 B（把 `isAvatar ? ArrivingAvatar.Facts() : null,` 改成 `null,` → 判据变红，
     异常原文 `Avatar detail ... is unavailable.`）。该变异**只**支撑这一半，本轮不再把它算到身份那一半上。

3. **到期日那个取值在代码里写明它是占位裁定。** 达成。`ArrivingAvatar.NeverExpires` 的
   `<remarks>` 本轮**一字未改**（`ItemContainers.cs` 散列前后相同：`8e7f085d…`），
   评审 P-7 已逐条核过四件都写到了。凭据是散列相等 + 评审的独立核对，不是重新宣称。

4. **非时装模板的落地路径一字不动。** 达成。凭据：`ItemContainers.cs` 本轮散列与上一轮交付态
   **完全相同**，本轮对生产逻辑零改动；且本轮 CK-004 全量 **2220 通过 / 0 失败**，
   既有的发放、补给、移动、拆分、合并、消耗、卡片直放判据无一变红，
   用例数增量精确为 +1 且已逐项目归因到新增的那一条时装用例。

5. **「发放 → 装载 → 存盘 → 再装载」全程无拒绝无回滚，物品仍在，身份与事实与入库时相同。** 达成。
   凭据：本轮 CK-006 下
   `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 绿，
   且本轮新加的 `Assert.NotEqual(landed.Count, first.Core.Value)` 是在这条判据**跑通四步之后**
   才比较的值——即这四步在绿态下确实各自走通。评审 P-4 已独立核过四步是四个 use case、
   跨过装载侧三条校验与存盘侧三条硬要求。

6. **`PvfEntryLifecycleContentSource` 里那句已不再成立的记载已更正，按仓规补记修正案。** 达成，
   且本轮把口径与基数一并订正：符合「以现在时断言档案与存储对时装不一致」这条判准的记载
   **共 8 处，已全部订正**，逐项与计数口径见上文「F-2 — 重扫的判准原文、订正后的基数」，
   不计入的六处也逐条写明了理由。第 7、8 项本轮订正，越界依仓规并已补记修正案（前后对照见上）。

**阻塞条件核对**：未触发。本轮未动 `IItemContainer.AddAsync` 或 `PutAsync` 的签名（生产代码零改动），
未新建第二条发放通路。

**被否掉的三条替代**：均未采用，且本轮未触及相关代码。
「身份必须大于零」那三处强制一字未放宽——本轮的变异 C 之所以能让第二条用例红，
正是靠 `FillAsync` 那条唯一性守卫仍然站着。

**硬约束核对**：`TreatWarningsAsErrors` 开启，本轮全部构建 0 警告 0 错误，无 CS9113；
未新增协议帧、未改任何帧的字节布局；`CommandHandlerArchitectureTests` 与
`NotificationHandlerArchitectureTests` 随 CK-004 全绿且未被改动；
未给 `PvfEquipmentSlots.ByName` 补任何键；未裁定契约兽槽位 24/25；
**未建 Git 检查点、未提交**；未触碰主检出与 `.worktrees/client-debug-lab`；
变异验证全程文件级备份 + `sha256sum`，**未使用任何 git 还原类命令**；
既有断言的期望值本轮零改写；基数一律写成「N 处，分别是：…」并给了计数口径，按成员名索引。

### 本轮交付态散列

```
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da *Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
e99bc395ffd28be46df4154393298667d16f0fba3f864ee5b0c34ae21edb7fb4 *Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs
7e9f4678af319acc514afd0d2ee179bc3f7325e6aaf16ac63fa62b6baf9b5349 *Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs
ba8bc2e0929672e2c63372affb6b762cb0511c2398059758b5857725a4951678 *Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs
```

前两行与上一轮交付态相同（`ItemContainers.cs`、`PvfEntryLifecycleContentSource.cs` 本轮未改）；
后三行为本轮改动的三个文件，其上一轮散列分别是
`b196b87a…`（`GrantedAvatarIsWholeTests.cs`）、`4a04f19f…`（`InventoryEntryAssembly.cs`）、
`f820c258…`（`SqliteItemDomainStore.cs`）。

工作区 `git status --porcelain`（未提交，无检查点）：10 项 `M` + 2 项 `??`
（`??` 为 `.rks-agent/tasks/server-agrees-what-an-avatar-is/` 与新建的 `GrantedAvatarIsWholeTests.cs`）。
相对评审所见多出的一项 `M` 是本轮 F-2 订正的 `InventoryEntryAssembly.cs`，纯注释。

## Independent Quick Review — QR-TASK02-R2

- 评审者：独立快审（fresh context，非执行者，与 QR-TASK02-R1 亦非同一 context）
- 评审的契约版本：Contract revision 1（无修正案）+ Controller Disposition D-02
- **判定：`passed`**
- **判定范围**：只判 F-1 与 F-2 的补救是否成立，以及返工本身有没有带进新问题。
  QR-TASK02-R1 逐条核过并通过的七项（`ItemKind == 8` 的设计判断、四处纯注释改动的合规性、
  既有断言零改写、往返判据真跨存盘侧、计数复现、偶发失败判别、占位裁定的写法）
  **本轮未重做**，按 D-02 保留。

### 被评审的 owned diff 身份

工作区 `.worktrees/server-agrees-what-an-avatar-is`，分支 `server-agrees-what-an-avatar-is`，
HEAD `565d33fe`，**未提交、无检查点**。`git status --porcelain` 实测 **10 项 `M` + 2 项 `??`**，
与执行者所报逐项相同。本轮交付态散列（评审者自测）：

```
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db *Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da *Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs
e99bc395ffd28be46df4154393298667d16f0fba3f864ee5b0c34ae21edb7fb4 *Server/tests/Dfo.Server.Application.Tests/Inventory/GrantedAvatarIsWholeTests.cs
7e9f4678af319acc514afd0d2ee179bc3f7325e6aaf16ac63fa62b6baf9b5349 *Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs
ba8bc2e0929672e2c63372affb6b762cb0511c2398059758b5857725a4951678 *Server/src/Dfo.Server.Persistence/Inventory/SqliteItemDomainStore.cs
```

五行与执行者「本轮交付态散列」逐字节相同。前两行与 QR-TASK02-R1 记录的上一轮交付态散列
（`8e7f085d…`、`2c388060…`）**也逐字节相同**——「生产逻辑本轮零改动」这一条**核实成立**。

### 评审者自跑的检查与实测（全部本轮自做，非复述）

1. **CK-004 全量** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → **退出码 0，首跑即绿，未遇偶发**（既知偶发按「异常类型 + 栈根 + 复跑即绿」判别，本轮未出现）。
   逐项目：Protocol 521 / Networking 52 / Pvf 60 / Host 6 / Persistence 317 /
   Infrastructure 236（跳过 2）/ Application **1028**，合计 **2220 通过 / 2 跳过 / 0 失败**。
   构建输出中 `warning`／`警告` 命中数 **0**（`TreatWarningsAsErrors` 开启，无 CS9113）。
   与执行者所报**逐项目一致**。增量 **+1** 相对 QR-TASK02-R1 亲测的 2219，
   且唯一变动项目是 Application.Tests（1027 → 1028），
   与新增的单条 `[SkippableFact] Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts` 对得上；
   往返判据加的那句 `Assert.NotEqual` 在既有用例内，不增用例数——**归因核实成立**。
2. **变异实测三次**（评审者自做，文件级备份 + `sha256sum` 还原并核散列，
   **全程未使用任何 git 还原类命令**）。备份落在会话 scratchpad 的 `ItemContainers.cs.green`。见下。
3. 逐字读了 `GrantedAvatarIsWholeTests.cs` 全文、`ItemContainers.LandAsync`／`ArrivingAvatar.NewIdentity`、
   `SqliteItemDomainStore.FillAsync` 的插入与守卫、
   `InventoryEntryAssembly.cs` 与 `SqliteItemDomainStore.cs` 的 `git diff`，
   并对 F-2 的判准独立重扫了 `Server/src` + `Server/tests`。

---

### 核过并通过（一）F-1 的补救是真的：变异 C 独立复现，且新断言是**纯断言红**

**变异前绿态**（三条过滤跑）：`已通过! - 失败: 0，通过: 3`，退出码 0。
备份散列 `8e7f085d…`，与工作副本相等。

**变异 C**（与执行者、与 QR-TASK02-R1 同一处同一改法）：`StoreBackedItemContainer.LandAsync` 的
`isAvatar ? landing with { Value = ArrivingAvatar.NewIdentity() } : landing,` → `landing,`。
评审者独立造出的变异态散列 **`cd388e911834331b48da322e29085511307647ac7be3e9e9725c412d381ee46b`**，
与执行者所报**逐字节相同**——两边做的是同一个变异，不是同名不同物。

**变异下实测**：`失败! - 失败: 2，通过: 1，总计: 3`，退出码 1。

- `A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts` 红在
  `GrantedAvatarIsWholeTests.cs:146`，错误消息 `Assert.NotEqual() Failure: Values are equal /
  Expected: Not 1 / Actual: 1`——**纯断言失败，无异常，确定性**。
- 同一条判据里位于其**前面**的 `Assert.True(first.Core.Value > 0)`（第 139 行）**没有红**
  ——它在变异下确实恒真。**「区分开了」这一点因此成立**：
  「抽取得到的身份」与「堆叠数量」现在由第 146 行这一句、而不是由 `> 0` 那一句承担。
- `The_archive_and_storage_answer_the_same_kind_for_one_avatar` 仍绿（该变异不触及种类）。

**还原**：`cp` 备份回位，`sha256sum -c` → `OK`（`8e7f085d…`），
过滤跑恢复 `已通过! - 失败: 0，通过: 3`，`git status --porcelain` 与变异前逐项相同。

### 核过并通过（二）执行者关于第二条用例「四句 NotEqual 没走到」的自陈**准确**

评审者实测的失败栈与执行者所载**逐帧一致**：
`SqliteItemDomainStoreSession.FillAsync (SqliteItemDomainStore.cs:624) → FlushAsync(:264) →
ApplyDecidedWritesAsync(:220) → StoreBackedItemContainer.ApplyDecidedWritesAsync (ItemContainers.cs:696) →
LandAsync(:493) → AddAsync(:421) → GrantedAvatarIsWholeTests.GrantAsync(:250) →
Two_granted_avatars_...(:227)`，异常 `System.InvalidOperationException : The world took the item and
named no identity.`。红发生在**第二次 `GrantAsync`（判据第 227 行）**，
而那四句 `Assert.NotEqual` 在第 234–239 行——**确实没有走到**。自陈准确，无夸大。

**这值得记为一条证据（E-1）**：`FillAsync` 的插入是
`INSERT ... SELECT ... WHERE @expire IS NULL OR NOT EXISTS (SELECT 1 FROM items standing WHERE
standing.avatar_expire_date IS NOT NULL AND substr(standing.item_core, 6, 4) = substr(@core, 6, 4))`，
返回空即抛该句。变异 C 下两件时装的身份都退化成 `1`，守卫**当场发作**。
这正面证明了**撞号守卫对本轮新入库的行确实生效**——QR-TASK02-R1 只能推断
「守卫只在第二件时装撞号时才发作」，本轮由实测把它变成了已证事实。它同时说明
「身份必须大于零／唯一」那三处强制确实一字未放宽（否则该变异不会以这种方式红）。

### 核过并通过（三）评审者自己做的两个更刁的变异——**残留缺口的边界已测出**

D-02 与本轮委托要求评审者自己试「把抽取换成固定常数」，以判断判据区分的是
「不等于数量」还是「抽取」。评审者做了，并顺带做了第三个：

| 变异 | 改法（`ArrivingAvatar.NewIdentity()`） | 变异态散列 | 过滤跑结果 |
|---|---|---|---|
| **C** | 调用点整个拿掉（`landing,`） | `cd388e91…` | **红 2 / 绿 1**；往返判据以**纯断言**红 |
| **D**（评审者新增） | `=> Random.Shared.Next(1, int.MaxValue)` 改成 `=> 7`（固定常数） | `fb1785d4…` | **红 1 / 绿 2**；红的是**本轮新增用例** |
| **E**（评审者新增） | 改成进程内自增 `Interlocked.Increment(ref nextOne) + 100` | （已还原，未另记） | **全绿 3 / 3** |

**变异 D 的结论——本轮委托点名要回答的那一条**：**判据没有留绿，D 被抓住了。**
所以「区分的只是『不等于数量』」这个担心**不成立**：
往返判据那句 `Assert.NotEqual(landed.Count, first.Core.Value)` 在 D 下确实是绿的（`7 != 1`），
但**本轮新增的第二条用例把它抓红了**——两件时装拿到同一个常数 `7`，`FillAsync` 的撞号守卫发作，
异常与 C 下同源。**这正是新增用例的价值所在：它覆盖了往返那一句覆盖不到的退化。**
两处补救**各有各的、不重叠的**杀伤力，不是同一条断言写两遍。

**如实报出的残留缺口（NB-1，非阻塞）**：变异 E——把抽取换成**进程内自增序列**——
**三条判据全绿**。也就是说，判据集能区分「无抽取」「固定常数」，但**不能**区分
「抽取」与「进程内自增」。这是真的缺口，但评审认为**不构成阻塞**，理由两条：
其一，`NewIdentity()` 的 `<remarks>` 自己写明拒绝自增的理由是
「a sequence would need a row of its own to stay distinct across restarts」——
即自增的坏处只在**跨进程重启**时显形，而契约验收第 2 条与所引 RQ 都没有要求跨重启唯一，
钉住它需要一条重启场景判据，超出本 Task 的验收面；
其二，契约要的「抽取得到的、大于零的身份值」的两种现实退化路径（不抽、抽成常数）
都已被本轮判据以实测抓红。**记为后续成果可选的加固点，在此点名而不是留在暗处。**

**另一条如实记载（NB-2，非阻塞）**：新增用例在两个变异下都是**异常红**而非断言红，
它自己那四句 `Assert.NotEqual` 至今没有任何变异让它们**以断言形式**红过。
执行者已就变异 C 主动写明这一点（措辞准确），本轮补充：变异 D 亦然。
不阻塞，因为「新加的断言本身会红」这一条由往返判据第 146 行承担，那是纯断言红，已实测。

**另一条如实记载（NB-3，非阻塞）**：`Assert.NotEqual(landed.Count, first.Core.Value)` 与
两件时装身份互异，都依赖 `Random.Shared.Next(1, int.MaxValue)` 恰好不撞上 `1`／不彼此相等，
概率各约 `2^-31`。属可忽略量级，且与生产代码本身的撞号概率同阶，不另计。

**还原核实**：D 与 E 之后均由同一份备份 `cp` 还原，`sha256sum -c` → `OK`（`8e7f085d…`），
过滤跑恢复 `失败: 0，通过: 3`，`git status --porcelain` 与变异前逐项相同。
**全程未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash` 一次未出现），
未提交、未建检查点，未触碰主检出与 `.worktrees/client-debug-lab`，未改动任何生产代码或判据。

### 核过并通过（四）F-2 的基数 8 与口径：**独立重扫，未发现第 9 处；被排除的各处排除正确**

评审者按执行者声明的判准（「以现在时断言『档案给出的种类』与『存储判定时装的种类』不一致」）
**独立重扫**，口径：`Server/src` + `Server/tests`，`--include=*.cs`，排除 `obj/`、`bin/`，
只取**注释行**（`^\s*(///|//)`），三趟正则并集后逐条读上下文：
(a) `disagree|do not agree|does not agree|disagreement|out of step|at odds|contradict|inconsistent|divergen|differ`；
(b) `kind 8|kind 1\b|the 8\b|an 8\b|equipment index|constant 1|AvatarKind`；
(c) `kind byte|archive's answer|two sources|both sources|differently`。
按成员名索引，不按行号。

**结论：执行者的 8 处基数完整，评审者未找到第 9 处。** 八项逐条复核：第 1 项
（`PvfEntryLifecycleContentSource.WornPieceMetadata`）里那句
「records an avatar as kind 8 and this still answers 1 for one, so that pair still disagrees」
读上下文确认它位于「What this paragraph said before:」的**引文之内**，是已订正的原文引用而非现行断言；
第 2、3、4、5、6 项同样确认已带 amended 段；第 7、8 项本轮订正（下条另评）。

**被排除的各处，评审者逐一复核，排除均正确**：

- `IItemPlacementRuleService` 接口级 `<remarks>`（"two copies of a kind number are two things
  **free to** disagree"）——讲的是同一个数字被抄在两个文件里的风险，不是档案与存储的那一对，
  且是「free to」而非现在时断言。**排除正确。**
- `ItemContainers.LandAsync` 的 `<remarks>`（"which is how the two **came to** disagree about
  nothing in particular"）——讲的是两个调用方各自抄了一份 switch，**过去时**，与档案无关。**排除正确。**
- `ClassificationRefusalTests` 的类级与 `AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`、
  `ItemClassificationTests.A_write_classifies_the_template_the_archive_declares_not_the_kind_the_row_carries`
  ——分歧是**判据自己构造**的（kind 2 写在档案称为时装的模板上）。**排除正确。**
- `IItemDomainStore.StoredItem`、`IItemContainer.ContainerSlotItem`
  （"the reading this classification exists to replace"）与 `InventoryEntryAssembly.SaveAsync`
  的行内注释——都是「行的字节由调用方填」的设计理由，不断言两个来源当前取值不同。**排除正确。**
- `CharacterCreationRuleService.EquipmentKind` 与 `ItemArrivalRuleService.EquipmentKind` 的
  `<summary>`（「档案的装备索引让每一件穿戴物是 1」）——**排除正确**：它是一句泛化，
  不含「两个来源不一致」的断言，落在判准之外。评审补充两点：
  其一，这句泛化在**本成果之前**就已被契约兽的 5 破坏，是**既有**记载债而非本轮引入；
  其二，执行者已在文中点名它并给出行为不受影响的实测理由
  （`ItemArrivalRuleService.BandsFor` 的第二个析取支），评审者读了该方法，理由属实。
  作为**记载债点名**处理，本轮不改，合规。
- `CreatureKindAgreesWithTheArchiveTests` 类级 `<remarks>`——通篇过去时
  （"they **did not** agree"、"handed"），讲契约兽那一对且已被解决。**不在判准内。**

### 核过并通过（五）本轮新订正的第 7、8 项：纯注释、逐字引原文、说明哪一半失效

- `git diff --numstat`：`InventoryEntryAssembly.cs` 15/1，`SqliteItemDomainStore.cs` 71/13。
- `InventoryEntryAssembly.cs` 的 diff **每一行都以 `///` 开头**，零行可执行代码。
- `SqliteItemDomainStore.cs` 相对 HEAD 的**非注释**差异经 `git diff -U0` 过滤后
  **仅 15 行，且全部是 TASK-01 的 `FactsOf(...)` 辅助与 `facts` 三元分支**，
  没有第二处代码改动——因此本轮对该文件的改动**只可能是注释**。
  执行者「两处 F-2 订正的 diff 每一行都以 `///` 开头」**核实成立**。
- **逐字引原文**：第 7 项引 "not read off the kind byte the row carries -- *those two disagree*,
  which is why the classification exists at all"，与被替换掉的原句**逐字相符**；
  第 8 项引 "the kind byte a core carries and the archive's answer *disagree*, which is the whole
  reason the classification exists"，同样**逐字相符**。
- **说明哪一半失效**：两处都明写失效的是 "disagree"／"those two disagree"，
  并写明仍成立的一半（第 7 项：仍只问档案，因为那个字节是调用方填的；
  第 8 项：`Write` 仍拒绝不声明 `WorldClass` 的写入），并给了「订正而非删除」的理由。
  `IdentityOfAsync` 与 `Write` 的签名、函数体、`ArgumentException` 守卫与消息**均未动**（见上条 diff 核实）。
- **越界合规**：两处均在 BD-003/BD-004/BD-008 之外，但由 D-02 明文授权，
  且按仓规「改已被证伪的记载可以越任务边界，但要补记修正案」处理，**已补记修正案**。

### 核过并通过（六）那句被证伪的自证：订正到位，上一轮小节原样保留

- 上一轮「### 执行者自查（逐条对照契约验收条件）」第 2 条**仍然原样在文中**，
  末句仍是「凭据是往返判据逐字段断言这六个值加 `Core.Value > 0`，且变异 B 把事实改空后它变红。」
  ——**未被删改**，与 QR-TASK02-R1 引用的原文一致。上一轮「跨边界」那段的「另有 **3 处**」
  也原样保留。**「不重写或删改既有小节」这一条核实成立。**
- 本轮「### 那句被证伪的自证的订正前后对照」**逐字引出了它此前说的是什么**，
  并把「错在哪」拆成两条：`Core.Value > 0` 恒真（附本轮变异实测为据，不是推演），
  以及变异 B 只触及「事实」那一半、把它的结论安到「身份」那一半是错误归因。
  两条评审者均已独立实测证实（`> 0` 在变异 C 下确实仍绿）。**订正准确、可证伪。**
- D-02 字面写的是「**删掉**那句已被证伪的自证」，执行者选择了「保留原句 + 另起订正」；
  评审认为这**满足 D-02 的实质要求**（「订正时要写明它此前说的是什么、错在哪」），
  且与本轮委托「不要重写或删改任何既有小节」的约束一致，也与仓规「订正而非抹去」同向。
  **不作为缺陷。**

### 核过并通过（七）返工未带进新问题

- **生产逻辑零改动**：`ItemContainers.cs`（`8e7f085d…`）与
  `PvfEntryLifecycleContentSource.cs`（`2c388060…`）散列与上一轮交付态**逐字节相同**，已核。
  另两个被改的生产文件的非注释差异已逐行核为 TASK-01 既有内容，本轮增量纯注释。
- **判据只增不改**：`GrantedAvatarIsWholeTests.cs` 现文经通读，
  既有的 `Assert.True(first.Core.Value > 0)`、六列事实的每一句 `Assert.Equal`、
  往返四句比较**均在原位**，期望值未见改写；新增内容是第 146 行一句与第 218–245 行一条用例。
- **计数与归因**：全量 2220/2/0 亲测复现，+1 精确落在 Application.Tests 且可归因到那一条新用例。
- **架构判据**：`CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests`
  随全量绿，未被改动。构建 0 警告 0 错误（无 CS9113）。
- **阻塞条件**：未触发（`AddAsync`／`PutAsync` 签名未动，未建第二条发放通路）。
- **被否掉的三条替代**：均未采用；「身份必须大于零／唯一」那三处强制未放宽——
  变异 C／D 都靠 `FillAsync` 那条守卫仍然站着才红，是反面凭据。

### 需要的补救

**无阻塞项。** 以下两条记为**非阻塞的观察**，供最终评审与后续成果参考，
**不要求本轮改动**（改它们会超出 D-02 的返工范围）：

| ID | 观察 | 建议归属 |
|---|---|---|
| NB-1 | 判据集能抓「无抽取」与「固定常数」，抓不到「进程内自增」（评审者变异 E 实测全绿）。缺口只在跨进程重启时显形，本 Task 验收面未要求。 | 记为后续成果的可选加固点 |
| NB-2 | 新增用例的四句 `Assert.NotEqual` 在变异 C 与 D 下都因撞号守卫先抛而未走到，其自身尚无断言形式的变异凭据。「新断言会红」由往返判据第 146 行承担，已实测。 | 记载即可 |

另记一条**正面证据 E-1**：变异 C／D 下 `FillAsync` 的撞号守卫在**新入库的行**上当场发作，
把 QR-TASK02-R1 只能推断的「守卫只在第二件时装撞号时才发作」变成了已实测的事实。

### 本轮评审的边界声明

评审者**未改动任何生产代码或判据**，未改动 Task 文件的任何既有小节（只在文末追加本节）。
三次变异全程**文件级备份 + `sha256sum` 还原并核散列**（三次还原后均为 `OK`，
末态散列与交付态逐字节相同，`git status --porcelain` 与评审开始时逐项相同），
**未使用任何 git 还原类命令**；未提交、未建 Git 检查点；
未触碰主检出与 `.worktrees/client-debug-lab`。
本节所有基数均写成「N 处／N 条，分别是：…」并给了计数口径，按成员名索引。


## Controller Disposition D-02b — 接受 QR-TASK02-R2 的 `passed`，分派两条非阻断观察

**判定**：接受。第二轮执行结果、自查、QR-TASK02-R2 的 `passed` 与被拥有的 diff 指向同一轮。
登记表中 TASK-02 置 `done`。

D-02 要求的两条补救都成立，且**杀伤力不重叠**：
「固定常数」变异（`NewIdentity() => 7`）逃得过往返判据那句 `NotEqual`（`7 != 1`），
但逃不过新增的「连发两件」用例——两件同号，撞号守卫发作。

**一处措辞出入，不作为缺陷**：D-02 字面写「删掉那句已被证伪的自证」，
执行者选了「保留原文 + 就地订正」。**执行者的做法更合本仓规矩**——
「改已被证伪的记载要补记修正案，不是抹去」。是我的处置措辞用词不准，
按本仓一贯做法应写「订正」而非「删掉」。此处照执行者的做法。

**两条非阻断观察的去向**：

| 观察 | 内容 | 去向 | 理由 |
|---|---|---|---|
| NB-1 | 变异 E（把抽取换成进程内自增 `Interlocked.Increment`）**三条判据全绿**——判据抓不到「抽取 vs 进程内自增」 | **TASK-06** 记为一条**具名的覆盖缺口** | 计划选「抽取不排序」的**理由**恰恰是「跨重启保持互异」，而这一点今天零覆盖。变化清单必须如实记它，不得写成「已覆盖」 |
| NB-2 | 新增用例自身那四句 `NotEqual` 在两个变异下都因守卫先抛而未走到，尚无断言形式的变异凭据 | **TASK-06** 一并记 | 同上，属覆盖口径的如实记载，不要求改判据 |

**一条要交给 TASK-03 的证据（E-1）**：
变异 C 下第二次发放时 `FillAsync` 抛出 `The world took the item and named no identity.`，
**正面证明撞号守卫对新入库的行确实生效**。
这原是 T-003 要证的东西之一，现已有一次实测。
T-003 **仍须自己补齐 CK-008／CK-009**——一次变异下的副产品不能代替为该主张写的判据，
但它可以作为交叉印证写进 T-003 的证据栏。
