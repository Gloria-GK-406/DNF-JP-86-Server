# TASK-05 — 按代码实况更新 AGENTS.md

## Task Unit Contract

- Revision: 1
- Outcome and references: 覆盖 RQ-010/RQ-011 / AC-014/AC-015 / plan T-005。
- Owned boundary:
  - 只改 `AGENTS.md`。
  - 记法遵循该文件自己的「记什么，不记什么」：过期的现状记载**直接改掉，不另起修正案段落**；不指向 `.rks-agent/`；不写过程、不写「本轮」；一个由多项相加得出的数要么当场写出加法要么不写。
  - 要点：装载侧那条按复核结论重写（`RequireUniqueSlots` 确在 `LoadCharacterAsync`/`LoadAccountAsync` 两条路径上按 (容器, 槽) 去重；`core_uid` 原先无检查、本次已补）；三条取值校验与缺口三的覆盖状态如实更新；**不得声称未被消掉的缺口已消掉**（例如契约兽互换、`SqliteWorldItemTable` 仍是缺口）。
- Required checks:
  1. CK-006（＝AC-014）：改动段落逐句与所述代码位置对照可核。
  2. CK-007（＝AC-015）：`git diff` 确认 `SelectCharacterAcceptedTranscriptTests.cs` 与 `CommandHandlerArchitectureTests.cs` 相对 HEAD 零差异。
  3. 全量判据退出码 0，跳过数仍为 2。
- Dependencies: TASK-01, TASK-02, TASK-03, TASK-04（记载必须描述最终实况）

## Result (round 1)

- **Changed:** 仅 `AGENTS.md`（7 增 16 删）。
  - 「已知缺陷与覆盖缺口」里删去两条**已不成立**的条目：装载侧缺对称拒绝、三条取值校验全仓零命中。缺口消失了就不再是缺口——按该文件自己的「过期的记载：现状图直接改掉，不另起修正案」。
  - 「两条判据的名字不再描述它证明的东西」改为「一条」，删去 `Relocate_Keeps…` 那一小条（判据已改名，且「那六个值被搬到新行上仍然零覆盖」已不成立），保留 `EveryIndexThatNamedARetiredTableIsGoneAndTheOneThatMovedWasRebuilt` 那条。
  - 在 `item_slots` 段（该表的约束正好写在那里）补四行：**`core_uid` 上没有唯一性**，所以「一件东西只在一个位置」由代码说，保存侧与装载侧各一处，且两处都数世界行而不数 `ItemIdentity`。
- **Why:**
  - 事实落在 `item_slots` 段而不是另起一节：那一段正是列举该表约束的地方，「哪条由约束管、哪条由代码管」应当在同一处读到（AGENTS.md「每种事实只有一个归处」）。
  - **曾经写过一段「时装搬迁两条路径」的说明，随后撤掉。** 它被插进了「已知缺陷与覆盖缺口」标题下——那里放的是缺口，不是已有的覆盖；而且两条判据的文档注释已各自说明机理并互相指路，写进 AGENTS.md 就是抄第二遍。撤掉后该文件净减 9 行。
  - **没有**声称任何未被消掉的缺口已消掉：契约兽互换、`SqliteWorldItemTable`、`character_condition` 五列等条目原样保留。
- **Checks:**
  - CK-006：`grep -n '装载侧没有\|全仓无断言\|后者仍然零覆盖\|两条判据的名字' AGENTS.md` 无命中。新增四行逐句对应 `InventoryEntryAssembly.SaveAsync`（世界行身份集合大小比较）与 `RequireOneNameEach`（两条入场读），均可当场核对。
  - CK-007（＝AC-015）：`SelectCharacterAcceptedTranscriptTests.cs` 与 `CommandHandlerArchitectureTests.cs` 的 SHA-256 与 `git show HEAD:` 逐字节相同——`1424a4a8…b1b1c69` 与 `ab9107bc…bdb29d9714`。
  - 全量判据：退出码 **0**，**2252 总 / 2250 通过 / 2 跳过 / 0 失败**。逐项目：Pvf 60、Networking 52、Protocol 528（524＋4）、Host 6、Persistence 329（320＋9）、Infrastructure 236＋2 跳过、Application 1039（不变）。落盘：`scratchpad/task05-full.txt`。

## Pending finding (self-check, before IQR verdict)

**新写的那句话过度声称了。** 我补进 `item_slots` 段的「『一件东西只在一个位置』这条由代码说，两侧各一处」会被读成「这条不变量已被全面守住」，而实际上：

`SqliteCharacterItemTable.ReadByCharacterAsync`（`InventoryItemTables.cs:75`）带 `AND place.list_type NOT BETWEEN 19 AND 23`，称号书那五个容器由**另一条读取路径**答（同文件 `:387-395` 的 `ReadAsync` → `TitleBookRow`，同样 `JOIN items ON held.item_uid = place.core_uid`）。所以一条称号书位置行与一条主背包位置行指名同一个世界行时，**两处 `RequireOneNameEach` 都看不见**。

这不是本次引入的缺陷，也不在本任务范围内（跨两条查询做关联属于设计改动）。但按 AGENTS.md「反向扫要扫写入方」——只说清拒绝在哪、不说清它够不到哪，正是那条教训点名的半截话。

**下一步动作**：待 TASK-05 的独立评审回执后，把这一句收紧到只声称它实际覆盖的范围。现在不改，是为了不与正在读该文件的评审抢改。

## Independent Quick Review (IQR-05-1)

- Reviewed execution round: Result (round 1)
- Contract revision: 1（无修正案）
- Owned diff identity:
  - `git diff --numstat -- AGENTS.md` ＝ `7 16`（三段 hunk：`item_slots` 段 +4、删两条缺口条目 −8、「两条→一条」段 −8/+3），与 Result 自述的「7 增 16 删」一致；`AGENTS.md` 现 `sha256 8ce7b3ae832cc87de2f481452d8a19d925f1718bc1cdfca882c94a12c949c1b1`。
  - `git status --short` 里其余四处（`InventoryEntryAssembly.cs`、`AvatarCarrierRulesTests.cs`、`SqliteItemDomainStoreTests.cs` 与两个未跟踪判据文件）属 T-001..T-004，不在本单元边界内；`SelectCharacterAcceptedTranscriptTests.cs`、`CommandHandlerArchitectureTests.cs` 均不在 `git status` 中，AC-015 成立。
  - 全工作树无 `.bak`／`.orig`／`.rej`／`.sha` 残留。

### Verdict: `passed`

### 复核到的事实

**一、`item_slots` 段新增四行（`AGENTS.md:532-535`）逐句核对，无一句需要采信自述。**

- 「`core_uid` 上没有唯一性」——`CharacterDatabaseSchema.cs:200` 的 `item_slots` 声明里唯一性只有 `UNIQUE(owner_scope, owner_id, list_type, slot_index)`；索引段 `:245` 的 `idx_item_slots_core ON item_slots(core_uid)` 是**普通索引**，同段 `:246` 的 `idx_item_slots_account_slot` 才带 `UNIQUE`。三条具名 CHECK（`:200`）也无一涉及 `core_uid` 的跨行唯一。**成立。**
- 「`SaveAsync` 落位前比世界行身份集合的大小」——`InventoryEntryAssembly.cs:224-229`：`var claimed = settled.Select(item => item.Identity).ToHashSet(); if (claimed.Count != settled.Count) throw`。它位于 `:234` 的 `ClearOwnPositionsAsync` 与 `:236` 起的 `HoldOwnAsync` 循环**之前**，「落位前」成立。`SettledItem.Identity` 是 `int`（`:256-266` 记录声明，`<param name="Identity">The row it was written to</param>`），取自 `:213` 的 `checked((int)written)`，即**世界行**而非 `ItemIdentity`。**成立。**
- 「`RequireOneNameEach` 在两条入场读取到行之后」——两处调用在 `:311`（`ReadCharacterItemsAsync`，被 `LoadCharacterAsync:63` 调用）与 `:339`（`ReadCargoItemsAsync`，被 `LoadAccountAsync:96` 调用），**确是两条入场读**。两处都是先 `var rows = await …ReadBy…Async(...)`，再 `RequireOneNameEach(rows.Select(row => row.ItemUid), …)`，再 `foreach (var row in rows)`——**确在读到行之后、逐行处理之前**。守卫体 `:736-747` 以 `HashSet<long>.Add` 判重，键为 `row.ItemUid`，**是世界行**。**成立。**
- 「堆叠物没有身份，按身份数会把它漏掉」——`IdentityOfAsync`（`:278-286`）对 `ItemWorldClass.Interchangeable` 返回 `null`。这不只是推理：TASK-01 第 3 轮把两处调用变异成跳过堆叠物，**只红 `APileNamedInTwoPositionsIsRefusedAlthoughItCarriesNoIdentity` 一条**（该文件 244-249 行）。**成立。**

**二、删去的两条缺口确已被消掉，且判据可证伪。**

- 装载侧对称拒绝：新判据文件 `OneThingInTwoPositionsIsRefusedOnLoadTests.cs` 有 4 条（角色、货柜、堆叠物、正常数据不变），TASK-01 记录「删去两处 `RequireOneNameEach` 调用 → 三条拒绝判据变红、正常数据那条仍绿，还原 `sha256sum -c` ＝ OK」。
- 三条取值校验：`EntryWriterAvatarShapeTests.cs` 以 `Assert.Equal` 比全文消息钉住 `Non-avatar inventory item carries avatar detail.` 与 `Avatar jewel socket must contain exactly 30 bytes.`（`:48`、`:60`，后者 `[Theory]` 取 29／31 两侧）；`AvatarCarrierRulesTests.cs` 新增三条分臂判据加一条正例。TASK-02 记录变异 A／B 各自变红，TASK-03 记录三臂逐子句变异「对应那条红、另两条绿」。**两条都不是「删了个仍然成立的缺口」。**

**三、「两条→一条」的编辑正确，且没有伤到幸存的那一条。**

- `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 在 `Server/` 下**零命中**（已改名为 `Relocate_LeavesASparedAvatarsRowStandingWithItsFactsUntouched`），故该小条描述的对象已不存在。
- 「那六个值被搬到新行上仍然零覆盖」也已不成立：`AnAvatarWhoseRowIsRebuiltIsHandedItsSixFactsAgain` 先断言 `Assert.NotEqual(standing, rebuilt)`（走重建而非幸存），再对六列逐值断言；TASK-04 记录 `CarriedAvatarsAsync` 恒空变异下**由 HEAD 的红 1 条变为红 2 条**。
- 幸存的那条（`AGENTS.md:598-600`）文字与 HEAD 逐字相同，只是提级为无子弹的续行。其内容仍准确：`ItemCarrierShapeTests.cs:342-344` 的正向断言确以 `entry.Value.Contains("ON item_slots(core_uid)")` 命中新增索引。

**四、没有把任何未消掉的缺口写成已消掉。**

- 契约兽互换（`:587`）、`SqliteWorldItemTable`（`:588-597`）、`character_condition` 五列（`:560`）、「没有迁移机制」（`:561`）、「时装取得下、穿不上」（`:562` 及其后整节）、「三处『结论对，但撑着它的不是代码』」（`:564-583`）——`git diff` 三段 hunk 一处也没有碰它们，逐条对照仍在原位、原文。
- TASK-03 复核指出的 HEAD `AGENTS.md:589`（`git show HEAD:AGENTS.md | sed -n '589p'` ＝ 「…**在 `Server/tests` 全域零命中**；」）**正是被删的那一行**，落在删去的第二条缺口内。
- 删除未留悬引：全文 `装载侧`／`保存侧` 零命中，`入场读` 仅余 `:534` 新增行与无关的 `:218`；`:747-760` 的「协议 handler 层普遍没有判据」清单与本次四条判据不相交（`GameInventoryDelivery` 不是 `ClientMessageHandler<>` 子类，19 个名字无一相关），未因本次改动变旧。

**五、记法（AGENTS.md「记什么，不记什么」）逐条过。**

- 无「本轮／这一轮」、无指向 `.rks-agent/`、无过程记账（全文命中只有 `:920` 那条规则本身）。
- 新增四行不含任何数，无「小计不写加法」的问题。
- 净 −9 行，是**净简化**而非追加评注；事实落在 `item_slots` 段（该表约束的唯一归处）而非另起一节，合「每种事实只有一个归处」。
- Result 里自述「曾写过一段时装搬迁说明随后撤掉」——工作树上确实没有该段，撤销是完整的。

### findings

无。

### 附：一处越界事实，不构成本单元缺陷

`Docs/Refactor/TableOwnershipRuling.md:776` 仍写着旧判据名 `SqliteItemStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`。该文件是带日期的裁定稿，按 AGENTS.md「过期的记载怎么办」属**保留原文、在开头补落地状态**那一类，且不在本单元 owned boundary（只改 `AGENTS.md`）内。记在此供控制方决定是否另开单元。

## Result (round 2 — 自查发现的过度声称，＋ 越界观察的处置)

- **Changed:** `AGENTS.md` 再加三行，把上一轮那句收紧：点明入场那两处**够不到称号书**——`ReadByCharacterAsync` 带 `list_type NOT BETWEEN 19 AND 23`（`InventoryItemTables.cs:75`），19..23 那五个容器由 `SqliteCharacterTitleBookTable.ReadAsync`（同文件 `:381`、`:387-395`）单独读，两条查询都 `JOIN items ON held.item_uid = place.core_uid`，所以一条称号书位置行与一条别处的位置行指名同一个世界行时，两处 `RequireOneNameEach` 都看不见。
- **Why:** 上一轮写的「两侧各一处」会被读成「这条不变量已被全面守住」。这不是本次引入的缺陷，跨两条查询做关联属于设计改动、不在边界内；但只说拒绝在哪、不说它够不到哪，正是 AGENTS.md「反向扫要扫写入方」点名的半截话。
- **写这三行时当场犯了一次同类错误并改掉**：先写成 `SqliteTitleBookTable`，实际类名是 `SqliteCharacterTitleBookTable`。已逐个 `grep` 核过本次写进 AGENTS.md 的每一个类型／成员名，均存在于 `Server/src`。

## 越界观察的处置：`Docs/Refactor/TableOwnershipRuling.md:776` —— **不动，理由如下**

该行写作 `SqliteItemStoreTests.Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore`。

1. **它属于「保留原文」那一类。** AGENTS.md「过期的记载怎么办」把带日期的裁定稿归为**保留原文、开头补落地状态**，并且正好点名这份文件是已经这么处理过的范例——不是直接改正文的那一类。
2. **它的落地状态段已经在了**，开头明写「That is a starting point, not a description of the database today … Read the counts as history」。整份文件已自陈为历史。
3. **那句话在本次改动之前就已经有两个名字是死的**：`SqliteItemStoreTests` 与 `SqliteItemStoreSession` 在 `Server/src` 与 `Server/tests` 全域**各 0 命中**（今天叫 `SqliteItemDomainStoreTests` / `SqliteItemDomainStoreSession`）。本次改名只是让同一句里第三个名字也变旧。
4. **所以补一条「该判据已改名」的落地状态反而更坏**：它会暗示同句其余两个名字是现行的。而按 AGENTS.md「落不到某个人某次要做的决定上，就不写」，为一份自陈历史的裁定稿里一个已有两处失效的句子再加一条单点更正，落不到任何决定上。

若日后要整体校准这份裁定稿，那是它自己的一次成果，边界与依据都与本任务不同。
