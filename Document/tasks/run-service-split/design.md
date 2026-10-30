# DungeonRunDomainService 拆解方案（#187 剩余项）

基线：main `85a352dcd`（#256 合并后）。状态：待用户裁定 §5。

## 1. 现状

`DungeonRunDomainService` 共 16 个分部文件、2736 行，对外实现 `IDungeonRunQueryDomainService` 加 8 个窄接口
（入场、战斗、参与者、地面物、结算、翻牌、计时、离场）。PR3 只把文件拆开、把调用方收窄，**实现仍是一个类、一把锁、一份状态**。

这个类里的进程内状态（全部受同一把 `gate` 保护）：

| 状态 | 归属 | 谁在读写（分部） |
|---|---|---|
| `runsById`、`runsByParty` | run 索引 | 几乎全部 |
| `deathClaims`、`participantEffectClaims` | 某局的击杀认领 | Deaths；Movement 用来拦移图 |
| `groundDropClaims`、`preparedGroundDropClaims` | 某局的拾取认领 | GroundDrops；Movement 拦移图 |
| `settlementClaims` | 某局的结算认领 | Settlement；Departure 离队时清 |
| `clearRewardCardClaims`、`preparedClearRewardCardClaims` | 某局的翻牌认领 | Cards；Departure 离队时清 |
| `wipeDeadlines`、`clearReturnDeadlines` | 某局的团灭／通关回城时刻 | Timers；Participants；Lookup 删局时清 |
| `nextClaim` | 认领序号 | 五个分部共用 |
| `DungeonRunAggregate`（Model.cs 420 行） | 每局的格子、参与者、进度、掉落编号、ModeState | 全部 |

分部之间的私有调用（脚本统计）只有这几条：`Lookup.RunOf / Remove / Publish`（索引）、
`Departure.EndRun`（被入场、计时、事务提交调用）、各认领表的 `Clear*Claims`（被删局调用）、
`Movement.TryMoveCore`（遗忘之地道具移图复用）。其余耦合全部经由“共享字段”发生。

## 2. 危险在哪

1. **认领表按局存放，却挂在全局。** 七张认领表与两张时刻表的键都带 `RunId`，删局时要逐张清理（`Lookup.Remove` 依次调 5 个 `Clear*`），
   离队、移图又各自去别人的表里查。新增一种认领就要记得改删局、离队、移图三处，漏一处就是“幽灵认领”拦住移图或泄漏。
2. **一把锁覆盖全部玩法。** 任何一处在锁里调外部服务或改错状态，都会波及所有地下城操作；也无法看出哪段代码负责哪份状态。
3. **新功能只能继续往这个类里加。** 遗忘之地（#242）就是这样把运行态加成了可空字段，PR4 才收回 `ModeState`。

## 3. 目标结构

```
DungeonRunStore（唯一有状态的单例）
  ├─ runsById / runsByParty、gate、claim 序号
  └─ DungeonRunAggregate（每局）
        ├─ 格子、参与者、进度、ModeState（已有）
        └─ RunClaims：击杀、效果、拾取、结算、翻牌认领，团灭／回城时刻（从全局表搬进来）

无状态的领域服务，每个实现一个已有窄接口，只依赖 Store 和自己用到的规则／协作者：
  DungeonRunQueryService      IDungeonRunQueryDomainService        （Lookup）
  DungeonRunEntryService      IDungeonRunEntryDomainService        （Entry、Movement、Transitions、遗忘之地移图、机关推进）
  DungeonRunCombatService     IDungeonRunCombatDomainService       （Deaths、Objects、DynamicActors）
  DungeonRunParticipantService IDungeonRunParticipantDomainService （Participants）
  DungeonRunGroundDropService IDungeonRunGroundDropDomainService   （GroundDrops）
  DungeonRunSettlementService IDungeonRunSettlementDomainService   （Settlement）
  DungeonRunCardService       IDungeonRunCardDomainService         （Cards）
  DungeonRunTimerService      IDungeonRunTimerDomainService        （Timers、遗忘之地计时）
  DungeonRunDepartureService  IDungeonRunDepartureDomainService    （Departure、EndRun）
```

要点：
- **状态只有一个 owner：Store。** 服务都不持有字段状态，`GameplayStateCensusTests` 只需登记 Store 一项，其余服务无状态可被普查证实。
- **每局的东西跟着局走。** 认领与时刻进 `RunClaims`，删局就是丢掉这一局，不再有逐表清理；移图拦截、离队清理都只问本局。
- **锁不拆。** 仍是 Store 上的一把 `gate`（玩法路径的串行本来就靠唯一执行器，这把锁只为执行器外的读一致），拆成多把锁会引入锁顺序问题，没有收益。
- **服务之间只经窄接口单向依赖。** 入场与计时要结束一局时调用离场接口的 `EndRun`；离场不反向依赖它们。拆完若出现环，说明那段逻辑属于 Store 或聚合，就下沉而不是加延迟解析。
- 事务参与者类（`EntryCommit`、`MoveCommit`、`GroundDropCommitParticipant` 等）跟随所属服务搬走，提交时改的是 Store／聚合，不再引用整个服务。
- 新服务的依赖一律必填（#183 规则），删掉现在构造器里的 `?? new` 与 `= null`。

## 4. 分步（3 个 PR，每步行为不变，可独立回滚）

| PR | 内容 | 风险控制 |
|---|---|---|
| A 每局状态进聚合 | 7 张认领表、2 张时刻表搬进 `DungeonRunAggregate.RunClaims`，键去掉 `RunId`；`Remove` 不再逐表清理；移图、离队改问本局。仍是一个类 | 纯搬迁；全量测试；补两条反例：删局后同局认领不可再提交，未结认领拦住移图 |
| B 抽出 Store | 索引、`gate`、认领序号、`RunOf/Publish/Remove` 移入 `DungeonRunStore`；聚合与模型类型移出服务成为 internal 类型。服务改为经 Store 访问。仍是一个服务类 | 纯搬迁；普查改为登记 Store |
| C 按窄接口拆类 | 9 个服务类各实现一个窄接口，DI 逐个登记；删除生产中的聚合接口 `IDungeonRunDomainService`（锦标赛服务、翻牌 CMD 改注入两个窄接口）；测试的 `TestParties.Runs()` 返回一个**仅测试用**的组合对象，内部共享同一个 Store | 写入口表 `GameplayWriteEntryTableTests` 的 62 处 `DungeonRunDomainService.X` 改为新类名；全量测试 + 现有地下城、两塔、祭坛金样本 |

C 之前先把 9 个服务的依赖图画出来，确认无环；有环的那段先在 A／B 里下沉到聚合或 Store。

## 5. 待用户裁定

1. **拆分粒度**：按现有 8 个窄接口 + 查询，共 9 个服务（推荐，接口已在 PR3 定好，调用方不用再改）；或者更粗的 4 个（入场移动／战斗掉落／结算翻牌／计时离场）。
2. **聚合接口 `IDungeonRunDomainService`**：生产中删除，测试改用仅测试用的组合对象（推荐，避免留一个转发门面）；或保留为生产聚合。
3. **与第 2 组的顺序**：第 2 组（#180/#181）计划在第 1 组之后搬地下城相关的 handler 业务。本方案改的是领域服务内部，A、B 两步不碰 Protocol，可与他们并行；C 会改两个注入点（锦标赛服务、翻牌 CMD）和大量测试，建议 C 落在第 2 组地下城部分之前或之后，不要同时进行。

## 6. 验证

每个 PR：`dotnet build` 0 错误，全量 `dotnet test` 0 失败，Codex 审核通过当前提交。A 补上 §4 的两条反例；C 前后各跑一次全部金样本测试并比对。
结构判据（#195）：C 合入时加一条结构测试，拒绝在任何 `DungeonRun*Service` 上声明实例字段状态（只允许 Store 持有），并带一个违规反例。

## 7. 实施结果与偏差

用户裁定：按 9 个服务拆分，生产代码删除聚合接口，三个阶段放进同一个 PR。

- **阶段 A**（单独提交）：7 张认领表与 2 张时刻表搬进 `DungeonRunAggregate.Claims`（`RunClaims`）。删局只调一次 `ReleaseOnRemoval`，
  已进入事务准备阶段的拾取与翻牌保留，随捕获它的事务完成。补 `DungeonRunClaimsTests`：删局前的击杀认领不能提交；一局的认领到不了另一局。
- **阶段 B 与 C 合成一个提交**：状态普查（`GameplayStateCensusTests`）要求可变的局聚合不能被任何 public／internal 成员交出去。
  单独抽出顶层 Store 再让服务来取聚合会违反这条，所以不保留中间形态：
  - `DungeonRunStore` 是外层类，持有目录、锁、认领序号，以及结束一局、释放参与者、打开 clear 这些跨服务的局级操作；聚合与认领是它的私有嵌套类型。
  - 9 个服务（查询由 Store 自己实现）是 Store 的嵌套类，每个在自己的文件里，有独立的依赖、DI 登记和写入断言名；
    它们登记在普查的 `StatelessEntries` 里，普查会拒绝它们持有状态，这就是“服务不得持有状态”的结构判据，没有另写测试。
  - 生产代码删除 `IDungeonRunDomainService` 与 `AddDungeonRunViews`；锦标赛服务改注入入场与结算两个窄接口，翻牌 CMD 改注入翻牌与计时两个窄接口，
    入场准入服务改注入查询接口。
  - 测试：聚合接口挪进 `tests/Support/DungeonRunTestComposition.cs`。`TestParties.Runs()` 手工组装一套 Store 与服务；
    `provider.DungeonRuns()` 组合生产容器里的服务；`AddDungeonRunsAs(...)` 用替身替换全部窄接口；只留给测试的四个重载改为扩展方法。
- 日志类别从 `DungeonRunDomainService` 变为 `DungeonRunStore.<服务名>`，诊断文档已改为按 `DungeonRunStore` 前缀配置。
