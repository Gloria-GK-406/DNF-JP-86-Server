---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 命令处理器只够得到领域形状

## Context

### Source Request

项目所有者分三轮给出同一条结构主张：

> 「让CMD只和领域服务交互。也就是Store和instance以及snapshot还有一些service。理论上
> cmdhandler只需要这几个类型的就能完成全部行为。如果混入了其他东西，那就是错了」
>
> 「我说的几个服务是那个DomainService。规则服务在DomainService中的。所以就是领域服务，
> store服务，instance和返回的snapshot」
>
> 「store只有第一种store，要改名成domainstore结尾了。其余的都是问题。应该是靠领域服务和
> 领域store和instance能拼出所有的业务结果才对。现在的内存容器和聚合啥的应该是还没做
> 适应性改造。你看看是不是。要么转为领域服务，要么转为领域store和instance的组合」

执行授权：「可以，先把任务契约和sdd搞起来把。multi-spec应该就行了。跑完了再和我说」

### Current Situation

前身任务 `domain-instances-and-snapshots` 已封存，它建立了 `ICharacter` 这个实例形状与
领域形状的快照。本任务是它的下一步：把词汇表收敛到底，并让约束不再依赖人的记性。

基线 `9c0de6c6` 的实测事实：

**注入面基本已经干净，但没有任何东西守着它。** 27 个 `ICommandHandler<,>`（与
`ProtocolModule.cs:96-198` 的 27 处注册一一对应）共 55 个构造器依赖，其中 36 次是严格的
`*DomainService`；其余 17 个 handler 100% 只用领域服务。负面清单已经为零——没有 `ILogger`、
没有发帧器、没有规则服务、没有 PVF 内容源、没有 Options、没有事务接缝。

**但有两处硬违规**：`GrantTutorialLevelCommandHandler`（`TutorialLevelUpSlice.cs:113`）注入
`ICharacterTable` 并在 `:135-137` 直接 `SetProgressAsync` 写行网关；
`PrepareSelectedCharacterSessionCommandHandler` 注入 `ISelectedSessionPositionPersistence`
这个持久化接缝。另有 14 次是命名与所在项目不到位而非结构错误。

**「store」一词现在指三种不同的东西**：真正实现 `IDomainStore` 的三个；叫 `Store` 却是内存
容器的 `OnlineStateStore`；以及六个 `Directory`/`Registry` 内存容器。`*Table`（约 55 个行
网关）与 `*Repository`（8 个）是 store 底下的两层而非别名，`SqliteCharacterStore.cs:14`
写着「Uses the tables the character repositories already create; it defines none of its own」。

**内存容器有正反两种形状并存**。正例 `DungeonRunDomainService` 把 `ConcurrentDictionary`
私有化，对外只有 `Begin`/`Find`/`TryMove`/`End` 这些领域动作。反例
`ISelectedSessionRuntimeDirectory` 把容器语义整个暴露成 `TryGet`/`ReplaceExact`，于是它持有
的那个大聚合 `SelectCharacterResult` 被 16 处读、9 处 `with` 后写回。该聚合自称是「一次性
应答信封……并不使其成为角色状态」，实际同时占了实例的行为、store 的存储位置与快照的类型
形状。其中 `Skills.ProtocolMirror`（剩余 SP/TP）自入场后从不重算，四个经验帧发送方都在回推
入场那一刻的陈值——这就是所有者实机报告的「升级之后 SP 不变」。

**三个接缝已经死了，而判据看不见**：`IActiveCharacterAccessDomainService`、
`IAccountCargoGoldService`、`IEquipmentEligibilityCharacterReader` 均无生产消费方，却
各自带着测试，所以全量判据全绿也给不出任何信号。同样地，
`IActiveCharacterDomainService` 四个成员里唯一的读方法零调用方，使得入场时发布的那份
`ActiveCharacterState` 副本只写不读。

**约束的执行机制已经存在，只用了一格**：`CommandHandlerArchitectureTests.cs` 已经在反射
遍历全部命令处理器检查构造器参数，但只守一条黑名单。`AGENTS.md:403` 自陈分层测试不存在；
`AGENTS.md:249` 那张 handler 能力白名单没有判据守护，已经漂移。

## Goal

命令处理器能够得到的东西只剩三种可注入的领域形状——领域服务、领域 store、实例——外加它们
交出的快照；违反这条会被判据当场挡住，而不是等下一个读代码的人发现。

现存那些既非三者、又暴露成可注入接缝的内存容器与聚合，各自归位：要么成为说领域话的领域
服务，要么拆成领域 store 与实例的组合。

## Scope

### In

- 命令处理器注入面的收敛：消除两处硬违规，并处置 14 次命名/所在项目不到位的注入
- 让这条约束成为判据的一部分，使新的违规无法悄悄进来
- `IDomainStore` 的三个实现按其含义改名，并消除「store」一词在领域层的一词多义
- `ISessionPresenceRegistry`、`IExpertStoreRegistry`、`IPvpLobbyRegistry` 转为领域服务，
  其内存容器私有化
- 拆解 `ISelectedSessionRuntimeDirectory` 所持的大聚合：角色自己的数（等级、经验、剩余
  SP/TP）改由角色实例回答
- 退役三个零消费方的接缝及其只写不读的残留副本，连同它们各自的测试
- 把这套词汇表与它对应的判据写进仓库约定；消解或如实记载 `AGENTS.md` 与代码的既有冲突

### Out

- 投影器成为出帧常态（`Docs/领域结构重构设计.md` §11、§12 第 6 步）
- 队伍聚合与地下城实例聚合（同文档 §12 第 2、4 步）
- HP/MP 等派生属性随等级成长——档案成长段未读、`character_stats` 无更新通路、无重算时机，
  三层都缺，是独立功能而非本任务的归位工作
- 升级后补发 `NOTI 0x0002 USERINFO`（角色面板重投影）
- `IEntryInventoryLeaseRegistry` 并入 `IItemStore` 的会话。它带 `Version` 与 `IsDirty`，是
  write-behind 缓存，与它缓存的行叠在同一批数据上形成两个 unit of work；这一条比其余重，
  所有者与执行方均同意推迟，此处显式记下以免被静默丢弃
- 让那 8 个直接调用写操作的 socket handler 改经命令总线。那是 `AGENTS.md:249` 已有规则的
  漂移，范围等同于给整个地下城子系统补一层命令，不在本任务内

## Constraints

- 改名必须按类型进行。全局字符串替换会误伤 `IExpertStoreRegistry`——那里的 `Store` 指玩家
  摆的摊，不是存储
- 判据命令必须带 `PVF_ARCHIVE_PATH`，否则读档案的用例会静默跳过而不是报红；基线那 2 个
  skip 正是档案已加载的标志
- 改名与容器改造不得改变任何可观察行为
- 现有测试的期望值与断言不得为迁就实现而改写；改名带来的符号重命名除外
- 不得触碰 `.worktrees/client-debug-lab`，那是无关的在做分支

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 每一个命令处理器的每一个构造器参数，都是领域服务、领域 store、实例（或交出实例的工厂），或在一张具名豁免清单里；豁免每项写明理由 | 对全部命令处理器构造器的一次完整枚举核对，含豁免清单及其理由 |
| SC-2 | 没有命令处理器直接注入规则服务、静态规则类、PVF 内容源或行网关 | 同一次枚举；两处硬违规（`ICharacterTable`、`ISelectedSessionPositionPersistence`）不再出现 |
| SC-3 | 违反 SC-1 或 SC-2 会让判据变红，而不是只在评审时被看见 | 一次故意引入的违规使判据失败，移除后恢复通过 |
| SC-4 | 领域层里「store」只指事务结算的真源，且名字这么说；`IExpertStoreRegistry` 那个「摆摊」语义在改名后仍能从名字读出 | 类型枚举：实现 `IDomainStore` 的三个类型的新名，与仓库中其余含 `Store` 的领域类型各自的含义 |
| SC-5 | 三个暴露成可注入接缝的内存容器改说领域话，其容器私有化；`SelectCharacterResult` 不再被当作可读写的角色状态持有 | 三个接口的成员枚举，加上大聚合读写点的前后对比 |
| SC-6 | 角色的等级、经验与剩余 SP/TP 由角色实例回答，不再从入场镜像读 | 生产代码里不存在从入场镜像读这三样的路径；且一次升级之后报给客户端的剩余 SP 是按新等级重算的值，而非入场时那个 |
| SC-7 | 三个零消费方接缝及其只写不读的角色状态残留副本不再存在 | 类型与调用点枚举；被一并移除的测试逐条对账 |
| SC-8 | 除 SC-6 那一处之外，服务器行为逐值不变 | 全量判据通过，用例数与基线 2015/2/0 的差额能与被退役死件的测试逐条对得上；`Server/tests` 的 diff 不含期望值或断言的改写（符号重命名除外） |
| SC-9 | 后来的读者能在仓库约定里找到这套词汇表，并知道违反时哪条判据会红；`AGENTS.md` 与代码的既有冲突被消解，或如实记为已知偏差并指明范围 | 被改动的记载逐条核对，含那五个 `Application` 内部服务跨层状态的裁决 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 承接方提出三批分工（store 改名+白名单判据+删死件 / 三个注册表转领域服务 / 拆大聚合与角色实例收编），所有者答「可以」，随即给出：「可以，先把任务契约和sdd搞起来把。multi-spec应该就行了。跑完了再和我说」 | 契约与 SDD 产物、一个任务工作区、配置的执行与验证、一次已评审结果的本地提交、一次未接受尝试的精确回滚。所有者要求跑完再汇报，执行期间无需逐批请示 |

## Requires Confirmation

- 合并到主干、推送、创建 PR、清理任务工作区或分支、任何远端变更

## Assumptions

- 退役三个死件会连同它们自带的测试一起移除，因而全量用例数会低于基线 2015。这是预期结果，
  按 SC-8 以逐条对账代替「不低于基线」的计数比较
- SC-6 里「剩余 SP 按新等级重算」不需要新增任何协议帧：经验通告帧本就带着 SP/TP 四个字段，
  改的是这些字段的取数来源
