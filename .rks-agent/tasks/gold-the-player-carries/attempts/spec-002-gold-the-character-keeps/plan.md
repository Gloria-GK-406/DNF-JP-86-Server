---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 角色身上的金币

## Implementation Approach

Spec 的核心结果是玩家看得见的那个数字变成真的；必要支撑是单一入口、上限、并发安全、起始值与变更通知；判据与实机核销属外围保证。

最早的核心任务是 **T-001：让随身金币有一个能安全增减的存储**。它必须排在最前，因为其余每一件事都消费它的答案——上限在它上面生效，角色对象的入口是它的外壳，三个收支路径调那个入口，通知发的是它返回的余额。

### 已在计划期定死的中心决定

**写入是单条语句，不是读-改-写。**随身金币在客户端是主背包槽 0，参照实现把它存在背包物品行的核心结构里；但那是**它的架构产物**——它有一份内存背包与一份数据库，靠脏集合与定时落库同步，所以「读出内存值、改、标脏」对它是自然的。本服务器没有那份内存背包：容器写走工作单元、随命令事务提交，因此经容器写金币必然是「读出当前行 → 加减 → 写回整行」。

这个形态本服务器**刻意避开过**：账号金库那侧把加法与「够不够扣」都压进单条 SQL，其记述写明理由是两次同时应答的拾取不能各自读到同一个总额再各自加。

查证结果是不能指望调用侧的串行化兜底：

- `IActiveCharacterAccessDomainService`（按角色的执行闸）**声明并注册了，但全仓零消费方**——命令路径上不存在按角色的串行化。
- 事务是 deferred 的，两个并发的读-改-写不会静默丢失，但会以写锁冲突的形式失败，那是把一个可以不存在的失败模式引进玩家的钱包。
- 剩下唯一的保护是「每个连接的收包回调是否顺序执行」，那是网络框架的行为假设。**不把玩家看得见的钱押在未经验证的框架假设上。**

**因此随身金币存为角色属性表上的一列，用与账号金库相同的单语句加/扣。**这带来三个附带好处：它与自己的携带上限（同表 `gold_carry_limit`）住在一起；该表的行在建角色时必定被创建，所以不存在「行缺失」这种状态，起始值天然确定；槽 0 在帧里由该列投影得到，帧的形状一个字节不变。

代价是存储布局与参照实现不同。这不影响线上表现——帧由所存内容构造，与存在哪里无关；而槽 1、槽 2 今天同样没有任何行，所以并没有拆散一个既有的一致来源。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Persistence/Schema/CharacterDatabaseSchema.cs` | 在 `character_stats` 增加随身金币列，并记述它是什么 | 现有表声明 | 带该列的表声明 |
| BD-002 | `Server/src/Dfo.Server.Abstractions/Persistence/InventoryTables.cs` | 为随身金币的加/扣声明存储契约，与账号金库那对同形但按角色 | 角色标识、数额 | 加后总额；扣后总额或拒绝 |
| BD-003 | `Server/src/Dfo.Server.Persistence/Inventory/InventoryStateTables.cs` | 实现该契约，两个方向各一条语句，钳在零与上限之间 | 同上 | 同上 |
| BD-004 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacter.cs` | 在角色对象上开出随身金币的读、加、扣三个成员，并改写其「entry point, not a container」的记述使之成为明确判据 | 数额 | 余额或拒绝 |
| BD-005 | `Server/src/Dfo.Server.Application/Characters/PlayingCharacter.cs` | 实现那三个成员：唯一入口，上限在此生效 | 角色标识、数额、上限 | 余额或拒绝 |
| BD-006 | `Server/src/Dfo.Server.Abstractions/Characters/ICharacterEntryRules.cs` 与 `Server/src/Dfo.Server.Application/Characters/CharacterEntryRuleServices.cs` | 由规则服务回答「这次入账落定为多少」，含上限处理 | 当前额、入账额、上限 | 落定额 |
| BD-007 | `Server/src/Dfo.Server.Application/Inventory/InventoryEntryAssembly.cs` | 主背包虚拟槽 0 由随身金币列投影，槽 1、2 维持现状 | 角色标识 | 含槽 0 的虚拟槽集合 |
| BD-008 | `Server/src/Dfo.Server.Protocol/Game/CardReward/SelectCardCommand.cs` | 翻牌收益与付费扣费改由角色对象承接 | 角色标识、数额 | 结果中的收付两值 |
| BD-009 | `Server/src/Dfo.Server.Protocol/Game/GroundPickup/GroundPickupSlice.cs` | 地面拾取的金币改由角色对象承接 | 角色标识、数额 | 结果中的数额与余额 |
| BD-010 | `Server/tests/` 下与上述各项对应的判据 | 新增覆盖上限、并发、起始值、收支落点与通知一致性的判据 | — | 通过的判据 |

## Ordered Tasks

### T-001: 随身金币有一个能安全增减的存储

- Covers: RQ-001, RQ-006, RQ-007
- Boundaries: BD-001, BD-002, BD-003
- Inputs: 现有 `character_stats` 表声明；账号金库那对单语句加/扣的既有实现作为形状参照
- Outputs: 角色属性表上的随身金币列；按角色的加/扣存储契约与实现

#### Steps

1. 在 `character_stats` 增加随身金币列，非空且默认为零，并在表的记述里写明它是玩家背包上显示的那笔钱、与同表两个上限的关系。
2. 声明按角色的加与扣两个存储成员，形状与账号金库那对一致：加返回加后总额；扣在持有不足时返回拒绝且不写任何东西。
3. 实现它们，每个方向一条语句，条件与算术同处一句，钳在零与有符号 32 位上限之间。扣不得以「加一个负数」实现。
4. 为并发写一个判据：多次同时到达的加都被计入。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿，计数不低于 1993 通过 / 2 跳过 / 0 失败 |
| CK-002 | Non-command: 检查加与扣各自的语句 | 条件与算术在同一条语句内；扣不是加负数 |
| CK-003 | Non-command: 检查新判据 | 覆盖同时到达的多次加，且断言全部被计入 |

### T-002: 携带上限有一个确定的落定规则

- Depends on: T-001
- Covers: RQ-005
- Boundaries: BD-006
- Inputs: 角色属性表上的携带上限；参照实现三种互不一致的超限行为作为反面
- Outputs: 一个无状态规则，回答「当前额加这一笔，落定为多少」

#### Steps

1. 在既有的角色进入规则服务处新增一个纯规则：给定当前额、入账额与上限，回答落定额。
2. 选定一种处理并写明理由，使每一条入账路径得到相同结果。
3. 为上限缺失或不可用的情形选定行为并写明——参照实现在此是 fail-open 的，本服务器要显式决定是否照做。
4. 判据覆盖：未触及上限、恰好触及、超过、以及上限本身为零或缺失。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-004 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿 |
| CK-005 | Non-command: 检查该规则的依赖 | 不持有仓储、注册表、目录或存储；只接收 facts |
| CK-006 | Non-command: 检查判据 | 四种情形各有一条，且期望值可由规则本身推出 |

### T-003: 角色对象上的唯一入口

- Depends on: T-001, T-002
- Covers: RQ-004, RQ-009
- Boundaries: BD-004, BD-005
- Inputs: 存储契约；上限规则；既有的角色对象与取得它的角色服务
- Outputs: 角色对象上的读、加、扣三个成员

#### Steps

1. 在角色对象的契约上开出读、加、扣三个成员，均不带角色标识。
2. 改写该契约里「it is an entry point, not a container」那段记述，使其成为明确判据：挂上来的是角色身上的钱这个语义，背包本身仍走自己的仓储。
3. 在实现里串起来：读上限、问规则、调存储；扣在持有不足时返回拒绝。
4. 判据覆盖：加、扣、扣到不足被拒、以及入账触及上限时按 T-002 落定。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-007 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿 |
| CK-008 | Non-command: 检查两侧调用形态 | 随身一侧不带也不能带角色标识；账号金库一侧仍强制传账号标识 |
| CK-009 | Non-command: 全仓检索改变随身金币列的位置 | 除该入口的实现外无第二处 |

### T-004: 登录时玩家看到的就是所存的

- Depends on: T-001
- Covers: RQ-001
- Boundaries: BD-007
- Inputs: 随身金币列；既有的虚拟槽装配与两个协议帧
- Outputs: 主背包槽 0 由该列投影

#### Steps

1. 让虚拟槽装配从随身金币列取槽 0 的数额，槽 1 与槽 2 维持现状。
2. 判据：所存为某个数额时，进入游戏的帧在槽 0 上写的就是它。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿 |
| CK-011 | Non-command: 检查帧的字节布局判据 | 槽 0 的条目位置与宽度与改动前相同，只是数额来源变了 |

### T-005: 赚的钱与花的钱都作用于它

- Depends on: T-003, T-004
- Covers: RQ-002, RQ-003
- Boundaries: BD-008, BD-009
- Inputs: 角色对象上的入口
- Outputs: 三个收支路径改向；账号金库不再承接它们

#### Steps

1. 翻牌收益改由角色对象承接。
2. 付费翻牌的扣费改由角色对象承接，并保持既有的「先扣后领、领不到则退回」次序。
3. 地面拾取的金币改由角色对象承接。
4. 清理这三处在前一个结果里留下的「这是错的落点」记述，因为它们不再成立。
5. 判据：三条路径各自断言金额落在随身金币上，且账号金库未被触及。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-012 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿 |
| CK-013 | Non-command: 检查三处的判据 | 每处都断言随身金币变化且账号金库不变 |
| CK-014 | Non-command: 检索这三个切片对账号金库的引用 | 仅余与本 Spec 无关的用途，或为零 |

### T-006: 变化时客户端被告知，且告知的与所存的一致

- Depends on: T-005
- Covers: RQ-008
- Boundaries: BD-008, BD-009
- Inputs: 既有的两个通知形态——地面拾取那个增量帧、以及背包变更的增量帧
- Outputs: 每条改变随身金币的路径都告知客户端

#### Steps

1. 核对地面拾取那条既有的通知：它发的是本次增量，客户端累加，因此改向后仍然正确——确认无需改动，并把这一判断记进判据。
2. 为翻牌收益与付费扣费选定通知形态，使客户端显示与所存一致。
3. 判据：一次变动后，客户端据帧算出的数字与服务器所存相同。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 全绿 |
| CK-016 | Non-command: 检查通知判据 | 由帧推出的数字等于所存数额 |

### T-007: 全量回归与实机核销准备

- Depends on: T-006
- Covers: RQ-010
- Boundaries: BD-010
- Inputs: 完整变更集
- Outputs: 全量判据证据；一份供授权方执行的实机核销清单

#### Steps

1. 运行全量判据并记录逐程序集计数。
2. 写出实机核销清单：打一把并翻牌、记数、退出重登、核对；拾取金币、核对；付费翻牌前后核对差额。每一步写明要记录什么。
3. 请授权方执行实机核销——这一步不由本服务器完成。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-017 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 计数不低于 1993 通过 / 2 跳过 / 0 失败，新增判据使通过数上升 |
| CK-018 | Non-command: 实机核销清单 | 三条场景各自记录了变动前、变动后与重新登录后的数字，且三者关系符合预期 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-001, T-004 | BD-001, BD-002, BD-003, BD-007 | CK-001, CK-010, CK-011, CK-018 |
| RQ-002 | T-005 | BD-008, BD-009 | CK-012, CK-013, CK-018 |
| RQ-003 | T-005 | BD-008 | CK-012, CK-013, CK-018 |
| RQ-004 | T-003 | BD-004, BD-005 | CK-007, CK-009 |
| RQ-005 | T-002 | BD-006 | CK-004, CK-005, CK-006 |
| RQ-006 | T-001 | BD-002, BD-003 | CK-002, CK-003 |
| RQ-007 | T-001 | BD-001 | CK-001 |
| RQ-008 | T-006 | BD-008, BD-009 | CK-015, CK-016, CK-018 |
| RQ-009 | T-003 | BD-004, BD-005 | CK-008 |
| RQ-010 | T-007 | BD-010 | CK-017 |

## Risks and Rollback

- Risk: 存储布局与参照实现不同（列而非背包物品行）。若日后要与参照实现的数据库互换，转换需要一步映射。缓解：线上表现完全不受影响，帧由所存内容构造；且本服务器与参照实现的数据库本就不互换。
- Risk: 上限的有效值可能不止角色属性表那一列——参照实现取「按等级基础上限与保存值的较大者」，基础上限来自档案。缓解：T-002 显式决定本服务器取哪一个，并把决定与理由写进规则；若决定要读档案，规则服务是唯一允许读内容源的地方。
- Rollback: 全部改动在任务工作区内且未提交，父控制器持有已接受基线 `e4f29e1d`；按未接受尝试的精确回滚即可退回。
