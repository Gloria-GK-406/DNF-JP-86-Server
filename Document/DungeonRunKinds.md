# 地下城玩法种类

新增或修改特殊地下城玩法（塔、活动副本、祭坛类）前阅读。入场资格与收费见
[DungeonAdmission.md](DungeonAdmission.md)，迷宫结构见 [DungeonStructureRules.md](DungeonStructureRules.md)。

## 两个轴

| 轴 | 类型 | 回答的问题 |
|---|---|---|
| 运行引擎 | `DungeonParticipationMode { Ordinary, IllusionTower, BossTower }` | 哪个运行时持有这局：普通 `DungeonRunDomainService`，还是两座塔各自的 run。Protocol 用 `DungeonLocator` 按副本占用登记分派 |
| 玩法种类 | `DungeonRunKind` + `DungeonRunMode`（`Abstractions/World/DungeonRunMode.cs`） | 普通引擎内这一局是哪种玩法，以及入场时冻结的计划 |

两塔不进入 `DungeonRunKind`：它们的事务、成员表与投影自成体系，并入后每个按种类分支的地方都要写“不适用”。

## 两座塔的共享投影

两塔各有自己的领域服务（`IBossTowerDomainService`、`IIllusionTowerDomainService`），但 Protocol 里的击杀奖励与结算投影
（`TowerActionProjection`）、离队与换主机投影（`TowerPartyDeparture`）、离队队列（`TowerDepartureProjectionQueue<T>`）
和队伍协调（`CoordinateTowerPartyAsync`）各只有一份，按 Abstractions 的共享只读视图 `ITowerRunSnapshot`、`ITowerActionOutcome` 编写。

两塔真正不同的地方由 `ITowerRunKind`（`Protocol/Game/Party/TowerRunKind.cs`）回答，每座塔一个实现：

| 分歧点 | Boss 塔 | 幻影塔 |
|---|---|---|
| 结算尾帧 | 奖励卡 8 槽、`CanRetryAsync` | 先发排行帧，奖励卡 4 槽、`CanContinueAsync` |
| 离队后房间已全员加载 | 自动开局并广播加载完成 | 只重发加载完成 |
| 队伍工作单元收件人 | 列出成员 | 不列，只靠足迹归属 |
| 回城 | 不恢复背包 | 死亡之塔先恢复自己的背包 |

新增两塔共有的投影时写在共享代码里；只有确属一座塔的行为才加进 `ITowerRunKind`。
入场、地图推进、定时回城仍按塔分开实现。塔快照里的 `TowerMember.IsLeader` 是塔内主机，
队长不在塔内时它不跟随队伍队长，不能当作队长的副本删去。

## 判别值

- 每局恰好一个 `DungeonLayout.Mode`，由负责该玩法的入场服务写入。无计划的种类（普通、次元、南部溪谷、陶之王国、封印宝藏、赫拉斯、金银角、情侣）共用 `DungeonRunMode.Plain(kind)`；
  带计划的种类各有嵌套记录（Hell、Mirror、Sao、Despair、Tournament、Altar、Guardian、GiantDragon、LostLand、DarkElfRuins）。构造函数私有，集合封闭。
- 入场时冻结的计划放在 `Mode`；只属于一种玩法、随局变化的运行态放在 run 唯一的 `ModeState` 槽（`DungeonRunModeState` 的嵌套记录，目前只有遗忘之地的发烧与探索时限）。
  新玩法需要运行态时加一个嵌套记录，不在 run、`DungeonRunState` 上为它另开可空字段。
- 识别玩法一律看 `Mode`，不再按副本号或 `Supports(id)` 判断已经开局的 run。直接构造 `DungeonLayout` 的测试也要写入对应 `Mode`，否则会按普通副本处理。
- 按 `DungeonRunKind` 写 switch 时列出每个成员，不写 `_ =>` 或 `default:`。构建只抑制 CS8524，保留 CS8509，
  所以新增种类会在每个未作答的地方编译失败；`DungeonRunKindTests` 扫描生产源码拒绝兜底分支，并带反例。

## 多种类共享的判定：能力表

`DungeonRunCapabilities.Of(kind)` 是这些判定的唯一出处，调用方只问 `run.Layout.Mode.Capabilities`：

| 能力 | 含义 | 非默认的种类 |
|---|---|---|
| `EndsOnBossClear` | 首领房清场是否开普通 clear、发首领通关奖励 | 比武大会、祭坛（各自按轮次结算） |
| `AppliesFailureWeakness` | 失败是否施加虚弱（再叠加内容的 `NoGiveupPenalty` 豁免） | 祭坛 |
| `DrawsClearCards` | 通关是否展示并发放翻牌 | 绝望之塔、遗忘之地、暗精灵遗迹 |
| `UnlocksDifficulty` | 通关是否提升记录难度 | 镜像、绝望之塔、遗忘之地、暗精灵遗迹 |
| `AllowsOrdinaryRechallenge` | 通关离开后能否恢复普通迷宫的重挑意图 | 地狱、镜像、SAO、绝望之塔、比武大会、祭坛、守护者祭坛、遗忘之地、暗精灵遗迹 |
| `Revival` | 复活币是否可用、需要谁同意 | 守护者祭坛禁止；比武大会、祭坛、暗精灵遗迹询问各自生命周期 |
| `PartyWipe` | 团灭由谁结算 | 祭坛失败、比武大会淘汰、守护者祭坛自管 50 秒返回 |
| `QuotaFamily` | 房间加载完成时刷新哪个次数族 | 镜像、祭坛、比武大会、赫拉斯 |

只属于一种玩法的行为（祭坛的波次与机关、守护者祭坛的怪物死亡旁路、地狱的冻结怪物、入场与移图帧）
仍按 `Mode` 的具体记录匹配，不进入能力表。

## 行为钩子与入场路由

- **首领房清场**：`IDungeonBossClearHook` 按种类注册（SAO、南部溪谷、暗精灵遗迹），在击杀的同一事务内执行，与击杀一起提交或回滚。
  同一种类最多一个钩子；`DungeonMapCleared` 订阅者在之后的作业里执行，给不了这个保证。
- **入场路由**：`IDungeonEntryRoutingDomainService` 持有特殊入场的先后顺序。专属玩法认得的副本不会落到镜像、循环或地狱／普通判断；
  两塔在此之前由 Protocol 按引擎分流。新增玩法时在这里登记顺序，Protocol 的 `GameSelectDungeonCommandHandler` 不感知具体玩法。

## 一局之内的到点推进

一局之内到点要发生的事（祭坛的波次与阶段、守护者祭坛的推进与回城、房间机关倒计时）由唯一的
`DungeonDeadlineScheduler`（`Application/World`）调度，每种是一个 `DungeonDeadlineSource`，在 `DungeonDeadlineKind` 里有一个成员。
它不是系统计划任务（`ScheduledTaskHostedService`）：截止时间随局出现和消失，不重试、不补跑，过期的靠代号与房间版本丢弃，
玩家操作随时会改变它，这些诉求与系统计划任务相反，不要合并。

- **拉取，不存副本。** 截止时间只在玩法自己的领域状态里。每轮调度器向来源要截止时间：来源提交扫描命令、逐项提交推进命令，
  返回最近的下一个截止时间。来源只组织类型化内部命令，不在执行器外读写状态。
- **由状态所有者唤醒。** 截止时间所在的状态提交时，所有者调用 `IDungeonDeadlineWake.Wake(kind)`：祭坛与守护者祭坛在各自运行态存储的提交里，
  房间机关和“击杀改变了存活怪物”在 `DungeonRunDomainService` 里。在事务内唤醒是安全的，重扫是执行器上之后的作业。
  CMD handler 不注入唤醒，`CommandHandlerArchitectureTests` 拒绝这个依赖。
- 唤醒按种类合并，只重扫被唤醒或已到期的来源。没有截止时间时等待唤醒；来源可声明空闲轮询（守护者祭坛 30 秒，借扫描清理离线的选择状态）。
  等待下限 50ms；扫描失败的来源 1 秒后重扫。

新增一种局内限时：在 `DungeonDeadlineKind` 加成员，实现来源并用 `AddDungeonDeadlineSource<T>()` 登记，在状态所有者的提交处唤醒。

## 是否在副本中

“这个角色是否在任何副本里”只问 `IDungeonParticipationDomainService.Find`。普通副本与两座塔开局时都会在这里登记占用。
需要知道具体在哪座塔、由谁收件时，用 `DungeonLocator.Locate`：它要求登记与塔的座位一致。
不要再用 `runs.Find(...) || illusion.IsInTower(...)` 这类拼接，它会漏掉另一座塔。

## 新增一种玩法的检查表

1. 在 `DungeonRunKind` 增加成员；如有入场计划，在 `DungeonRunMode` 增加嵌套记录。
2. 修完所有编译失败的 switch，并在能力表里为它作答。
3. 入场服务在 layout 上写入 `Mode`，并在入场路由中登记顺序。
4. 如果清场需要同事务结算，实现并注册 `IDungeonBossClearHook`。
