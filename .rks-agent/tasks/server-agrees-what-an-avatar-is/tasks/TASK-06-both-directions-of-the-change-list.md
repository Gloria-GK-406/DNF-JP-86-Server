# TASK-06 — 行为变化清单，两个方向都扫过

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables:

产出一份完整性由实测支撑的可观察行为变化清单，与一张入场帧字节差异的归因表。

- Referenced spec requirements: RQ-008, RQ-010
- Referenced plan sections: plan.md 的 T-006（Steps 1–4，CK-016..CK-018）；Owned Boundaries 的 BD-006
- Owned boundary:

BD-006 `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（新建）

- Permitted files or interfaces:

- `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`

- Dependencies: TASK-02, TASK-03, TASK-04, TASK-05
- Required dependency outputs: 前四个任务各自的实测与变异结果，含 TASK-04 报出的「一处变化、一处不变」
- Acceptance conditions:

1. **正向扫**：从本轮改动的每一处起因往下游追，列出它触达的每一条判定。
2. **反向扫**：把本轮新增或改变的拒绝与放行路径逐条列出，每条回答它在生产上是否可达、有无判据覆盖。
3. 每条给出位置（**按成员名索引，不按行号**）、生产可达性与判据有无。给出数目时同时列出构成它的各项与计数口径。
4. 比对入场帧的整表字节；有差异则逐处归因到清单里的某一条。**无法归因的差异即为缺陷，不得放过。**

- Task-specific authorization:

Task Unit **不建 Git 检查点、不提交**。接受提交由 Single Spec 工作区控制器在完整评审与新鲜验证之后做**一次**。
变异验证**一律用文件级备份 + `sha256sum` 还原并核散列**；**禁止任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash`）——本仓曾因此丢过未提交的工作。
不得触碰主检出与 `.worktrees/client-debug-lab`。

- Blocking conditions:

**方法义务，不得违反**：反向那一半**不得用「全量判据全绿」代替**——全绿恰恰意味着没有既有用例走到那些新路径上。
上一个成果正是在这里被独立评审拦下，漏掉 13 条新增生产拒绝。
每条「有判据覆盖」的声称都要有一次**变异实测**支撑，**不得只凭 grep 断言**。

- Required methods:

- `test-driven-development`：先让判据红，再让它绿。
- 覆盖主张必须由**变异实测**支撑，**不得只凭 grep 断言**——本仓已两次证明按消息文本或标识符搜索会给出错误的覆盖读数。
- 记法：**不写读者无法证伪的基数**。给出数目时写「N 处，分别是：…」并说明计数口径；按成员名索引，**不按行号**。
- 遇到未预期的失败时用 `systematic-debugging`，先取得证据再改。

- Required checks:

| ID | Check | Expected result |
|---|---|---|
| CK-016 | Non-command：对清单里每一条声称「有判据覆盖」的路径，逐条做一次变异实测 | 每条都能让至少一个判据变红；不能变红的条目改写为「无覆盖」 |
| CK-017 | Non-command：比对入场帧整表的字节 | 要么逐字符不变，要么每一处差异都归因到清单里的某一条 |
| CK-018 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 零失败；用例数相对 2216 的差额逐条归因 |

- Required evidence:

- 清单本身，含正向与反向两节。
- CK-016 每条变异的散列、红/绿结果与被红的判据名。
- 字节锚的比对结果（散列或整表比对输出）与归因表。
- 用例数差额的逐条归因。

- Constraints:

- 分层：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host
- `TreatWarningsAsErrors` 开启，**CS9113（未读的主构造器参数）是错误**
- 不新增协议帧，不改任何帧的字节布局
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单**不得放宽或扩充**
- 领域层 `store` 只指事务结算的真源
- 既有断言的期望值**不得为迁就实现而改写**；唯一例外是正对应 `spec.md` 列出的某条行为变化，且须逐条归因
- 不裁定契约兽槽位 24 还是 25；**不给 `PvfEquipmentSlots.ByName` 补任何键**
- 注释是参考材料不是铁律，判断以代码设计为准；但**改已被证伪的记载要补记修正案**，改「要求」必须停下来问


## Contract Amendment A-06 — 纳入 TASK-02 快审移交的两条覆盖缺口

由控制器在处置 QR-TASK02-R2 时记录。approved Spec 边界未变
（RQ-008 要求清单如实给出「有无判据覆盖」，本条正属其中）。

**新增的验收条件第 5 条**：清单里必须**具名记下**下列两处覆盖缺口，
不得因为相关条目「有判据」就整体写成已覆盖：

1. **（NB-1）「抽取 vs 进程内自增」无覆盖。** 实测：把 `ArrivingAvatar.NewIdentity()`
   换成进程内自增，三条相关判据**全绿**。
   **这一条要紧，因为计划选「抽取不排序」的理由恰恰是「跨重启保持互异」**——
   即本轮真正依赖的那条性质今天零覆盖，缺口只在跨进程重启时显形。
2. **（NB-2）新增用例 `Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`
   自身那四句 `NotEqual` 尚无断言形式的变异凭据**：在已做的两次变异下它都因
   `FillAsync` 的守卫先抛而未走到那四句。「新断言会红」这条性质由往返判据里
   `Assert.NotEqual(landed.Count, first.Core.Value)` 那一句承担。

两条都**不要求改判据**，要求的是**记载口径如实**。


## Contract Amendment A-06b — 纳入 TASK-03 快审暴露的两条，供反向扫使用

由控制器在处置 QR-TASK03-R1 时记录。approved Spec 边界未变（仍属 RQ-008）。

**新增的验收条件第 6 条**，两项都要进反向扫那一节：

1. **一处无覆盖的记载主张。** `SqliteItemDomainStore.FillAsync` 的记载里
   「盲区自我延续」的**后半截**——即「子查询上那条 `avatar_expire_date IS NOT NULL` 过滤
   是它看不见旧行的原因」——**今天零判据覆盖**：
   把该过滤单独去掉（保留左侧 `@expire IS NULL OR`），全部相关判据仍全绿。
   出处：QR-TASK03-R1 的变异 MR-A。**清单里要如实记它无覆盖，不得因为相邻条目有判据就整体写成已覆盖。**
2. **一处爆炸半径。** 把 `FillAsync` 守卫里两个 `avatar_expire_date` 限制**一起**去掉时，
   除对照判据外还会打红两条既有判据，分别是
   `WorldRowSurvivesTheMoveTests.TwoWornPiecesSwappingPositionsEachKeepTheirOwnIdentity`
   与 `WorldRowSurvivesTheMoveTests.AMoveNeitherDeletesNorBuildsAWorldRow`。
   即**这条守卫与搬动通路是连着的**。反向扫时这条通路不得被漏掉。


## Contract Amendment A-06c — 订正 A-06b 第 2 项的措辞

由控制器在处置 QR-TASK03-R2 时记录。**A-06b 第 2 项的说法不够准确，此处订正。**

A-06b 第 2 项写的是：把两个 `avatar_expire_date` 限制**一起**去掉会打红两条搬动判据，
「即**这条守卫与搬动通路是连着的**」。
**「一起去掉」这半句是准确的，但由它得出的因果表述会误导读者**——
它读起来像「动那条过滤就会波及搬动通路」，而实测不是这样。

三角变异对照（出处：QR-TASK03-R1 的 MR-A 与 QR-TASK03-R2 的 MR-B）：

| 变异 | 左侧 `@expire IS NULL OR` | 子查询 `standing.avatar_expire_date IS NOT NULL` | 结果 |
|---|---|---|---|
| MR-A | 保留 | 去掉 | 全绿 |
| MR-B | 去掉 | 保留 | 全绿 |
| M3 | 去掉 | 去掉 | 3 红 |

**订正后的说法**：非时装写入面前是**两道彼此独立的屏障**，
**任一道单独存在即足以放行**；**搬动通路只在两道同时失守时才暴露**。
反向扫记这一条时用这个说法，**不要用「连着的」或任何单因表述**。


## Contract Amendment A-06d — 再次订正爆炸半径的记法，并新增一条结构事实

由控制器在处置 QR-TASK03-R3 时记录。**A-06c 给出的说法仍然过强，此处收窄。**

A-06c 写的是「两道彼此独立的屏障，**任一道单独存在即足以放行**」。
QR-TASK03-R3 证明这句支撑不足：两道屏障**并不对称**。

**T-006 反向扫记这一条时，只写观测，不写一般结论**：

| 变异 | 左侧 `@expire IS NULL OR` | 子查询 `avatar_expire_date IS NOT NULL` | CK-008 观测 |
|---|---|---|---|
| MR-A（QR-TASK03-R1） | 保留 | 去掉 | 320 / 0 |
| MR-B（QR-TASK03-R2） | 去掉 | 保留 | 320 / 0 |
| M3（多方各做） | 去掉 | 去掉 | 317 / 3 红 |

并**明写**：这些变异**不足以**确定哪一道屏障在一般情形下承重；
MR-B 全绿只支撑「CK-008 现有夹具下成立」。

**新增的验收条件第 7 条——一条要进反向扫的结构事实**（证据类型：**代码级读出，非变异实测**）：

> `SqliteItemDomainStore.FillAsync` 守卫的子查询**两侧都不带 `ItemKind` 限制**：
> 候选集是一切 `avatar_expire_date` 非空的旧行，比对键是 `substr(item_core, 6, 4)`。
> 而普通装备的 `Core.Value` 并非恒为 0——`CharacterRosterService.EquipmentCore`
> 给它抽 `Random.Shared.Next(1, int.MaxValue)`，`GameInventoryDelivery.CarriedGoldCore`
> 用它装金币额；判据里 `Sword`／`Ring` 的 `value: 0` 是夹具性质。
> **今天到不了生产**：交付态左侧短路使非时装写入根本不进子查询。

出处：QR-TASK03-R3 的 B-2。记它时要标明证据类型与不可达性，**不得写成一次实测结论**。


## Contract Amendment A-06e — 纳入 TASK-04 快审的一条覆盖事实

由控制器在处置 QR-04-1 时记录。仍属 RQ-008。

**新增的验收条件第 8 条**：反向扫要记下这条**覆盖事实**——

> 变异 B（把 `ItemArrivalRuleService.BandsFor` 里的 `|| !ItemSlotRuleService.AllowsQuickSlot(...)`
> 去掉）在本轮新判据之前，**1028 个既有用例全绿**。
> 即 `BandsFor` 的这条分支此前**没有任何既有判据覆盖**，是本轮新判据第一次钉住它。
> 出处：QR-04-1 非阻断观察第 ① 条。

另两条非阻断观察（第六节可补记等价性论证使「不变」的凭据更强；
两句「两次 `ForGrant` 相比」的断言各自与钉死具体值的断言配对）**记为已知、不要求改判据**。


## Contract Amendment A-06f — 纳入 TASK-05 快审的一条量具缺口

由控制器在处置 QR-05-A 时记录。仍属 RQ-008。

**新增的验收条件第 9 条**：反向扫**单列**下面这条量具缺口——

> 时装拖拽若把目标列写成 `InventoryContainerType.Avatar`（=1）而非 `Equipment`（=3），
> 请求在到达 `PutOnAsync` 之前即于 `ListNotServed` 处走单参 `Refused`，
> 记录读作 `item 0 …->N refused as ListNotServed`：**槽号仍在，模板号丢失**。
> 出处：QR-05-A。**证据类型：代码路径读出，非实机观测**——
> 真实客户端写哪个列号尚未观测，这正是实机要回答的问题之一。

另记一条已知限制：`TakeOffAsync` 的三个拒绝返回全走单参 `Refused`，
走那条路径时 `{ItemId}` 是 0。


## 执行结果（TASK-06）

**本任务只写了一个文件**：`.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（新建，BD-006）。
未改动任何生产代码或判据；变异期间的临时改动逐次由文件级备份还原并核散列（见下）。
未建 Git 检查点、未提交；**未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash` 一次未出现）；
未触碰主检出与 `.worktrees/client-debug-lab`。

### 一、清单的规模

- **正向扫 48 条**：起因 A（`PvfEntryLifecycleContentSource.WornPieceMetadata`，时装种类 1→8）下 35 条；
  起因 B（`ItemContainers.ArrivingAvatar` ＋ `StoreBackedItemContainer.LandAsync`）下 5 条；
  起因 C（`StoredItem.AvatarDetail` ＋ `SqliteItemDomainStoreSession.FillAsync`）下 3 条；
  起因 D（`MoveItemSpaceSlice` 的两个拒绝点与拒绝日志）下 5 条。
  计数口径：一条 = 一个被起因触达的判定点（按成员名；同一成员内多处判定分别计；跨起因的重复判定点只计一次）。
- **反向扫 28 条**：变严或新增的拒绝 16 条（RV-01..RV-16）、变松或消失的拒绝 3 条（RV-17..RV-19）、
  新增或改变的放行 7 条（RP-01..RP-07）、量具缺口 2 条（RG-01、RG-02）。
  计数口径：一条 = 一个「路径 × 方向」。
- 反向扫里写作「**有覆盖**」的 **14 条**，写作「**无覆盖**」的 **8 条**（另加第五节的 NB-1，共 9 条），
  写作「**未测定**」的 **7 条**。三个数相加 29，比 28 行多的一行是 RP-06（它在两个方向上各有一个判断，被计两次）。

### 二、CK-016 —— 逐条变异实测

方法：文件级备份（会话 scratchpad）＋ `sha256sum` 还原并核散列；每次变异后跑同一条全量命令
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`。
交付态基线：**2230 通过 / 2 跳过 / 0 失败，EXIT=0**。

| ID | 位置（成员名） | 变异后散列 | 该次结果 | 变红的判据 |
|---|---|---|---|---|
| MU-1 | `PvfEntryLifecycleContentSource.WornPieceMetadata`（`Avatar => AvatarItemKind` → `=> WornPieceItemKind`） | `a1e8a7c4968dd74a03f1ba4335514379d73a2f2bce4af1a06012939314fd6781` | 失败 7 | `GrantedAvatarIsWholeTests` 三条 ＋ `AvatarSlotRulesAfterTheKindChangeTests` 四个用例 |
| MU-2 | `ItemSlotRuleService.AllowsQuickSlot`（去 `or KindAvatar`） | `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864` | 失败 5 | `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)` ＋ `AvatarSlotRulesAfterTheKindChangeTests` 四个用例 |
| MU-3 | `ItemArrivalRuleService.BandsFor`（去 `\|\| !AllowsQuickSlot(...)`） | `3593447673aa86a3a6a9a96c9604e19c4cce7c6fa8546d6fce90535963f00d4e` | 失败 1 | `AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` |
| MU-4 | `StoreBackedItemContainer.LandAsync`（去身份抽取） | `cd388e911834331b48da322e29085511307647ac7be3e9e9725c412d381ee46b` | 失败 2 | `GrantedAvatarIsWholeTests.A_granted_avatar_is_loaded_saved_and_loaded_again_with_the_same_facts`（纯断言红，`Assert.NotEqual() Failure`，line 146）、`.Two_granted_avatars_carry_two_drawn_identities_rather_than_two_counts`（异常红，`The world took the item and named no identity.`，抛在 `GrantAsync` line 250） |
| MU-5 | `LandAsync`（`Facts()` → `null`） | `6222be998e9fe2354c8df8c4e5be7ec67aad7a9e88a4be120ca059324a0c3948` | 失败 2 | 同 MU-4 那两条 |
| MU-6 | `SqliteItemDomainStoreSession.FillAsync` 守卫左侧（`@expire IS NULL` → `1 = 1`） | `a826cf2e0764926078899549f804db946661a8847e1442b23238bfeec62d746b` | 失败 3 | `TwoAvatarsDoNotShareOneIdentityTests` 两条 ＋ `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` |
| MU-7 | `FillAsync` 的 `facts` 取值（写入自带的到不了 INSERT） | `e959bb14bc86f158911bc729919aa3b98745209c7f7605b679a91d4c9a970219` | 失败 7 | Persistence 4 条（`SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` ＋ `TwoAvatarsDoNotShareOneIdentityTests` 三条）、Application 2 条（`GrantedAvatarIsWholeTests` 两条），另 1 条既知偶发（下注） |
| MU-8 | `MoveItemSpaceHandler.ResolveAsync` 拒绝日志（`result.ItemId` → `0`） | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | 失败 2 | `RefusedEquipIsRecordedWithItsTemplateTests` 两条整句比对判据 |
| MU-9 | `PutOnAsync` 的 `NotWearable`（双参 → 单参） | `1308ad14e620007adaa6a5edd3022e4f8f5837142ca892b524e4fe943e810c58` | 失败 1 | `An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| MU-10 | `PutOnAsync` 的 `ItemCannotOccupySlot`（双参 → 单参） | `17ecabfeb72050aadc6ae7908188223e00083194cf4f75f691e94046e79a43f3` | 失败 1 | `A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` |
| MU-11 | `InventoryEntryAssembly.WithAvatarDetail` 的 `Core.Value <= 0` 拒绝（停用） | `d9aa6bfde2f5700563a913b4076a7d43b5aeebf92c2067aba45ef1febc704d71` | **失败 0 / 通过 2230** | 无 → 记作**无覆盖** |
| MU-12 | `WithAvatarDetail` 的畸形拒绝（停用） | `a3057a4b87336373d9253901233c658d24b83db94e6220dc631e5ec8294124a4` | **失败 0 / 通过 2230** | 无 → **无覆盖** |
| MU-13 | `InventoryEntryAssembly.SaveAsync` 脏租约时装扫描（停用） | `2843a41cc85007200dbe99b3a72d6754308dbb0a354d7b1be89033a096bd8c3a` | **失败 0 / 通过 2230** | 无 → **无覆盖** |
| MU-14 | `SaveAsync` 档案／行种类不符拒绝（停用） | `b5806850e0f8b414618861983e0edad66db2cf9cc45b1e7be1b9b5ccd5dda37d` | 失败 1 | `ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` |
| MU-15 | `StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 第三字段 | `bff6c3b4aedb74af926ec4e5ef9f8c57f508dd88eb3fbd654ba35bca96c5be3d` | 失败 1 | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`；**字节锚未红** |
| MU-16 | `GameInventoryDelivery.WriteItem` 第三字段 | `976d98c00749f56f963aec08600b9e5182a447e34afd31e48eac74d96f394054` | 失败 1 | **字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**，该次只有它红 |
| MU-17 | `GameInventoryDelivery.WriteItem` 缺事实拒绝（停用） | `597a2e3608d249c1aae783bc9b0454ce250cae90f7631e7cfb386ebf4680460e` | 失败 1 | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_HighRiskAvailabilityDistinctionsAreNotZeroFilled` |
| MU-18 | `ArrivingAvatar.NewIdentity`（抽取 → 进程内自增 `Interlocked.Increment`） | `57afb061fb23c5630a93877df97b924950d3173483f50da05c1494ebedb841f1` | **失败 0 / 通过 2230** | 无 → **无覆盖**（A-06 的 NB-1，本轮亲自复现） |
| MU-19 | `GameInventoryDelivery.WriteItem` 珠宝槽长度拒绝（停用） | `13776f94f023949821059a7f90700f3a90fc47e0d5c4ab22adf196e6fdcd9f35` | **失败 0 / 通过 2230** | 无 → **无覆盖** |

另有两次过滤运行：MU-4 之后为取失败形态跑
`--filter "FullyQualifiedName~GrantedAvatarIsWholeTests"`（失败 2 / 通过 1，形态见上表 MU-4 行）；
MU-7 之后为核既知偶发跑 `--filter "…CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold"`，
在同一份变异态上得「失败 0 / 通过 1」。既知偶发判别是「异常类型 ＋ 栈根 ＋ 复跑即绿」，
本轮只取到「复跑即绿」这一项，未取到异常类型与栈根，故只记观测不下判断。

**还原**：19 次变异逐次由备份 `cp` 还原并 `sha256sum -c`，九个文件末态逐一 `OK`：

```text
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da  PvfEntryLifecycleContentSource.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db  ItemContainers.cs
0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177  ItemSlotRuleService.cs
c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba  ItemArrivalRuleService.cs
7e9f4678af319acc514afd0d2ee179bc3f7325e6aaf16ac63fa62b6baf9b5349  InventoryEntryAssembly.cs
87f22d2cd107eec558a542e14cd8e9a93d0300bb42c358a898a7a8469a9c87fe  SqliteItemDomainStore.cs
d96cdb0127c277610a30b672567053317ebade93367a2685d0aa7ecc7b9550d9  MoveItemSpaceSlice.cs
18c22acf2209378aa5e947899d8559f7385dc976aae895dac1e99f67f6ff2dd5  GameInventoryDelivery.cs
be1fd3acb3970d6c5048eb6401eff6170706734045db023726c058022ecde752  StateSynchronizationPayloadCodec.Entry.cs
```

`git status --porcelain` 本轮前后均为 16 行（11 个 ` M` ＋ 5 个 `??`），逐行一致。

### 三、CK-017 —— 入场帧的整表字节

**比对结果：逐字符不变，无差异需要归因。**

- 字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedKnightFrameSha256` 是一张 62 个 SHA-256 的字面量表，
  由 `AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` 与当前转录逐帧 `Assert.Equal`。
- 该文件本轮未被改动：`git diff HEAD --stat -- <该文件>` 输出为空；
  工作副本 `sha256sum` 与 `git show HEAD:<该文件> | sha256sum` 同为
  `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。
- 该判据在交付态下通过（本轮亲跑过滤运行 11 条通过 / 0 失败，EXIT=0；三次交付态全量里 Protocol 依次 524／0）。

**归因表（四处起因为什么一处字节也没动，证据类型均为代码级读出）**：

| 起因 | 为什么到不了这 62 帧 |
|---|---|
| A `WornPieceMetadata` | 转录链是 `EncodeThroughRegisteredEncoders(SelectedCharacterStateMessageFactory.Create(result))`，`result` 由 `SelectCharacterPayloadTestData.CreateComplete()` 给出；链上没有档案，物品行的 `ItemKind` 由夹具的 `Core(8, 2002, 9001)` 等直接给出 |
| B `ArrivingAvatar` ＋ `LandAsync` | 同一条链上没有容器（`StoreBackedItemContainer`／`IItemContainer` 不参与） |
| C `StoredItem.AvatarDetail` ＋ `FillAsync` | 同一条链上没有存储（`IItemDomainStore`／`SqliteItemDomainStoreSession` 不参与） |
| D `MoveItemSpaceSlice` | `MoveItemSpaceResponse` 不在 `AcceptedStaticIdentities` 的 62 个身份里；且拒绝分支的响应由 `MoveItemSpaceResponse.Failure(message, ErrorCodeFor(result.Status))` 造出，`result.ItemId` 只出现在同分支的 `LogInformation` 实参里 |

**锚的敏感性（说明这个「不变」不是空判）**：MU-16 让字节锚变红且该次只有它红；MU-15 没让字节锚变红。
即这张表在背包侧的时装编码上有反应，在 NOTI-2 支援装备那一支上没有反应。

### 四、CK-018 —— 全量输出与相对 2216 的逐条归因

```text
已通过! - 失败: 0，通过:   524，已跳过: 0，总计:   524 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败: 0，通过:    60，已跳过: 0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败: 0，通过:    52，已跳过: 0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败: 0，通过:   320，已跳过: 0，总计:   320 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败: 0，通过:     6，已跳过: 0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败: 0，通过:   236，已跳过: 2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败: 0，通过:  1032，已跳过: 0，总计:  1032 - Dfo.Server.Application.Tests.dll (net10.0)
EXIT=0
```

合计 **2230 通过 / 2 跳过 / 0 失败，退出码 0**。两条跳过仍是
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`。

相对 2216 的差额 **+14**，逐条归因（计数口径：`dotnet test` 每个 dll 那一行的「总计」）：

| 项目 | 2216 基线 | 现在 | 差 | 构成它的各项 |
|---|---|---|---|---|
| Protocol | 521 | 524 | +3 | `RefusedEquipIsRecordedWithItsTemplateTests` 三条（TASK-05） |
| Networking | 52 | 52 | 0 | —— |
| Pvf | 60 | 60 | 0 | —— |
| Host | 6 | 6 | 0 | —— |
| Persistence | 316 | 320 | +4 | `SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`（TASK-01，1）＋ `TwoAvatarsDoNotShareOneIdentityTests` 三条（TASK-03） |
| Infrastructure | 236（另 2 跳过） | 236（另 2 跳过） | 0 | —— |
| Application | 1025 | 1032 | +7 | `GrantedAvatarIsWholeTests` 三条（TASK-02）＋ `AvatarSlotRulesAfterTheKindChangeTests` 四个用例（TASK-04：三个 `InlineData` ＋ 一个 `Fact`） |
| **合计** | **2216** | **2230** | **+14** | 3 ＋ 4 ＋ 7 = 14，与派工给的三处项目变动逐项目一致 |

交付态的三次全量运行（开工基线、MU-18 还原后、MU-19 还原后的收尾）读数相同：2230 / 2 / 0，EXIT=0。
`ObjectDisposedException` 在这三次运行的输出里未出现。


## 自查（TASK-06，逐条对照契约验收条件与六份修正案）

### 契约的四条验收条件

1. **正向扫** —— 达成。清单第二节，48 条，按四个起因分表，逐条给出判定点的成员名、
   本轮它对一件时装的答案变了没有、以及凭据。计数口径写在表头。
   凭据：起因 A 的下游由 MU-1／MU-2／MU-3 三次变异分别打红 7／5／1 条判据；
   起因 B 由 MU-4／MU-5／MU-6 打红；起因 C 由 MU-7 打红；起因 D 由 MU-8／MU-9／MU-10 打红。
2. **反向扫** —— 达成。清单第三节，28 条，分四张表（新增／变严的拒绝、变松／消失的拒绝、
   新增／改变的放行、量具缺口），每条一栏「生产可达性」、一栏「判据有无」、一栏「凭据」。
   **没有用「全量判据全绿」代替这一步**：第三节开头写明这一点，且 14 条「有覆盖」逐条点名到一次具名变异；
   MU-11／MU-12／MU-13／MU-19 四次「全绿」在清单里被读作**无覆盖**而不是「已覆盖」。
   上一个成果被拦下的那类漏项在这里逐条列出：RV-02、RV-03、RV-04（搬动与取下通路上的新拒绝）、
   RV-06、RV-07、RV-09、RV-11（装载与投递侧新到得了的四条拒绝）、
   RV-12、RV-13（NOTI-2 那一支的两条）、RV-14（幸存者匹配）、RV-18、RV-19（两条变松的），
   加上 RV-01、RV-05、RV-08、RV-10、RV-15、RV-16 六条与 RG-01、RG-02 两条量具缺口。
3. **每条给位置、可达性、覆盖；给数目时同时列出各项与计数口径** —— 达成。
   清单里按成员名索引，未用行号索引。出现的每个基数都写成「N 条，分别是：…」并附计数口径：
   起因 4 处、正向 48 条（35＋5＋3＋5）、反向 28 条（16＋3＋7＋2）、
   有覆盖 14／无覆盖 8（＋NB-1）／未测定 7、变异 19 次、用例差额 +14（3＋4＋7）。
4. **入场帧整表字节比对，有差异逐处归因** —— 达成，见上文 CK-017 一节：**逐字符不变，无差异需要归因**，
   并另附四处起因为什么到不了这 62 帧的归因表，以及两次锚敏感性实测（MU-16 红、MU-15 未红）。

### 六份修正案，逐条

- **A-06（1）NB-1** —— 达成。清单第五节 NB-1 具名记下：本轮亲自把 `ArrivingAvatar.NewIdentity()`
  换成进程内自增（MU-18，变异后散列 `57afb061…`），观测到全量 **失败 0 / 通过 2230**。
  并写明这一条要紧的理由：本轮选「抽取不排序」的理由正是「跨重启保持互异」，
  而这条性质按本清单的定义记作**无覆盖**。修正案写的是「三条相关判据全绿」，本轮把观测面扩到了全量。
- **A-06（2）NB-2** —— 达成。清单第五节 NB-2 记下那四句 `NotEqual` 至今没有断言形式的变异凭据，
  并附本轮亲自取到的失败形态：MU-4 下该用例以异常红（`The world took the item and named no identity.`，
  抛在 `GrantAsync` line 250），四句 `NotEqual` 在其后、没有走到；
  「新加的断言会红」由往返判据 line 146 的 `Assert.NotEqual` 承担，MU-4 下它是纯断言红。
  出处（QR-TASK02-R2 的 NB-2）已标注为非本轮实测的那一半。
- **A-06b ＋ A-06c ＋ A-06d** —— 按 **A-06d（最终版）** 记，达成。清单第六节**只写观测三角表**
  （MR-A 保留左侧去子查询 → 320 / 0；MR-B 去左侧保留子查询 → 320 / 0；M3 两者都去 → 317 / 3 红），
  逐行标出施加方与出处，并**明写**「这些变异不足以确定哪一道屏障在一般情形下承重」。
  清单里没有出现「唯一」「任一…即足以」「只有当…才」这类说法——
  自查判准不是词表，是逐句问「这句在说个例还是说一类」，施用结果见下一小节。
  A-06b 第 1 项（「盲区自我延续」后半截无覆盖）在第六节末尾如实记下并注明出处 QR-TASK03-R1 的 MR-A。
- **A-06d 的结构事实（验收条件第 7 条）** —— 达成。清单第六节末单列，
  抬头即写 **证据类型：代码级读出，非变异实测**，出处标 QR-TASK03-R3 的 B-2；
  三件读出（守卫子查询两侧都不带 `ItemKind` 限制；`CharacterRosterService.EquipmentCore` 抽随机数；
  `GameInventoryDelivery.CarriedGoldCore` 装金币额）由本轮直接读取工作区文件核对；
  采用控制器 D-03c 认可的措辞「**并非恒为 0**」；并另起一段写明**今天到不了生产**
  （交付态左侧短路使非时装写入不进子查询）。
- **A-06e** —— 达成。清单第五节单列：变异 B（去掉 `BandsFor` 的 `|| !AllowsQuickSlot(...)`）
  在本轮新判据之前 **1028 个既有用例全绿**，即该分支此前无既有判据覆盖，出处 QR-04-1 非阻断观察第 ① 条；
  并附本轮亲做的 MU-3（同一处同一改法，散列 `3593447673…` 与 TASK-04／QR-04-1 所记逐字符相同，失败 1）。
  QR-04-1 另两条非阻断观察在同一节记为已知、不要求改判据。
- **A-06f** —— 达成。清单第 3.4 节把这条量具缺口**单列**为 RG-01：
  目标列写成 `Avatar`(=1) 而非 `Equipment`(=3) 时，请求在 `MoveItemSpaceCommandHandler.HandleAsync` 的
  `ListNotServed` 处更早被拒，记录读作 `item 0 …->N refused as ListNotServed`，**槽号仍在、模板号丢失**；
  并标明 **证据类型：代码路径读出，非实机观测**，出处 QR-05-A。
  `TakeOffAsync` 三个拒绝返回走单参 `Refused`、`{ItemId}` 为 0 的已知限制记为 RG-02，逐个列出那三个返回。

### 记法口径的自查（不用词表）

判准（D-03c）：写完后逐句问「这句在说一个个例，还是在说一类？」。施用结果：

- **改写过的句子（7 处，分别是）**：
  ① 覆盖三词的定义由「凡是…」改为直接写死三个定义；
  ② 3.5 节「每一条都对应…」改为「逐条在第四节能找到…」；
  ③ MU-1 行「Protocol 与 Persistence 该次均报 0 失败」改为分开两句；
  ④ 7.1「三次全量运行里 Protocol 均为 524」改为「依次为 524／0、524／0、524／0」；
  ⑤ 第八节「三次…均为 2230」改为「依次为…，三次相同」；
  ⑥ 7.2 两处「这张表对…敏感／不敏感」改为「这张表在 MU-16／MU-15 这一处改动上有反应／没有反应」；
  ⑦ NB-1 的「判据集里没有一条重启场景的用例」改为「MU-18 那次运行是在一个进程里跑的，没有把这个分别照出来」。
- **改口为代码级读出的（3 处）**：RV-05 生产可达性一栏（原文把短路当成成因陈述，现标为代码级读出，
  并把「哪一道屏障承重」指回第六节的「明写未确立」）；起因计数口径那一段（现附本轮亲自复算的行数）；
  A-27 的凭据栏（由「MU-16 让字节锚变红」改为「本轮未对这一块单独施加变异…记作未测定」）。
- **改归类的（3 处）**：RG-01、RG-02 由「无覆盖」改为「未测定」（本轮未对它们施加变异）；
  A-27 由借用 MU-16 改为「未测定」。
- **本清单里说「一类」的句子**，处理为三种情形：一是**代码级读出**（标明证据类型、附文件与成员名，
  并写明不由其推出一般命题）；二是对**可枚举的字面表**的计数（`PvfEquipmentSlots.ByName` 的 16 条／
  以 `avatar]` 结尾的 0 条；`SelectCharacterPostEntryServices` 那个字面集合）——读者可自行复算；
  三是**引用他人写下的句子**，逐处标为引文并注出处（A-06e 的「1028 个既有用例全绿」、
  A-06d 的三角表、A-06f 的记录形状），不作为本清单自己的主张。
- **引文核对**：本清单引到的每一处外部说法都注了出处到持久记载的小节名
  （TASK-01 执行结果、TASK-02 执行结果与 QR-TASK02-R2、QR-TASK03-R1／R2／R3、QR-04-1、QR-05-A）。
  本轮**没有把派工指令当作持久记载引用**。

### 硬约束核对

- **只写了 `behavior-change-list.md` 一个文件**（本 Task 文件的这两个新小节除外，它是契约要求的交付记录）；
  未改动任何生产代码或判据；变异期间的临时改动 19 次逐次还原并核散列，末态九个文件 `sha256sum -c` 逐一 `OK`。
- **未建 Git 检查点、未提交**；**未使用任何 git 还原类命令**；本轮跑过的 git 命令只有只读的
  `git status --porcelain`、`git diff`（含 `--numstat`／`--stat`／`-U0`）、`git rev-parse`、`git show HEAD:<path>`。
- 未新增协议帧、未改任何帧的字节布局（CK-017 的字节锚逐字符不变即是实测面）。
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 随交付态全量绿，未被改动。
- 未给 `PvfEquipmentSlots.ByName` 补任何键；未裁定契约兽槽位 24／25。
- 未触碰主检出与 `.worktrees/client-debug-lab`。
- 不写读者无法证伪的基数：清单里每个数目都附了计数口径与构成它的各项。
- **无阻塞。**


## Independent Quick Review — QR-06-1

- **判定：`changes_required`**
- 评审的契约版本：Contract revision 1 ＋ 六份修正案 A-06、A-06b、A-06c、A-06d、A-06e、A-06f（全集）
- 评审的 owned diff：`git status --porcelain` 16 行（11 个 ` M` ＋ 5 个 `??`），HEAD = `565d33fee899556476f8a582605fb4f439261c0f`，未提交。
  本任务写下的文件是 `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（506 行，未跟踪目录内）。
- 评审者是与执行者不同的上下文，fresh context；本轮只读了 TASK-06 文件、清单、TASK-01..05 的相关小节与工作区源码；
  未读 `spec.md`／`TaskContract.md`／`findings-*.md`。

### 一、本轮亲跑的命令与观测

同一条命令，七次：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 次 | 施加的变异 | 变异后散列 | 与执行者所记散列 | 观测 |
|---|---|---|---|---|
| 1 | 无（交付态） | —— | —— | 524/60/52/320/6/236（另 2 跳过）/1032 = **2230 通过 / 2 跳过 / 0 失败，EXIT=0** |
| 2 | MU-14 复现：`InventoryEntryAssembly.SaveAsync` 的 `worldClass == ItemWorldClass.Avatar && item.Core.ItemKind != 8` → `== 88` | `b5806850e0f8b414618861983e0edad66db2cf9cc45b1e7be1b9b5ccd5dda37d` | **逐字符相同** | 失败 1，红的是 `Dfo.Server.Persistence.Tests.Inventory.ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot`（`Assert.Throws() Failure: No exception was thrown`） |
| 3 | MU-8 复现：`MoveItemSpaceHandler.ResolveAsync` 拒绝日志第三实参 `result.ItemId` → `0` | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | **逐字符相同** | 失败 2，红的是 `RefusedEquipIsRecordedWithItsTemplateTests` 的 `A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` 与 `An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| 4 | MU-16 复现：`GameInventoryDelivery.WriteItem` 第三个字段三元 → `core.Value` | `976d98c00749f56f963aec08600b9e5182a447e34afd31e48eac74d96f394054` | **逐字符相同** | 失败 1，红的**只有**字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline` |
| 5 | MU-13 复现：`SaveAsync` 脏租约时装扫描 `.Where(value => value.Core.ItemKind == 8)` → `== 88` | `2843a41cc85007200dbe99b3a72d6754308dbb0a354d7b1be89033a096bd8c3a` | **逐字符相同** | **失败 0 / 通过 2230 / 跳过 2，EXIT=0** |
| 6 | MU-15 复现：`StateSynchronizationPayloadCodec.Entry.WriteNoti2EquippedItem` 第三个字段 → `unchecked((uint)core.Value)` | `bff6c3b4aedb74af926ec4e5ef9f8c57f508dd88eb3fbd654ba35bca96c5be3d` | **逐字符相同** | 失败 1，红的是 `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`；字节锚未红 |
| 7 | 全部还原之后 | —— | —— | **2230 通过 / 2 跳过 / 0 失败，EXIT=0** |

还原方式：文件级备份 `cp` ＋ `sha256sum` 核对；**本轮未使用任何 git 还原类命令**。
九个文件在开工与还原后的散列，与清单第〇节那张表逐行相同；`git status --porcelain` 前后均为 16 行。

即：抽查的三条「有覆盖」（RV-17／MU-14、RP-07／MU-8、RP-06 编码分支／MU-16）**逐条复现变红**；
抽查的一条「改写为无覆盖」（RV-06／MU-13）**复现全绿**；MU-15 的「字节锚未红」也复现。
六个散列与执行者所记逐字符相同，说明施加的是同一处同一改法。

### 二、逐点核对结果

**「有覆盖」的凭据 —— 通过。** 见上表第 2／3／4 行。

**四条「改写为无覆盖」的重分类 —— 通过。** MU-13 复现为 0 失败，「无覆盖」这个读数正确。

**证据类型标签 —— 通过。** 逐条核过：A-06d 的结构事实在第六节末，抬头写着
「证据类型：代码级读出，不是变异实测的结论」，并另起一段写「今天到不了生产」；
A-06f 的 RG-01 写着「证据类型：代码路径读出，非实机观测」，RG-02 写着「证据类型：代码级读出」；
A-27 由借用 MU-16 改为「未测定」；RV-18／RV-19 判据栏「未测定」、凭据栏「代码级读出」。未见混标。

**记法口径 —— 通过（逐句读，未用词表）。** 未见全称量词；第六节写着「这些变异不足以确定哪一道屏障
在一般情形下承重」，第九节列出六条「不足以确定」。引文出处（QR-04-1 非阻断观察第 ① 条的
「1028 个既有用例全绿」）本轮回到 TASK-04 文件核对，逐字相符。

**字节锚的敏感性论证 —— 成立，但只在它自己声明的范围内。**
MU-16 那次只有字节锚一条红，本轮复现，所以这张表在 `GameInventoryDelivery.WriteItem` 的 kind 8 分支上
确实不是惰性的。但它证明的是**编码器那一处**，不是四处起因；清单第 7.4 节自己已经写明
「它不足以确定一件发放来的时装出现在背包里时入场那一批帧会长什么样」，也写明 MU-15／MU-16 各是一种改法。
在这个范围内论证成立，记为非阻断。

**计数归因 —— 通过，本轮独立复算。**
+14 的构成由判据文件本身可复算（口径：`[Fact]`／`[SkippableFact]` 各计 1，
`[Theory]`／`[SkippableTheory]` 按其 `[InlineData]` 条数计）：
`AvatarSlotRulesAfterTheKindChangeTests` 一个 `[SkippableTheory]` 带三个 `[InlineData]` ＋ 一个
`[SkippableFact]` = 4，`GrantedAvatarIsWholeTests` 三个 `[SkippableFact]` = 3，Application 合计 +7；
`TwoAvatarsDoNotShareOneIdentityTests` 三个 `[Fact]` ＋ `SqliteItemDomainStoreTests` 的 diff 里新增的一个
`[Fact]`（`AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`）= 4，Persistence +4；
`RefusedEquipIsRecordedWithItsTemplateTests` 三个 `[Fact]` = 3，Protocol +3。
`ItemClassificationTests.cs` 的 diff 里没有增删任何 `[Fact]`／`[Theory]`／`[InlineData]`，与「+0」一致。
本轮全量读数 2230/2/0 与清单第八节所记相同。

另核过三条可证伪的记载，都成立：
`PvfEquipmentSlots.ByName` 的字典字面量是 16 个 `["…"]` 键，其中以 `avatar]` 结尾的 0 个；
`AcceptedKnightFrameSha256` 的字面量表是 62 个 SHA-256 行；
`ItemPlacementContent.cs`／`IItemPlacementRuleService.cs`／`ItemPlacementRuleService.cs`／`InventoryEntryAssembly.cs`
四个文件的 `git diff HEAD -U0` 里，除两行文件头外的 `+`／`-` 行全部以 `///` 开头
（分别 20/20、11/11、14/14、16/16）——「只改了记载」这句成立。

### 三、阻断项：反向扫漏掉了一条本轮改变的放行与一条本轮新增的拒绝

**这是第 29 条，也是本判定的唯一阻断理由。**

起因 B 把落地 core 的 `Value` 由 `opened.Count` 换成 `ArrivingAvatar.NewIdentity()`。
清单的 B-01 与 RP-02 都写下了「此前那一位是 `opened.Count`」，但两个方向的扫描都**没有再问一句
「这一位原来的读者是谁」**。答案在同一个程序集里，链条只有两步，都是代码级读出：

1. `ContainerSlotItem.Count => Core.Value`
   （`Server/src/Dfo.Server.Abstractions/Inventory/IItemContainer.cs`，成员 `ContainerSlotItem.Count`，
   它自己的注释写的是「How many the slot holds. For anything that does not stack, one.」）。
2. `StoreBackedItemContainer.DecideMoveAsync`
   （`Server/src/Dfo.Server.Application/Inventory/ItemContainers.cs`）里
   `var moveCount = requestedCount <= 0 || requestedCount > moving.Count ? moving.Count : requestedCount;`，
   随后两处判定读它：目标格空时 `moveCount == moving.Count ? ContainerMoveKind.Relocate : ContainerMoveKind.Split`，
   目标格有别的东西时 `if (moveCount != moving.Count) return (null, ItemMoveRefusal.PartialSwapNotAllowed);`。

于是，**在背包里拖动一件发放来的时装、目标格落在它自己的波段（9..64）内**时：

- **RX-01（本轮改变的放行）**：改动前 `moving.Count` 是 1（`Value` 就是 `opened.Count`），
  客户端请求的 `MoveCount` 也是 1（`MoveItemSpaceDecoder` 从 `body[7..]` 读 4 字节整数；
  `MoveItemSpaceSlice` 自己的 `<remarks>` 抄下的实机请求
  `00 09 00 40 38 00 00 01 00 00 00 03 0F 00` 在那四个字节上是 `01 00 00 00`），
  两者相等 → 走 `Relocate`。改动后 `moving.Count` 是抽取出来的身份
  （`Random.Shared.Next(1, int.MaxValue)`），与 1 不等 → 改走 **`Split`**。
  `ItemContainerOperations.SplitAsync` 会把源格重写成
  `moving.Core with { Value = moving.Count - moveCount }`（身份被减 1），
  并在目标格写下 `StackableItemCore.Create(moving.ItemKind, moving.ItemId, moveCount)` ——
  一行 kind 8、`Value` 为 1、**不带任何事实**的新行（`PutAsync` 那一路把 `avatarDetail` 传 null）。
  该行在 `SqliteItemDomainStoreSession.FillAsync` 里 `item.AvatarDetail` 为 null、`carried` 也取不到，
  六列落成 NULL；下一次入场装载时 `InventoryEntryAssembly.WithAvatarDetail` 的 `detail is null` 分支
  （即清单的 RV-08）会抛 `Avatar detail 1 is unavailable.`。
- **RX-02（本轮新增的拒绝）**：目标格有别的东西且不是同一模板时，改动前
  `moveCount == moving.Count`（都是 1）→ 走 `Swap`；改动后不等 → 返回
  **`ItemMoveRefusal.PartialSwapNotAllowed`**，经 `StatusFor` 变成一次拒绝。
  这条拒绝在本轮之前对一件时装是走不到的。

**生产可达性**：与清单自己给 RV-02／RV-03 写下的理由相同——`AddAsync` 把发放来的时装落在自有段，
`MoveItemSpaceCommandHandler.HandleAsync` 的 Main→Main 一支调 `bag.MoveToAsync`。
区别只在目标格：RV-02／RV-03 说的是目标落在 3..8，RX-01／RX-02 说的是目标落在 9..64。

**判据有无**：本轮六次运行里没有一条判据碰过这条路径——上表逐条列出的红里没有一条经
`MoveToAsync` 或 `DecideMoveAsync`；执行者的 MU-2 那次五条红同样没有（清单在 RV-02／RV-03 的凭据栏
已经这么记了）。按清单第〇节写死的定义，这两条应记作「未测定」（本轮未对 `DecideMoveAsync` 施加变异）。

**为什么这是阻断项**：契约验收条件第 2 条要求「把本轮新增或改变的拒绝与放行路径逐条列出」，
Blocking conditions 又写明上一个成果正是在反向扫的遗漏上被拦下。
RX-01／RX-02 是起因 B 直接改变的一条放行判定与直接新增的一条拒绝判定，
两者都不在反向扫的 28 条里，也不在正向扫的 48 条里（起因 B 下的 B-01..B-05 停在 `FillAsync`，
没有往容器动词方向追）。这与上次被拦下的是同一类遗漏：
**只顺着「新写进去的那个值去了哪张表」往下追，没有问「这个位置原来的读者是谁」**。

**这一条另有一个不止于记载的后果**：RX-01 描述的是一次普通的背包整理会给库里落下一行
kind 8、无事实的行，而下一次入场装载会在 `WithAvatarDetail` 抛出。
本 Task 的 owned boundary 只有清单文件，所以 TASK-06 内的整改是**如实记下这两条**；
是否要动生产代码由控制器裁定，不在本任务边界内。

### 四、非阻断观察（记录，不要求本轮改）

共 4 条，分别是：

1. **同一起因、同一条链上的另一处**：`ItemContainerOperations.ConsumeAsync` 里
   `var remaining = item.Count - count;` 与 `if (remaining == 0) ClearAsync(...)`。
   对一件 `Value` 是身份的时装，消耗 1 之后 `remaining` 不为 0，于是走
   `PutAsync(… Value = remaining)` 而不是清空。证据类型：代码级读出；
   这条路径对时装是否可达本轮未追，故不并入阻断项。
2. **一条本轮新到得了的库级拒绝未列**：`items` 表上
   `avatar_jewel_socket BLOB CHECK (avatar_jewel_socket IS NULL OR length(avatar_jewel_socket) = 30)`
   （`Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs`）。
   起因 C 之前，`FillAsync` 的 `@socket` 只有 `CarriedAvatarsAsync` 一个来源，读回来的是已经过这条
   CHECK 的行；起因 C 之后 `FactsOf` 把写入自带的 `byte[]` 原样绑上去——`FactsOf` 自己的 `<remarks>`
   就写着「What is bound is the array the record carries, whatever length it is. The table is what says
   a socket that is present is thirty bytes」。今天生产上唯一的生产者是 `ArrivingAvatar.Facts()`
   （恒 30 字节），所以这条拒绝**今天到不了**，与 RV-18／RV-19 同类。证据类型：代码级读出。
3. **一处措辞偏紧**：A-01 的凭据栏写「字面量 8，不借发放通路的常量」。
   判据里写的是 `Assert.Equal(KindStorageJudgesAnAvatarBy, …)`，而 `KindStorageJudgesAnAvatarBy` 是
   判据文件自己的 `private const byte … = 8;`。「不借发放通路的常量」这半句成立，
   「字面量 8」这半句不精确。文风级。
4. **正向扫少了一个读 `ItemKind` 的判定点**：`GameInventoryDelivery.WriteItem` 的
   `writer.Write(core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16));`。
   一件时装在改动前后都落在 `else` 那一支（1 与 8 都不等于 2），答案不变，与 A-06／A-11／A-33 同类，
   但它不在起因 A 的 35 条里。正向扫不是本轮判定的重心，记为非阻断。

另：C-03 的凭据栏用的是「三次全量运行都报 2230 …移动、拆分、合并、消耗、卡片直放那批既有判据
就在这 1032 条里」。这是一条「不变」的凭据而不是「有覆盖」的凭据，且第九节已写明
「这不足以确定它们在别的输入上的答案」，按契约的 Blocking conditions 不算违反。
但这正是第三节那条遗漏藏身的地方：那 1032 条里没有一条把一件 kind 8 的行送进 `MoveToAsync`，
所以「移动那批判据全绿」在时装这件事上什么也没说。建议整改时在 C-03 旁注一句。

### 五、要求的整改

1. 在反向扫（3.1 与 3.3）里补上 RX-01 与 RX-02 两条，各自给出位置（成员名）、生产可达性、
   判据有无（按第〇节定义应为「未测定」）与证据类型（代码级读出），并相应更新 3.5 节的三个数与计数口径。
2. 把非阻断观察第 2 条（`avatar_jewel_socket` 的 CHECK）按 RV-18／RV-19 的写法补进反向扫，
   或明写为什么把它排除在「本轮新增或改变的拒绝路径」之外。
3. 在 C-03 的凭据栏补一句，说明那 1032 条里没有一条把 kind 8 的行送进 `MoveToAsync`。
4. RX-01 描述的是一次普通背包拖动会落下一行读不回来的时装行，这已超出 BD-006 的边界，
   属于要不要回到生产代码的问题，**本评审不作裁定，只报出证据**，交由控制器处置。

### 六、本评审的边界

- 未改动任何生产文件、判据文件或清单文件；六次变异逐次由文件级备份还原并核散列，
  九个文件末态与清单第〇节那张表逐行相同。
- 未建 Git 检查点、未提交；跑过的 git 命令只有只读的 `git status --porcelain`、
  `git diff`（含 `-U0`／`--numstat`）、`git rev-parse`。**未使用任何 git 还原类命令**。
- 未触碰主检出与 `.worktrees/client-debug-lab`。
- 本判定只说这一个 Task Unit 是否可以走执行者的接受逻辑，不是最终质量批准。


## Controller Disposition D-06 — QR-06-1 找到的第 29 条不是记载遗漏，是本轮引入的缺陷

**判定**：TASK-06 退回 `todo` 并**重新挂依赖**，等新建的 **TASK-08** 修好之后再重跑。
理由：RX-01／RX-02 一旦修掉，清单里这两条的内容就会变；先写清单再修，等于写一份注定要重写的清单。

### 第 29 条是什么

起因 B 把落地 core 的 `Value` 由 `opened.Count` 换成了 `ArrivingAvatar.NewIdentity()`，
**但 `ContainerSlotItem.Count` 无条件读同一个字段**
（`IItemContainer.cs`：`public int Count => Core.Value;`，注释写着「How many the slot holds」）。

于是在背包里拖一件发放来的时装、目标落在自有段 9..64 时
（客户端 `MoveCount` 是 1，由 `MoveItemSpaceDecoder` 从 `body[7..]` 读）：

- **RX-01**：`moveCount(1) != moving.Count(抽取的身份)` → 这次移动由 `Relocate` 改走 **`Split`**。
  `SplitAsync` 把源格写成 `Value = 身份 - 1`，并在目标格写下
  `StackableItemCore.Create(8, itemId, 1)`——**一行 kind 8、`Value` 为 1、不带六列事实的新行**。
  它在 `FillAsync` 里六列落成 NULL，**下一次入场装载抛
  `Avatar detail 1 is unavailable.`**（即清单自己的 RV-08）。
- **RX-02**：目标格有别的东西时 → **`PartialSwapNotAllowed`**，改动前对时装走不到。

**可达性**：与清单给 RV-02／RV-03 写的理由相同，只是目标格换成 9..64——
**玩家整理背包最常做的动作**，也正是所有者实机第一步要做的动作。

### 为什么必须修，不能只记

严格按 RQ-003 的字面（装载／存盘／再装载），它今天仍然成立。
但本 Spec 的 Why 写明它的目的是**让实机验证能开始**，
而 RX-01 的后果是「GM 发一件时装、在背包里拖一下，它就坏了」。
**只记不修，等于交付一个第一步就会踩坏的量具。**

诚实的定性：这是把一个**既有的**潜伏冲突变成了**可达**——
此前没有任何生产通路创建时装，`Count => Core.Value` 这个重载从未被时装触发过。
**让它可达的是本轮。**

### 处置

1. 新建 **TASK-08**「一件时装不是一堆东西」，修这个冲突。
2. TASK-06 的 `depends_on` 增加 TASK-08，退回 `todo`，待 TASK-08 完成后重跑，
   届时把 RX-01／RX-02 按**修好之后**的实际情形记入（可能变成「已消除的缺陷」而非「新增拒绝」）。
3. QR-06-1 的四条非阻断观察一并留给 TASK-06 重跑时处理：
   `ConsumeAsync` 的 `remaining = item.Count - count` 属同一条链；
   `items.avatar_jewel_socket` 的 CHECK 因起因 C 新增了绑定来源却未列；
   A-01 说「字面量 8」而判据用的是文件内 `const`；
   正向扫少了 `WriteItem` 的 `ItemKind == 2 ? Marker16` 这一「不变」判定点。

### 已核实、重跑时不必重做的

抽查 3 条「有覆盖」逐条复现变红（MU-14／MU-8／MU-16，变异后散列逐字符相同）；
抽查 1 条重分类（MU-13）复现「无覆盖」读数正确；证据类型标签未见混标；
记法逐句读未见全称量词；计数归因 +3/+4/+7 独立复算通过；
字节锚与 HEAD 逐字节相同（控制器另行独立核过）。


## Contract Amendment A-06g — TASK-08 修完之后，重跑清单要改记的东西

由控制器在处置 QR-08-1 时记录。TASK-08 已 `done`，本任务可重新进入执行。

**已写成的那份 `behavior-change-list.md` 大体可用，但下列各条必须按修后的实情改写或新增。**

### 一、RX-01／RX-02 的定性变了

QR-06-1 发现的那两条（背包内拖动发放来的时装 → 走 `Split` 损坏它；
目标格有东西 → `PartialSwapNotAllowed`）**已由 TASK-08 修掉**。
清单里**不得**再把它们记成「本轮新增的拒绝／放行」，
应记为**「本轮引入并在本轮修掉的缺陷」**，并写明：
- 引入者：起因 B 把落地 core 的 `Value` 由 `opened.Count` 换成抽取的身份
- 放大者：`ContainerSlotItem.Count` 无条件读同一字段（**该属性的 `<summary>` 基线就写着
  「For anything that does not stack, one」，实现与它相反**——本成果第三次遇到注释漂移到反面）
- 修法：`Count => this.IsPile ? Core.Value : 1`
- 修后实测：目标格有东西时是**整件对调**（`Moved { MoveCount = 1 }`），不是被拒
- 发现者：QR-06-1（**不是执行者自查**）

### 二、新增要记的观测与缺口

1. **`Count` 的读点 11 处**（口径：`Server/src` 下取 `ContainerSlotItem` 类型表达式的 `.Count`），
   逐个判过未见被修法弄坏者；关键闸门是 `WornPieceMetadata` 对每个非堆来源写死 `StackLimit = 1`。
   出处 QR-08-1。
2. **三条未实测路径**，逐条记为「不足以确定」：`ConsumeAsync` 对非堆、
   `ForDailyRefill` 对 worn piece、`DailyRefillService` 求和对 worn piece。出处 QR-08-1。
3. **`Count => 1` 的反向变异全量打红 22 条、跨 8 个测试类**——该属性被广泛承重。出处 QR-08-1。

### 三、QR-06-1 的四条非阻断观察，一并处理

`ConsumeAsync` 的 `remaining = item.Count - count` 属同一条链；
`items.avatar_jewel_socket` 的 CHECK 因起因 C 新增了绑定来源却未列；
A-01 说「字面量 8」而判据用的是文件内 `const`；
正向扫少了 `WriteItem` 的 `ItemKind == 2 ? Marker16` 这一「不变」判定点。

### 四、计数基线变了

全量现为 **2234 通过 / 2 跳过 / 0 失败**（此前 2230）。
相对本轮开工基线 **2216** 的差额是 **+18**，逐项目：
Protocol +3（521→524）、Persistence +4（316→320）、Application +11（1025→1036）。
**重跑时按这个新基线归因。**

### 五、不必重做的

QR-06-1 已核实：抽查 3 条「有覆盖」逐条复现变红；抽查 1 条重分类复现；
证据类型标签未见混标；记法逐句读未见全称量词；字节锚与 HEAD 逐字节相同。
**这些结论仍然成立，重跑时可沿用，但凡因 TASK-08 而变的条目必须重测。**


## 执行结果（TASK-06，第二轮 —— 按 A-06g 重跑）

**本轮只写了一个文件**：`.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（BD-006，就地更新，不是重写）。
本 Task 文件的这两个新小节是契约要求的交付记录，不改动也不删除任何既有小节。
未改动任何生产代码或判据；23 次变异期间的临时改动逐次由文件级备份还原并核散列。
**未建 Git 检查点、未提交**；**未使用任何 git 还原类命令**（`git checkout --` / `git restore` / `git stash` 一次未出现）；
本轮跑过的 git 命令只有只读的 `git status --porcelain`、`git diff`（含 `--stat`）、`git rev-parse`、`git show HEAD:<path>`。
未触碰主检出与 `.worktrees/client-debug-lab`。

`git rev-parse HEAD` = `565d33fee899556476f8a582605fb4f439261c0f`，未提交。
`git status --porcelain` 在本轮开工与收尾各读一次，**均为 18 行**（12 个 ` M` ＋ 6 个 `??`），逐行一致。

### 一、改了清单的哪些条目，每条改前改后

| # | 位置 | 改前 | 改后 |
|---|---|---|---|
| 1 | 文件抬头 | 无版本说明 | 新增一段，写明这是第二版、第一版被 QR-06-1 拦下的是哪一条、D-06 判定了什么、第二版按修后实情改写 |
| 2 | 第〇节的交付态散列表 | 九个文件 | **十二个**：加 `IItemContainer.cs`（`a39f94f9…`，TASK-08 改的那个）、`InventoryItemTables.cs`（`60877044…`）、`CharacterDatabaseSchema.cs`（`95332a76…`）；并写明 `git status --porcelain` 由 16 行变 18 行、多出的两行是什么 |
| 3 | 第〇节「有覆盖」的定义 | 「在 3.5 节逐条列出（14 条）」 | 「（15 条）」 |
| 4 | 第一节标题与表 | 「起因（4 处）」 | 「起因（**5 处**）」，新增起因 E（`ContainerSlotItem.Count`），并附「起因 E 的由来」六点（含 `git show HEAD:` 对那句 `<summary>` 的核实、发现者是 QR-06-1） |
| 5 | 第二节合计 | 48 条 | **60 条**（A 36 ＋ B 5 ＋ C 3 ＋ D 5 ＋ E 11） |
| 6 | A-01 的凭据栏 | 「字面量 8，不借发放通路的常量」 | 改为抄断言原文 `Assert.Equal(KindStorageJudgesAnAvatarBy, fromTheArchive!.ItemKind)`，并写明 `KindStorageJudgesAnAvatarBy` 是该判据文件自己的 `private const byte … = 8;`：前半句成立、「字面量 8」这半句不精确。出处 QR-06-1 非阻断观察第 ③ 条 |
| 7 | 起因 A 的表 | 止于 A-35 | 新增 **A-36**：`GameInventoryDelivery.WriteItem` 的 `core.ItemKind == 2 ? core.Marker16 : Math.Max(0, core.Marker16)`，记「不变」。出处 QR-06-1 非阻断观察第 ④ 条 |
| 8 | B-01 的凭据栏 | 只指向 MU-4／NB-1／NB-2 | 加一句「这一位换了值，谁在读它」，指向起因 E 那张表与 3.6，并注明第一版停在 `FillAsync` 没往容器动词方向追（出处 QR-06-1 第三节） |
| 9 | C-03 的凭据栏 | 「三次全量运行都报 2230…这 1032 条里」 | 改为两次运行、2234／1036；补记 QR-06-1 第四节末那条建议，并写明第二版的实情已变（`DraggedAvatarStaysWholeTests` 三条走的正是 `MoveToAsync` → `DecideMoveAsync`，MU-20 让它们变红） |
| 10 | 第二节 | 无起因 E | 新增整张「起因 E（11 条）」表：E-01..E-11，附计数口径与三处被排除的读点（Protocol 的四处读 `.Core`、`MoveItemSpaceSlice` 的 `MoveCount`、`TaggedIdReportHandler` 的两处别的类型） |
| 11 | 第三节合计 | 28 条 | **36 条**，并逐条列出新增 8 条的来历 |
| 12 | 3.1 | 16 条 | **19 条**：新增 RV-20（入场存盘侧撞号守卫，**有覆盖**／MU-22）、RV-21（`avatar_jewel_socket` CHECK，**无覆盖**／MU-23）、RV-22（`ConsumeAsync` 的 `remaining < 0`，未测定） |
| 13 | RV-02／RV-03 的凭据栏 | 只写「MU-2 那次 5 条红里没有走这条路径的」 | 加一句第二轮的复核：新增的 `DraggedAvatarStaysWholeTests` 确实经 `DecideMoveAsync`，但在 MU-2 那次是绿的，所以读数不变 |
| 14 | RV-06／RV-07／RV-09／RV-11 的凭据栏 | 「报 0 失败（2230 通过 / 2 跳过）」等 | 改为 2234 |
| 15 | 3.2 | 3 条 | **4 条**：新增 **RV-23**（起因 E 让普通装备／契约兽这类非堆的 `PartialSwapNotAllowed` 消失，**无覆盖**） |
| 16 | 3.3 | 7 条 | **9 条**：新增 **RP-08**（同一处的另一方向：目标格空时由 `Split` 改回 `Relocate`，**无覆盖**）、**RP-09**（`InventoryItemState.Holds` 的 Template 分支，`Server/src` 内 0 个读点，未测定） |
| 17 | 3.5 小结 | 14／8／7，合计 29，28 行 | **15／11／9，合计 35，34 行**（多出的一行仍是 RP-06），另注 3.6 的 2 条 |
| 18 | 新增 3.6 | 无 | **「本轮引入并在本轮修掉的缺陷（2 条）」**：RX-01／RX-02 按 A-06g 改记，含引入者、放大者（`<summary>` 的核实）、修法、修后实测读数、发现者是 QR-06-1；并写下 `Count` 的 11 处读点回归面、三条未实测路径、`Count => 1` 打红 22 条跨 8 个类 |
| 19 | 第四节 | 19 次变异，基线 2230 | **23 次变异，基线 2234**，整张表重做；新增「与第一版读数的差别」小表；MU-18 散列改为本轮实测的 `49e29258…`（附与第一版 `57afb061…` 差别的说明）；新增 MU-20..MU-23 |
| 20 | NB-1 | 散列 `57afb061…`、2230 | 本轮实测的 `49e29258…`、2234；补一句 TASK-08 的四条新判据也在这 2234 条里 |
| 21 | NB-2 | 「MU-5 下该用例同样变红，本轮未取其失败形态」 | 本轮取到了：MU-5 下是 `Avatar detail <数> is unavailable.`，也没有走到那四句 |
| 22 | 7.1 | 两条实测 | 三条（加「开工与收尾各测一次，两次读数相同」） |
| 23 | 7.3 | 四处起因 | **五处**，新增起因 E 那一行 |
| 24 | 7.4 | 两条 | 三条（新增起因 E 那条「不足以确定」） |
| 25 | 第八节 | 2230，+14，Application +7 | **2234，+18，Application +11**，逐条列出 `DraggedAvatarStaysWholeTests` 四条；并写明基线换过一次；既知偶发一栏改为「第二轮的 MU-7 那次它没有出现」 |
| 26 | 第九节 | 19 次变异、未测定七处 | 23 次变异、未测定九处；新增两条「不足以确定」（起因 E 只被两次变异照过；RV-23／RP-08 的可达性是代码级读出、没有真的拖过一次）；新增一条关于 3.6 的（同模板时装那一支没有实测） |

### 二、本轮新做的变异，各自的散列、红／绿、被红的判据名

**23 次，全部是整条 slnx 的全量运行，一次 `--filter` 也没用。**
命令：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`
方法：文件级备份（`cp` 到会话 scratchpad）＋ `perl -0777 -pi` 就地改 ＋ `sha256sum` 记散列 ＋ `cp` 还原并核散列。

**为什么 MU-1..MU-19 也要重做**：TASK-08 新增了 `DraggedAvatarStaysWholeTests` 四条判据，
第一版记的失败数与红集在其中四次变异下已经不成立（下表）。按 CK-016 与 A-06g 第五节，
因 TASK-08 而变的条目必须本轮实测，所以 19 次逐次重跑，不沿用第一版读数。

| ID | 变异后散列 | 与第一版散列 | 该次结果 | 被红的判据 |
|---|---|---|---|---|
| MU-1 | `a1e8a7c4968dd74a03f1ba4335514379d73a2f2bce4af1a06012939314fd6781` | 逐字符相同 | 失败 10（第一版 7） | `AvatarSlotRulesAfterTheKindChangeTests` 4 ＋ `GrantedAvatarIsWholeTests` 3 ＋ **`DraggedAvatarStaysWholeTests` 3（新增）** |
| MU-2 | `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864` | 逐字符相同 | 失败 5（同第一版） | `AvatarSlotRulesAfterTheKindChangeTests` 4 ＋ `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)`。`DraggedAvatarStaysWholeTests` 在这一次是绿的 |
| MU-3 | `3593447673aa86a3a6a9a96c9604e19c4cce7c6fa8546d6fce90535963f00d4e` | 逐字符相同 | 失败 1（同） | `AvatarSlotRulesAfterTheKindChangeTests.An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it` |
| MU-4 | `cd388e911834331b48da322e29085511307647ac7be3e9e9725c412d381ee46b` | 逐字符相同 | 失败 3（第一版 2） | `GrantedAvatarIsWholeTests` 2 ＋ **`DraggedAvatarStaysWholeTests.A_drag_of_one_onto_another_avatar_trades_the_two_slots`（新增）** |
| MU-5 | `6222be998e9fe2354c8df8c4e5be7ec67aad7a9e88a4be120ca059324a0c3948` | 逐字符相同 | 失败 4（第一版 2） | `GrantedAvatarIsWholeTests` 2 ＋ **`DraggedAvatarStaysWholeTests` 2（新增）**；四条同为 `Avatar detail <数> is unavailable.`，栈根 `InventoryEntryAssembly.WithAvatarDetail` |
| MU-6 | `a826cf2e0764926078899549f804db946661a8847e1442b23238bfeec62d746b` | 逐字符相同 | 失败 3（同） | `SqliteItemDomainStoreTests.TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding` ＋ `TwoAvatarsDoNotShareOneIdentityTests` 2 |
| MU-7 | `e959bb14bc86f158911bc729919aa3b98745209c7f7605b679a91d4c9a970219` | 逐字符相同 | 失败 8（第一版 7，含 1 条既知偶发） | Persistence 4（`SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates` ＋ `TwoAvatarsDoNotShareOneIdentityTests` 3）＋ Application 4（`GrantedAvatarIsWholeTests` 2 ＋ **`DraggedAvatarStaysWholeTests` 2**）。**第一版那条 `CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold` 本轮没有出现** |
| MU-8 | `97b36b8bd3d897d4782bea4f8e925c0374b36e2efe7c9ae132018acb89b22578` | 逐字符相同 | 失败 2（同） | `RefusedEquipIsRecordedWithItsTemplateTests` 2 |
| MU-9 | `1308ad14e620007adaa6a5edd3022e4f8f5837142ca892b524e4fe943e810c58` | 逐字符相同 | 失败 1（同） | `RefusedEquipIsRecordedWithItsTemplateTests.An_avatar_refused_as_unwearable_is_recorded_with_its_template_and_that_slot` |
| MU-10 | `17ecabfeb72050aadc6ae7908188223e00083194cf4f75f691e94046e79a43f3` | 逐字符相同 | 失败 1（同） | `RefusedEquipIsRecordedWithItsTemplateTests.A_piece_aimed_at_the_wrong_slot_is_recorded_with_its_template_and_that_slot` |
| MU-11 | `d9aa6bfde2f5700563a913b4076a7d43b5aeebf92c2067aba45ef1febc704d71` | 逐字符相同 | **失败 0 / 通过 2234，EXIT=0** | 无 → 无覆盖 |
| MU-12 | `a3057a4b87336373d9253901233c658d24b83db94e6220dc631e5ec8294124a4` | 逐字符相同 | **失败 0 / 通过 2234** | 无 → 无覆盖 |
| MU-13 | `2843a41cc85007200dbe99b3a72d6754308dbb0a354d7b1be89033a096bd8c3a` | 逐字符相同 | **失败 0 / 通过 2234** | 无 → 无覆盖 |
| MU-14 | `b5806850e0f8b414618861983e0edad66db2cf9cc45b1e7be1b9b5ccd5dda37d` | 逐字符相同 | 失败 1（同） | `ClassificationRefusalTests.AnEntrySaveRefusesARowTheArchiveReadsAsAnAvatarAndTheRowDoesNot` |
| MU-15 | `bff6c3b4aedb74af926ec4e5ef9f8c57f508dd88eb3fbd654ba35bca96c5be3d` | 逐字符相同 | 失败 1（同） | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_SupportRelationIsOwnerCorrectAndLoadedEmptyRemainsEmpty`；**字节锚未红** |
| MU-16 | `976d98c00749f56f963aec08600b9e5182a447e34afd31e48eac74d96f394054` | 逐字符相同 | 失败 1（同） | **字节锚 `SelectCharacterAcceptedTranscriptTests.AcceptedEntryTranscript_AllStaticFrameBytesMatchAcceptedBaseline`**，该次只有它红 |
| MU-17 | `597a2e3608d249c1aae783bc9b0454ce250cae90f7631e7cfb386ebf4680460e` | 逐字符相同 | 失败 1（同） | `SelectCharacterPayloadCodecTests.SelectCharacterPayload_HighRiskAvailabilityDistinctionsAreNotZeroFilled` |
| MU-18 | `49e292582387c8986d2afe58aca29c90b2608ae086c450eb3083eaae639c18be` | **不同**（第一版 `57afb061…`） | **失败 0 / 通过 2234** | 无 → 无覆盖（NB-1）。散列不同的原因：同一改法，但本轮把 `private static int nextIdentity;` 放在 `NewIdentity()` 之前、并用了全限定的 `System.Threading.Interlocked`。观测结果与第一版相同 |
| MU-19 | `13776f94f023949821059a7f90700f3a90fc47e0d5c4ab22adf196e6fdcd9f35` | 逐字符相同 | **失败 0 / 通过 2234** | 无 → 无覆盖 |
| **MU-20**（新） | `d0c8abe02bdaff2977576d005cc42adcadf505141acca09e248f5e528e39844f` | 与 TASK-08 的 MU-REV 逐字符相同 | 失败 3 | `DraggedAvatarStaysWholeTests` 三条：`A_drag_of_one_moves_the_whole_avatar_and_empties_the_slot_it_came_from`（`Assert.Null() Failure: Value is not null`，line 108）、`A_drag_of_one_onto_another_avatar_trades_the_two_slots`（`Expected: Moved { MoveCount = 1, ItemId = 101550507 }` / `Actual: Refused { Reason = PartialSwapNotAllowed }`，line 205）、`An_avatar_that_was_dragged_is_loaded_again_with_the_same_identity_and_facts`（`Avatar detail 1408050310 is unavailable.`） |
| **MU-21**（新） | `1ca8c5681174ce32093088c0bdf25f64bc5d188346cdab70e6e59d0073c9d6eb` | 与 TASK-08 的 MU-REV2 逐字符相同 | 失败 22（Persistence 6 / Application 16） | 跨 8 个测试类：`SplitPileTests` 3、`SqliteEntryInventoryMutationRepositoryTests` 3、`MoveItemSpaceHandlerTests` 5、`UseStackableHandlerTests` 3、`TutorialRewardPlacementTests` 3、`BagPlacementVerbTests` 3、`PersistentContainerBoundaryTests` 1、`DraggedAvatarStaysWholeTests` 1。判据名逐条抄在清单第四节。**与 QR-08-1 记的 22 条／8 个类逐条相同** |
| **MU-22**（新） | `e5781485e986ecceae0a753dd9befdd262cfb85fc5a0220965fb7999e6b23ab1` | 无对照（第一版没有这次变异） | 失败 2 | `SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` 与 `.DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds`，两条的读数同为 `Assert.Throws() Failure: No exception was thrown` / `Expected: typeof(System.IO.InvalidDataException)` → RV-20 **有覆盖** |
| **MU-23**（新） | `d87b201864269cae14d4c1bf8422f8e8b1e4bc832f93fdc4eaa54f01c5de00a7` | 无对照 | 失败 1 | `ItemCarrierShapeTests.ItemsCarriesEveryConstraintItsSourcesDeclared`。本轮读了这条判据：它比的是 `DeclarationAsync("items")` 的 DDL 文本，没有做一次违反该 CHECK 的写入 → 按第〇节的第二种读法，RV-21 那条**拒绝路径**记作**无覆盖** |

**还原**：23 次逐次由备份 `cp` 还原，还原后逐次 `sha256sum` 与该次变异前的散列比对，23 次比对逐次读到相同。
收尾时另做两件：`sha256sum -c` 校验备份目录（12 个文件逐一 `OK`）；
把工作区这 12 个文件逐个与开工基线散列比对（12 个逐一 `OK`）。末态：

```text
2c3880602589b242e9b5447f6db2dd102d47002d3fa090d85f6af41c757240da  PvfEntryLifecycleContentSource.cs
8e7f085d840ef8c2d08dffbe1c91e9bc7f108b3d714fd129af456facb55b13db  ItemContainers.cs
0438efe963c79cb54ae5f608c7b3c5e8abb94c79aa60073a2ddf835e4e8aa177  ItemSlotRuleService.cs
c6fb4bac69d03fa461670a04a4af67df5de88a1776bf9c7e88180d0024e0f4ba  ItemArrivalRuleService.cs
7e9f4678af319acc514afd0d2ee179bc3f7325e6aaf16ac63fa62b6baf9b5349  InventoryEntryAssembly.cs
87f22d2cd107eec558a542e14cd8e9a93d0300bb42c358a898a7a8469a9c87fe  SqliteItemDomainStore.cs
60877044b4d3070f9c927f4e13d5bf9a3be41d144f07ad0e155cff3f26a3e533  InventoryItemTables.cs
95332a7628bd0671920b5d741923c20e50c3b32830b166e0f33b25f7650ec768  CharacterDatabaseSchema.cs
d96cdb0127c277610a30b672567053317ebade93367a2685d0aa7ecc7b9550d9  MoveItemSpaceSlice.cs
18c22acf2209378aa5e947899d8559f7385dc976aae895dac1e99f67f6ff2dd5  GameInventoryDelivery.cs
be1fd3acb3970d6c5048eb6401eff6170706734045db023726c058022ecde752  StateSynchronizationPayloadCodec.Entry.cs
a39f94f96f3000cb393c1f34c9631d10477a1d48d5c7a501d5afd04ae41cd659  IItemContainer.cs
```

### 三、CK-017 —— 字节锚复核结果

**逐字节相同，与 A-06g 给的 `1424a4a8…` 相符；无差异需要归因。**

- 工作副本 `sha256sum Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
  = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。
- `git show HEAD:<同一路径> | sha256sum` = `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`。
- `git diff HEAD --stat -- <同一路径>` 输出为空。
- 三条在本轮开工与收尾各测一次，两次读数相同。
- **本轮自己核过，没有沿用 D-06「控制器另行独立核过」那句。**
- 敏感性：MU-16 让这张锚变红且该次只有它红；MU-15 没让它变红；MU-20 与 MU-21 都没让它变红。
- 归因表在清单 7.3，本轮新增起因 E 那一行（同一条转录链上没有容器，`IItemContainer` 与 `ContainerSlotItem` 不参与）。

### 四、CK-018 —— 全量输出与相对 2216 的逐项目归因

收尾那次运行（开工那次逐项相同）：

```text
已通过! - 失败:     0，通过:    52，已跳过:     0，总计:    52 - Dfo.Server.Networking.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   524，已跳过:     0，总计:   524 - Dfo.Server.Protocol.Tests.dll (net10.0)
已通过! - 失败:     0，通过:    60，已跳过:     0，总计:    60 - Dfo.Pvf.Tests.dll (net10.0)
已通过! - 失败:     0，通过:     6，已跳过:     0，总计:     6 - Dfo.Server.Host.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   320，已跳过:     0，总计:   320 - Dfo.Server.Persistence.Tests.dll (net10.0)
已通过! - 失败:     0，通过:   236，已跳过:     2，总计:   238 - Dfo.Server.Infrastructure.Tests.dll (net10.0)
已通过! - 失败:     0，通过:  1036，已跳过:     0，总计:  1036 - Dfo.Server.Application.Tests.dll (net10.0)
EXIT=0
```

52 + 524 + 60 + 6 + 320 + 236 + 1036 = **2234 通过 / 2 跳过 / 0 失败，退出码 0**。

| 项目 | 2216 基线 | 现在 | 差 | 构成它的各项 |
|---|---|---|---|---|
| Dfo.Server.Protocol.Tests | 521 | 524 | **+3** | `RefusedEquipIsRecordedWithItsTemplateTests` 三条（TASK-05） |
| Dfo.Server.Networking.Tests | 52 | 52 | 0 | —— |
| Dfo.Pvf.Tests | 60 | 60 | 0 | —— |
| Dfo.Server.Host.Tests | 6 | 6 | 0 | —— |
| Dfo.Server.Persistence.Tests | 316 | 320 | **+4** | `SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`（TASK-01，1）＋ `TwoAvatarsDoNotShareOneIdentityTests` 三条（TASK-03） |
| Dfo.Server.Infrastructure.Tests | 236（另 2 跳过） | 236（另 2 跳过） | 0 | 两条跳过仍是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 与 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` |
| Dfo.Server.Application.Tests | 1025 | 1036 | **+11** | `GrantedAvatarIsWholeTests` 三条（TASK-02）＋ `AvatarSlotRulesAfterTheKindChangeTests` 四个用例（TASK-04：三个 `InlineData` ＋ 一个 `Fact`）＋ `DraggedAvatarStaysWholeTests` 四条（TASK-08）。3 ＋ 4 ＋ 4 = 11 |
| **合计** | **2216** | **2234** | **+18** | 3 ＋ 4 ＋ 11 = 18，与 A-06g 第四节给的「Protocol +3、Persistence +4、Application +11」逐项目一致 |

计数口径：`dotnet test` 每个 dll 那一行的「总计」；判据侧 `[Fact]`／`[SkippableFact]` 各计 1，
`[Theory]`／`[SkippableTheory]` 按其 `[InlineData]` 条数计（本轮读三个新增判据文件复算过）。

**既知偶发**：本轮 25 次运行（23 次变异 ＋ 开工 ＋ 收尾）的输出里，
`ObjectDisposedException: 'SQLitePCL.sqlite3'` 未出现，
栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败未出现；
第一版 MU-7 那次读到的 `CharacterSettingsTests.WhatIsAnsweredIsWhatTheRowsHold` 本轮的 MU-7 那次没有出现。

### 五、反向扫重扫的结果 —— **有第 30 条**

按契约的 Blocking conditions（上一个成果漏 13 条、QR-06-1 又找到第 29 条），
本轮**从代码出发重扫了一遍**，不只是改动已有条目。重扫的做法与找到的东西：

**重扫的做法（四条轴，逐条说）**：

1. **种类字节轴**：口径是 `Server/src` 下含 `ItemKind` 且带 `==`／`!=`／`is`／`switch` 的非注释行，
   读到 **29 行**。其中 4 行（`DropRules.KindOf` 与 `DropRuleService` 的三处）读的是
   `DropRules.KindOf(DropRateSlot)` 给的 `DropItemKind` 枚举，不是 `Core.ItemKind` 或 `ItemPlacement.ItemKind`，
   不受起因 A 影响。剩下 25 行逐个对照清单，**只有一行在 A-01..A-35 里找不到**——
   `GameInventoryDelivery.WriteItem` 的 `core.ItemKind == 2 ? core.Marker16 : …`（已补为 A-36，QR-06-1 已先报）。
2. **身份／`Core.Value` 轴**：口径是 `Server/src` 下含 `Core.Value` 或 `.Core with { Value` 的非注释行，
   读到 **18 行**。
   `SelectCharacterPostEntryServices` 的 `Math.Max(0, value.Core.Value)` 在 kind 集合 `2 or 3 or 4 or 7 or 9 or 10 or 11` 之内，1 与 8 都不在，是 A-33；
   `InventoryEntryAssembly` 第 524 行那个投影限于 `SlotIndex is > CarriedGoldSlot and <= 2`，时装落 9..64，进不去；
   `StateSynchronizationPayloadCodec.Entry` 第 819 行读的是契约兽的 `PetKey`（kind 5）。
   **找到一个此前未列的**：`InventoryItemState.Holds` 的 Template 分支（已补为 RP-09，`Server/src` 内 0 个读点）。
3. **六列事实轴**：取一切 `avatar_expire_date` / `avatar_jewel_socket` / `avatar_clear_id` / `avatar_color1` / `avatar_delete_date`。
   **这一轴找到了第 30 条**（见下）。
4. **起因 E 轴**：取一切 `ContainerSlotItem` 类型表达式的 `.Count`，11 处（与 QR-08-1 相同），
   逐处问「相对**本轮开工基线**它的答案变了没有」——**这一问找到了 RV-23 与 RP-08**（见下）。

**第 30 条 —— RV-20：入场存盘侧的撞号守卫**
（`SqliteCharacterItemTable.UpdateOwnAsync` 与 `.InsertOwnAsync` 的
`NOT EXISTS (SELECT 1 FROM items held WHERE` ＋ 常量 `CarriesTheAvatar`；
`InventoryEntryAssembly.SaveAsync` 把零行读成 `InvalidDataException`）。
它与 RV-05（`SqliteItemDomainStoreSession.FillAsync` 的守卫）是**两处不同的语句、两条不同的写入通路**，
清单第一版只列了后者。它本轮新到得了：`BindAvatar` 在 `detail is null` 时把 `@avatarKey` 绑 `DBNull`，
起因 A ＋ B 之后一件发放来的时装才带着 kind 8 与六项事实走到这里，`@avatarKey` 才绑成身份的四个字节。
**判据有无：有覆盖**，由本轮新做的 MU-22 支撑（2 红，两条都以 `Assert.Throws` 失败）。

**另外两条也是本轮重扫新找到的 —— RV-23 与 RP-08：起因 E 对普通装备的影响。**
`CharacterRosterService.EquipmentCore` 给新建角色的起始装备写 `Value: Random.Shared.Next(1, int.MaxValue)`；
`TakeOffAsync` 把 `piece.Core` 原样放进主列；随后一次 Main→Main 拖动进 `DecideMoveAsync`。
基线上 `Count => Core.Value` 使这样一件装备的 `moving.Count` 是那个抽出来的数，
而客户端 `MoveCount` 是 1，两者不等 → 目标格空走 `Split`（RP-08）、目标格有东西返回 `PartialSwapNotAllowed`（RV-23）。
起因 E 之后 `Count` 答 1，两者相等 → `Relocate` / `Swap`。
**本轮回去读过的五份记载（清单第一版、QR-06-1、TASK-08 执行结果、QR-08-1、D-08）里，
没有一份就普通装备讲过这条链**——它们讲的都是时装。
**判据有无：无覆盖**——MU-20 的 3 条红与 MU-21 的 22 条红逐条列出后，
没有一条把一件带抽出来的 `Value` 的普通装备送进 `MoveToAsync`。
**可达性论证的证据类型是代码级读出，不是实测**：本轮没有把这样一件装备真的拖过一次，
这一点在清单第九节明写。

**这三条的边界处置**：RV-20 与 RP-09 属记载补全。
**RV-23／RP-08 描述的是一个在基线上就存在、被起因 E 顺带消掉的行为差**——
它不在 BD-006 之外要求任何改动（本 Task 的 owned boundary 只有清单文件），
但它是「起因 E 的影响面比历次记载写的更宽」这一事实，**报出来交由控制器判读**。

### 六、A-06g 三条「新增要记的」与 QR-06-1 四条非阻断观察的落点

| 来源 | 要求 | 落在清单哪里 |
|---|---|---|
| A-06g 一 | RX-01／RX-02 改记为「本轮引入并在本轮修掉的缺陷」，含引入者／放大者／`<summary>`／修法／修后实测／发现者 | 新增的 **3.6 节**；`<summary>` 的核实另写在第一节「起因 E 的由来」第 3 点 |
| A-06g 二 1 | `Count` 的读点 11 处、口径、`WornPieceMetadata` 是关键闸门 | 正向扫的**起因 E 表（E-01..E-11）**＋ 3.6 的「修法的回归面」段 |
| A-06g 二 2 | 三条未实测路径逐条记「不足以确定」 | 3.6 的三点，另在 E-05／E-10／E-11 与 RV-22 各写一次 |
| A-06g 二 3 | `Count => 1` 打红 22 条跨 8 个类 | 3.6 末段 ＋ 第四节 MU-21 行（逐条列出 22 个判据名） |
| QR-06-1 观察 ① | `ConsumeAsync` 的 `remaining = item.Count - count` 属同一条链 | **RV-22** ＋ **E-05** |
| QR-06-1 观察 ② | `items.avatar_jewel_socket` 的 CHECK 因起因 C 新增了绑定来源却未列 | **RV-21**（含 MU-23 的读数与它为什么记作无覆盖） |
| QR-06-1 观察 ③ | A-01 说「字面量 8」而判据用的是文件内 `const` | **A-01 的凭据栏**已订正 |
| QR-06-1 观察 ④ | 正向扫少了 `WriteItem` 的 `ItemKind == 2 ? Marker16` | **A-36** |
| QR-06-1 第五节第 3 点 | C-03 旁注一句「那 1032 条里没有一条把 kind 8 的行送进 `MoveToAsync`」 | **C-03 的凭据栏**已补，并写明第二版的实情已变（`DraggedAvatarStaysWholeTests` 走的正是那条路） |

### 七、沿用了 QR-06-1／D-06 哪些结论，重测了哪些

**沿用（A-06g 第五节允许）**：证据类型标签未见混标；记法逐句读未见全称量词
（这两条本轮另做了一次自查，见下一小节）；抽查 3 条「有覆盖」逐条复现变红、抽查 1 条重分类复现
——这四次抽查的**对象**（MU-14／MU-8／MU-16／MU-13）本轮全部重跑过一遍，读数与 QR-06-1 记的相同。

**重测（因 TASK-08 而变，不得沿用）**：MU-1..MU-19 全部重跑；两个基线（2230 → 2234）；
字节锚（本轮自己核，未沿用 D-06 的「控制器另行独立核过」）；
+14 → +18 的归因（本轮读三个新增判据文件复算）。

## 自查（TASK-06 第二轮，逐条对照契约验收条件与七份修正案）

### 契约的四条验收条件

1. **正向扫** —— 达成。清单第二节 **60 条**，按五个起因分表（A 36／B 5／C 3／D 5／E 11），
   逐条给出判定点的成员名、本轮它对一件时装（起因 E 是「对一件不堆叠的东西」）的答案变了没有、以及凭据。
   计数口径写在表头，起因 E 另写了自己的读点口径与三处被排除的读点。
2. **反向扫** —— 达成。清单第三节 **36 条**，分五张表（新增／变严的拒绝 19、变松／消失的拒绝 4、
   新增／改变的放行 9、量具缺口 2、本轮引入并修掉的缺陷 2），每条一栏生产可达性、一栏判据有无、一栏凭据。
   **没有用「全量判据全绿」代替这一步**：第三节开头写明，且 15 条「有覆盖」逐条点名到一次具名变异；
   MU-11／MU-12／MU-13／MU-19 四次「全绿」被读作**无覆盖**而不是「已覆盖」。
   **重扫的结果与第 30 条报在上文第五小节。**
3. **每条给位置、可达性、覆盖；给数目时同时列出各项与计数口径** —— 达成。
   按成员名索引，未用行号索引（引用判据里的行号只出现在抄失败读数的地方，且注明是判据文件）。
   出现的每个基数都写成「N 条，分别是：…」并附计数口径：起因 5 处、正向 60 条（36＋5＋3＋5＋11）、
   反向 36 条（19＋4＋9＋2＋2）、有覆盖 15／无覆盖 11／未测定 9、变异 23 次、
   `Count` 读点 11 处、`Count => 1` 打红 22 条跨 8 类、用例差额 +18（3＋4＋11）。
4. **入场帧整表字节比对，有差异逐处归因** —— 达成，见上文第三小节：**逐字节相同，无差异需要归因**；
   另附五处起因为什么到不了这 62 帧的归因表与四次锚敏感性读数（MU-16 红；MU-15、MU-20、MU-21 未红）。

### 七份修正案，逐条

- **A-06（1）NB-1** —— 达成。清单第五节 NB-1 具名记下，本轮亲做（MU-18，散列 `49e29258…`，
  全量失败 0 / 通过 2234），并写明这一条要紧的理由与散列与第一版不同的原因。
- **A-06（2）NB-2** —— 达成。清单第五节 NB-2；本轮把 MU-5 下的失败形态也取到了
  （`Avatar detail <数> is unavailable.`，也没有走到那四句），第一版这一句写的是「本轮未取其失败形态」。
- **A-06b ＋ A-06c ＋ A-06d** —— 按 A-06d（最终版）记，第六节未改动：只写三角观测表，
  逐行标出施加方与出处，并明写「这些变异不足以确定哪一道屏障在一般情形下承重」。
  A-06b 第 1 项（「盲区自我延续」后半截无覆盖）在第六节末如实记下并注出处。
  **第二轮没有重做 MR-A／MR-B／M3**：它们施加在 `SqliteItemDomainStore.cs` 上，
  TASK-08 没有改这个文件，也没有新增走那条守卫的判据（本轮的 MU-6 读数与第一版相同即是佐证）。
- **A-06d 的结构事实（验收条件第 7 条）** —— 达成，第六节末单列，抬头写证据类型与出处，未改动。
- **A-06e** —— 达成，第五节单列，未改动；本轮的 MU-3 散列 `3593447673…` 与所记逐字符相同，失败 1。
- **A-06f** —— 达成，3.4 的 RG-01／RG-02，未改动。
- **A-06g** —— 达成，落点逐条列在上文第六小节的表里。
  第四节的计数基线已按 2234／+18 改；第五小节报了重扫结果与第 30 条。

### 记法口径的自查（不用词表，逐句问「个例还是一类」）

判准（D-03c）：写完后逐句问「这句在说一个个例，还是在说一类？」。本轮施用结果：

- **写完之后改窄的句子（4 处，分别是）**：
  ① 起因 E 表与 3.6 里原写「`WornPieceMetadata` 对每一个非堆来源都写死 `StackLimit = 1`」——
  这是在说一类；改为「该方法返回语句里 `StackLimit` 那一位是字面量 `1`，与 `worldClass` 取什么值无关（本轮读该方法核对）」，
  并把由它推出的那半句标为**代码级读出**；
  ② RV-23 原写「此前的记载…**都只**就时装讲这条链」——改为「本轮回去读过的五份记载里，没有一份就普通装备讲过这条链」，
  把范围收到我实际读过的五份；
  ③ 第四节还原那句原写「**每一次**的还原后散列**都**与该次变异前的相同」——
  改为「23 次比对逐次读到相同」，写成一串观测而不是一条一般命题；
  ④ 3.5 原写「3.6 的两条**都**写作有覆盖」——改为「RX-01 与 RX-02 各写作有覆盖」。
- **本轮新写的句子里说「一类」的地方**，处理为三种情形，与第一版同：
  一是**代码级读出**（标证据类型、附文件与成员名，并写明不由它推出一般命题）——
  RV-20 的可达链、RV-21 的「今天到不了」、RV-23／RP-08 的可达链、RP-09 的「0 个读点」、E-02／E-03／E-04／E-09；
  二是对**可枚举物**的计数（`Count` 读点 11 处、`MU-21` 的 22 条红跨 8 类、
  `ItemKind` 比较行 29 行、`Core.Value` 读写行 18 行）——读者可自行复算，口径都写了；
  三是**引用他人写下的句子**（A-06e 的「1028 个既有用例全绿」、A-06d 的三角表、A-06f 的记录形状、
  QR-08-1 的 11 处与 22 条），逐处标为引文并注出处，不作为本清单自己的主张。
- **引文核对**：本轮引到 QR-08-1 的两处数（11 处读点、22 条／8 个类）都由本轮**亲自复现**，
  不是只抄；引到 `<summary>` 那句话时，出处标的是 `git show HEAD:` 的读数与 D-08，两者本轮都读过。
  引到 QR-06-1 的四条非阻断观察时，逐条回到 TASK-06 文件的「四、非阻断观察」小节核对措辞。
  **本轮没有把派工指令当作持久记载引用**——A-06g 是写在本 Task 文件里的修正案，是持久记载。

### 硬约束核对

- **只写了 `behavior-change-list.md` 一个文件**（本 Task 文件的这两个新小节除外，它是契约要求的交付记录；
  既有小节一句未改、一句未删）；未改动任何生产代码或判据。
- 23 次变异期间的临时改动逐次还原并核散列；末态十二个文件的散列与开工基线逐行相同（上文第二小节附表）。
- **未建 Git 检查点、未提交**；**未使用任何 git 还原类命令**；跑过的 git 命令只有只读的四种。
- 未新增协议帧、未改任何帧的字节布局（字节锚逐字节不变即是实测面）。
- `CommandHandlerArchitectureTests` 与 `NotificationHandlerArchitectureTests` 随交付态全量绿，未被改动。
- 未给 `PvfEquipmentSlots.ByName` 补任何键；未裁定契约兽槽位 24／25。
- 未触碰主检出与 `.worktrees/client-debug-lab`。
- 不写读者无法证伪的基数：清单里每个数目都附了计数口径与构成它的各项。
- **无阻塞。** 上文第五小节报出的 RV-23／RP-08 是一条越出本 Task 边界的事实陈述，
  **本执行不作裁定，只报出证据**，交由控制器处置。


## Independent Quick Review — QR-06-2（第二轮／重跑轮）

- **判定：`passed`**
- 评审的契约版本：Contract revision 1 ＋ 七份修正案 A-06、A-06b、A-06c、A-06d、A-06e、A-06f、A-06g（全集），
  并读了 D-06、QR-06-1 与 TASK-08（含 QR-08-1、D-08）。
- 评审的 owned diff：`git rev-parse HEAD` = `565d33fee899556476f8a582605fb4f439261c0f`，未提交；
  `git status --porcelain` **18 行**（12 个 ` M` ＋ 6 个 `??`），与执行者所记逐行一致。
  本任务写下的文件是 `.rks-agent/tasks/server-agrees-what-an-avatar-is/behavior-change-list.md`（693 行，未跟踪目录内）。
- 评审者与执行者是不同上下文，fresh context。本轮读了：TASK-06 全文、清单全文、TASK-08、TASK-04 的相关小节、
  以及工作区源码；**未读** `spec.md`／`TaskContract.md`／`findings-*.md`。
- 本评审**未改动任何文件**（含清单）；三次变异逐次由文件级备份 `cp` 还原并核散列；
  **未使用任何 git 还原类命令**；跑过的 git 命令只有只读的 `git status --porcelain`、`git diff`（含 `-U0`／`--stat`）、
  `git rev-parse`、`git show HEAD:<path>`。未触碰主检出与 `.worktrees/client-debug-lab`。

### 一、本轮亲跑的命令与观测

同一条命令，五次：
`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo`

| 次 | 施加的变异 | 变异后散列 | 与执行者所记散列 | 观测 |
|---|---|---|---|---|
| 1 | 无（交付态，开工） | —— | —— | 524/60/52/6/320/236（另 2 跳过）/1036 = **2234 通过 / 2 跳过 / 0 失败，EXIT=0** |
| 2 | **MU-22 复现**：`SqliteCharacterItemTable.CarriesTheAvatar` 常量尾部加 ` AND 1 = 0` | `e5781485e986ecceae0a753dd9befdd262cfb85fc5a0220965fb7999e6b23ab1` | **逐字符相同** | 失败 2，红的是 `SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_RefusesToRewriteARowOntoAnAvatarAnotherCharacterHolds` 与 `.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision`，两条读数同为 `Assert.Throws() Failure: No exception was thrown` |
| 3 | **MU-5 复现**：`StoreBackedItemContainer.LandAsync` 的 `isAvatar ? ArrivingAvatar.Facts() : null` 改成 `null` | `6222be998e9fe2354c8df8c4e5be7ec67aad7a9e88a4be120ca059324a0c3948` | **逐字符相同** | 失败 **4**（`GrantedAvatarIsWholeTests` 2 ＋ `DraggedAvatarStaysWholeTests` 2），四条的消息同为 `System.IO.InvalidDataException : Avatar detail <数> is unavailable.` |
| 4 | **MU-2 复现**：`ItemSlotRuleService.AllowsQuickSlot` 去掉 `or KindAvatar` | `180538b4ca9a6debbbe64f2c9bf09a84539f5182215a1b4d775c84285b507864` | **逐字符相同** | 失败 5：`AvatarSlotRulesAfterTheKindChangeTests` 4（`A_quick_slot_refuses_the_avatar_the_archive_declares` 的 slotIndex 3／5／8 三个 ＋ `An_arriving_avatar_lands_where_the_earlier_kind_byte_landed_it`）＋ `ItemSlotRuleServiceTests.The_quick_slots_refuse_the_kinds_that_belong_to_another_list(itemKind: 8)`。`DraggedAvatarStaysWholeTests` 这一次是绿的 |
| 5 | 全部还原之后（收尾） | —— | —— | **2234 通过 / 2 跳过 / 0 失败，EXIT=0** |

还原后四个被备份文件的散列与开工时相同：`InventoryItemTables.cs` `60877044…`、`ItemContainers.cs` `8e7f0858…`、
`ItemSlotRuleService.cs` `0438efe9…`、`SqliteItemDomainStore.cs` `87f22d2c…`——四个都与清单第〇节那张十二行表的对应行逐字符相同。
`git status --porcelain` 收尾仍为 18 行。
五次运行的输出里 `ObjectDisposedException: 'SQLitePCL.sqlite3'` 未出现，
栈根为 `CharacterDatabaseSchema.KeepWritersAndReadersApart` 的失败未出现。

### 二、逐点核对结果

**1. 抽查的「有覆盖」逐条复现变红（3 条，分别是）**：RV-20／MU-22、RV-08 与 RP-03／MU-5、RV-01／MU-2。
三次的变异后散列与执行者所记**逐字符相同**，说明施加的是同一处同一改法；三次都变红，红集与所记相同。
**重跑读数变化的解释成立**：MU-5 第一版记 2 红、第二版记 4 红，本轮亲测为 4 红，多出的两条正是 TASK-08 新增的
`DraggedAvatarStaysWholeTests` 两条。MU-2 第一版与第二版同记 5 红，本轮亲测为 5 红，
且 `DraggedAvatarStaysWholeTests` 在那一次确实是绿的——这正是清单 RV-02／RV-03 保持「无覆盖」读数的理由，成立。

**2. RV-20（第 30 条之一）—— 成立，且「有覆盖」经本轮实测。**
`SqliteCharacterItemTable.UpdateOwnAsync` 的 `AND NOT EXISTS (SELECT 1 FROM items held WHERE ... AND held.item_uid <> @uid)`
与 `.InsertOwnAsync` 的 `WHERE NOT EXISTS(SELECT 1 FROM items held WHERE ...)`
确是两处不同的语句（两处都由常量 `CarriesTheAvatar` 拼进去），且与 `SqliteItemDomainStoreSession.FillAsync` 的守卫
（RV-05，走 `@expire IS NULL OR` ＋ `standing` 子查询）分属两条写入通路——本轮读两个文件核对。
可达链上的那一步也核过：`BindAvatar` 写的是
`parameters.AddWithValue("@avatarKey", detail is null ? DBNull.Value : AvatarKey(core));`，
`InventoryEntryAssembly.SaveAsync` 交的是 `var avatarDetail = item.Core.ItemKind == 8 ? item.AvatarDetail : null;`。

**3. RV-23／RP-08（本轮最要紧的一条）—— 这条链在基线上成立，本评审独立核过四段语句。**

- `CharacterRosterService.EquipmentCore` 的返回语句是
  `new(piece.ItemKind, piece.ItemId, Random.Shared.Next(1, int.MaxValue), 0, piece.Durability, …)`；
  `InventoryItemCoreState` 的第三个位置参数是 `Value`（`Server/src/Dfo.Server.Data/Inventory/InventoryModels.cs` 第 84 行）。
  即起始装备的 `Core.Value` 是抽出来的数。该方法在同文件 `InsertOwnAsync(EquipmentCore(piece), null, cancellationToken)` 处被调用。
- 基线 `565d33fe` 的 `IItemContainer.cs` 第 300／301 行是
  `/// <summary>How many the slot holds. For anything that does not stack, one.</summary>` 与
  `public int Count => Core.Value;`（`git show HEAD:` 核过，行号与清单所记相同）。
- `MoveItemSpaceCommandHandler.TakeOffAsync` 写的是 `await bag.PutAsync(command.DestinationSlotIndex, piece.Core, cancellationToken)`——
  装备表那一格的 core 原样进主列；`HandleAsync` 的 Main→Main 一支调 `bag.MoveToAsync(...)`。
- `StoreBackedItemContainer.DecideMoveAsync` 的夹取是
  `var moveCount = requestedCount <= 0 || requestedCount > moving.Count ? moving.Count : requestedCount;`，
  随后 `displaced is null` 时 `moveCount == moving.Count ? Relocate : Split`，
  `displaced` 非空且模板不同时 `if (moveCount != moving.Count) return (null, ItemMoveRefusal.PartialSwapNotAllowed);`。
- `ContainerSlotItem` 在 `Server/src` 里只有一处构造（`StoreBackedItemContainer.ToSlotItemAsync`），
  `WorldClass` 是非空参数，由 `ItemPlacementRuleService.ClassifyAsync` 给出，
  而该方法的返回语句是 `placement?.WorldClass ?? ItemWorldClass.Interchangeable`——
  普通装备取 `Equipment`，`IsPile` 为假，故起因 E 之后 `Count` 答 1。

四段合起来：**基线上一件起始装备被取下到背包、再在背包内以 `MoveCount = 1` 拖动时，
`moving.Count` 是那个抽出来的数、`moveCount` 被夹到 1，两者不等 → 目标格空走 `Split`、目标格有别的东西返回 `PartialSwapNotAllowed`；
起因 E 之后两者相等 → `Relocate`／`Swap`。这条链在基线上成立。**
清单把它记作「无覆盖」、把可达性标为「代码级读出，未实测」，并在第九节明写「没有把这样一件装备真的拖过一次」——
**这个标签既不过强也不过弱**（见下文第四节第 1 条：本评审还找到一条更宽的可达路径，方向与清单一致，只是清单没写到）。

**4. 反向扫的完整性 —— 本评审自己从代码出发重扫，没有找到第 31 条。** 扫法与结果记在第三节。

**5. 证据类型标签 —— 未见标错。** 逐条核过：RV-21 的「今天到不了」写作代码级读出并附 MU-23 的读数与它为什么记作无覆盖；
RV-18／RV-19 判据栏「未测定」、凭据栏「代码级读出」；RG-01「代码路径读出，非实机观测」、RG-02「代码级读出」；
第六节的结构事实抬头写「证据类型：代码级读出，不是变异实测的结论」；第七节归因表每行都带证据类型一栏；
3.6 的 RX-01／RX-02 写「有覆盖」并点名 MU-20 那次的三条红与各自的失败读数——与第〇节写死的定义相符。

**6. 记法口径 —— 逐句读，未用词表；未见全称量词，未见支撑不足的一般命题。**
第六节的「候选集是一切 `avatar_expire_date` 非空的旧行」是 A-06d 验收条件第 7 条要求的原措辞，且标了证据类型；
第九节列了七条「不足以确定」，其中 RV-23／RP-08 与起因 E 的回归面各占一条。
引文核对：A-06e 的「1028 个既有用例全绿」回到 TASK-04（QR-04-1 非阻断观察第 ① 条与其第三节）核对，**逐字相符**；
MU-3 的散列 `3593447673…` 与 TASK-04 记的变异 B 后散列逐字符相同；
`<summary>` 那句引文与 `git show HEAD:` 的第 300 行逐字相符。**未见把派工指令当作持久记载引用。**

**7. 字节锚与计数 —— 本评审独立复核，成立。**
`sha256sum Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs`
与 `git show HEAD:<同一路径> | sha256sum` 同为 `1424a4a82b28ec94a75145b237d89117d61b65b251722819b8586d15db1b1c69`；
`git diff HEAD --stat` 对该路径输出为空；`AcceptedKnightFrameSha256` 的字面量表是 62 行 SHA-256（第 32–93 行，本轮亲自数过）。
全量 **2234 / 2 / 0、EXIT=0**，开工与收尾两次相同。
+18 的归因由判据文件本身复算（口径：`[Fact]`／`[SkippableFact]` 各计 1，`[Theory]`／`[SkippableTheory]` 按 `[InlineData]` 条数计）：
`AvatarSlotRulesAfterTheKindChangeTests` 1 个 `[SkippableTheory]`＋3 个 `[InlineData]`＋1 个 `[SkippableFact]` ＝ 4、
`DraggedAvatarStaysWholeTests` 4 个 `[Fact]`、`GrantedAvatarIsWholeTests` 3 个 `[SkippableFact]` → Application **+11**；
`TwoAvatarsDoNotShareOneIdentityTests` 3 个 `[Fact]` ＋ 已跟踪判据文件 diff 里新增的唯一一个 `[Fact]`
（`SqliteItemDomainStoreTests.AWriteThatCarriesAnAvatarsFactsPutsThemOnTheRowTheWorldCreates`）→ Persistence **+4**；
`RefusedEquipIsRecordedWithItsTemplateTests` 3 个 `[Fact]` → Protocol **+3**。
`git diff HEAD -U0 -- Server/tests` 里以 `+` 开头的 `[Fact]`／`[Theory]`／`[InlineData]` 行合计 1 行，
即已跟踪判据文件只多出这一个用例，与 `ItemClassificationTests.cs` 记「+0」一致。3＋4＋11 ＝ **+18**。

### 三、本评审自己做的反向重扫 —— 没有找到第 31 条

扫法（五条轴，逐条给口径与我读到的行数，读者可复算）：

1. **种类字节轴**：`Server/src` 下含 `ItemKind` 且带 `==`／`!=`／` is `／`switch` 的非注释行，我读到 **31 行**
   （执行者记的是 29 行——这是计数口径的差，不是遗漏）。其中 4 行读的是 `DropRules.KindOf` 给的 `DropItemKind` 枚举
   （`DropRules.KindOf` 一处 ＋ `DropRuleService` 三处），不受起因 A 影响；剩下 27 行逐个对照清单，
   **每一行都能在 A-01..A-36 里找到**（`SelectCharacterPreparationService` 的那一行是 A-34、
   `InventoryEntryServices` 的那一行是 A-35、`GameInventoryDelivery` 的 `ItemKind == 2` 是 A-36、
   幸存者匹配的那一行是 A-16／RV-14）。
2. **`Core.Value` 轴**：`Server/src` 下含 `Core.Value`／`core.Value`／`.Core with { Value` 的非注释行逐个读过。
   `InventoryEntryAssembly` 那个投影的 `Where` 确实是 `item.SlotIndex is > CarriedGoldSlot and <= 2`（本轮读过，清单的排除理由成立）；
   `SelectCharacterPostEntryServices` 的负重求和确实限于 `is 2 or 3 or 4 or 7 or 9 or 10 or 11`；
   `HoldAsync` 绑的是 `pile ? PileOf(item.Core) : 1`，非堆恒 1，起因 B 动不到它。
3. **六列事实轴**：`Server/src` 下含 `avatar_expire_date` 的行逐个读过（`InventoryItemTables.cs` 七处、
   `SqliteItemDomainStore.cs` 四处、`CharacterDatabaseSchema.cs` 一处）。
   `SqliteWorldItemTable.UpdateAsync` 是一条不带撞号守卫的六列写入，但 `IWorldItemTable` 在 `Server/src` 下
   除声明与实现外无读点（与 RP-09 同类），不是本轮新增或改变的路径。
4. **起因 E 轴**：`ContainerSlotItem` 类型表达式的 `.Count` 逐处读过（计数说明见第四节第 3 条），
   逐处问「相对本轮开工基线它的答案变了没有」，答案与 E-01..E-11 一致。
5. **另做一轴（执行者未用）**：`InventoryEntryAssembly.cs`／`ItemContainers.cs`／`SqliteItemDomainStore.cs`／
   `GameInventoryDelivery.cs` 四个文件里的 `throw` 逐条读过，逐条问「本轮之后一件时装或一件非堆到得了它吗、答案变了吗」。
   未读到清单之外的新增或改变项。邮箱那一支（`WriteMailboxSummary` 的 `avatar`／`stackable`／`creature`）判的是
   `attachment.ItemKind` 这个**字符串**列，不是 `Core.ItemKind`，起因 A 动不到它。

**结论：没有第 31 条。** 36 条之外，我没有读到本轮新增或改变的拒绝／放行判定。
下面四条是补强，不是遗漏项，记为非阻断。

### 四、非阻断观察（记录，不要求本轮改）

共 4 条，分别是：

1. **RV-23／RP-08 的可达性比清单写的更宽——方向与清单一致，只是清单没写到。**
   `GroundPickupSlice.CarryOverAsync`（`Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs`）
   在一件**装备**落进背包之后，立刻把那一格重写成
   `landed.Core with { Value = checked((int)found.Instance), Durability = found.Durability }`，
   `found.Instance` 由 `GameDropIdentity.ForKill(run, actorSequence)` 或 `.ForDeclaredObject(run, objectIndex)` 给出。
   即**从地上捡起来的装备**在背包里带着一个掉落身份号做 `Core.Value`，
   走的是与 RV-23／RP-08 同一条 `DecideMoveAsync`。证据类型：代码级读出（本轮读该方法与 `Announced` 核对，未实测）。
   清单 RV-23 的可达性栏只写了「起始装备经 `TakeOffAsync` 进主列」这一条路径。
   补上这一条会把 RV-23／RP-08 的可达性从「新建角色的起始装备被取下之后」扩到「打怪捡装备之后」，
   即起因 E 顺带消掉的那个行为差在基线上的触及面比清单目前写的更常见。
   这不改变清单的判断（可达、无覆盖），只让可达性一栏更强。
2. **一个本轮答案改变、但未列进正向扫的判定点**：
   `SqliteCharacterItemTable.Avatar(SqliteDataReader reader, int ordinal)` 的
   `reader.IsDBNull(ordinal) ? null : new AvatarDetailRow(...)`。
   起因 A ＋ B ＋ C 之后，一件发放来的时装的行六列非空，这一句由答 `null` 改为答一个 `AvatarDetailRow`。
   它的下游分支（`WithAvatarDetail` 的三条检查）在清单里齐全（A-20..A-23、RV-07／RV-08／RV-09），
   所以没有拒绝路径被漏掉；漏的是这一个判定点本身，属正向扫的完整性，不是反向扫的遗漏。
3. **两处计数口径的小出入，两处都可由读者复算，不影响任何结论**：
   其一，起因 E 表的口径写的是「取一个静态类型为 `ContainerSlotItem` 的表达式的 `.Count`」，
   按这个口径我数到 **12 条语句**（`SplitAsync` 源格写值、`MergeAsync` 的三处、`ConsumeAsync` 的 `remaining`、
   `DecideMoveAsync` 的夹取／空格分支／`displaced.Count`／`moveCount != moving.Count`、`PilesOf`、
   `DailyRefillService` 的 `held.Sum`、`ItemUseEffectDomainService.ApplyAsync` 的 `var remaining = item.Count;`），
   清单报 11 处，差在 E-11 把后两句合成一条、而 E-10 记的是下游 `ItemPile.Count` 的读点。
   清单在 E-08／E-11 两行里写明了合记，读者可复算，故只是口径而非错记。
   其二，种类字节轴我数到 31 行、清单记 29 行；两条差额（`SelectCharacterPreparationService` 与
   `InventoryEntryServices` 那两行）在清单里以 A-34／A-35 各占一行，**没有被漏掉**。
4. **RV-21 的「新增绑定来源」只点了一条**：清单说起因 C 之后 `SqliteItemDomainStoreSession.FactsOf`
   把写入自带的 `byte[]` 原样绑上去，是那条 CHECK 的新来源。同一轮里 `BindAvatar`
   （`InventoryItemTables.cs`，RV-20 那两条语句用的就是它）也把调用方交来的 `detail.JewelSocket` 原样绑到 `@socket`，
   而一件发放来的时装是本轮才走到那条通路的（理由与 RV-20 同）。即那条 CHECK 今天有两条新到得了的绑定来源，
   清单只写了一条。两条今天都到不了（唯一生产者 `ArrivingAvatar.Facts()` 交 30 个零字节），
   所以 RV-21 的读数（今天到不了、无覆盖）不变。

另：清单 RV-23 写「本轮回去读过的五份记载里，没有一份就普通装备讲过这条链」。
本评审核过：QR-08-1 的「逐处判断」第 1、7 处是就**非堆**这一类讲的（普通装备在其内），
TASK-08 执行结果第 1 节写了「起始装备的身份由 `CharacterRosterService.EquipmentCore` 以同样方式抽取」。
两处都没有把「起始装备 → `TakeOffAsync` → 背包 → `DecideMoveAsync`」这条链写出来，
所以清单那句按字面成立；但它读起来比记载实际留白的程度更强一点。文风级，记为非阻断。

### 五、边界

- 未改动任何生产文件、判据文件或清单文件；本 Task 文件只**追加**了这一个小节，既有小节一句未改、一句未删。
- 三次变异逐次由文件级备份还原并核散列，四个备份文件末态与开工首态逐行相同；
  收尾全量 2234 / 2 / 0、EXIT=0；`git status --porcelain` 前后均 18 行。
- 未建 Git 检查点、未提交；**未使用任何 git 还原类命令**。
- 未触碰主检出与 `.worktrees/client-debug-lab`。
- 本判定只说这一个 Task Unit 是否可以走执行者的接受逻辑，**不是最终质量批准**。


## Controller Disposition D-06b — 接受 QR-06-2 的 `passed`

**判定**：接受。重跑的执行结果、自查、QR-06-2 的 `passed` 与交付物
`behavior-change-list.md` 指向同一轮。TASK-06 置 `done`。

**两个关键问题都有独立答案**：
- **RV-23／RP-08 那条链在基线上成立**（评审独立核了四段语句）；
  证据类型标签「代码级读出、未实测」判为**不过强也不过弱**。
- **没有第 31 条**（评审按五条轴重扫：种类字节、`Core.Value` 全部读写行、
  六列事实全部出现处、`ContainerSlotItem.Count` 全部读点、四个受影响文件的全部 `throw`）。

**重跑是必要的，有数为证**：19 次旧变异重跑后 MU-1 由 7 红变 10、MU-4 由 2 变 3、
MU-5 由 2 变 4、MU-7 由 7 变 8。沿用第一轮的读数会交出一份过时的清单。

### 一条要转给 TASK-07 的影响面事实

QR-06-2 指出：`GroundPickupSlice.CarryOverAsync` 在一件**装备**落进背包后
立刻把该格重写成 `landed.Core with { Value = checked((int)found.Instance), … }`。
即基线上 RV-23／RP-08 描述的那个毛病**不只影响建号的起始装备，
而是每一件从地上捡起来的装备**。清单的可达性栏只写了起始装备那一条路径。
**这不推翻清单的判断（可达、无覆盖），只让它更强。**

### 三条非阻断，转下游完整变更评审

`SqliteCharacterItemTable.Avatar` 的 `IsDBNull` 是本轮答案改变但未列进正向扫的判定点
（下游分支已齐全）；RV-21 的新绑定来源实际有两条（`FactsOf` 与 `BindAvatar`）；
`Count` 读点按其自述口径评审数到 12 条语句而清单报 11 处（合记已在行内写明，可复算）。
