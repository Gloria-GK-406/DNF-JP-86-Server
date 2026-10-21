---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 2
state: ready
---

# Specification: 「会话仍有效」「角色在线」收成具名判定

## Why and Outcome

在线身份从 #186 起只有一条记录，但调用方仍在各处手写「查出来再比较」「查不到就当不在线」，以及在线用户标志组合，口径各不相同。

交付后：
- 手里有会话的，问 `IsSelected` 或 `IsInWorld`。
- 手里只有角色、线上号或连接、想知道对方在不在线的，用 `TryGetInWorld`。
- 拆除和清理问「还有没有东西要释放」，用 `StandingOf` / `ReleaseExact`。

除 RQ-011 的有意收紧外，协议行为和持久化结果不变。

路径相对 `Server/src/`。

## Definitions

- **记录在**：参数会话与该账号的选中会话记录值相等（今天 `Find(session.AccountId) == session` 的含义），不论阶段、隔离与连接。
- **连接仍归它**：该会话的 `ConnectionId` 上仍登记着在线用户，账号一致；如果记录已经有所属在线用户，登记的就是它。也就是 `OnlineStateRegistry.IsRevoked` 为假。
- **有效**：记录在，且连接仍归它，不论阶段与隔离。
- **在世界**：有效，阶段为 `InWorld`，连接是游戏端点，所属在线用户就是这条连接上的用户。这就是今天 `IsActiveExact` 的接受范围，Game 端点和所属在线用户两个条件都保留。
- 三个具名判定都不看隔离标记。隔离由管线与 `SelectedSessionSafety` 负责。
- **当前性判定**：回答「这个会话是否仍是当前的、是否在世界、是否仍归这条连接」「那个角色在不在线」的表达式。包括以 `is null` / `is { }` 决定是否处理或投递的查找。
- **保留的查找**：
  - (a) 请求方按自己这条连接解析会话，之后交给命令或领域服务判定。
  - (b) 清理路径与返回选角为取出待释放会话、确认已经没有残留记录所做的查找。包括 `SelectCharacterLifecycleServices.cs:199`、`CompleteReturnToSelectionCommandHandler`、`OnlineUserConnectionObserver.cs:106`。
  - (c) `ClientSettingsDomainService.IsCurrent` 中「尚无会话」的分支（`FindByConnection(…) is null`）。它要的是「这条连接上还没有选中会话」，三个判定都表达不了，按保底规则原样保留。
  - 以上三类在 AC-001 的归类表里逐条列出。
- **选角工作流路径**：`SelectCharacterHandler` 本身、它派发的各阶段命令，以及这些命令调用的领域服务方法。
- **清理路径**：拆除服务、补偿命令、断线观察者。

## Requirements

### 判定接口

| ID | Requirement |
|---|---|
| RQ-001 | `ISelectedSessionDomainService.IsSelected(session)`：会话有效时为真。 |
| RQ-002 | `ISelectedSessionDomainService.IsInWorld(session)` 加严为「在世界」。 |
| RQ-003 | `ISelectedSessionDomainService.TryGetInWorld(CharacterId / ushort wireUserId / NetworkConnectionId, out SelectedSessionIdentity session)`：在同一次加锁内按键找到选中会话记录；该会话在世界时返回真并带出会话，否则返回假。 |
| RQ-004 | 三者由 `OnlineStateRegistry` 直接实现，`SelectedSessionDomainService` 只做门面。删除 `IAuthenticatedGamePresenceQuery.IsActiveExact` 及其实现，接口其余成员保留。`Find…`、`StandingOf`、`ReleaseExact` 的语义不变。 |

### 替换

| ID | Requirement |
|---|---|
| RQ-005 | `X.Find…(key) ==/!= session` 形式的会话当前性比较全部替换为具名判定。key 若取自 `session` 自身，或者按构造必然等于 `session` 的对应字段，直接替换。key 若来自别处、比较里顺带断言了「key 属于这个会话」，把这条断言显式写出来，与具名判定并列。 |
| RQ-006 | 以查不到当作不在线的会话查找（`is { } x`、`is not { } x`、`is null`、`?.` 后续使用）改为 `TryGetInWorld`，选角工作流路径上的改为有效性检查。保留的查找不改。 |
| RQ-007 | 用于当前性的在线用户组合（`TryGetByConnection` + `CharacterActive` + 账号 + `CurrentCharacterId`）改为 `IsInWorld(session)`。如果在线用户读取另有用途（例如取连接对象），只保留那部分。 |
| RQ-008 | 同一条件里被包含的判定删除，例如 `IsSelected && IsInWorld`、`IsInWorld && IsActiveExact`。 |
| RQ-009 | 只做当前性判定的本地 helper 删除，调用处直接用具名判定。把当前性与其他事实组合在一起的 helper 保留，内部改用具名判定；这类事实包括副本或塔的代数、在线用户代数、连接对象（例如 `ClientSettingsDomainService.IsCurrent`、两塔的 `Current`、`SelectCharacterHandler.EnsureCurrent`）。与当前性无关的 helper 不动，包括 `TownAreaMember.Owns`、`IsCurrentLeader`、`IsCurrentPeriod`。 |
| RQ-010 | 测试替身实现新成员时，必须如实反映替身自身持有的状态，不得恒真或恒假。 |

### 口径

| ID | Requirement |
|---|---|
| RQ-011 | **用途规则：**<br>- 选角工作流路径中，关于工作流自己会话的检查问有效（`IsSelected`）；工作流里今天已按在世界口径检查的命令，保持 `IsInWorld`。<br>- 工作流里查别人的地方（同伴、大厅成员、投影、投递）按投影和投递处理，用 `TryGetInWorld` / `IsInWorld`。<br>- 清理路径用 `StandingOf(session) is not null` / `ReleaseExact`，与今天一样接受连接已撤销的记录。<br>- 其余玩法写操作、查询、投影、投递问 `IsInWorld`。<br>- 同一个领域服务方法若同时被选角工作流和玩法入口调用，方法内用 `IsSelected`。玩法入口原来有检查的，改为 `IsInWorld`；原来没有检查的，不新增。<br>**收紧的范围：**<br>- 玩法路径不再接受尚未入世、或连接已撤销的会话；<br>- 选角工作流里的领域服务不再接受连接已撤销的会话。 |
| RQ-012 | **保底规则：**替换后，每个调用点的接受范围都不得比原来宽，RQ-011 的收紧除外。原判定中两级之外更严格的条件要保留。 |
| RQ-013 | 删除同一调用链里被包含的重复检查后，原来有保护的入口仍然要有等价保护。会话命令内部的检查，不能因为管线已经检查过就全部删掉；原来没有检查的入口，也不新增检查。`SelectedSessionSafety` 的接受范围和管线行为不变。 |
| RQ-014 | 跨账号把角色当作目标的检查（组队、交易、同伴投递等），以及非命令路径（定时任务、事件订阅者），在 plan 里逐条列出收紧后的行为。 |
| RQ-014a | 涉及旁人的查找，原来「查不到就抛异常」的，收紧后改为跳过这个人，不能让旁人的状态变成请求方的失败（例如 `PlanPvpAsync` 里大厅成员的查找）。查请求方自己会话的，保持原来的失败方式。 |

### 投递

| ID | Requirement |
|---|---|
| RQ-015 | `NotificationAudienceResolver`：精确会话人群用 `IsInWorld(exact)`；角色人群用 `TryGetInWorld(characterId, out session)`。<br>`NotificationBus.IsStillHere`：精确会话人群用 `IsInWorld(exact)`；角色人群要求 `TryGetInWorld(characterId, out session)` 为真，且 `session.ConnectionId == recipient.ConnectionId`。<br>不经人群、直接按连接发送的投递（例如发给离开者本人的拆除通告）不受影响。 |
| RQ-016 | 通知处理器和投影里「按连接或角色取出会话，再和期望值或角色比较」的写法，改为 `TryGetInWorld`。 |

### 其余

| ID | Requirement |
|---|---|
| RQ-017 | 更新以下仓库文档：<br>- `AGENTS.md`：Users/Sessions 段落、「在线身份只有一条记录」段落、通知人群里关于 `FindByCharacter` 调用点的说明；<br>- `Document/` 下提到 `IsActiveExact` / `IsInWorld` 的说明。 |
| RQ-018 | 为「写操作中按数据库再查角色归属」创建 GitHub issue，列出已知调用点，打上 `refactor-signal` 标签。 |

## Acceptance

| ID | Covers | Acceptance |
|---|---|---|
| AC-001 | RQ-004～RQ-009 | 在 `Server/src` 中检索，满足：<br>- 会话类 `Find…(…) ==/!=` 比较为 0 处；<br>- `IsActiveExact` 为 0 处；<br>- 剩余的会话 `Find…` 调用逐条归入「保留的查找」(a) 或 (b)；<br>- `OnlineUserState.CharacterActive` 剩余的读取逐条说明为非当前性用途；<br>- RQ-009 所说的纯当前性 helper 为 0 处。 |
| AC-002 | RQ-001～RQ-004 | 单元测试覆盖选中、在世界、隔离、连接撤销、被替换、已释放各状态，验证 `IsSelected`、`IsInWorld`、`TryGetInWorld`（三个重载）的答案。 |
| AC-003 | RQ-011、RQ-014 | plan 里列出选角工作流路径和清理路径的调用点清单，以及 RQ-014 的跨账号与非命令路径清单。选角、顶替、断线、拆除、补偿流程的既有测试通过。 |
| AC-004 | RQ-011 | 回归测试证明收紧生效：<br>- 至少一条玩法写操作和一条投影，拒绝连接已撤销的会话；<br>- 至少一条跨账号目标检查，拒绝尚未入世的角色；<br>- 选角工作流里至少一个领域服务，拒绝连接已撤销的会话。 |
| AC-005 | RQ-015、RQ-016 | 投递测试：<br>- 精确会话人群：会话连接已撤销时不投递；<br>- 角色人群：解析出的连接变化后不投递，连接不变时照常投递，角色尚未入世时不投递。 |
| AC-006 | RQ-010、RQ-012、RQ-013 | 完整解决方案构建 0 警告 0 错误；全量测试除基线已有的失败外全部通过；测试数量和断言的改动逐条说明。 |
| AC-007 | RQ-017 | 文档差异。 |
| AC-008 | RQ-018 | issue 链接。 |
