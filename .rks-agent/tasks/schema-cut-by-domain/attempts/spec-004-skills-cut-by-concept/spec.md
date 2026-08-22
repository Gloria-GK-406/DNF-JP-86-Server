---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: 技能按概念重切

## Why and Outcome

技能今天分两张表，分法是它服务哪种玩法。两张表的列名逐列相同、五个非 BLOB 列类型相同、键相同，读出来喂给同一种领域状态；不同的只是谁写它们——PvE 行由花技能点写，PvP 行由决斗频道的策略播种写。这两个动作永不碰同一行，所以它们本就是一张表的两组行。

但两张表**不是等价的**：PvP 那张多带四条保证——五个非 BLOB 列的 `NOT NULL`、`CHECK (page_index BETWEEN 0 AND 1)`、级联外键、以及 `UNIQUE (character_id, page_index, skill_id)`——而 PvE 那张一条都没有。合并的难处不在于把行放到一起，而在于这四条保证既不能丢，也不能在加到 PvE 行上时改变任何现行行为。

要变成真的是：技能由一处承载，`mode` 说的只是「哪一组页」而不重新解释任何一列；四条保证一条不少；而客户端收到的每一个字节与今天完全相同。

## Task Perspective

- Core outcome: 库里技能这一块的形状与已封存的裁定一致——一张表、一个 `mode` 列、两组互不相干的行。
- Necessary support: 四条只在 PvP 一侧声明的保证在合并后逐条存续；它们加到 PvE 行上这一收紧不改变任何现行写入的结果。没有这两样，形状对了而库变松或变紧。
- Peripheral assurance: 下发帧的字节比对与表征用例的可证伪性。

## Scope and Non-Goals

### In Scope

- `character_skills` 加 `mode` 列吸收 `character_pvp_skills`，键、唯一性、检查、外键与可空按裁定落位。
- 两张表的读写路径、按表接口与其行类型随之改写。
- `PvpSkillProfileService` 的读标记—写技能—写标记序列随之改写。
- 领域层的跨表组装随形状更新。
- 表征用例改写到新形状。

### Non-Goals

- 不合并两张职业专属页表。裁定 S-4 以红线判定它们各是别的概念。
- 不动角色本体、物品侧与账号侧的表。
- 不为既有数据设计回填、修复或迁移路径。
- 不改变对外协议行为。

## Constraints

- 裁定是设计权威。
- 红线：`mode` 之所以允许，是因为它不重新解释任何一列——两个 mode 下每一列含义完全相同。
- 四条保证逐条存续，按页的用 `mode` 限定。
- 开发阶段擦库不迁移。
- 覆盖不可净减少。

## Definitions

| Term | Meaning |
|---|---|
| 裁定 | `Docs/Refactor/TableOwnershipRuling.md`，本 Spec 的设计权威 |
| 四条保证 | `character_pvp_skills` 声明而 `character_skills` 未声明的四件事：五个非 BLOB 列的 `NOT NULL`、`CHECK (page_index BETWEEN 0 AND 1)`、级联外键、`UNIQUE (character_id, page_index, skill_id)` |
| 收紧 | 四条保证加到 PvE 行上之后，PvE 行受到的比今天更严的约束 |

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | 库形状在技能这一块上与裁定的目标形状逐列一致；`character_pvp_skills` 不存在；表数 44 → 43 |
| RQ-002 | 同一个角色可以同时持有两个 `mode` 的行，两组互不干扰：改一组不影响另一组的读取结果 |
| RQ-003 | `mode` 不重新解释任何一列：两个 mode 下 `page_index`、`slot`、`skill_id`、`level`、`extra_values` 的含义与读写路径相同 |
| RQ-004 | 五个非 BLOB 列在合并后都是 `NOT NULL`，写入其中任一为空被拒 |
| RQ-005 | `mode` 为 `'pvp'` 的行其 `page_index` 超出 0..1 被拒；`mode` 为 `'pve'` 的行不受此限 |
| RQ-006 | 删除一个角色时其两个 mode 的技能行都被级联清除 |
| RQ-007 | 同一 `(character_id, mode, page_index, skill_id)` 的第二行被拒；不同 mode 的同 `skill_id` 不冲突 |
| RQ-008 | 四条保证加到 PvE 行上这一收紧不改变任何现行写入的结果：今天能写成功的 PvE 行改动后仍写成功 |
| RQ-009 | 两张职业专属页表仍在库中，非本职业持有时仍被拒绝而非忽略 |
| RQ-010 | PvP 技能的播种序列行为不变：未播种则播种并置标记，已播种则跳过 |
| RQ-011 | 选人下发的帧与改动前逐字节相同 |
| RQ-012 | 今天由整记录表征用例守住的每一列在新形状下仍被守住，且该守卫可证伪 |
| RQ-013 | 持久化类仍只指名一张表；跨表组装仍在领域层，且每条记录只有一个定义点 |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001 | 一个以新形状建起来的库 | 把技能表的列与裁定的目标形状比对 | 列名与归属逐一相同；`character_pvp_skills` 不存在；表数 43 |
| AC-002 | RQ-002, RQ-003 | 一个同时持有 PvE 与 PvP 技能行的角色 | 分别载入两组 | 两组内容各自与写入时相同，互不串行 |
| AC-003 | RQ-004 | 一个新形状的库 | 写入一行，其五个非 BLOB 列中任一为空 | 被拒 |
| AC-004 | RQ-005 | 一个新形状的库 | 写入 `mode='pvp'` 且 `page_index=2` 的行；再写入 `mode='pve'` 且 `page_index=2` 的行 | 前者被拒，后者被接受 |
| AC-005 | RQ-006 | 一个持有两个 mode 技能行的角色 | 删除该角色 | 两组行都不复存在 |
| AC-006 | RQ-007 | 一个已有某 `skill_id` 的页 | 在同一 mode 同一页再写一行同 `skill_id`；在另一 mode 的同页写一行同 `skill_id` | 前者被拒，后者被接受 |
| AC-007 | RQ-008 | 今天写 PvE 技能行的那条路径 | 走一次建角色并附带初始技能 | 与改动前一样写入成功，行数与内容相同 |
| AC-008 | RQ-009 | 一个非黑骑士的角色持有黑骑士连招页；一个非骑士的角色持有骑士盾牌 | 载入 | 两次都被拒绝，理由与改动前一致 |
| AC-009 | RQ-010 | 一个尚未播种 PvP 技能的角色；以及一个已播种的 | 各走一次载入或初始化 | 前者播种并置标记，后者跳过；两次的结果与改动前相同 |
| AC-010 | RQ-011 | 同一个角色在改动前与改动后 | 走一次选人并录下下发帧 | 两次的字节完全相同 |
| AC-011 | RQ-012 | 改写后的表征用例 | 构造一次列错位或 mode 混淆 | 用例变红 |
| AC-012 | RQ-013 | 改动后的持久化层与领域层 | 清点每个持久化类指名的表，以及每条记录的定义处 | 每个类只指名一张表；每条记录只有一个定义点 |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001 | AC-001 | 新形状与裁定目标形状的逐列比对结果 |
| EV-002 | RQ-002, RQ-003 | AC-002 | 两个 mode 互不干扰的用例结果 |
| EV-003 | RQ-004..RQ-007 | AC-003..AC-006 | 四条保证各自的拒绝与接受用例结果 |
| EV-004 | RQ-008 | AC-007 | 收紧无害的清点与建角色用例结果 |
| EV-005 | RQ-009, RQ-010 | AC-008, AC-009 | 职业专属页拒绝与播种序列的用例结果 |
| EV-006 | RQ-011 | AC-010 | 全量测试结果与整记录表征用例结果 |
| EV-007 | RQ-012 | AC-011 | 可证伪性演示结果 |
| EV-008 | RQ-013 | AC-012 | 持久化类与记录定义处的清点 |

## Assumptions

- 两表的五个非 BLOB 列类型相同、`extra_values` 语义相同（两侧读取都把 NULL 读成空数组）。裁定已核实；实施须复核一次。
- 今天写 PvE 技能行的唯一路径是建角色时的初始技能，它提供全部五个值。**但 `UNIQUE (character_id, page_index, skill_id)` 加到 PvE 行上是裁定未讨论的一处收紧**：若某个职业的初始技能清单在同一页出现两次同一个 `skill_id`，改动后建角色会失败。实施必须先证明这不会发生，或先更新裁定。
- 目标是本机开发库，现有数据可直接丢弃。
