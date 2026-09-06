# TASK-04 — 种类改变之后，两条既有判定各自的结局被坐实

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

用判据坐实两件事：一件时装**不能**被放进快捷栏的格子（变化）；一件时装到达时被考虑的落点范围**与改动前相同**（不变）。

- Referenced spec requirements: RQ-005, RQ-006
- Referenced plan sections: plan.md 的 T-004（Steps 1–3，CK-011..CK-012）；Owned Boundaries 的 BD-008
- Owned boundary:

BD-008 `Server/tests/`（只新增判据）

- Permitted files or interfaces:

- `Server/tests/Dfo.Server.Application.Tests/` 下的判据
- 若需要，`Server/src/Dfo.Server.Application/Inventory/` 下相关规则的**记载**（不改判定逻辑）

- Dependencies: TASK-02
- Required dependency outputs: TASK-02 交付的时装新种类
- Acceptance conditions:

1. 判据证明一件时装不能被放进快捷栏的格子。记载写明**这不是新规则**——排除项本就写着时装与时装徽章，此前失效只因发放侧标错了种类。
2. 判据证明一件时装到达时被考虑的落点范围与改动前相同：只有它自己那一段，不含快捷栏。
3. 这两条各自被记进本任务的执行结果，一条标为「变化」，一条标为「查过的不变」，供 TASK-06 汇总。

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**如实报告义务**：「落点范围不变」今天只是推演。**必须用判据坐实**；若实测发现落点其实变了，**如实报出并记进变化清单**，**不得为了符合推演而改判据**。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-011 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo` | 快捷栏拒绝的判据通过；落点范围不变的判据通过 |
| CK-012 | Non-command：备份并核散列后，把快捷栏排除项里的时装那一项去掉，重跑 CK-011，再还原并核散列 | 快捷栏判据变红；还原后恢复绿 |

- Required evidence:

- 两条检查的实际输出与退出码。
- CK-012 变异前后的散列与红/绿结果。
- 落点范围实测结果的原文（**不是推演的复述**）。

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

### 一、改了哪些文件

任务结束时 `git status --porcelain` 的输出，与任务开始时相比多出一行：

```
?? Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarSlotRulesAfterTheKindChangeTests.cs
```

其余各行与开始时逐字相同（10 行 ` M` 与 3 行 `??`，都是 TASK-01/02/03 留下的）。计数口径：任务开始与结束各跑一次 `git status --porcelain`，比较两次输出。

新增文件里的成员，分别是：

- 类型 `AvatarSlotRulesAfterTheKindChangeTests`
- 判据 `A_quick_slot_refuses_the_avatar_the_archive_declares`：`SkippableTheory`，3 个 `InlineData`，分别是 `ItemSlotRuleService.QuickSlotStart`、`5`、`ItemSlotRuleService.QuickSlotEnd`
- 判据 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`：`SkippableFact`
- 私有夹具 `ArchivePlacementAsync`：向运营方自己的档案要 `101550507` 的落点，不打桩
- 私有常量 `AvatarItemId`、`KindStorageJudgesAnAvatarBy`、`KindTheEquipmentIndexHandedThisTemplateBefore`、`ConsumableKind`

该文件最终态散列 `64030bc29bb363bf8d68a4c2aae9164963d36cd31f74f39f11271df67a254bb1`。

`Server/src/Dfo.Server.Application/Inventory/` 下没有本任务的改动：`ItemSlotRuleService.cs` 结束时散列 `0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177`、`ItemArrivalRuleService.cs` 结束时散列 `c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba`，与任务开始时测得的两个值相同。这两个文件在 `git status` 里也没有出现，即与 HEAD 一致。

过程中另建过一个临时探针文件 `Server/tests/Dfo.Server.Application.Tests/Inventory/TempArrivalProbe.cs`，用来把实际返回值打印出来（原文见第四节），取得输出后即删除；上面那次结束时的 `git status` 里没有它。

「这不是新规则」写在新类型的 `<remarks>` 里，引了两处仓内持久记载的原文，两处都逐字核对过：

- `Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs` 的类型 `<remarks>`：
  「The exclusions are the kinds whose home is another list entirely -- creatures and their gear, avatars and their emblems」
  （用 `git show HEAD:` 取同一文件核对过，本轮之前就是这句）
- `Server/src/Dfo.Server.Application/Inventory/ItemPlacementRuleService.cs` 的 `ClassifyAsync` `<remarks>` 里被引号保留下来的改动前旧句：
  「the byte and the archive still disagree about an avatar -- stored as kind 8, given kind 1 by the equipment index」

### 二、两条检查的实际输出与退出码

**CK-011** `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo`

- 加判据之前（本任务开始时测得的基线）：
  `已通过! - 失败:     0，通过:  1028，已跳过:     0，总计:  1028` — EXIT=0
- 加判据之后（最终态）：
  `已通过! - 失败:     0，通过:  1032，已跳过:     0，总计:  1032` — EXIT=0
- 两条新判据在这一次里都通过。

**CK-012** 见第三节的变异 A。

**另跑的全量** `PVF_ARCHIVE_PATH=... dotnet test Server/86JP.Server.slnx --nologo` — EXIT=0，逐项目：

| 项目 | 派工给的基线 | 本次 | 差 |
|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 521 | 0 |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 |
| Dfo.Pvf.Tests | 60 | 60 | 0 |
| Dfo.Server.Host.Tests | 6 | 6 | 0 |
| Dfo.Server.Persistence.Tests | 320 | 320 | 0 |
| Dfo.Server.Infrastructure.Tests | 236（另 2 跳过） | 236（另 2 跳过） | 0 |
| Dfo.Server.Application.Tests | 1028 | 1032 | +4 |
| 合计 | 2223 通过 / 2 跳过 | 2227 通过 / 2 跳过 / 0 失败 | +4 |

增量 +4 出现在 `Dfo.Server.Application.Tests`，构成它的各项分别是：`A_quick_slot_refuses_the_avatar_the_archive_declares` 的 3 个用例（slotIndex 3、5、8），与 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` 的 1 个用例。计数口径：`dotnet test` 为每个 dll 报的那一行「总计」。

两个跳过仍是 `Dfo.Server.Infrastructure.Tests` 里的 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

### 三、每条新判据各自的变异实测

两次变异都用文件级备份 + `sha256sum` 还原并核散列，备份放在会话临时目录；没有执行 `git checkout --`、`git restore`、`git stash` 或任何提交。

**变异 A（即 CK-012）—— 把快捷栏排除项里的时装那一项去掉**

- 文件 `Server/src/Dfo.Server.Application/Inventory/ItemSlotRuleService.cs`，成员 `AllowsQuickSlot`
- 改法：把 `or KindAvatar` 从 `itemKind is not (KindCreature or KindCreatureEquipment or KindCreatureConsumable or KindAvatar or KindAvatarEmblem)` 里去掉
- 变异前散列 `0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177`
- 变异后散列 `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864`
- 重跑 CK-011：`失败!  - 失败:     5，通过:  1027，已跳过:     0，总计:  1032` — EXIT=1
- 这次运行里变红的 5 个用例，分别是：
  - `AvatarSlotRulesAfterTheKindChangeTests.A_quick_slot_refuses_the_avatar_the_archive_declares(slotIndex: 3)`、`(slotIndex: 5)`、`(slotIndex: 8)` —— 3 个，都红在
    `Assert.False(SlotRules.CanOccupyMainSlot(placement, slotIndex));`
    报 `Assert.False() Failure`，运行器指到该判据文件 line 81
  - `AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` —— 1 个，红在
    `Assert.IsType<ItemArrival.NoRoom>(ArrivalRules.ForGrant([], 1, asItIsNow, ownBandFull));`
    报 `Assert.IsType() Failure: Value is not the exact type`，line 130。这一处是顺带的：`BandsFor` 那一支读的就是 `AllowsQuickSlot`，所以这次改动也改了到达那一支的答案
  - 既有判据 `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)` —— 1 个
- 还原：`cp` 备份文件回原位，散列回到 `0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177`；重跑 CK-011：
  `已通过! - 失败:     0，通过:  1032，已跳过:     0，总计:  1032` — EXIT=0

**变异 B —— 只动到达那一支，用来单独看第二条判据是不是恒真**

- 文件 `Server/src/Dfo.Server.Application/Inventory/ItemArrivalRuleService.cs`，成员 `BandsFor`
- 改法：把条件里的 `|| !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 去掉，只留 `if (placement.ItemKind == EquipmentKind)`
- 变异前散列 `c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba`
- 变异后散列 `3593447673aa86a3a6a9a96c9604e19c4cce7c6fa8546d6fce90535963f00d4e`
- 重跑 CK-011：`失败!  - 失败:     1，通过:  1031，已跳过:     0，总计:  1032` — EXIT=1
- 这次运行里变红的用例只有 1 个：`An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`，红在
  `Assert.IsType<ItemArrival.NoRoom>(ArrivalRules.ForGrant([], 1, asItIsNow, ownBandFull));`
  报 `Assert.IsType() Failure: Value is not the exact type`，line 130
- 还原：`cp` 备份文件回原位，散列回到 `c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba`；随后跑的全量为 0 失败、EXIT=0
- 观测：在变异 B 那次运行里，变红的是上面这一个用例；在变异 A 那次运行里，1028 个既有用例中变红的是 `The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)` 这一个。

### 四、落点范围的实测结果原文

用一个临时探针把实际返回值打印出来，命令 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/tests/Dfo.Server.Application.Tests --nologo --filter "FullyQualifiedName~TempArrivalProbe"`，EXIT=1（探针用 `Assert.Fail` 打印）。失败消息原文：

```
placement(101550507) = ItemPlacement { ItemKind = 8, StackLimit = 1, SlotStart = 9, SlotEnd = 64, WorldClass = Avatar }
emptyset  kind8 -> IntoEmptySlot { SlotIndex = 9, Count = 1 }
emptyset  kind1 -> IntoEmptySlot { SlotIndex = 9, Count = 1 }
ownfull   kind8 -> NoRoom { }
ownfull   kind1 -> NoRoom { }
ownfull   kind2 -> IntoEmptySlot { SlotIndex = 3, Count = 1 }
barslot3  kind8 canOccupy -> False
barslot5  kind8 canOccupy -> False
barslot8  kind8 canOccupy -> False
barslot3  kind1 canOccupy -> True
ownstart9 kind8 canOccupy -> True
```

配置说明：

- `kind8` 是档案现在给 `101550507` 的那份落点，原样使用
- `kind1`、`kind2` 是把同一份落点的种类字节改写成 1、2 之后再问一次，别的字段不动
- `emptyset` 是占用集合为空
- `ownfull` 是占用集合为 9..64 这 56 个格子（口径：`SlotEnd - SlotStart + 1 = 64 - 9 + 1`），快捷栏 3..8 不在这个集合里

只就这次观测说：在 `ownfull` 这个配置下，kind8 与 kind1 都答 `NoRoom`；同一个占用集合下把种类字节换成 2，答的是快捷栏第 3 格，所以那两个 `NoRoom` 不是因为快捷栏也被占了。在 `emptyset` 配置下，kind8 与 kind1 都答第 9 格，也就是档案给的那一段的头一格。

### 五、两条各自的定性（供 TASK-06 汇总）

| 条目 | 位置（按成员名） | 定性 | 凭据 |
|---|---|---|---|
| 一件时装被快捷栏的格子拒绝 | `ItemSlotRuleService.CanOccupyMainSlot`、`ItemSlotRuleService.AllowsQuickSlot` | **变化** | 观测：`101550507` 的档案落点（kind 8）在槽 3、5、8 上答 False，同一份落点换成 kind 1 在槽 3 上答 True，在自己那一段的头一格（9）答 True。判据 `A_quick_slot_refuses_the_avatar_the_archive_declares`；变异 A 让它的 3 个用例都红 |
| 一件时装到达时被考虑的落点范围 | `ItemArrivalRuleService.BandsFor`（经 `ForGrant` 观测） | **查过的不变** | 观测：`emptyset` 下 kind 8 与 kind 1 都答第 9 格；`ownfull`（快捷栏空着）下都答 `NoRoom`。判据 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`；变异 B 让它红 |

「变化」那一条不是新规则：排除项在本轮之前就写着时装与时装徽章（第一节引的原文，已用 `git show HEAD:` 核对），此前没生效是因为发放侧给这个模板的种类字节是 1。

### 六、这些实验不足以确定的

- 这些实验用的是 `equipment.lst` 里 `101550507` 这一个模板。它们不足以确定别的时装模板在这两条判定下的答案。
- 「改动前」那一侧，是把同一份档案落点的种类字节改写成 1 之后再问一次，不是在改动前的代码上跑出来的。它们不足以确定改动前的代码在别的输入上会怎么答。
- 变异 A 与变异 B 各是一种改法。它们不足以确定别的改法会不会让这两条判据变红。
- 落点观测走的是 `ForGrant`。它们不足以确定 `ForDailyRefill` 这条路对同一个模板会怎么答——本任务没有问过那条路。
- 这里没有查时装能不能被穿上，也没有查槽号出处；本任务不涉及。

### 七、自查：逐条对照验收条件

1. **验收条件 1（快捷栏拒绝 + 记载写明不是新规则）** —— 凭据：`A_quick_slot_refuses_the_avatar_the_archive_declares` 的 3 个用例在第二节那次 1032 全绿里通过；变异 A 下 3 个都红在 `Assert.False(SlotRules.CanOccupyMainSlot(placement, slotIndex));`。新类型的 `<remarks>` 写了这不是新规则，并引了第一节列出的两处仓内原文，两处逐字核对过（一处用 `git show HEAD:` 核对，一处对读工作区文件）。
2. **验收条件 2（落点范围与改动前相同：只有它自己那一段，不含快捷栏）** —— 凭据：第四节探针原文，`ownfull` 下 kind8 与 kind1 同为 `NoRoom` 而 kind2 落在第 3 格；判据 `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` 通过；变异 B 下它红在 `Assert.IsType<ItemArrival.NoRoom>(...)`。实测与派工里的推演一致，没有出现落点变了的情况。
3. **验收条件 3（两条各标一个定性）** —— 凭据：第五节的表，一条标「变化」，一条标「查过的不变」。
4. **CK-011** —— 凭据：第二节，最终态 1032 通过 / 0 失败 / EXIT=0，两条新判据都在里面。
5. **CK-012** —— 凭据：第三节变异 A，变异前后散列都记了，变异后 5 个红（含快捷栏那 3 个），还原后散列回到变异前的值且重跑 1032 全绿 EXIT=0。
6. **边界 BD-008（只新增判据）** —— 凭据：第一节的 `git status --porcelain` 比较，多出的只有一个 `??` 的测试文件；`Server/src/` 下两个被变异过的文件结束时散列与开始时相同，且未出现在 `git status` 里。
7. **不建检查点、不提交、不用 git 还原** —— 凭据：本任务执行过的 git 命令只有只读的 `git status --porcelain`、`git diff --stat HEAD`、`git rev-parse --abbrev-ref HEAD`、`git show HEAD:<path>`；两次还原都是 `cp` 备份文件 + `sha256sum` 核对。
8. **恒真自查** —— 凭据：两条新判据各做了一次变异，各自都红了，红在第三节写明的那一句。第二条判据里另有一条控制断言（`ownfull` 下把种类换成 2 应落在快捷栏第 3 格），它是用来说明那次 `NoRoom` 不是「哪儿都满了」；探针原文第 6 行是它的实测值。
9. **既知偶发** —— 本任务跑过的判据运行里没有出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，也没有出现栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar` 的失败。
10. **不给 `PvfEquipmentSlots.ByName` 补键、不新增协议帧** —— 凭据：本任务只新增了一个测试文件，第一节列出了它的成员，里面没有触到这两处。

## Independent Quick Review — QR-04-1

- 判定：**passed**
- 评审的契约版本：Contract revision 1，无修订案
- 评审者上下文：fresh，与执行者不同
- 被评审的自有改动身份：工作区未提交，基线 `HEAD = 565d33fee899556476f8a582605fb4f439261c0f`。本轮 TASK-04 的增量为一个新文件
  `Server/tests/Dfo.Server.Application.Tests/Inventory/AvatarSlotRulesAfterTheKindChangeTests.cs`
  （散列 `64030bc29bb363bf8d68a4c2aae9164963d36cd31f74f39f11271df67a254bb1`，与执行结果第一节所记相同）。

### 评审者自己跑过的命令与取得的证据

计数口径：下面每一行都是评审者在本轮亲自执行的一次命令，输出取自该次运行。

1. `git status --porcelain` → 14 行，分别是 10 行 ` M` 与 4 行 `??`。4 个 `??` 分别是
   `.rks-agent/tasks/server-agrees-what-an-avatar-is/`、`AvatarSlotRulesAfterTheKindChangeTests.cs`、
   `GrantedAvatarIsWholeTests.cs`、`TwoAvatarsDoNotShareOneIdentityTests.cs`。
   与执行结果第一节记的「10 行 ` M` 与 3 行 `??` 加上本轮那一个」一致。
   目录树里没有 `TempArrivalProbe.cs`：`find Server -iname "*Probe*"` 的命中里没有测试探针，
   `Server/tests/Dfo.Server.Application.Tests/Inventory/` 的列目录里也没有。**临时探针确已删除。**
2. `git rev-parse HEAD` → `565d33fee899556476f8a582605fb4f439261c0f`；`git stash list` → 空。未提交、未入栈。
3. `sha256sum` 三个文件 → `ItemSlotRuleService.cs` `0438efe9…8a177`、`ItemArrivalRuleService.cs` `c6fb4bac…0e4ba`、
   新判据文件 `64030bc2…54bb1`。三个都与执行结果所记相同。两个源文件不出现在 `git status` 里，即与 HEAD 逐字一致。
   `git diff --stat HEAD -- Server/src/Dfo.Server.Application/Inventory/` 只列出 `InventoryEntryAssembly.cs`、
   `ItemContainers.cs`、`ItemPlacementRuleService.cs` 三个（都是前几轮的），不含这两个。
4. 全量 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
   → EXIT=0。逐 dll：`Dfo.Pvf.Tests` 60/0、`Protocol` 521/0、`Networking` 52/0、`Persistence` 320/0、
   `Infrastructure` 236 通过另 2 跳过、`Host` 6/0、`Application` 1032/0。**合计 2227 通过 / 2 跳过 / 0 失败。**
   与执行结果第二节的表逐格相同。
5. **变异 B 重做**（评审者自己做的，`cp` 备份 + `sha256sum`，未用任何 git 还原命令）：
   把 `ItemArrivalRuleService.BandsFor` 的 `|| !ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)` 去掉。
   变异后散列 `3593447673aa86a3a6a9a96c9604e19c4cce7c6fa8546d6fce90535963f00d4e`
   —— 与执行结果所记**逐字符相同**，即评审者复现的是同一份字节。
   重跑 CK-011 → `失败!  - 失败:     1，通过:  1031，总计:  1032`，EXIT=1。
   唯一变红的用例是 `AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`，
   报 `Assert.IsType() Failure: Value is not the exact type / Expected: ItemArrival+NoRoom / Actual: ItemArrival+IntoEmptySlot`，
   运行器指到该判据文件 line 130，即 `Assert.IsType<ItemArrival.NoRoom>(ArrivalRules.ForGrant([], 1, asItIsNow, ownBandFull));`。
   **红的是执行者说的那一条，红在执行者说的那一句。** 还原后散列回到 `c6fb4bac…0e4ba`。
6. **变异 A 也重做了**（派工只要求 B，评审者顺带做了 A）：
   把 `AllowsQuickSlot` 里的 `or KindAvatar` 去掉，变异后散列
   `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864` —— 亦与执行结果**逐字符相同**。
   重跑 CK-011 → `失败!  - 失败:     5，通过:  1027，总计:  1032`，EXIT=1。5 个红的分别是：
   `A_quick_slot_refuses_the_avatar_the_archive_declares(slotIndex: 3)`、`(slotIndex: 5)`、`(slotIndex: 8)` 三个（都在 line 81）、
   `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` 一个（line 130）、
   既有判据 `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)` 一个。
   与执行结果第三节所记的名单相同。还原后散列回到 `0438efe9…8a177`。
7. 还原之后重跑 `dotnet test Server/tests/Dfo.Server.Application.Tests --nologo`
   → `已通过! - 失败: 0，通过: 1032，总计: 1032`，EXIT=0。**两次变异都已还原且散列复原。**
8. 本轮评审跑过的四次判据运行里没有出现 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，
   也没有出现栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar` 的失败。

### 恒真陷阱逐句复查

评审者逐条读了新判据文件的每一句断言，问的是「实现被改坏时它凭什么会红」。逐句结论，共 9 句，分别是：

**`A_quick_slot_refuses_the_avatar_the_archive_declares`（4 句）**

- `Assert.Equal(KindStorageJudgesAnAvatarBy, placement.ItemKind)` —— 读的是运营方档案经
  `ItemPlacementRuleService.PlacementForAsync` 给出的落点，不打桩；发放侧若不再给这个模板 8，它红。
- `Assert.False(SlotRules.CanOccupyMainSlot(placement, slotIndex))` —— 变异 A 实测红（评审者自己复现）。
- `Assert.True(SlotRules.CanOccupyMainSlot(placement, placement.SlotStart))` —— 弱但非恒真：
  `CanOccupyMainSlot` 的自有段那一支被改坏时它红。
- `Assert.True(SlotRules.CanOccupyMainSlot(placement with { ItemKind = 1 }, slotIndex))` ——
  这是排除「快捷栏对谁都说不」的对照；`CanOccupyMainSlot` 若不看种类字节而一律拒绝这几格，它红。

**`An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`（5 句）**

- `Assert.Equal(new IntoEmptySlot(SlotStart, 1), ForGrant(…, nothingOccupied))` —— 钉死了具体值，非恒真。
- `Assert.Equal(ForGrant(asItWasBefore, nothingOccupied), ForGrant(asItIsNow, nothingOccupied))` ——
  两次调用相比，**单独看是弱的**：一个完全不看种类字节的 `BandsFor` 也能让它绿。但它与上一句配对，上一句钉住了值。
- `Assert.IsType<ItemArrival.NoRoom>(ForGrant(asItIsNow, ownBandFull))` —— 变异 B 实测红（评审者自己复现）。
- `Assert.Equal(ForGrant(asItWasBefore, ownBandFull), ForGrant(asItIsNow, ownBandFull))` ——
  与上上句同样是「两次调用相比」的弱句；变异 B 下它也会红（kind 1 走 `EquipmentKind` 那一支仍答 `NoRoom`，
  kind 8 变成第 3 格），只是运行器先在前一句停下。
- `Assert.Equal(new IntoEmptySlot(QuickSlotStart, 1), ForGrant(asItIsNow with { ItemKind = 2 }, ownBandFull))` ——
  **这就是派工点名要核的那个对照。评审者核过：它真的排除了「哪儿都满了」。**
  `ownBandFull` 是 `Enumerable.Range(SlotStart, SlotEnd - SlotStart + 1)`，实测落点为 9..64，
  所以这个占用集合是 {9..64}，快捷栏 3..8 不在里面。这一句拿一个快捷栏收的种类（2）去问同一个占用集合，
  答的是第 3 格，**即证明第 3 格在这个集合下是空的**。因此上面那一句的 `NoRoom` 不可能是「快捷栏也满了」，
  只能是快捷栏根本没被提供给 kind 8。

**结论：新判据文件里没有恒真断言。** 两句「两次调用相比」的断言单独看偏弱，但各自都与一句钉死具体值的
断言配对，且两条判据各自的承重句都被评审者自己的变异实测打红过。

### 对第三点（探针输出是否支撑「落点范围不变」）的判断

**支撑，但要看清它支撑的是哪一步。** 探针比的是同一份档案落点在 kind 8 与 kind 1 两个字节下的答案，
不是改动前后的两份代码。它直接坐实的是「这个模板在 `ForGrant` 下的落点不随种类字节在 8/1 之间变化」。
其中 `emptyset` 那两行不具区分力（kind 8 即便被额外提供快捷栏做回退，空集下仍会先落在自有段的第 9 格）；
真正有区分力的是 `ownfull` 那两行加上 kind2 那一行的对照。

从「不随字节变化」到「与改动前相同」还差一步。评审者核了这一步：
`Server/src/Dfo.Server.Infrastructure/Content/PvfEntryLifecycleContentSource.cs` 的 `WornPieceMetadata`
在本轮的 `git diff HEAD` 里，只改了种类字节那一个选择（`worldClass == Creature ? 5 : 1`
换成三分支的 `Creature => 5, Avatar => 8, _ => 1`），而 `StackLimit`、`SlotStart`、`SlotEnd`
三个是同一行里没被改动的字面量 `1`、`9`、`64`；同时 `ItemSlotRuleService.cs` 与
`ItemArrivalRuleService.cs` 与 HEAD 逐字一致（散列已核）。`ForGrant` 只读 `StackLimit`、
`SlotStart`、`SlotEnd`、`ItemKind` 四个字段。所以对这一个模板而言，改动前的落点与现在的落点**恰好只差种类字节**，
把字节改写回 1 再问一次，与在改动前的代码上问，答案相同。

执行结果第六节自己把这一步记成了「不足以确定」，即它**少报**而非多报。这不是缺陷，只是可以更强。

### 记法口径逐句核

判准是「这句在说个例还是在说一类」，逐句读，未用词表搜。核过的记载有两处：
执行结果第一至七节，与新判据文件的 `<summary>`/`<remarks>`。结论：**没有发现全称量词，也没有发现支撑不足的一般命题。**
几处值得点名的：

- 第三节「在变异 A 那次运行里，1028 个既有用例中变红的是 …(itemKind: 8) 这一个」——
  限定在那一次运行里，且可枚举。评审者复现该运行，确认既有用例里红的就是这一个。
- 第四节以「只就这次观测说：」开头，随后各句都限定在所列配置上。
- 第五节「这不是新规则」是关于两段具体文本的断言，不是关于一类规则的断言；
  评审者核了两处出处：`ItemSlotRuleService.cs` 的类型 `<remarks>` 第 16–17 行确有那句
  （该文件与 HEAD 逐字一致，故「本轮之前就是这句」成立），
  `ItemPlacementRuleService.cs` 的 `git diff HEAD` 里被删去的那几行确有「the byte and the archive still
  disagree about an avatar -- stored as kind 8, given kind 1 by the equipment index」。两处引文都对得上。
- 判据文件 `<remarks>` 里的「a `CanOccupyMainSlot` that had begun refusing this slot without regard to the
  byte would fail it」与「without it, an occupied set that had covered the bar as well would produce the
  same "no room"」是对**本文件某一句断言**的说明，不是对一类实现的断言，且都与实际断言对得上。
- 第六节存在，列了 5 条「这些实验不足以确定…」，分别是：别的时装模板、改动前的代码在别的输入上、
  别的改法、`ForDailyRefill` 这条路、穿戴与槽号出处。

### 非阻断观察（留给下游完整变更评审，本轮不要求改）

共 3 条，分别是：

1. 第三节可以顺带记一句「变异 B 那次运行里 1028 个既有用例全绿」——评审者复现时看到的正是这个，
   它说明 `BandsFor` 里那个 `AllowsQuickSlot` 分支在本轮之前没有任何既有判据覆盖，是新判据独自扛的。
   现在的写法只说了「变红的只有 1 个」，没点出这层意思。
2. 第六节第二条（「改动前那一侧是字节改写，不是改动前的代码」）可以补上本次评审核到的那一步：
   `WornPieceMetadata` 的另外三个字段是未改动的字面量，`ForGrant` 只读这四个字段，
   所以对这一个模板而言字节改写与改动前代码等价。补上之后「不变」这一条的凭据会强一档。
3. 判据文件里两句「两次 `ForGrant` 相比」的断言单独看偏弱（一个完全不看种类字节的实现也能让它们绿）。
   现有配对已经够用，此处仅作记录。

### 需要的整改

无。


## Controller Disposition D-04 — 接受 QR-04-1 的 `passed`

**判定**：接受。执行结果、自查、QR-04-1 的 `passed` 与被拥有的增量
（单个新判据文件 `AvatarSlotRulesAfterTheKindChangeTests.cs`）指向同一轮。TASK-04 置 `done`。

**一轮通过，值得记下原因**：本任务的派工把前三个任务被评审逐次逼出来的三条**前置**了——
记法口径（不得出现全称量词）、自查判准（逐句问个例还是一类，不用词表搜）、
恒真告诫（每条新判据交付前各做一次变异）。前三个任务里这三条是事后补的，本任务是事前给的。

**一处错在保守那一侧**：执行者把「落点与改动前相同」记为「不足以确定」，
而评审补核后确认它成立——`WornPieceMetadata` 本轮的 diff 只改了种类字节那一个选择，
同一行的 `StackLimit`／`SlotStart`／`SlotEnd` 是未改动的字面量，
`ItemSlotRuleService` 与 `ItemArrivalRuleService` 与基线逐字一致，而 `ForGrant` 只读这四个字段。
**少报不是多报**，是本轮第一次错在这一侧。评审的等价性论证已在 QR-04-1 第 3 节，可被 T-006 引用。

**两条定性**（供 T-006 汇总）：
- 快捷栏拒绝时装 = **变化**
- 到达落点范围 = **查过的不变**（凭据：变异 B 加评审的等价性论证）

**非阻断观察的去向**：QR-04-1 列的三条转 **TASK-06**，其中第 ① 条另见下。
