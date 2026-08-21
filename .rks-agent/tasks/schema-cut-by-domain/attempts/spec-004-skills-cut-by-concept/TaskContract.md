---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-5, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: 技能按概念重切

## Context

### Source Request

根任务的第四个也是最后一个成果，由根控制器在成功者决定时划定。裁定给的次序把它排在角色本体之后、与物品侧并列；两者都已作为 spec-002（`5aba3a9`）与 spec-003（`1a77ef8`）接受。

根授权原话：「不，一口气全改了。可以按multi-spec来处理。」

### Current Situation

`Docs/Refactor/TableOwnershipRuling.md` 是本成果的设计权威。它对技能侧给出两条相反的裁定：

- **M-4：`character_skills` 吸收 `character_pvp_skills`。** 两表列名逐列相同、五个非 BLOB 列类型相同，键都是 `(character_id, page_index, slot)`；PvE 行由花技能点写，PvP 行由决斗频道的策略播种写，两个动作**永不共用同一行**，所以第三条判据成立。加一个 `mode` 列之后各写各的行。
- **S-4 / C-3：`character_dark_knight_combo_skill_pages` 与 `character_knight_shield_deck` 不合并。** 两者键的元数相同但索引的是不同的空间，载荷更是不同的种类——一个是页面正文 BLOB，一个是盾牌的 item id。合并需要一个用其值决定载荷列含义的判别列，正是红线所禁。根契约的 SC-5 字面要求合并职业专属页，与它自己的非目标冲突；红线更根本，冲突已作为 C-3 记录，且根契约 revision 2 已把 SC-5 重述为结果层表述。

`character_pvp_skills` 比 `character_skills` 多带**四条**保证：五个非 BLOB 列的 `NOT NULL`、`CHECK (page_index BETWEEN 0 AND 1)`、级联外键、以及 `UNIQUE (character_id, page_index, skill_id)`。合并后每一条都要存续，按页的那几条要用 `mode` 限定。

spec-003 落地后库形状为 44 张表。本成果落地后为 43，即裁定给出的目标形状。

## Goal

同一个技能概念不因它服务 PVE 还是 PVP 而重复承载；职业专属技能页保持分开，且非本职业持有时仍被拒绝而非忽略。对外协议行为一字不变。

## Scope

### In

- `character_skills` 加 `mode` 列吸收 `character_pvp_skills`，其键、唯一性、检查、外键与可空按裁定落位。
- 这两张表的读写路径随之改写，包括按表接口与其行类型；`PvpSkillProfileService` 那一处读标记与写技能的序列一并改写。
- 领域层的跨表组装随表形状更新，跨表组装仍在领域层且每条记录只有一个定义点。
- 表征用例改写到新形状，覆盖不得净减少。

### Out

- 不合并 `character_dark_knight_combo_skill_pages` 与 `character_knight_shield_deck`。裁定 S-4 以红线判定它们各是别的概念，合并需要新的裁定与用户授权。
- 不动角色本体、物品侧与账号侧的表。
- 不为既有数据编写回填、修复或迁移路径。
- 不改变对外协议行为，包括下发内容、字段顺序与错误路径。
- 不退回上一轮已确立的形状。
- 不改动 `Tool/` 与 `.worktrees/legacy-reference/`。

## Constraints

- `Docs/Refactor/TableOwnershipRuling.md` 是设计权威。实施若发现其中某条事实不成立，先更新裁定并说明理由，然后才按新裁定实施。
- 红线：不得把不同概念压成 `(kind, key, value…)` 形态的通用行。`mode` 是允许的，因为它不重新解释任何一列——两个 mode 下每一列的含义完全相同；S-4 拒绝的那处正相反。
- `character_pvp_skills` 的四条保证在落点上逐条存续，按页的用 `mode` 限定。
- 非本职业持有职业专属页时仍被拒绝而非忽略。
- 开发阶段擦库不迁移。
- 今天被表征用例覆盖的列，改动后必须仍被覆盖；覆盖可以重写，不可净减少。
- 对外协议行为一字不变。
- 全程不产生任务级提交；成果经评审与新鲜验证后由父控制器做一次已接受结果提交。
- 测试以 `-m:1` 运行，`PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 指向 `D:/project/DNF/DNFClient-debug/Script.pvf`。
- C# 文件为 CRLF、无 BOM UTF-8；net10.0，警告即错误。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 技能由 `character_skills` 一处承载，`character_pvp_skills` 不复存在 | 形状守卫从裁定的逐列账本机械生成期望列集，与 `PRAGMA table_info` 比对；`character_pvp_skills` 在库里不存在；表数 44 → 43 |
| SC-2 | `mode` 不重新解释任何一列 | 两个 mode 下每一列的读取与写入走同一条路径、得到同种含义的值；用例覆盖同一角色同时持有两个 mode 的行且互不干扰 |
| SC-3 | `character_pvp_skills` 的四条保证逐条存续 | 用例覆盖：五个非 BLOB 列各自 `NOT NULL`；PvP 行的 `page_index` 超出 0..1 被拒；删除角色时级联清除；同一 `(character_id, mode, page_index, skill_id)` 重复被拒 |
| SC-4 | 这四条保证现在也约束 PvE 行，且这一收紧不改变任何现行行为 | 指出今天写 PvE 行的每一条路径都已提供这些值；用例覆盖 PvE 行的写入照旧成功 |
| SC-5 | 职业专属页仍分两张表，非本职业持有仍被拒绝 | 两张表仍在库中；覆盖非本职业持有被拒绝的用例通过 |
| SC-6 | PvP 技能的播种序列在新形状下行为不变 | 覆盖「未播种则播种、已播种则跳过」的用例通过，且其判据仍是 `character_pvp.skills_initialized` |
| SC-7 | 对外协议行为一字不变 | 整记录表征用例的期望输出一字未改且通过；全量测试通过，红色项仅限已知的满载超时且隔离复跑为绿 |
| SC-8 | 今天由整记录表征用例守住的列在新形状下仍被守住，且守卫可证伪 | 表征用例的改动只落在播种语句上；至少一次可证伪性演示先红后绿 |
| SC-9 | 上一轮确立的形状未回退 | 持久化类与其所指名表的清点显示无类指名超过一张表；跨表组装仍在领域层，领域记录的定义点未增加 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 继承根契约的 `task` 行：「不，一口气全改了。可以按multi-spec来处理。」 | 本契约 Scope/In 所列的表形状重切与随之而来的读写路径、领域组装与用例更新；在共享任务工作树内执行与验证；经评审与新鲜验证后由父控制器做一次已接受结果提交 |

## Requires Confirmation

- 任何超出本 Scope/In 的表形状变更，特别是合并两张职业专属页表。
- 合并到主干、推送、创建 PR、分支或工作树清理。

## Assumptions

- 两表的五个非 BLOB 列类型相同、`extra_values` 语义相同。裁定已核实；实施须在动手前复核一次。
- 今天写 PvE 技能行的路径都提供了那五个列的值，因此把 `NOT NULL` 加到 PvE 行上不会让任何现行写入失败。若发现有路径不提供，须先更新裁定。
- 目标是本机开发库，现有数据可直接丢弃。
