---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 装载侧重复指名的拒绝、三条取值校验的判据、时装搬迁的正向覆盖

## Context

### Source Request

所有者原文（要点逐条保留）：

> 三条缺口，同一个验收口径：**补上缺失的拒绝与缺失的判据，正常数据上的行为逐值不变。**
>
> **一、装载侧没有「同一件东西被两个位置指名」的拒绝**
> 保存侧有：`InventoryEntryAssembly.SaveAsync`（约 145 行）把身份集合 `.Distinct()` 之后与原集合比大小。
> 装载侧没有。`InventoryEntryAssembly.ReadCharacterItemsAsync`（约 305–329 行）逐行只做三件事：解析 owner 类型、校验 owner 是这个角色、`RequireSlot` 校验槽号范围。**没有任何重复检查。**
> **我实测到一处与 AGENTS.md 不符，请你自己复核后按实际情况修正那句记载**：AGENTS.md 现在写「入场读只校验 (容器, 槽) 不重复，不校验 `core_uid` 不重复」。但那个循环里连 (容器, 槽) 的重复检查也没有——槽的唯一性是数据库 `item_slots` 上 `UNIQUE(owner_scope, owner_id, list_type, slot_index)` 给的，而 `core_uid` 上没有任何约束。**先自己验证这个读法对不对**，再决定记载怎么写。
>
> **二、三条取值校验全仓零断言**
> 三条都在生产里各有一处、在 `Server/tests` 全域 **0 处**，删掉它们全量判据仍会绿：
> 1. `GameInventoryDelivery` 的 `"Non-avatar inventory item carries avatar detail."`
> 2. `GameInventoryDelivery` 的 `"Avatar jewel socket must contain exactly 30 bytes."`
> 3. `InventoryEntryAssembly` 约 390 行：珠宝槽恰好 30 字节 ＋ 两个颜色落在 `0..ushort.MaxValue`
> （`avatar_jewel_socket` 另有 schema CHECK 兜底，其余两条没有。）
>
> **三、一条判据的名字不描述它证明的东西**
> `Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 现在靠「行没被删」通过。
> **注意不要照抄一句已被证伪的话**：让 `SqliteItemDomainStoreSession.CarriedAvatarsAsync` 恒返回空表，`Dfo.Server.Persistence.Tests` 今天会**红 1 条**（`TheWorldRefusesASecondRowOnAnAvatarAlreadyStanding`，因为空表使 `@expire` 为 NULL、守卫短路放行）。
> 所以它钉住的是**守卫对这次检索的依赖**；而「那六个时装值被正确搬到新行上」**今天仍然零覆盖**。要补的是后者。

硬约束（所有者原文）：正常数据上行为逐值不变，既有判据的期望值与断言不得为迁就实现而改写；不新增协议帧，不改任何帧的字节布局；入场 62 帧字节锚必须与 HEAD 逐字节相同；`CommandHandlerArchitectureTests` 必须继续通过，判定谓词与具名豁免清单（只有 `TimeProvider`）不得放宽；`TreatWarningsAsErrors` 开启，CS9113 是错误；分层由项目引用强制；每个任务单元结束时判据必须全绿，不允许攒红。

交回要求：改了哪些文件、判据读数与退出码、字节锚是否逐字节未变、产出的产物路径、接受提交的 SHA、以及未解决的风险。

### Current Situation

仓库 `E:/Project/DNF/DNF-JP-86-Server`，分支 `main`，HEAD `25dccec6`，工作区干净。`.rks-agent/` 已在该提交清空，无既有任务记录可参照。

判据基线：`PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` ＝ 2237 通过 / 2 跳过 / 0 失败，退出码 0。那 2 个跳过是「PVF 档案确实加载了」的标志，不是缺陷。

已就地复核的事实（HEAD `25dccec6`，读代码得出）：

- `InventoryEntryAssembly` 的两条装载路径**各自调用了** `RequireUniqueSlots`——`LoadCharacterAsync` 第 75 行 `RequireUniqueSlots(items, "character inventory")`、`LoadAccountAsync` 第 103 行 `RequireUniqueSlots(items, "account cargo")`。该辅助方法（第 721 行）按 `(ContainerType, SlotIndex)` 去重。所以「(容器, 槽) 的重复检查」**在装载路径上存在**，只是不在 `ReadCharacterItemsAsync` 那个循环里，而在它的调用方。**AGENTS.md 现有那句「入场读只校验 (容器, 槽) 不重复，不校验 `core_uid` 不重复」按「入场读」这个粒度是准确的**；源请求中「连 (容器, 槽) 的重复检查也没有」这一句只对那个循环成立，对入场读不成立。
- 装载路径上**确实没有**任何按 `core_uid`（读侧即 `row.ItemUid`）去重的检查——`ReadCharacterItemsAsync`（第 310–329 行）与 `ReadCargoItemsAsync`（第 331–358 行）都没有。
- 保存侧的拒绝**不在**第 145 行。第 145 行的 `.Distinct().ToArray()` 只是喂给 `DeleteOwnExceptAsync` 的幸存者名单，不是拒绝。真正的拒绝在第 224–229 行：`var claimed = settled.Select(item => item.Identity).ToHashSet(); if (claimed.Count != settled.Count) throw new InvalidDataException("A dirty inventory names the same thing in more than one position.");`，比的是**落位后的世界行身份**，对堆叠物与具名物**一视同仁**。因此装载侧的对称拒绝应当同样按 `row.ItemUid` 而不是按 `ItemIdentity`（后者对堆叠物为 `null`，会漏掉堆叠物）。
- `InventoryStorageItem`（`Abstractions/Inventory/IInventoryEntryServices.cs` 第 15 行）**不携带** `ItemUid`，所以按 `core_uid` 去重只能落在两个读循环内部（`row.ItemUid` 在手处），或另行传递。
- 缺口三的机理已查清：`Relocate_KeepsAnAvatarsDetailsBecauseTheLinkIsInsideTheCore` 里，`Survivors` 的第二趟 `SameContent` 让新落点认领了原行，该行进入 `spared` 因而**不被删除**，`CarriedAvatarsAsync` 又因 `survivors.ContainsKey` 而**根本不检索**它。于是那次断言 `Assert.Equivalent(detail, …)` 读到的是从未被动过的原行。「六个时装值被搬到一个**新建**的世界行上」这条路径（`CarriedAvatarsAsync` 检索 → `FillAsync` 绑定六列）在 `Dfo.Server.Persistence.Tests` 里没有任何断言经过。

## Goal

在不改变正常数据上任何逐值行为的前提下，使这三处「结论对但没有代码或判据撑着」的缺口各自获得它缺的那一半：装载侧获得与保存侧对称的「同一件东西被两个位置指名」的拒绝；三条取值校验各自获得一条删掉生产校验就会红的判据；「时装的六个值被搬到新建的世界行上」获得正向覆盖。同时按实际复核结果处置 AGENTS.md 中关于装载侧校验的那句记载。

## Scope

### In

- 装载侧拒绝：`InventoryEntryAssembly` 的角色与账号货柜两条装载路径，对「同一个世界行（`core_uid`）被两个位置指名」给出拒绝，口径与保存侧第 224–229 行对称（含堆叠物）。
- 该拒绝的判据：一条会红的判据，钉住它拒绝重复、并钉住正常数据仍然装载成功。
- 三条取值校验各补一条判据：`GameInventoryDelivery` 的「非时装带细节」、`GameInventoryDelivery` 的「珠宝槽恰好 30 字节」、`InventoryEntryAssembly.WithAvatarDetail` 的「珠宝槽 30 字节 ＋ 两个颜色落在 `0..ushort.MaxValue`」。
- 缺口三的正向覆盖：一条判据，走「原世界行被删除、新世界行被建出」的那条路，断言六个时装值落在新行上。
- AGENTS.md：按本契约 Context 里已复核的事实，修正「已知缺陷与覆盖缺口」一节中与实际不符的表述，并把本任务补上的覆盖从缺口清单中如实更新。改法遵循 AGENTS.md「过期的记载怎么办」——本文件属于「现状图」，直接改掉，不另起修正案段落。
- 判据全绿的维持：每个任务单元结束时全量判据绿。

### Out

- 不新增协议帧，不改任何帧的字节布局，不改 `GameInventoryDelivery` 在正常数据上写出的任何字节。
- 不给 `PvfEquipmentSlots.ByName` 补时装槽号键（那是另一件事，且没有出处）。
- 不给 `SqliteWorldItemTable` 补时装唯一性守卫（AGENTS.md 明记为有意留下的、生产到不了的缺口）。
- 不接契约兽领域，不补契约兽「取下互换」的判据。
- 不清扫无人引用的世界行，不引入迁移机制。
- 不改既有判据的期望值与断言以迁就实现。
- 不做合并、推送、建 PR、删除分支或 worktree、清理。

## Constraints

- 正常数据上行为逐值不变；既有判据的期望值与断言不得为迁就实现而改写。
- 不新增协议帧，不改任何帧的字节布局。
- `Server/tests/.../SelectCharacterAcceptedTranscriptTests.cs` 的入场 62 帧字节锚必须与 HEAD `25dccec6` 逐字节相同。
- `CommandHandlerArchitectureTests` 必须继续通过；其判定谓词与具名豁免清单（只有 `TimeProvider`）不得放宽。
- `TreatWarningsAsErrors` 开启，CS9113（未读的主构造器参数）是错误。
- 分层由项目引用强制：Data → Abstractions → Application → {Infrastructure, Persistence, Networking} → Protocol → Host；不得为取证反向引用。
- 每个任务单元结束时判据必须全绿，不允许攒红。
- 判据偶发失败只按「异常类型 `ObjectDisposedException: 'SQLitePCL.sqlite3'` ＋ 栈根在 AGENTS.md 那三条清单内 ＋ 复跑即绿」三样一起判别，不按用例名；清单外的新栈根不得直接归入。跑全量判据时输出落盘，不用会滤掉明细的过滤器。
- 禁止任何 git 还原类命令（`git checkout --` / `git restore` / `git stash`）。要做变异核实就用文件级备份 ＋ `sha256sum` 还原并核散列，每次用完立刻还原。
- 不得触碰 `.worktrees/client-debug-lab`。
- 文档与注释遵循 AGENTS.md「记什么，不记什么」：只写代码说不出的东西，不写过程，不写没有出处的数，每种事实只有一个归处；不在 AGENTS.md 里指向 `.rks-agent/`，不写「本轮／这一轮」。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 装载侧对「同一个世界行被两个位置指名」给出拒绝，角色装载与账号货柜装载两条路径都拒绝，且对堆叠物同样成立。 | 新判据在重复 `core_uid` 的存储数据上观察到拒绝；把该生产拒绝删去后同一判据变红。 |
| SC-2 | 正常（无重复）存储数据在装载侧的结果与 HEAD 逐值相同。 | 既有装载相关判据未改动其期望值且继续通过；新判据含一条正常数据装载成功的断言。 |
| SC-3 | `"Non-avatar inventory item carries avatar detail."` 有判据钉住。 | 删去该生产校验后全量判据出现红；保留时全绿。 |
| SC-4 | `"Avatar jewel socket must contain exactly 30 bytes."` 有判据钉住。 | 删去该生产校验后全量判据出现红；保留时全绿。 |
| SC-5 | `InventoryEntryAssembly.WithAvatarDetail` 的「珠宝槽 30 字节 ＋ 两个颜色落在 `0..ushort.MaxValue`」有判据钉住，且三个分支（长度、Color1、Color2）各自可辨。 | 删去该生产校验后全量判据出现红；保留时全绿。 |
| SC-6 | 「时装的六个值被搬到一个新建的世界行上」有正向判据：该判据经过的路径确实删除了原世界行并建出新行。 | 判据断言新行的 `item_uid` 与原行不同，且六个值逐个相等；令 `CarriedAvatarsAsync` 恒返回空表时该判据变红。 |
| SC-7 | 入场 62 帧字节锚与 HEAD `25dccec6` 逐字节相同。 | `SelectCharacterAcceptedTranscriptTests.cs` 相对 HEAD 的 diff 为空（或其 SHA-256 与 HEAD 版本一致）。 |
| SC-8 | 全量判据全绿且通过数不低于基线加上新增判据数。 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` 退出码 0，读数记录在案（通过/跳过/失败），跳过仍为 2。 |
| SC-9 | AGENTS.md 中关于装载侧校验的记载与代码一致，且本任务补上的覆盖已如实反映在缺口清单中。 | AGENTS.md 相关段落与 `InventoryEntryAssembly`、`SqliteItemDomainStoreTests` 的实际内容逐句可核对。 |
| SC-10 | `CommandHandlerArchitectureTests` 通过，其判定谓词与具名豁免清单未被放宽。 | 该文件相对 HEAD 的 diff 为空；该用例在全量判据中通过。 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「你是这个任务的主控 agent。**从 `using-rks-agent` 开始**，按它路由到的 skill 链走完整个流程：任务契约 → 路由决策 → spec → plan → 执行 → 评审 → 验证。需要子代理时自己派。」＋「# 要做的事 三条缺口，同一个验收口径：**补上缺失的拒绝与缺失的判据，正常数据上的行为逐值不变。**」＋「所有者授权覆盖：任务契约、路由决策、**一个任务 worktree**、配置的执行与验证、经评审与新鲜验证之后的**一次本地接受提交**、一次未接受尝试的精确工作流回滚。」 | 任务契约、路由决策、一个任务 worktree、配置的执行与验证、经评审与新鲜验证之后的一次本地接受提交、一次未接受尝试的精确工作流回滚。派发子代理已获常驻授权。 |

## Requires Confirmation

- 合并、推送、建 PR、删除分支或 worktree、清理——所有者明确排除，做完呈现选项即可。
- 若要改动 AGENTS.md 中的「要求」类条目（而非已证伪的记载），须停下来问所有者。

## Assumptions

- 基线读数 2237/2/0 与退出码 0 在本机可复现；本任务开始前会实测一次并以实测为准。
- `.worktrees/client-debug-lab` 与本任务无交集，全程不触碰。
- 「同一个世界行被两个位置指名」的对称口径按保存侧第 224–229 行理解为**按世界行身份**（含堆叠物），而非按 `ItemIdentity`（堆叠物为 `null`）。若评审认为应仅限具名物，属于口径调整而非实现细节，需要回到所有者。
