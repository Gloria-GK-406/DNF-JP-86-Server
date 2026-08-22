---
kind: rks-verification
decision: pass
---

# Verification — spec-002 角色本体按概念重切

## 被评价的主张

「角色本体的每一张表都能用一个概念说清边界，没有一张能由『协议包第几段』解释；每角色至多一行的状态不再只因为『至多一行』而单独成表。对外协议行为一字不变。」

## 逐条条件与证据

| 条件 | 证据 | 结果 |
|---|---|---|
| SC-1 六张目标表的列与裁定的目标形状逐列一致 | `CharacterBodyShapeTests` 的六条 Theory：从裁定的逐列账本机械生成期望列集，与 `PRAGMA table_info` 逐一比对。28 / 30 / 48 / 8 / 3 / 7 全部相等。评审另行独立对照裁定「Target shape」一节确认无多无缺 | pass |
| SC-2 每角色至多一行的状态不再只因为「至多一行」而成表 | `TheTablesWhoseColumnsMovedAreGoneAndTheOneWithItsOwnConceptIsNot`：五张折入的表在库里不存在，`character_expert_job` 存在——它保留是因为它是一个概念，不是因为它窄 | pass |
| SC-3 源表的每一条约束在落点上存续 | `TheCascadingForeignKeyArrivesOnTheTableThatAbsorbedIt` 与六条 `ACharacterBodyTableIsKeyedByTheCharacterAlone`；评审逐条追了九张源表的键/唯一性/检查/外键，全部找到落点 | pass |
| SC-4 六张表在建角色时各得一行，八个非零默认值逐一正确 | `CharacterBodyCreationTests`：六条断言「建一个角色后该表恰有一行」，八条断言逐一读回非零默认值（`skill_tree_index` -1、`direction` 5、`area_state` 3、`level` 1、`user_state_bits` 3、`return_user_flag` 1、`pc_room_id` 65537、`crystal_cube_grade` 255） | pass |
| SC-5 对外协议行为一字不变 | 全量 **1061 通过 / 2 跳过 / 1 失败**，唯一那条红是 `Host.Tests` 的已知满载超时假红，隔离复跑 4/4 通过。三份整记录表征用例的**期望输出一字未改**且通过——底下九张表换成六张而领域记录逐字段相同。用例数 1032 → 1062 全部可解释：+15 形状守卫、+15 出生守卫 | pass |
| SC-6 今天由表征用例守住的列仍被守住，且守卫可证伪 | 三份表征用例的 diff 只落在播种语句上，期望值零改动。可证伪性演示两次：把 `stamina` 与 `mood_value` 在读取的 SELECT 里对调 → 表征立刻红；把选项块的读取换回丢失空值语义的写法 → 出生守卫立刻红。两次还原后均全绿 | pass |
| SC-7 上一轮确立的形状未回退 | 脚本清点 `Server/src/Dfo.Server.Persistence` 下每个类 SQL 中出现的表名：**零个类指名超过一张表**。领域记录文件 `ICharacterEntryStateServices.cs` 与 `CharacterModels.cs` 的 `git diff` 为空——跨表组装仍在领域层，每条记录仍只有一个定义点 | pass |

## 约束

| 约束 | 证据 | 结果 |
|---|---|---|
| 裁定是设计权威，偏离须先更新裁定 | 实施中发现的一处（每日挑战「已领取」的表达方式由行存在改为 `claimed_at` 非空）已回写裁定 M-5，然后才实施 | pass |
| 不为既有数据编写回填、修复或迁移路径 | 改动中无任何回填或迁移语句；可补列清单只做修剪与随新列的补入，这是既有机制而非新增迁移 | pass |
| 覆盖不可净减少 | 用例数 1032 → 1062。唯一被替换的失效场景 `orphan-claim` → `group-out-of-range`：前者在合并后的单表单主键下结构上不可构造，后者覆盖了一条此前没有用例的守卫，经评审判定等价 | pass |
| 构建零警告 | `dotnet build Server/86JP.Server.slnx` 零错误零警告（`TreatWarningsAsErrors`） | pass |
| 全程不产生任务级提交 | `git rev-parse HEAD` 仍为 `9c535a9715ebd142fd197026c80d711a3036abcc` | pass |

## 一处仍待时间验证的事

裁定的机械一致性检查（比对裁定与库形状声明）现在报红，因为裁定描述的是改动**前**的 53 张表而库形状已经是 49。这不是缺陷：裁定是对一个特定起点做的决定，不是对当前状态的描述。已在裁定开头加上时间锚点，说明它的计数是历史、目标形状才是终点。后续两块实施落地后，该检查会继续与当前形状不符，这是预期的。

## 结论

`pass`。七条成功条件各有当前证据，五条约束均成立。评审发现的一条 A 级（选项块的空值语义）已修，并补上了让它漏过去的证据缺口——新守卫已验可证伪。
