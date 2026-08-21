---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-3, SC-4, SC-8, SC-9, SC-10]
depends_on: []
---

# TaskContract: 角色本体按概念重切

## Context

### Source Request

根任务的第二个成果，由根控制器在成功者决定时划定。裁定给出的次序把它排在最前：它是唯一被两处跨块耦合指定为必须先做的一块。

根授权原话：「不，一口气全改了。可以按multi-spec来处理。」

### Current Situation

`Docs/Refactor/TableOwnershipRuling.md`（提交 `9c535a9`）已把改动前 53 张表、420 列的去向逐一定下，并给出 43 张表的目标形状。它是本成果的设计权威：目标表的列与来源、键与必须携带的约束、类型与可空的继承规则及其例外、六张每角色一行的表由谁建行、八个不能写错的非零默认值，都在那份记录里。本契约不重述它，只划边界。

角色本体今天是 `characters`、`character_subtype0_fields`、`character_subtype1_fields` 三张表，切分线是选人下发那一包协议的段落，因此身份、派生数值与运营态按「第几段」混在一起。另有六张「每角色至多一行」的表各自成表：`character_init_flags`、`character_gold_limits`、`character_crystal_contract`、`character_dimension_flags`、`character_expert_job`、`character_pvp_skill_state`。

## Goal

角色本体的每一张表都能用一个概念说清边界，没有一张能由「协议包第几段」解释；每角色至多一行的状态不再只因为「至多一行」而单独成表。对外协议行为一字不变。

## Scope

### In

- 按裁定的目标形状重建角色本体：`characters`、`character_stats`、`character_condition`、`character_pvp`、`character_settings`、`character_expert_job` 六张表，其列、键、约束、默认值与建行时机以裁定为准。
- 九张源表（上述三张本体表加六张至多一行的表）的读写路径随之改写，包括按表接口与其行类型。
- 每日挑战两表合并为 `character_daily_challenges`；`character_invisible_falgs` 更名为 `character_invisible_flags`。两处均已获单独授权。
- 领域层的跨表组装随表形状更新，跨表组装仍在领域层且每条记录只有一个定义点。
- 表征用例改写到新形状，覆盖不得净减少。

### Out

- 不动物品域与技能域的表。`character_gold_limits` 迁入 `character_stats` 之后，物品侧对它的那一处读取必须在本成果内改为读新位置——否则树是坏的；只做这一处最小改动，物品域自身的重切不在本成果内。
- 不为既有数据编写回填、修复或迁移路径。
- 不改变对外协议行为，包括下发内容、字段顺序与错误路径。
- 不改动裁定中标为暂定或悬置的归宿；如需改动，先更新裁定。
- 不改动 `Tool/` 与 `.worktrees/legacy-reference/`。

## Constraints

- 裁定是本成果的设计权威。与它不一致的实施必须先更新裁定并说明理由，不得就地偏离。
- 源表的每一条约束在其列的落点上存续；丢掉一条就是静默丢失一条今天有的保证。
- 八个非零默认值必须逐一写对；其中五个的值直接或经映射到达客户端。
- 开发阶段擦库不迁移：直接以新形状建库，不得写回填、修复或迁移路径。
- 今天被表征用例覆盖的列，改动后必须仍被覆盖；覆盖可以重写，不可净减少。
- 全程不产生任务级提交；仅在评审与新鲜验证通过后做唯一一次已接受结果提交。
- 测试以单进程方式运行（`-m:1`），并需 `PVF_ARCHIVE_PATH` 与 `Pvf__ArchivePath` 指向 `D:/project/DNF/DNFClient-debug/Script.pvf`。
- C# 文件为 CRLF、无 BOM UTF-8；目标框架 net10.0，警告即错误。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 角色本体的每一张表都能用一个概念说清边界，没有一张能由「协议包第几段」解释 | 库形状声明中六张目标表的列，与裁定的目标形状逐列一致 |
| SC-2 | 每角色至多一行的状态不再只因为「至多一行」而成表 | 库形状声明中五张折入的表不复存在；保留的 `character_expert_job` 可指出它承载的概念 |
| SC-3 | 源表的每一条约束在落点上存续 | 九张源表的键、唯一性、检查与外键逐条追到落点；`character_pvp` 带上级联外键 |
| SC-4 | 六张每角色一行的表在建角色时各得一行，八个非零默认值逐一正确 | 建角色后读回六张表各有一行；八个默认值逐一与裁定所列相同 |
| SC-5 | 对外协议行为一字不变 | 选人下发帧的字节与改动前一致；全量测试通过，红色项仅限已知的满载超时且隔离复跑为绿 |
| SC-6 | 今天由表征用例守住的列仍被守住 | 表征用例改写后仍具可证伪性：对每一处承载，构造一次列错位或列缺失能使其变红 |
| SC-7 | 上一轮确立的形状未回退 | 持久化类仍只指名一张表；跨表组装仍在领域层且每条记录只有一个定义点 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 根 TaskContract 的 task 授权行；用户原话「不，一口气全改了。可以按multi-spec来处理。」 | 在共享任务工作树中完成上述范围内的实施与验证；经评审与新鲜验证后由根控制器接受为一次已接受结果提交 |
| scope expansion | 根 TaskContract 的 scope expansion 授权行：用户单独授权每日挑战两表合并与 `character_invisible_falgs` 拼写更正 | 这两处表形状变更纳入本成果 |

## Assumptions

- 裁定中标为已核实的事实成立。若实施中发现某条不成立，先更新裁定再继续。
- 目标是本机开发库，现有数据可直接丢弃。
