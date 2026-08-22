---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-2, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: 物品承载按概念重切

## Context

### Source Request

根任务的第三个成果，由根控制器在成功者决定时划定。裁定给出的次序把角色本体排在最前，物品侧与技能侧随后、彼此之间任意次序；角色本体已作为 spec-002 接受（提交 `5aba3a9`），两处跨块耦合都已消化，物品侧因此可以开工。

根授权原话：「不，一口气全改了。可以按multi-spec来处理。」

### Current Situation

`Docs/Refactor/TableOwnershipRuling.md`（提交 `9c535a9`，spec-002 追加过一段）是本成果的设计权威：`items` 与 `character_rentals` 的列与来源、`character_item_values` 的拆分、要携带的键与约束、类型与可空的继承规则及其例外、以及三条写不成 `CHECK` 的规则，都在那份记录里。本契约不重述它，只划边界。

今天一件物品分属四张表——`character_new_items`（角色背包，已带 `owner_scope`/`owner_id`/`list_type`/`slot_index` 与 `UNIQUE`）、`account_cargo_new_items`（账号仓库，`list_type` 在列上却不在约束里）、`character_new_titlebook`（称号册，用 `category` 充当 `list_type`）、`character_avatar_detail`（时装的附加事实，按时装 UID 而非按物品行主键定位）。租借同一个概念有两张列语义相同的表 `character_rental_items` 与 `rental_items`，且共用同一个读取器。`character_item_values` 用 `list_kind`（`'cooltime'`/`'effect'`）重新解释 `value`，是库里最清晰的一处判别列。上一代分解式的 `character_items`(20 列) 与 `account_cargo_items`(15 列) 没有任何写入路径，只被一处迁移守卫问「空不空」。

spec-002 落地后库形状为 49 张表。

## Goal

任意归属下的一件物品由同一处承载；同一件事的状态不再有两份列语义相同的表；不存在只被用来做空判的上一代物品表；不存在用一列重新解释另一列的物品表。对外协议行为一字不变。

## Scope

### In

- 按裁定的目标形状建立 `items`：`character_new_items`、`account_cargo_new_items`、`character_new_titlebook`、`character_avatar_detail` 四张表的列并入，其列、键、约束与可空规则以裁定为准。
- 按裁定建立 `character_rentals`，`character_rental_items` 与 `rental_items` 随之退役。
- 按裁定把 `character_item_values` 拆成 `character_item_cooldowns` 与 `character_item_effects`，`list_kind` 退役。
- 退役 `character_items` 与 `account_cargo_items`，连同只依赖它们的那一处迁移守卫。
- 上述各表的读写路径随之改写，包括按表接口与其行类型、索引声明与可补列清单。
- 领域层的跨表组装随表形状更新，跨表组装仍在领域层且每条记录只有一个定义点。
- 表征用例改写到新形状，覆盖不得净减少。
- 裁定指名由物品侧实施回答的五处悬置事项 D-5、D-6、D-7、D-15、D-16：每一处或者实现一个结论，或者以证据说明为何维持现状。

### Out

- 不动 `character_item_locks`、`character_collectbox_slots`、`mailbox_attachments`。裁定分别以 S-6、S-5/C-4、具名裁定判定它们各自是别的概念；改动它们需要新的裁定。
- 不动角色本体、技能与账号侧的表。
- 不为既有数据编写回填、修复或迁移路径。
- 不改变对外协议行为，包括下发内容、字段顺序与错误路径。
- 不退回上一轮已确立的形状。
- 不改动 `Tool/` 与 `.worktrees/legacy-reference/`。

## Constraints

- `Docs/Refactor/TableOwnershipRuling.md` 是设计权威。实施若发现其中某条事实不成立，先更新裁定并说明理由，然后才按新裁定实施；不得在实施里默默偏离。
- 红线：不得把不同概念压成 `(kind, key, value…)` 形态的通用行。X-1 的拆分正是为消除现存的一处。
- 源表的每一条约束在其列的落点上存续，或由裁定明确说明为何不存续。裁定已点名两处不能直接存续的：`character_avatar_detail` 的 `PRIMARY KEY (item_uid)`（D-15）与 `account_cargo_new_items` 的 `UNIQUE(account_id, slot_index)`（D-16）。
- 裁定列出的三条写不成 `CHECK` 的规则必须由代码继续保证：哪些行有时装列（`item_core[0] == 8`）、仓库行永远没有时装列、角色的时装必须带非零 UID。
- 开发阶段擦库不迁移。
- 今天被表征用例覆盖的列，改动后必须仍被覆盖；覆盖可以重写，不可净减少。
- 对外协议行为一字不变。
- 全程不产生任务级提交；成果经评审与新鲜验证后由父控制器做一次已接受结果提交。
- 测试以 `-m:1` 运行，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 指向 `D:/project/DNF/DNFClient-debug/Script.pvf`。
- C# 文件为 CRLF、无 BOM UTF-8；net10.0，警告即错误。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 一件物品由 `items` 一处承载，无论持有它的是角色背包、账号仓库还是称号册；时装的附加事实是该行的列而不是另一张表 | 形状守卫从裁定的逐列账本机械生成期望列集，与 `PRAGMA table_info` 比对；`character_new_items`、`account_cargo_new_items`、`character_new_titlebook`、`character_avatar_detail` 在库里不存在 |
| SC-2 | 称号册的 `category` 与容器类型之间的双向变换正确，且角色背包的读取不会撞上称号册的行 | 用例覆盖：`category` 0..4 写入后以 `list_type` 19..23 存在；按 `category` 读回得到原值；同一角色同时持有背包 0 号槽与称号册 0 号槽时两行共存而不违反 `UNIQUE`；角色背包读取排除 19..23 |
| SC-3 | 租借只有一处承载，且两张源表的行经同一读取器得到的结果不变 | 形状守卫；`character_rental_items` 与 `rental_items` 不存在；租借的表征用例期望值不变 |
| SC-4 | 没有一张物品表用一列重新解释另一列 | `character_item_values` 不存在，`character_item_cooldowns` 与 `character_item_effects` 各自存在且不含 `list_kind`；两类值分别读回原有的两个领域列表 |
| SC-5 | 不存在只被用来做空判的上一代物品表 | `character_items` 与 `account_cargo_items` 不存在；生产代码中不再有针对它们的读取路径与迁移守卫 |
| SC-6 | 源表的每一条约束在落点上存续，两处不能直接存续的各有结论 | 逐条清点九张源表的键、唯一性、检查与外键并指出落点；D-15 与 D-16 各自或实现（存储时装 UID 列加部分唯一索引 / 部分唯一索引）或以证据说明维持现状，结论写进裁定 |
| SC-7 | 三条写不成 `CHECK` 的规则在代码中存续 | 用例覆盖：非时装行没有时装列；仓库里的时装不带附加事实而角色的时装缺附加事实是错误；角色的时装 UID 为零或负时被拒绝 |
| SC-8 | 对外协议行为一字不变 | 物品侧整记录表征用例的期望输出一字未改且通过；全量测试通过，红色项仅限已知的满载超时且隔离复跑为绿 |
| SC-9 | 今天由整记录表征用例守住的列在新形状下仍被守住，且守卫可证伪 | 表征用例的改动只落在播种语句上；至少一次可证伪性演示：构造一次列错位或语义回退使守卫变红，还原后转绿 |
| SC-10 | 上一轮确立的形状未回退 | 持久化类与其所指名表的清点显示无类指名超过一张表；跨表组装仍在领域层，领域记录的定义点未增加 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 继承根契约的 `task` 行：「不，一口气全改了。可以按multi-spec来处理。」 | 本契约 Scope/In 所列的表形状重切与随之而来的读写路径、领域组装与用例更新；在共享任务工作树内执行与验证；经评审与新鲜验证后由父控制器做一次已接受结果提交 |

## Requires Confirmation

- 任何超出本 Scope/In 的表形状变更，特别是触及 Scope/Out 所列三张表的改动。
- 合并到主干、推送、创建 PR、分支或工作树清理。

## Assumptions

- 上一代的两张物品表在生产代码中没有写入路径，只被那一处迁移守卫读取。裁定已核实；实施须在动手前复核一次。
- `character_new_items.character_id` 恒等于 `owner_id`（裁定已对三条插入路径与两处更新核实）。实施须确认没有出现第四个写入者。
- 两处租借承载的是同一个概念，其列语义一一对应。
- 目标是本机开发库，现有数据可直接丢弃。
