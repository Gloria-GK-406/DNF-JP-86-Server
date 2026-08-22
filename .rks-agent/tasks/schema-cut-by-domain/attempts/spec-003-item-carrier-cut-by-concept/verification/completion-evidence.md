---
kind: rks-verification
decision: pass
---

# Verification — spec-003 物品承载按概念重切

## 被评价的主张

「任意归属下的一件物品由 `items` 一处承载；同一件事的状态不再有两份列语义相同的表；不存在只被用来做空判的上一代物品表；不存在用一列重新解释另一列的物品表。对外协议行为一字不变。」

## 逐条条件与证据

| 条件 | 证据 | 结果 |
|---|---|---|
| SC-1 一件物品由 `items` 一处承载，时装的附加事实是该行的列 | `ItemCarrierShapeTests` 的 `AnItemSideTableHasExactlyTheColumnsTheRulingGivesIt`：`items` 16 列、`character_rentals` 4 列、两张拆分表各 4 列，与裁定的逐表列表相等；`TheTablesWhoseColumnsMovedAreGone`：九张源表在库里不存在。评审独立对照裁定的「Target shape」确认无多无缺 | pass |
| SC-2 称号册的双向变换正确，背包读取不会撞上称号册 | `TitleBookCarrierTests` 8 条：五个类目各自存为 19..23 且读回原值（Theory 五例）、同一角色的背包 0 号槽与称号册 0 类 0 号槽共存且各走各的载入、背包读取排除 19..23、保存背包不动称号册。第二轮评审对每一条做了真改真跑的可证伪性实测（五次），逐条确认会红 | pass |
| SC-3 租借只有一处承载 | 形状守卫：`character_rental_items` 与 `rental_items` 不存在，`character_rentals` 以 `(character_id, item_id)` 为键；租借的表征期望值一字未改且通过 | pass |
| SC-4 没有一张物品表用一列重新解释另一列 | `NeitherHalfOfTheSplitKeepsTheColumnThatDecidedWhatTheOtherMeant`：两张拆分表都不含 `list_kind`，`character_item_values` 不存在；两类值仍读回原有的两个领域列表，顺序与改动前相同（表征期望值未改） | pass |
| SC-5 不存在只被用来做空判的上一代物品表 | `character_items` 与 `account_cargo_items` 不在库里；生产代码中 `ILegacyCharacterItemTable` / `ILegacyAccountCargoTable` 及两处「migration required」守卫已删除。该后果（旧形状的库载入得到空背包而不是拒绝）由裁定明确记录为决定而非事故 | pass |
| SC-6 源表的每条约束在落点上存续，两处不能直接存续的各有结论 | 评审逐条对照 `git show HEAD:...CharacterDatabaseSchema.cs` 与改动后的声明，键/UNIQUE/CHECK/外键/默认值/六条索引的去向全部找到落点；D-15 与 D-16 各以一个部分唯一索引恢复，`ItemCarrierShapeTests` 两条断言其存在与形状。D-15 的强度与原来的差异（同一角色两行同 UID 这半边更严）已在裁定中写明并说明为何接受 | pass |
| SC-7 三条写不成 `CHECK` 的规则在代码中存续 | `AvatarCarrierRulesTests` 5 条：非时装行不去找时装列、仓库里的时装不带时装列且不被拒绝、角色的时装缺时装列被拒、角色的时装 UID 为 0 或 -1 被拒。其中后两条**此前全仓无人守**，是本成果补上的 | pass |
| SC-8 对外协议行为一字不变 | 全量 **1100 通过 / 2 跳过 / 0 失败**。三份整记录表征用例通过，期望输出的唯一改动是仓库两行的代理键（1、2 → 5、6）——两个自增键空间合并成一个的必然结果，该值经评审核实**不出现在任何下发帧里**，只出现在拒绝消息的文字中。其余期望值逐字节未改 | pass |
| SC-9 今天由表征守住的列仍被守住，且守卫可证伪 | 表征用例的 diff 只落在播种语句与上述两个代理键上。可证伪性演示四次：`items` 读取里对调 `list_type` 与 `slot_index` → 表征红；去掉称号册逆变换的减 19 → 表征红；去掉 sweep 的称号册过滤 → 保存守卫红；去掉读取的称号册过滤 → 三条称号册守卫红。四次还原后均全绿，还原文件与备份逐字节相同。第二轮评审另做五次独立实测 | pass |
| SC-10 上一轮确立的形状未回退 | 脚本清点 `Server/src/Dfo.Server.Persistence` 下 **69 个类**（忽略注释，只看代码里出现的表名）：**零个类指名超过一张表**。领域记录文件 `InventoryModels.cs` 与 `IInventoryEntryServices.cs` 的 `git diff` 为空——跨表组装仍在领域层，每条记录仍只有一个定义点 | pass |

## 约束

| 约束 | 证据 | 结果 |
|---|---|---|
| 裁定是设计权威，偏离须先更新裁定 | 实施中三处回写裁定后才实施：D-15 由「不恢复」改判为「恢复」（依据是既有用例已构造该情形）、M-2 的第二条后果（item store 移动物品必须带走时装列）、以及 D-15 强度自述的更正。其余五处悬置事项按计划定案并回写 | pass |
| 红线：不得用一列重新解释另一列 | X-1 的拆分消除了库里最清晰的一处；形状守卫断言两张拆分表都不含 `list_kind` | pass |
| 不为既有数据编写回填、修复或迁移路径 | 改动中无任何回填或迁移语句；可补列清单只做修剪（删去 36 条指向已消失表的条目），未新增任何条目——四张目标表都是新表，一个有它们的库就有完整的它们 | pass |
| 三条写不成 `CHECK` 的规则由代码继续保证 | 见 SC-7 | pass |
| 覆盖不可净减少 | 用例数 1064 → 1102，Δ+38：+27 形状守卫、+5 时装规则守卫、+8 称号册守卫、+2 取代用例、−2 迁移守卫用例、−1 wrong-owner、−1 仓库 duplicate。三处减少各有理由，见下 | pass |
| 构建零警告 | `dotnet build Server/86JP.Server.slnx` 零错误零警告（`TreatWarningsAsErrors`） | pass |
| 全程不产生任务级提交 | `git rev-parse HEAD` 仍为 `5aba3a9dcea2b302797eb33097ae923a7a824a45` | pass |

## 三处被删或被取代的用例

每一处都逐条核对过它原本断言的是不是即将消失的东西，并由第一轮评审复核。

- `LoadAsync_LegacyOnlyInventoryRequiresMigration`（2 例）**删除**：它守的是上一代两张表有行时拒绝入场；两张表已退役，情形结构上不可构造。
- `RejectsInvalidStoredCharacterItems(wrong-owner)` **取代为** `LoadAsync_LeavesOutTheItemsOfEveryOwnerButTheOneAskedFor`：读取现在按 owner 过滤，别人的行根本读不到，没有可拒绝的东西；新用例守的是更要紧的一件事——过滤真的把别人的物品排除在外。
- `LoadAccountAsync_RejectsInvalidStoredAccountCargo(duplicate)` **取代为** `TheCargoRefusesASecondItemInASlotItAlreadyHolds`：D-16 恢复了仓库自己的槽位唯一性，重复行在写入时就被拒。

`RequireUniqueSlots` 仍由角色侧的 `duplicate` 一例覆盖。

## 评审经过

两轮，均在独立的新上下文中进行。

- **第一轮判 `changes_required`**，两条 A 级，都成立：一次租约保存会删光该角色的称号册（sweep 缺容器过滤）；以及计划点名的称号册变换四条断言从未写过——后者正是前者漏过去的原因。两条都已修，并各自验过可证伪。另有九条 B 级记录不改。
- **第二轮判 `passed`**：两条 A 级确已修好；八条新断言逐条实测可证伪（唯一例外是「存为 19..23」那一条 scalar 断言，它证的是夹具自己写进去的字面量，因为称号册今天只有读路径、正变换在生产代码里尚无实现者——评审判为 B 级）；九条 B 级无一升级；另新记三条 B 级。评审自跑全量与实施者一致。

## 结论

`pass`。十条成功条件各有当前证据，七条约束均成立。两轮评审、四次可证伪性演示（外加评审的五次独立实测）都指向同一件事：这次搬运没有改变客户端收到的任何一个字节，而它试图捕捉的那一类静默失效——搬运时丢掉一条只活在代码里的规则——在第一轮被抓到了一条，并已连同它的证据缺口一起补上。
