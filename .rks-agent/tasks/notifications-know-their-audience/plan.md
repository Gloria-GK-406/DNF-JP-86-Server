---
kind: rks-plan
spec:
  path: ./spec.md
state: ready
---

# Implementation Plan: 通知知道自己要告诉谁

## Implementation Approach

### 迁移哪一处扇出——这是唯一会传播的选择，先定死

三处候选逐个看过：

| 候选 | 形状 | 适配度 |
|---|---|---|
| **PvP 大厅公示**（`SelectCharacterLifecycleServices.PlanPvpPublication`） | 纯扇出。人群来自 `pvp.ReadyOn(port, characterId)` 返回的一组角色；内容统一 | **最好**。连接查找就在第 171 行 `sessions.FindByCharacter(...)`，摘掉它正是 RQ-004 要的那个证明 |
| 会话清理通告 | 「必送给本人」+「尽力送旁人」两段 | 前半段不属于通知服务——那是给本人的必送，不是扇出。只有后半段适配 |
| 相邻入场（`SessionAdjacentEntryDomainService`）与清理里的 `presence.InArea(...)` | 区域人群 | 适配，但它们的人群直接来自既有的 `InArea` 查询，摘不掉任何连接查找 |

**主迁移选 PvP 大厅公示**，理由是它同时兑现两条要求：证明人群词汇对得上现实（`TheseCharacters`），
以及**去掉一处真实存在的、领域服务里的连接查找**。后者是这一轮最值钱的收益，而三处里只有它有。

区域那两处放在 T-007 单独评估：它们能给 `InArea` 人群一个真实调用方，但摘不掉连接查找，所以价值
低一档，且带来行为改变的风险更高（区域人群的解析涉及监听端口筛选）。**判定规则写在 T-007 里，
不在这里预先决定。**

### 人群第一版收到哪里

由主迁移倒推：PvP 需要 `TheseCharacters`。若 T-007 判定区域那两处也迁，则需要 `InArea`。

`OneCharacter` 在本轮**没有真实调用方**——但它是 `TheseCharacters` 的退化情形，不单独立类型；
调用方给一个单元素集合即可。少一个类型，少一处解析分支。

频道、全服、组队按 Spec 的 Non-Goals **不实现**。人群用封闭层级表达，将来加一种时编译器会强制
所有解析处理它——这正是选封闭层级而非枚举的理由。

### 顺序的依据

人群类型必须先于解析器；处理器接口必须先于注册表；注册表必须先于启动校验；总线必须先于投递；
投递必须先于重试（重试是投递失败之后的事）；这一切必须先于迁移——迁移是拿现实来验前面对不对的
那一步，放在最后才有意义。

摘要看似可以最后加，但它**必须与投递同时做**：投递里「成功 / 弃权 / 人不在了 / 失败」四种归类是
摘要的内容，事后补会让归类从行为反推。

### 一处必须在实现里守住、事后看不出来的决定

**重试重新解析、重新取内容，不缓存任何东西。** 「显然的」实现是把编码好的帧存下来重发——那会让
一个旧值覆盖新值（先发的失败进队列，后发的成功，然后前者重试成功）。这条在代码里看不出违反，
只能靠判据钉住，所以 T-005 有一条专门的判据。

## Owned Boundaries

| ID | Owned path or interface | Change intent | Inputs | Outputs |
|---|---|---|---|---|
| BD-001 | `Server/src/Dfo.Server.Abstractions/Notifications/` 新增人群类型 | 封闭层级，以角色表达 | — | 人群作为值 |
| BD-002 | `Server/src/Dfo.Server.Application/Notifications/` 人群解析 | 人群 → 收件人（角色 + 当前连接） | BD-001；既有的在线与在场服务 | 唯一做连接解析的地方 |
| BD-003 | `Server/src/Dfo.Server.Abstractions/Notifications/` 通知标记与处理器接口 | 类型自带契约；按收件人产出消息 | 既有 `ICommand<TResult>` 的写法 | 类型安全的注册面 |
| BD-004 | `Server/src/Dfo.Server.Application/Notifications/` 注册表与启动校验 | 照搬既有两个注册表的形状 | BD-003 | 未注册的类型在接受连接前被拒 |
| BD-005 | `Server/src/Dfo.Server.Abstractions/Notifications/` 总线接口与摘要类型 | 发布方法的泛型约束；摘要的四种归类 | BD-001, BD-003 | 调用点无从选错 |
| BD-006 | `Server/src/Dfo.Server.Application/Notifications/` 总线实现与投递 | 逐个、超时、失败隔离、两类失败分流、产出摘要 | BD-002, BD-004, BD-005；既有投递计划 | 一次发布的完整行为 |
| BD-007 | `Server/src/Dfo.Server.Application/Notifications/` 重试队列与其排空 | 内存队列；重试重新解析重新取内容；上限来自注册 | BD-006 | 失败的第二次机会 |
| BD-008 | `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs`、`Server/src/Dfo.Server.Abstractions/Sessions/ISelectCharacterLifecycleServices.cs` | PvP 公示改经通知服务；该服务不再查连接 | BD-006 | 一处真实调用方 |
| BD-009 | `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/` PvP 公示的通知类型与处理器 | 通知类型与其处理器住在对应切片里 | BD-003, BD-008 | 迁移的协议侧 |
| BD-010 | `Server/tests/` 下新增判据 | 覆盖 RQ-001..RQ-014 各自可核对的部分 | BD-001..BD-009 | 可证伪 |
| BD-011 | `AGENTS.md` | 记下 `CMD ⇒ Notify`，并如实写下没做的部分 | T-001..T-008 的实际结果 | 记载与代码一致 |

## Ordered Tasks

### T-001: 人群是一个值，且只有一个地方把它变成连接

- Covers: RQ-003, RQ-004
- Boundaries: BD-001, BD-002, BD-010
- Inputs: 既有的在线状态与在场服务
- Outputs: 人群类型；解析器

#### Steps

1. 定义人群为封闭层级。第一版只放**有真实调用方**的形态：`TheseCharacters`。
   `OneCharacter` 不单独立类型——它是前者的退化情形，调用方给单元素集合。
2. 实现解析：人群 → 收件人（角色 + 它此刻所在的连接）。角色当下没有连接的，**不出现在收件人里**。
3. 枚举全仓把角色解析成连接的位置，确认解析器是唯一一处（迁移之前 `PlanPvpPublication` 里那处
   仍在，属预期；T-006 之后应归零）。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-001 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-002 | Non-command: 人群类型的成员枚举，并尝试构造一个非法组合 | 形态封闭；非法组合无法构造 |
| CK-003 | 新增用例：人群里有一个没有连接的角色 | 该角色不出现在收件人里 |
| CK-004 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-002: 通知类型自带它的处理器契约

- Depends on: T-001
- Covers: RQ-001, RQ-002, RQ-005, RQ-006, RQ-007
- Boundaries: BD-003, BD-004, BD-010
- Inputs: 既有 `ICommand<TResult>` 的写法与它拒绝协变的理由；既有两个注册表与其启动校验
- Outputs: 通知标记、处理器接口、注册表、启动校验

#### Steps

1. 定义通知标记与处理器接口。处理器按收件人产出该收件人的消息；产出空表示弃权。
2. 处理器接口上写明**只读**：它不得写任何状态，写属于 CMD 阶段。
3. 注册与启动校验照搬既有两个注册表的形状。
4. 确认「调用点指定一个与实参不符的通知类型」不能通过编译。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-005 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-006 | Non-command: 一次故意不匹配的调用尝试 | 不能通过编译；记录编译器的拒绝 |
| CK-007 | 新增用例：未注册处理器的通知类型 | 启动校验拒绝，且发生在开始接受连接之前 |
| CK-008 | Non-command: 检视处理器接口与本成果新增的实现 | 不写任何状态 |
| CK-009 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-003: 一次发布逐个投递，一个失败不影响其余

- Depends on: T-002
- Covers: RQ-008, RQ-009, RQ-012
- Boundaries: BD-005, BD-006, BD-010
- Inputs: 既有的投递计划与其失败隔离
- Outputs: 总线、投递、摘要

#### Steps

1. 定义总线的发布方法与摘要类型。摘要的归类必须是四种：成功、弃权、人不在了、失败。
2. 实现投递：逐个收件人，每个带全局一致的超时。**不做并发，不做节流。**
3. **两类失败分流**：解析之后投递之前离线 → 「人不在了」，不进重试；投递失败或超时 → 「失败」，
   进重试。处理器抛异常也算「失败」。
4. 摘要与投递同时做，不事后补。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-010 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-011 | 新增用例：一群收件人中间一个投递失败 | 其余各自收到自己的消息 |
| CK-012 | 新增用例：一个解析后离线、一个投递失败 | 摘要把前者归为「人不在了」、后者归为「失败」 |
| CK-013 | 新增用例：处理器对某收件人产出空 | 该收件人无消息，且摘要归为「弃权」而非失败 |
| CK-014 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-004: 失败的收件人有第二次机会，但不带着旧内容

- Depends on: T-003
- Covers: RQ-010, RQ-011
- Boundaries: BD-007, BD-010
- Inputs: T-003 的失败归类
- Outputs: 内存重试队列与其排空

#### Steps

1. 重试项记 **(通知, 角色, 已用次数)**——**记角色不记连接**。
2. 排空时：把角色重新解析成**当时**的连接（没有则丢弃，不算失败），重新向处理器取内容，再投递。
3. 上限来自注册时的声明；达上限即丢弃。
4. 队列在内存里，不落库。进程结束即消失。
5. **不做去重。**

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-015 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-016 | 新增用例：进入重试之后、重试之前，该收件人的内容变了 | 送出的是变化后的值 |
| CK-017 | 新增用例：进入重试之后该角色换了连接 | 送到新连接上 |
| CK-018 | 新增用例：连续失败超过注册声明的上限 | 达上限后不再重试，该项被丢弃 |
| CK-019 | Non-command: 变异——把重试改成重发缓存的帧 | CK-016 失败；指名其全限定名与 Expected/Actual；还原后恢复 |
| CK-020 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-005: PvP 公示改经通知服务，那处连接查找消失

- Depends on: T-004
- Covers: RQ-013, RQ-014, RQ-004
- Boundaries: BD-008, BD-009, BD-010
- Inputs: `PlanPvpPublication` 现有实现；`pvp.ReadyOn(port, characterId)`
- Outputs: 一处真实调用方；领域服务不再查连接

#### Steps

1. 在对应的协议切片里定义 PvP 公示的通知类型与其处理器。
2. `PlanPvpPublication` 改为只回答「哪些角色」，把 `sessions.FindByCharacter(...)` 那一句去掉。
3. 调用方改为发布通知，人群是 `TheseCharacters`。
4. 对照迁移前后的帧、收件人集合与投递语义，把每一处差异逐条列出并指到代码窗口（口径见契约 revision 2）。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-021 | `dotnet build Server/86JP.Server.slnx --nologo` | 零警告零错误 |
| CK-022 | Non-command: `SelectCharacterLifecycleServices` 中连接查找的计数 | 0 |
| CK-023 | Non-command: 全仓把角色解析成连接的位置枚举 | 只有通知服务的解析器 |
| CK-024 | Non-command: 迁移前后帧、收件人集合与投递语义的对照 | 帧逐值相同；**收件人集合与投递语义**的每一处差异都在清单里并指到代码窗口（口径见契约 revision 2） |
| CK-025 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过；该处既有判据的期望值未被改写 |

### T-006: 评估其余两处扇出，按判定规则处理

- Depends on: T-005
- Covers: RQ-013
- Boundaries: BD-001, BD-002, BD-008, BD-010
- Inputs: 会话清理通告；相邻入场与清理里的 `presence.InArea(...)`
- Outputs: 迁移或不迁移的裁定与其理由

#### Steps

1. **判定规则**：一处扇出迁移，当且仅当它能在**帧内容不变**的前提下迁移、**收件人集合与投递语义的
   每一处差异都能逐条列出并指到代码窗口**，且迁移之后它所在的位置不比现在更难读。
   （原规则写的是「收件人集合与帧内容都不变」。按其字面，TASK-05 自己的迁移也过不了门——
   D-5 与 D-7 是通知总线的性质，任何走它的扇出都会带上。契约 revision 2 下调后同步改这里。）
2. 会话清理通告的「必送给本人」那一段**不迁移**——那是给本人的必送，不是扇出。只评估「尽力送旁人」
   那一段。
3. 若判定迁移，则人群需要 `InArea` 形态，一并加入封闭层级。
4. 若判定不迁移，**如实写出理由**，并记入交付。不迁移不是失败——本成果已有 T-005 这个真实调用方。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-026 | Non-command: 两处的逐个评估与裁定 | 每处都有明确裁定与依据 |
| CK-027 | Non-command: 若迁移，迁移前后帧、收件人集合与投递语义的对照 | 同 CK-024 的口径 |
| CK-028 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |

### T-007: 仓库约定记下这个模式与没做的部分

- Depends on: T-006
- Covers: RQ-015, RQ-016, RQ-017
- Boundaries: BD-011
- Inputs: T-001..T-006 的实际结果
- Outputs: 与代码一致的记载；收口证据

#### Steps

1. 记下 `CMD ⇒ Notify` 这个模式，以及「通知处理器只读」「通知在事务提交之后」两条约束。
2. **如实写下没做的部分**：周期触发不在本成果里；没有发布订阅；那 46 处回给请求方的发送未动；
   频道、全服、组队三种人群未实现及其原因；若 T-006 判定不迁移，一并写明。
3. 收口：全量判据；复核注入面判据的判定谓词与豁免清单未被放宽；把测试改动中每一处改动的期望值
   逐条归因。

#### Validation

| ID | Check | Expected result |
|---|---|---|
| CK-029 | Non-command: 记载与代码逐条核对 | 无一句比代码强 |
| CK-030 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` | 通过 |
| CK-031 | `dotnet test --filter "FullyQualifiedName~CommandHandlerArchitectureTests"` | 通过；判定谓词与豁免清单 diff 为空 |
| CK-032 | Non-command: `Server/tests` 的 diff 审查 | 凡改动的期望值都能归因到类型改名或本成果新增，无一处是行为差异 |

## Requirement Coverage

| Requirement | Tasks | Boundaries | Evidence |
|---|---|---|---|
| RQ-001 | T-002 | BD-003, BD-005 | CK-006 |
| RQ-002 | T-002 | BD-004 | CK-007 |
| RQ-003 | T-001 | BD-001 | CK-002 |
| RQ-004 | T-001, T-005 | BD-002, BD-008 | CK-003, CK-022, CK-023 |
| RQ-005 | T-002 | BD-003 | CK-008 |
| RQ-006 | T-002, T-003 | BD-003, BD-006 | CK-013 |
| RQ-007 | T-002 | BD-003 | CK-008 |
| RQ-008 | T-003 | BD-006 | CK-011 |
| RQ-009 | T-003 | BD-006 | CK-012 |
| RQ-010 | T-004 | BD-007 | CK-016, CK-017, CK-019 |
| RQ-011 | T-004 | BD-007 | CK-018 |
| RQ-012 | T-003 | BD-005, BD-006 | CK-012, CK-013 |
| RQ-013 | T-005, T-006 | BD-008, BD-009 | CK-022, CK-026 |
| RQ-014 | T-005 | BD-008, BD-009 | CK-024 |
| RQ-015 | T-007 | BD-011 | CK-030, CK-032 |
| RQ-016 | T-007 | BD-011 | CK-031 |
| RQ-017 | T-007 | BD-011 | CK-029 |

## Risks and Rollback

- Risk：重试被实现成重发缓存的帧。这在代码里看不出违反，只能靠判据。缓解：CK-019 用变异证明
  CK-016 会红。
- Risk：为了给 `InArea` 人群找调用方而勉强迁移区域那两处，带出行为改变。缓解：T-006 的判定规则
  以「帧内容不变 + 差异可逐条指到代码窗口」为准入条件（原为「收件人集合与帧内容都不变」，
  随契约 revision 2 下调），不迁移是允许的结果。
- Risk：主迁移做不到逐值不变。**已发生**：收件人集合做不到，帧做到了。按路由决策的 reroute 条件停下并
  返回所有者，所有者裁定下调口径（契约 revision 2 / spec C-3）。原缓解措施的后半句保留如下作为记录：这是路由决策里写明的 reroute 条件
  ——停下重新裁定人群形状，不要改判据迁就实现。
- Risk：顺手把那 46 处回给请求方的发送也套进人群抽象。缓解：Spec 的 Non-Goals 明写，T-007 的记载
  要如实写出「未动」。
- Rollback：未提交前可按精确工作流回滚还原到接受基线。

## Assumptions

- 判据命令必须带档案路径环境变量，否则读档案的判据静默跳过而不是报红
- 既知偶发失败的判别依据是「异常类型 + 栈根 + 复跑即绿」，不是用例名
- 既有的投递计划已经区分必送与尽力并做了失败隔离；本计划在它之上补人群，不重做投递
- `pvp.ReadyOn(port, characterId)` 返回的是角色，因此 PvP 公示的人群天然是 `TheseCharacters`
