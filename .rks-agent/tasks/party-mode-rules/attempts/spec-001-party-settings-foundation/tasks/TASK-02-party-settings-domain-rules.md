# TASK-02 — 队伍设置模型、共享规则与原子领域转移

## Task Unit Contract

- Revision: 2。
- Outcome and references: 交付不可变 12 字节设置、唯一共享规则服务，以及无队创建/已有队更新的原子
  领域转移，覆盖 Plan T-002、RQ-001/RQ-002/RQ-003/RQ-005/RQ-006/RQ-007、
  AC-001/AC-002/AC-004/AC-005/AC-006 的领域部分。
- Owned boundary: `Server/src/Dfo.Server.Data/Parties/`、
  `Server/src/Dfo.Server.Abstractions/Parties/`、`Server/src/Dfo.Server.Application/Parties/`、
  `ApplicationModule.cs` 的必要注册，以及对应 Application tests。不得新增/注册 `0x000C` codec、
  Protocol handler 或 type-1 encoder，不修改协议资料状态。Revision 2 允许仅为新 `PartySnapshot`/
  `IPartyDomainService` 形状保持编译而机械更新既有 Protocol test fixture/stub；不得改变其协议断言。
- Dependencies: TASK-01 已通过独立审查的 current-A21 规范块证据：`[2]=capacity`、
  `[9]=category`、`[10..11]=ModeTargetRaw u16-le`，其余字节无损保留；默认类别为 Normal (`2`)。
- Required checks: 先记录能因缺失模型/规则/领域入口失败的 RED；GREEN 覆盖防御性复制、byte-exact
  round-trip、默认值、三种类别/未知类别、容量 `1..4`、容量小于成员数、当前会话/队长/旧代际拒绝、
  party identity 保持、revision 单调、并发原子性、无状态污染与生产单例注册；运行 Party/Application
  聚焦回归及 `git diff --check`。

## Detailed Plan

1. 先在 `PartyDomainServiceTests` 增加行为 RED：默认邀请建队得到 Normal 设置；任意合法 12B 设置经
   模型 round-trip 且输入/输出数组不能回写快照；无队 leader 可显式创建单人队；已有队 leader 原地
   更新；非 leader、旧 session、非法类别/容量、容量小于成员数均不推进 revision。
2. 在 Data 建立协议无关值对象：`PartyCategory` 只允许 `Story=0`、`Cycle=1`、`Normal=2`；
   `PartySettings` 私有持有复制后的 12B，以只读字段暴露 capacity/category/target，并用内容相等而非
   数组引用相等；`ToWireBytes()` 每次返回新副本。默认块沿用现有名册信息字节，仅将 `[9]` 修正为 `2`。
3. 在 Abstractions 增加 `IPartyModeRuleService`，集中提供默认设置和合法性判定；规则至少校验精确长度、
   类别、容量 `1..4`、容量不小于当前成员数。领域服务只能消费规则结果，不复制这些判断。
4. 扩展 `PartySnapshot` 使标题与设置随同一个 revision 不可变发布；标题按原始字节冻结，暂不解释 UI
   语义。扩展失败原因与 `IPartyDomainService`，提供一个根据当前成员状态原子地“创建或更新设置”的
   typed 操作；调用方传入精确 `SelectedSessionIdentity` 和已解码的设置/标题。
5. 在 `PartyDomainService` 的现有 `sync` 内完成所有判断和写入：当前 session 无队时创建新 generation、
   slot 0 leader、revision 1；已有队时必须为同代际 leader，合法更新后 revision `+1`，party id/
   generation/members/invites 不变。任何拒绝在首次写入前返回，通知计划为空。
6. 邀请隐式建队统一使用规则服务的默认 Normal 设置；`IsFull` 和可用 slot 受该 party 的 capacity 控制，
   仍保留最大四槽物理不变量。快照复制设置与标题，旧快照不随后续更新变化。
7. 生产注册一个 singleton 规则服务并注入 singleton `PartyDomainService`；更新现有 fixture/stub 和跨项目
   编译点，不让 Application 引用 Infrastructure。
8. GREEN 运行新增设置/领域测试，再运行全部 `PartyDomainServiceTests`、相关 session cleanup 与
   Application 项目回归；逐项解析结果并记录命令、Passed/Failed/Skipped、`git diff --check`。

## Result — Round 1

### Changed

- 新增内容相等且防御性复制的 `PartySettings`/`PartyTitle`；12 字节块类型化读取 capacity、
  `PartyCategory` 与 little-endian `ModeTargetRaw`，未知字节仍 byte-exact 往返。
- 新增单例 `IPartyModeRuleService`/`PartyModeRuleService`，唯一提供 A21 Normal 默认块并裁决三个已知
  类别、容量 `1..4`、容量不得小于当前成员数。
- `PartySnapshot` 固定携带 settings/title；`PartyNotificationPlan.SettingsChange` 明确区分 Created/Updated，
  后继协议单元无需从 revision 猜通知阶段。
- `PartyDomainService.CreateOrUpdateSettingsExact` 在现有同一把 process-local 锁内完成当前会话、精确
  成员、队长、规则校验与创建/更新；创建单人队，更新保持 party id/generation/members，成功 revision
  单调推进，拒绝不消费邀请或改变快照。邀请形成的新队伍也消费规则服务的 Normal 默认设置，动态容量
  同时约束邀请与接受。
- 新增 7 个设置/规则/领域测试；既有 Application fixture 注入规则服务，Protocol fixture/stub 只做
  Revision 2 允许的构造/接口适配，没有新增协议行为。

### Why

设置现在是领域快照的一部分，而不是 encoder 内的匿名常量；所有共享合法性只在规则服务决定。领域
结果显式携带创建/更新语义和不可变快照，使后继 `0x000C` handler 能保持“协议调用 Command、Command
调用领域服务”的边界，并能在锁外按同一 revision 投影而不重查可变状态。

### Checks

- TDD RED：首次只加入 `PartySettingsDomainTests` 后，定向命令因 `PartySettings`/`PartyCategory` 未定义
  编译失败（CS0246），见 `evidence/task02/task02-red.txt`；随后才加入生产实现。
- 聚焦 GREEN：`FullyQualifiedName~PartySettingsDomainTests|PartyDomainServiceTests|
  SelectedSessionPartyCleanupTests`，TRX 逐项 `Passed=22`、无 skipped/failed。
- 既有 Party Protocol 回归：`FullyQualifiedName~Party`，TRX 逐项 `Passed=48`。
- 带 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` 的完整 Application 重跑，
  TRX 逐项 `Passed=1376`、无 skipped/failed。首轮仅 `NotificationDeliveryTests` 的 50ms 墙钟断言在并发
  负载下偶发测到约 2.6s；单项复跑 `Passed=1`，完整套件再次重跑全绿，未修改该无关代码。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：成功，0 warning / 0 error；工作树与
  staged diff 的 `git diff --check` 均通过。静态检索显示类别/容量/默认值判断只在规则服务，领域只映射
  规则结果。

### Remaining

- 本单元未接入任何 `0x000C` decoder/handler 或 type-1 encoder；现有 type-0 encoder 的匿名块将在
  TASK-03 消费 `PartySnapshot.Settings` 时替换，并以既有完整向量证明除默认 category 外无变化。
- 标题长度/UTF-8 边界属于 TASK-03 严格 decoder；模式目标的业务语义仍属于后继 Story/Cycle 规格。
- 等待独立 quick review；通过后再把 TASK-02 标记 done 并详细计划 TASK-03。

## Independent Quick Review — Round 1

```yaml
kind: rks-review
verdict: changes_required
```

- P1：容量规则尚未完全收敛；领域仍有独立 `MaximumCapacity=4`/`IsFull`，Request/Accept/TryAdd 绕过
  规则服务。若规则默认容量为 1，首次接受可能先创建 party/写 membership/消费邀请，再因 TryAdd 失败，
  形成失败但状态污染。
- P2：合法容量只测 4；缺容量 1/2/3、邀请后缩容导致 Accept 拒绝且不污染，以及默认规则在 Accept
  侧失败的回归。
- 其余不可变模型、Normal 默认、精确会话/队长、identity/revision/kind 与 Protocol 机械适配均通过。

## Result — Round 2

### Changed

- `IPartyModeRuleService` 现在同时给出 `MaximumCapacity` 与 `ValidateAdmission`；合法容量上限、当前状态
  校验和“再加入一人”判断均只由同一规则服务给出。`PartyState` 的物理槽数组也由该最大值构造，不再
  另写常量；领域内的 `IsFull`/`TryAdd` 业务判断已移除。
- Request 对已有队与默认建队都调用 admission 规则；Accept 在既有 generation 与首次建队两条路径
  再次调用同一规则，应对邀请后设置变化。
- Accept 先完成会话、generation、队长、城镇、成员、规则和空槽全部检查；只有此后才一次性提交
  generation/party/membership/invitation/member 写入。业务拒绝路径现在不会先创建队伍或消费邀请。
- 合法容量矩阵补齐 `1..4`；新增“邀请后缩到当前人数，Accept 拒绝且 invitation/snapshot/revision
  不变，扩容后同邀请成功”和“默认规则在 Request 后变化导致 Accept 拒绝，无半建队且恢复后同邀请
  成功”两个回归。

### Checks

- remediation 聚焦测试 TRX：`Passed=24`、无 failed/skipped。
- Release solution build：0 warning / 0 error；Party Protocol 回归仍为 `Passed=48`。
- 静态搜索确认 capacity 的合法上限只在 `PartyModeRuleService.MaximumCapacity` 定义；四个领域准入点只
  调用 `ValidateAdmission`，`PartyState` 只负责物理空槽定位/已校验后的写入。
- unstaged/staged `git diff --check` 均通过。

### Remaining

- 等待 Round 2 fresh quick review；T-003/T-004 边界不变。

## Independent Quick Review — Round 2

```yaml
kind: rks-review
verdict: passed
```

- Reviewer 确认 `MaximumCapacity`、`Validate`、`ValidateAdmission` 已统一由规则服务暴露；生产容量上限
  与合法性只有一个来源，Request/Accept 的既有队与默认建队路径都消费 admission 结果。
- Accept 在 party/membership/invitation/member 任一写入前已完成 generation、精确成员、队长、规则与
  物理空槽检查；`PartyState` 只保留由规则上限构造的槽数组、空槽定位和已校验写入。
- 新增容量 1..4、邀请后缩容/恢复、默认变化/恢复用例均成立；fresh Application 聚焦 `24/24`、
  Protocol Party `48/48`，staged/unstaged `git diff --check` 均通过。P1/P2 已闭合，无新阻断项。

## Post-result Product Amendment（2026-09-12）

TASK-02 接受后，实机差分确认设置块 `[1]=1` 会显示回归玩家/赛丽亚祝福队伍标志；用户随后明确当前
不实现该玩法并要求默认及所有队伍值为 `0`。TASK-04 Revision 3 因此在不重开本已接受单元的前提下，
修订子 TaskContract/Spec，并把 `[1]` 从“未知无损字段”改为规则服务拥有的规范化字段：兼容接收，领域
保存与后续通知统一为 `0`，其他 11 字节仍无损。实现与新 TDD 证据归 TASK-04 所有。
