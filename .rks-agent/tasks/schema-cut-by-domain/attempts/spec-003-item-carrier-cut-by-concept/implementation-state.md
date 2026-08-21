# Implementation state — spec-003

八个实施任务已全部完成，全量套件全绿。这份记录留下每一步的证据、实施中改判裁定的两处，以及两处被取代的用例与理由。

基线：`5aba3a9dcea2b302797eb33097ae923a7a824a45`。未产生任何任务级提交。

## 已完成

| 任务 | 结果 | 证据 |
|---|---|---|
| T-001 五处悬置事项定案，回写裁定 | 完成 | 见下「实施中改判裁定的两处」。D-5、D-6、D-7、D-16 按计划定案；**D-15 定案与计划相反**，理由是清点时找到了构造该情形的现存用例 |
| T-002 目标形状、约束落点与称号册变换的守卫先行 | 完成 | 新增 `Server/tests/Dfo.Server.Persistence.Tests/Schema/ItemCarrierShapeTests.cs`，27 条断言。列清单取自裁定的逐表列表。先跑 27/27 全红，逐条确认失败原因是「目标表不存在 / 约束缺失」而非断言写错 |
| T-003 清点三条规则与跨角色写入的用例，补齐缺口 | 完成 | 清点结果见下。缺口两条已补：新增 `Inventory/AvatarCarrierRulesTests.cs`，5 条断言，**在改动任何生产代码之前跑通 5/5** |
| T-004 表征播种改到目标形状，期望值原样不动 | 完成 | 两份表征的 diff 只落在 `INSERT` 上，期望值的唯一改动是仓库两行的代理键（见下） |
| T-005 库形状改为目标形状 | 完成 | 九张源表的声明换成四张目标表；索引按裁定落位并加两个部分唯一索引；可补列清单删去 36 条指向已消失表的条目。表数 49 → 44。`ItemCarrierShapeTests` 27/27 转绿 |
| T-006 按表接口与读写路径 | 完成 | `items` 三个读取类、`character_rentals` 一个、拆分后的两个；退役 `ICharacterAvatarDetailTable` 与三个 legacy 接口；DI 随之更新。全解决方案构建**零错误零警告** |
| T-007 领域组装与迁移守卫退役 | 完成 | 领域记录文件 `git diff` 为空；两处迁移守卫连同上一代两张表的读取删除 |
| T-008 行为保持证据 | 完成 | 见下 |

## 实施中改判裁定的两处

**D-15 由「不恢复」改判为「恢复」。** 计划里我给的初判是不恢复：时装详情的主键保证的是「一个时装 UID 至多一条描述」，而在 `items` 上加唯一索引保证的是「一个时装 UID 至多一件物品」，后者严格更强，会拒绝今天的库接受的状态。做 T-003 的清点时发现这个判断错了——`SqliteEntryInventoryMutationRepositoryTests.DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision` **今天就构造了这个情形**：角色 70 持有 UID 9000 的时装，角色 60 的租约试图插入携带同一 UID 的物品，今天被拒绝并整体回滚，用例还断言角色 70 的时装未被触碰。所以这条保证既不是理论上的、也不是够不着的，而是一条有用例覆盖的现行行为。合并后不给它继任者，那次插入就会成功——既是行为变更，又丢掉一条已覆盖的断言。已按 D-15 自己提的办法恢复：`items.avatar_uid` 一列加 `idx_items_avatar_uid` 部分唯一索引。裁定已改写，`items` 由 15 列变 16 列，列账本与附录同步更新。

**M-2 的一个后果被补记。** 裁定说时装的附加事实与物品同寿，这对物品这个**概念**成立、对物品这一**行**不成立：`SqliteItemStoreSession` 移动物品的办法是删掉旧行插入新行，并且写明了这样做「安全」的理由——附加事实挂在核心块里的时装 UID 上，在另一张表里。合并后这个理由不再成立。`SqliteItemStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 断言移动后事实还在，所以 store 现在在删除前按时装 UID 读回这些事实、插入时写回去。已记入裁定的「Two consequences carried over from the merge itself」。

## T-003 的清点结果

| 规则 | 今天由谁守 |
|---|---|
| 非时装行不去找时装列 | `SqliteInventoryEntryRepositoryTests.LoadAsync_Preserves...`（间接）；**新增** `AvatarCarrierRulesTests.AnItemThatIsNotAnAvatarIsLoadedWithoutAvatarFacts` |
| 仓库里的时装不带时装列 | `InventoryLoadCharacterizationTests.AnAvatarInTheCargoIsCarriedWithoutADetailRow`；**新增**同名断言一条 |
| 角色的时装缺时装列是错误 | **此前无人守** → 新增 `ACharactersAvatarWithNoFactsIsRefusedRatherThanServedEmpty` |
| 角色的时装 UID 为零或负被拒绝 | **此前无人守** → 新增 `ACharactersAvatarWithoutAUsableUidIsRefused`（0 与 -1 两例） |
| 一个角色写不到另一个角色的时装 | `DirtyLeaseSave_PersistsAvatarDetailsAuthoritativelyAndRollsBackUidCollision`（已覆盖，且是 D-15 改判的依据） |

## 两处被取代的用例与一处被删的用例

| 原用例 | 处置 | 理由 |
|---|---|---|
| `LoadAsync_LegacyOnlyInventoryRequiresMigration`（2 例） | 删除 | 它守的是上一代两张表存在且有行时拒绝入场；两张表已退役，情形结构上不可构造。该后果（旧形状的库载入得到空背包而不是拒绝）由裁定明确记录为决定 |
| `LoadAsync_RejectsInvalidStoredCharacterItems(wrong-owner)` | 取代为 `LoadAsync_LeavesOutTheItemsOfEveryOwnerButTheOneAskedFor` | 读取现在按 owner 过滤，别人的行根本不会被读到，没有可拒绝的东西。**新用例守的是更要紧的一件事**：过滤真的把别人的物品排除在外——过滤若丢了 owner，一个角色会拿到另一个角色的背包，而后面没有任何拒绝会发现 |
| `LoadAccountAsync_RejectsInvalidStoredAccountCargo(duplicate)` | 取代为 `TheCargoRefusesASecondItemInASlotItAlreadyHolds` | D-16 恢复了仓库自己的槽位唯一性，重复行在写入时就被拒，载入拒绝那条路不可达。新用例断言的正是恢复后的约束 |

`RequireUniqueSlots` 仍由角色侧的 `duplicate` 一例覆盖，未失去守卫。

## 一处被改动的表征期望值

仓库两行的 `StorageEntryId` 由 1、2 改为 5、6。角色物品与账号仓库过去是两张各带自增键的表，各自从一数起；现在是一张表、一个计数。这是两个键空间合并成一个的必然结果，二者不可能同时保留。该代理键**不出现在任何下发帧里**，只出现在拒绝消息的文字中（`Missing($"AvatarDetail[{item.StorageEntryId}]")` 一类）。理由已写进那份表征用例的类注释。

## 计划内的有界调整

- T-002 与 T-003 的次序对调了一半：T-003 的清点与补齐必须在任何生产代码改动之前跑绿，而 T-002 的形状守卫是先红的，两者放在同一个 `--filter` 下会互相干扰。做法是先跑 T-003（全绿），再加 T-002 的守卫（全红）。不改变任何验收。
- 时装 UID 冲突的拒绝**写成了写入语句的条件**（插入用 `ON CONFLICT(avatar_uid) ... DO NOTHING`，更新用 `NOT EXISTS`），而不是交给唯一索引抛。理由：交给索引抛会把 `InvalidDataException` 变成 `SqliteException`，而那条用例断言的是前者；把条件写在语句里，写入触碰不到行，调用方原有的 `touched != 1` 检查照旧抛出同一个类型。

## 行为保持证据

- **全量 1092 通过 / 2 跳过 / 0 失败。** 基线 1061/2/1（那一条红是 `Host.Tests` 的已知满载假红，本次它通过了）。用例数 1064 → 1094，Δ+30 全部可解释：+27 形状守卫、+5 时装规则守卫、+2 取代用例、−2 迁移守卫用例、−1 wrong-owner、−1 仓库 duplicate。
- **三份整记录表征用例通过**，期望输出的唯一改动是上面那两个代理键；其余一个字节未改。
- **领域记录未被触碰**：`InventoryModels.cs` 与 `IInventoryEntryServices.cs` 的 diff 为空。
- **可证伪性两次**：把 `list_type` 与 `slot_index` 在 `items` 的读取里对调 → 表征立刻红；把称号册逆变换的减 19 去掉 → 表征立刻红。两次还原后均全绿，还原文件与备份逐字节相同。
- **一表一类**：脚本清点 `Server/src/Dfo.Server.Persistence` 下 69 个类（忽略注释，只看代码里出现的表名），**零个类指名超过一张表**。
- **构建零警告**：`dotnet build Server/86JP.Server.slnx` 零错误零警告。

## 评审第一轮的整改

第一轮判 `changes_required`，两条 A 级，都成立，都已修。

**A-1：一次租约保存会删光该角色的称号册。** `DeleteOwnExceptAsync` 合并后是「删掉这个角色 `items` 里不在保留名单上的每一行」，而保留名单只可能来自五个背包容器——称号册的 19..23 永远不在其中。于是每次保存把称号册整片删掉，下一次选人下发五个空类目。改动前不可能：那条 DELETE 指名 `character_new_items`，称号册在另一张表且全仓无写入。**修法**：给两个分支都加上 `AND list_type NOT BETWEEN 19 AND 23`，与同一个类里读取那一侧的过滤对称。

**A-2：称号册变换的四条断言从没写。** 计划 T-002 第 4 步点名要它们，我做了第 1、2、3、5 步而漏了第 4 步，`ItemCarrierShapeTests` 27 条里一条都没有。这个空洞正是 A-1 漏过去的原因——全仓没有任何角色同时持有物品行与称号册行。**修法**：新增 `Server/tests/Dfo.Server.Persistence.Tests/Inventory/TitleBookCarrierTests.cs`，8 条断言：五个类目各自存为 19..23 且读回原值（Theory 五例）、同一角色的背包 0 号槽与称号册 0 类 0 号槽共存且各走各的载入、背包读取排除 19..23、以及保存背包不动称号册（A-1 的守卫）。

两条整改都验过可证伪：把 sweep 的过滤去掉 → `SavingTheInventoryLeavesTheTitleBookWhereItIs` 立刻红；把读取的过滤去掉 → 三条称号册守卫立刻红。两次还原后全绿，还原文件与备份逐字节相同。

另接受评审的一条 B 级并改了裁定：D-15 那一段原写「恢复的强度不多不少」，在**同一个角色持有两行携带同一 UID** 这半边不成立——今天两行共用一条描述、载入通过，恢复后第二行被拒。已改写为「在要紧的那半边等同、在另一半边更严，并说明为何接受」。

全量重跑：**1100 通过 / 2 跳过 / 0 失败**（较整改前 +8，正是新增的八条）。

## 当前状态

工作区 24 个已跟踪文件改动加四个新文件（`ItemCarrierShapeTests.cs`、`AvatarCarrierRulesTests.cs`、`TitleBookCarrierTests.cs`，以及本 attempt 的过程工件），全量全绿，未产生任务级提交。第一轮评审的两条 A 级已修，待第二轮确认。
