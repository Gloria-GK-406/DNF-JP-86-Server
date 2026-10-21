---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: 「会话仍有效」「角色在线」收成具名判定，口径按用途统一

## Context

### Source Request

处理 issue #185「『会话仍是当前』63 处手写、至少 4 种算法」。用户在开始这一轮时说：“我们直接在这个分支全搞完了再一次性提交”。

Agent 在 #186 之后重新清点，给出现状和看法。用户指出：这些写法用的是 Find，行为其实是检查；按语义看，就是「这个会话到底有没有效」和「通知里的这个人到底在不在线」。用户提出 `checksessionavaliable(session)` / `checkcharOnline(charid)` 两类检查，命名交给 Agent，也接受 TryGet 形式。

Agent 给出的方案：
- 会话检查只留两级：`IsSelected`（当前选中，任何阶段）和 `IsInWorld`（在世界且连接仍归它，现有 `IsInWorld` 加严）。
- 角色在线用 `TryGetInWorld`。
- 生命周期路径用 `IsSelected`，其余玩法、投影、投递一律用 `IsInWorld`。
- 先机械替换，再统一口径。
- 写操作里按数据库再查归属的检查先保留，另记一条。
- 会话命令内部保留一处具名检查。
- 隔离留在管线。

用户回复：“其他的都OK，tryget也算能接受”，随后又说：“来吧，开始行动吧。加油”。本会话中 AGENTS.md 只作参考意见。

revision 2 吸收了独立审查（`reviews/task-contract-review.md`，r1 为 changes_required）的两条阻断项：

- **`IsSelected` 的定义细化为「当前选中且连接仍归它，任何阶段」**，与选角工作流 `EnsureCurrent` 今天的口径一致，也对应用户所说的「这个会话到底有没有效」。连接已被撤销的会话不算有效。这一条属于用户交给 Agent 的细节，汇报时向用户单独说明。
- **「按角色、线上号或连接查别人，以查不到作为不在线」的写法纳入范围。**

### Current Situation

基准 `ea6a1544`（#186 已封存），路径相对 `Server/src/`。#186 之后，在线身份是 `OnlineStateRegistry` 一把锁下、每个账号一条选中会话记录：阶段 `Selected`/`InWorld`、隔离标记，以及由记录推导出的「连接是否撤销」。调用方仍在各处手写当前性判定。下面按行检索，不含注释：

- **查出会话再与手里的会话比较**：会话类约 300 行，另有约 20 行副本、塔的 `RunId` 比较不算。四种查法（按账号、角色、连接、线上号）读的是同一条记录，答案相同，都是「是否当前选中」，不管连接是否撤销。
- **按角色、线上号或连接查别人**，再以 `is { }` / `is null` 决定是否处理或投递：约 90 处（例如 `NotificationAudienceResolver`、`CharacterVisibleSessionDomainService`、`RentalSlice`、`WeaknessRecoveryService`）。口径同样是任何阶段、连接是否撤销都算。
- **`IsInWorld`**（当前且阶段为在世界）：调用 12 处。
- **「在世界且连接仍归它」两种写法**：`IAuthenticatedGamePresenceQuery.IsActiveExact` 调用 6 处；手写的在线用户组合（`CharacterActive` 状态 + 账号 + `CurrentCharacterId`）约 32 处。
- **「当前选中且连接仍归它」**：`SelectCharacterHandler.EnsureCurrent`，在选角工作流的每一步之间检查。
- **自定义当前性 helper**：约 10～13 个，同名不同义。其中 `ClientSettingsDomainService.IsCurrent` 还比对在线用户代数，并允许尚无会话（登录阶段）。
- **口径不一与叠写**：同类操作口径不一，被包含的条件常被重复叠写（例如 `OnlineExpiredItemCleanup` 一个条件里连着四个判断）。

`TownAreaMember.Owns` 是城镇区域成员租约的比对，不属于会话当前性。

## Goal

**有效性只有两级：**
- **「是否有效」**：当前选中且连接仍归它。适用于选角工作流（发布前后的各阶段）。
- **「是否在世界」**：有效且已进入世界。适用于其余玩法、投影与投递。

「这个会话还有没有效」「这个角色在不在线、是哪个会话」，都只由会话领域服务上的具名判定回答，判定在在线身份的一致性边界内一次读出。调用方不再手写「查出来再比较」「查不到就当不在线」，也不再拼接在线用户标志。

拆除、补偿与断线清理要处理连接已撤销的残留记录，它们问的是「还有没有东西要释放」，由既有的 `StandingOf` / `ReleaseExact` 回答。

除这次有意的口径收紧外，协议行为与持久化结果不变。

## Scope

### In

- 在 `ISelectedSessionDomainService` 上提供以下判定，由在线身份直接实现：
  - `IsSelected(session)`：有效。
  - `IsInWorld(session)`：在世界，现有方法加严。
  - `TryGetInWorld(…, out session)`：按角色、线上号、连接三种键查找。
- 删除 `IAuthenticatedGamePresenceQuery.IsActiveExact`，并入加严后的 `IsInWorld`。
- 把生产代码中全部当前性判定替换为具名判定，并按用途归级。当前性判定包括：
  - 查出来再比较；
  - 以查不到决定不处理、不投递、不在线的查找；
  - 在线用户标志组合；
  - 叠写条件；
  - 各处自定义的当前性 helper。
- 通知人群解析与投递前检查改用具名判定：
  - 精确会话人群看完整会话；
  - 角色人群看「该角色在世界」，投递前保留「解析出的连接仍是它的」。
- 补充或改写自动测试：
  - 具名判定在各状态下的答案；
  - 口径收紧处的拒绝；
  - 选角、拆除、断线、顶替流程不受影响。
- 更新描述这些判定的仓库文档。
- 为「写操作中按数据库再查角色归属」创建一个 GitHub issue 记录，不在本次处理。

### Out

- 不改 `SelectedSessionSafety` 的隔离、连接级栅栏、`Capture` / `DemandCurrent` 的接受范围，也不改管线行为。其中的表达式可以改写为具名判定，但必须保留更严格的连接对象比较。
- 不改执行门、会话租约与加锁原语（#182、#204）。
- 不删写操作中按数据库再查归属的检查（`BelongsToAsync`、`FindOwnerAsync` 等）。
- 以下 `Find…` 查找保留，不算当前性判定：
  - 请求方按自己这条连接解析会话、之后交给命令或领域服务判定的查找；
  - 生命周期清理为取出待释放会话所做的查找。
- 不改用于当前性以外用途的在线用户状态读取，不改 `TownAreaMember.Owns`。
- 不改协议字段、数据库结构、客户端；不合并、推送、开 PR 或部署。

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-1 | #185 / 用户：「会话是否有效」收成一个检查，不再到处 Find 再比 | in_scope | Goal；Scope In 1、3；SC-1、SC-2 | 用户提出 `checksessionavaliable(session)`；「有效」不含连接已撤销 |
| RI-2 | 用户：「通知里的人是否在线」按角色检查；接受 TryGet 形式 | in_scope | Scope In 1、3、4；SC-1、SC-4 | 用户提出 `checkcharOnline(charid)`，“tryget也算能接受”；包括以查不到当作不在线的查找 |
| RI-3 | 用户：命名交给 Agent | in_scope | Scope In 1 | “命名啥的还是你比较专业” |
| RI-4 | Agent 提议、用户认同：有效性两级，选角工作流问有效，其余问在世界 | authorized_change | Goal；Scope In 3；Constraints 1；SC-3 | 玩法路径不再接受未入世或连接已撤销的会话，选角工作流里的领域服务不再接受连接已撤销的会话；均为收紧。用户先说“其他的都OK”，再说“开始行动吧” |
| RI-5 | Agent 提议、用户认同：先机械替换、再统一口径 | in_scope | Constraints 4 | 同 RI-4 授权 |
| RI-6 | Agent 提议、用户认同：数据库再查归属先保留、另记一条 | in_scope | Scope In 7；Scope Out 3；SC-6 | 同 RI-4 授权 |
| RI-7 | Agent 提议、用户认同：会话命令内部保留一处具名检查；隔离留在管线 | in_scope | Constraints 3；Scope Out 1 | 同 RI-4 授权 |
| RI-8 | #185 关联：加锁原语与租约不在本次 | preserved_upstream | issue #182、#204 | 两个 issue 仍开启 |
| RI-9 | 用户：全部 issue 在本分支做完后再一次性推送 | in_scope | Scope Out 6；Authorization | Source Request 第一段的原话；本次只做本地检查点提交 |

## Constraints

- **用途规则**：
  - `SelectCharacterHandler` 工作流的各阶段命令及其调用的领域服务方法，问 `IsSelected`。工作流里今天已按在世界口径检查的命令不放宽。
  - 拆除、补偿、断线清理用 `StandingOf` / `ReleaseExact`。
  - 其余玩法写操作、查询、投影、投递，问 `IsInWorld`。
- **保底规则**：替换后，每个调用点的接受范围都不得比原来宽。只允许 RI-4 授权的收紧。原判定若含两级之外更严格的条件（例如连接代数、连接对象比较），保留该条件。
- **重复检查的处理**：删掉同一调用链内被包含的重复检查后，原来有保护的入口仍须有等价保护；会话命令内部的检查不因管线已经检查过而全部删除。不为原本没有检查的入口新增检查。
- **两步走**：先做语义不变的替换，再做口径收紧。每处收紧都要有测试证据。
- **一次迁完、删净**：不留旧写法、转发层或 `[Obsolete]`（依据 `Docs/Agent/RefactorPreferences.md` §2）。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 生产代码（`SelectedSessionSafety` 按 Scope Out 1 处理）中不再有：查出来再比较的会话当前性判定；以查不到当作不在线的会话查找（Scope Out 4 保留的两类除外）；用于当前性的在线用户标志组合；`IsActiveExact`；各处自定义的纯当前性 helper | 代码检索结果；保留的 `Find…` 逐条归类 |
| SC-2 | `IsSelected`、`IsInWorld`、`TryGetInWorld` 由在线身份在一处一致性边界内实现；在选中、在世界、隔离、连接撤销、被替换、已释放各状态下答案正确 | 实现检查与单元测试 |
| SC-3 | 每个调用点按用途规则归级，且没有一处比原来宽；收紧处对未入世或已撤销连接的会话拒绝；选角、拆除、补偿、断线、顶替流程测试通过；跨账号目标和非命令路径的收紧行为有说明 | 调用点归类记录、回归测试 |
| SC-4 | 人群解析与投递按人群形态使用具名判定：精确会话人群看完整会话，角色人群看「在世界」，投递前再比对解析出的连接；拆除通告等必须送达的投递不因收紧被丢弃 | 通知相关测试 |
| SC-5 | 完整解决方案构建通过；既有自动测试全部通过（数量与断言变化有说明）；文档与新判定一致 | 构建与测试输出、变化说明、文档差异 |
| SC-6 | 「写操作中按数据库再查归属」已有 issue 记录 | issue 链接 |
| SC-7 | 向用户报告结果、未解决问题、`IsSelected` 定义的细化，以及验证结论 | 最终汇报 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | Agent 给出可启动判断（命名、两级、归类规则、SDD 规模、本地检查点提交）后，用户回复：“来吧，开始行动吧。加油” | 编写契约与 SDD 产物；在工作区 `task/refactor-session-20261021` 内实施、验证、做本地检查点提交；创建一个记录 DB 归属再查的 issue。不含合并、推送、PR、部署或清理工作区 |

## Assumptions

- 本账号、本连接的会话命令，在选角工作流的独占租约内要等到发布之后才执行。跨账号把这个角色当目标的操作、定时任务与事件订阅者，能看到 `Selected` 阶段的会话；收紧后它们会拒绝或跳过尚未入世的角色，这属于 RI-4 授权的收紧，由 Spec 列出并给出测试证据。
- 现有自动测试足以证明未收紧路径的行为不变。
