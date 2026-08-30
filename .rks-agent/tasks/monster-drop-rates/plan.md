---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Plan: 怪物死了会按档案掉东西

## 次序为什么是这个次序

内容源先立住，规则才有真东西可验；规则立住之前写领域服务，只能拿桩去验，
而桩正是「自洽的错读」的温床——判据同意实现，实现同意判据，档案没人问。

所以 **T-001..T-004 每一件都以「对着真档案断言具体数值」收尾**，之后的任务才允许用桩。

`Docs/DropRates.md` 是本 Plan 的数值出处，逐条引用其节号。

## Boundaries

| ID | 路径 | 改动意图 |
|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Content/DropRateContent.cs` | 新增：费率表的记录与内容源接缝 |
| BD-002 | `Server/src/Dfo.Server.Infrastructure/Content/PvfDropRateTableSource.cs` | 新增：读 `itemdropinfo_*.etc` |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Content/DropItemPoolContent.cs` | 新增：物品池的记录与内容源接缝 |
| BD-004 | `Server/src/Dfo.Server.Infrastructure/Content/PvfDropItemPoolSource.cs` | 新增：读 `ItemDictionary.etc` 与 `stackable.lst` |
| BD-005 | `Server/src/Dfo.Server.Abstractions/World/DropRules.cs` | 新增：纯规则——品级带、稀有度、门、抽 |
| BD-006 | `Server/src/Dfo.Server.Abstractions/World/DropRandom.cs` | 新增：可复现随机源 |
| BD-007 | `Server/src/Dfo.Server.Abstractions/World/IMonsterDropService.cs` | 新增：领域接缝与掉落记录 |
| BD-008 | `Server/src/Dfo.Server.Abstractions/World/IDropRateAdjuster.cs` | 新增：爆率增强的改率接缝 |
| BD-009 | `Server/src/Dfo.Server.Application/World/MonsterDropService.cs` | 新增：领域服务实现 |
| BD-010 | `Server/src/Dfo.Server.Application/World/NoDropRateAdjustment.cs` | 新增：一期的空实现 |
| BD-011 | `Server/src/Dfo.Server.Abstractions/World/IDungeonRunDomainService.cs` | 修改：本局已分配的掉落与场景槽号 |
| BD-012 | `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs` | 修改：同上的实现 |
| BD-013 | `Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs` | 修改：掉落条数不再恒零 |
| BD-014 | `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | 修改：能取走怪掉的东西 |
| BD-015 | `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`、`Dfo.Server.Application/ApplicationModule.cs`、`Dfo.Server.Infrastructure/InfrastructureModule.cs` | 修改：登记新内容源与服务 |
| BD-016 | `Server/tests/**` 对应的判据文件 | 新增/修改 |

## Requirement Coverage

| RQ | 任务 | 边界 | 检查 |
|---|---|---|---|
| RQ-001 | T-001 | BD-001, BD-002 | CK-001, CK-002 |
| RQ-002 | T-002 | BD-005 | CK-003, CK-004 |
| RQ-003 | T-002 | BD-005 | CK-005 |
| RQ-004 | T-003 | BD-003, BD-004 | CK-006 |
| RQ-005 | T-003 | BD-003, BD-004 | CK-007 |
| RQ-006 | T-004 | BD-005 | CK-008, CK-009 |
| RQ-007 | T-006 | BD-005 | CK-012 |
| RQ-008 | T-008 | BD-009 | CK-016 |
| RQ-009 | T-009 | BD-013 | CK-018 |
| RQ-010 | T-010 | BD-014 | CK-019, CK-020 |
| RQ-011 | T-005, T-008 | BD-006, BD-009 | CK-010, CK-017 |
| RQ-012 | T-007 | BD-008, BD-010 | CK-014 |
| RQ-013 | T-011 | BD-001..BD-013 | CK-021 |
| RQ-014 | T-011 | — | CK-022 |

## Tasks

### T-001 费率表内容源

读怪物费率表。每行七个数是 `低级 高级 + 五个率`；三张加成表各五组，与五个率一一对应。
接缝按文件参数化，二期换 `itemdropinfo_clearreward.etc` 即可复用。

- 输入：`Docs/DropRates.md` 第三节
- 输出：BD-001、BD-002 及其判据
- CK-001 `dotnet test --filter FullyQualifiedName~PvfDropRateTableSourceTests` —— 断言等级段
  `81 200` 读出 `金币 750 / 类型1 10 / 类型2 135 / 类型3 8 / 类型4 5`
- CK-002 同上 —— 断言档次倍率里装备那一组是 `0.2 / 2 / 7 / 16.8`

### T-002 稀有度阶梯

累积门槛加基数。**基数从表里读**，摇点范围是 `1..基数−1`。门槛与前一位相同时该档为零。

- 依赖：T-001
- 输出：BD-005 的稀有度部分及其判据
- CK-003 `dotnet test --filter FullyQualifiedName~RarityLadderTests` —— 对第 1 组断言
  白 70.00% / 蓝 26.49% / 紫 2.51% / 粉 1.00%
- CK-004 同上 —— 构造一组含相同相邻门槛的输入，断言该档概率为零
- CK-005 同上 —— **断言史诗与传说都不为零**（写死分母就会让这条红）

### T-003 物品池内容源

装备取自 `ItemDictionary.etc`，堆叠物取自 `stackable.lst` 与各 `.stk`。
按 `(品级, 稀有度)` 分桶、桶内按声明的资格值加权。**未声明资格值者不入池。**

- 输入：`Docs/DropRates.md` 第五节
- 输出：BD-003、BD-004 及其判据
- CK-006 `dotnet test --filter FullyQualifiedName~PvfDropItemPoolSourceTests` —— 断言
  `10087807`（设计图，品级 57、稀有度 2、资格值 1000）在其桶中且权重为 1000
- CK-007 同上 —— 断言 `10088621` 与 `10088620`（材料，未声明资格值）**不在任何桶中**

### T-004 抽取规则

`(种类, 等级, 稀有度, 随机源) → 物品`。等级换品级带，带内各桶合并后按权重抽一件。

- 依赖：T-003
- 输出：BD-005 的抽取部分及其判据
- CK-008 `dotnet test --filter FullyQualifiedName~DropPickTests` —— 取出的物品品级落在带内
- CK-009 同上 —— 桶为空时不产出且不抛出

### T-005 可复现随机源

由本局种子与房间派生。**不要求与游戏本身的随机数一致**——没有任何东西核对它；
要求的只是同一输入两次给同样的序列。这一点要在成果物里写明，免得被误当成还原了原算法。

- 输出：BD-006 及其判据
- CK-010 `dotnet test --filter FullyQualifiedName~DropRandomTests` —— 同种子两次序列相同
- CK-011 同上 —— 相邻房间的序列不重叠（与 actor 运行号同样的理由）

### T-006 门

五个率各自独立掷；每个命中的物品类型槽各掷一次稀有度。
率 = 基础率 × 档次 × 难度 × 组队，上限为基数。**吃已算好的率，不自己查表。**

- 依赖：T-001、T-002、T-005
- 输出：BD-005 的门部分及其判据
- CK-012 `dotnet test --filter FullyQualifiedName~DropGateTests` —— 五率全零时不产出
- CK-013 同上 —— 倍率相乘后超过基数时被夹到基数

### T-007 爆率增强接缝

一个改率的接缝加一期的空实现。空实现返回原样。

- 输出：BD-008、BD-010 及其判据
- CK-014 `dotnet test --filter FullyQualifiedName~DropRateAdjusterTests` —— 空实现下率逐字段不变

### T-008 本局的掉落分配与领域服务

怪掉的东西哪儿也没声明，编号**只能分配不能推导**。号源与已分配的掉落归本局状态
（`IDungeonRunDomainService`），与已有的 `DeadActors` / `TakenGroundDrops` 并列。

领域服务编排：取率 → 过增强接缝 → 门 → 逐个抽 → 向本局要编号 → 产出列表。

- 依赖：T-004、T-006、T-007
- 输出：BD-007、BD-009、BD-011、BD-012、BD-015 及其判据
- CK-015 `dotnet test --filter FullyQualifiedName~MonsterDropServiceTests` —— 五率全不中时列表为空
- CK-016 同上 —— 金币与装备两率都中时产出两件且编号不同
- CK-017 同上 —— 同一输入两次调用逐字段相同

### T-009 线级：掉落进击杀通告

`MonsterDeathSlice` 里那个恒零的掉落条数改为真实内容，每条按已记录的 48 字节布局写。

- 依赖：T-008
- 输出：BD-013 及其判据
- CK-018 `dotnet test --filter FullyQualifiedName~MonsterDeathCodecTests` —— 一条掉落时帧长与
  各字段位置符合布局；零条时与改动前逐字节相同

### T-010 拾取怪掉的东西

现有拾取按房间**声明**的掉落解析编号。怪掉的东西不在声明里，拾取要能在本局已分配的掉落中找到它。

- 依赖：T-008、T-009
- 输出：BD-014 及其判据
- CK-019 `dotnet test --filter FullyQualifiedName~GroundPickupTests` —— 按编号取走一件怪掉的东西
- CK-020 同上 —— 同一编号再请求一次不再给第二件

### T-011 标注、回归与全套

把只有上游一个来源的读法在其近旁标注为读法（`Docs/DropRates.md` 第十一节第二档：
五率独立掷、类型槽到种类的映射、门槛四组对应四个类型槽）。怪物档次取不到时按普通处理，
并写明是缺省而非结论。

- 依赖：T-001..T-010
- 输出：BD-001..BD-013 的注释
- CK-021 Non-command: 逐条走查第十一节第二档的三条读法，确认各自在代码中被标注
- CK-022 `dotnet build Server/86JP.Server.slnx` 与 `dotnet test Server/86JP.Server.slnx --no-build`
  —— 零警告零错误，既有判据未经修改仍通过

## 执行须知

所有测试运行前必须设 `PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`，
否则读档案的判据会假红。

基线：`main` = `289a98ec`，工作树 `.worktrees/monster-drop-rates`，
分支 `monster-drop-rates`，全套 1614 通过 / 2 跳过 / 0 失败。
