---
kind: rks-task-contract
revision: 1
state: ready
parent: ../../TaskContract.md
contributes_to:
  - SC-1
  - SC-2
  - SC-3
  - SC-4
  - SC-7
  - SC-8
  - SC-9
depends_on: []
---

# TaskContract: 守住注入面

## Context

### Source Request

来自根任务 `commands-reach-only-domain-shapes` 的第一个有界成果。根契约的目标是把命令处理器
够得到的东西收敛成三种可注入的领域形状，并让约束可机检。本成果负责其中「让名字说出类别、
让判据守住它」的那一半，不动内存容器的形状，也不动大聚合。

### Current Situation

基线 `9c0de6c6`。27 个 `ICommandHandler<,>`（与 `ProtocolModule.cs:96-198` 的 27 处注册
一一对应）共 55 个构造器依赖。其中 36 次是严格的 `*DomainService`，其余 17 个 handler
100% 只用领域服务——注入面**本来就基本干净**，缺的是让它保持干净的东西。

余下 19 次落在白名单外，分三类：

**两处是真违规。** `GrantTutorialLevelCommandHandler`（`TutorialLevelUpSlice.cs:113`）注入
`ICharacterTable` 并在 `:135-137` 直接 `SetProgressAsync` 写行网关——比领域 store 还低一层；
`PrepareSelectedCharacterSessionCommandHandler`（`SelectCharacterPhaseCommands.cs:32`）注入
`ISelectedSessionPositionPersistence` 这个持久化接缝。

**十四次是名字与所在项目没跟上，形状本身是对的。** `ICharacterExperienceGrant`（4 次）的实现
`CharacterExperienceGrant(ICharacterTable, IExperienceRuleService)` 正是「领域服务持有规则
服务、对外不暴露规则」的目标形状，只是名字没说出来。五个 `Application` 内部 `I*Service`
（9 次）同理，而 `AGENTS.md:391-395` 白纸黑字称它们「不跨层暴露」——协议层的命令处理器正在
跨层注入它们，文档与代码直接冲突。`IExperienceBonusService`（1 次）也是这一类。

**一次应当豁免。** `SelectCardCommandHandler` 注入 `TimeProvider`（`SelectCardCommand.cs:175`），
是 BCL 的环境能力而非领域概念。

「store」这个词现在指三种东西：真正实现 `IDomainStore` 的三个；叫 `Store` 却是内存容器的
`OnlineStateStore`；六个 `Directory`/`Registry` 内存容器。名字不说出类别，判据就没法按类型
判断，只能退化成一张要人工维护的长名单。

三个接缝已经死了而判据看不见：`IActiveCharacterAccessDomainService`（连同
`IActiveCharacterWriteScope`）、`IAccountCargoGoldService`、`IEquipmentEligibilityCharacterReader`
均无生产消费方，却各自带着测试，所以全量跑绿也给不出任何信号。相关地，
`IActiveCharacterDomainService` 四个成员里唯一的读方法 `GetRequired` 零调用方，使得入场时
发布的那份 `ActiveCharacterState` 副本只写不读。

执行机制已有半成品：`CommandHandlerArchitectureTests.cs` 已经在反射遍历全部命令处理器检查
构造器参数，但只守「不许注入 `ICommandBus`」一条黑名单。`AGENTS.md:403` 自陈分层测试不存在。

## Goal

每一个命令处理器的每一个构造器参数，都能**由类型本身判定**属于领域服务、领域 store、还是
交出实例的工厂；判不出来的只剩一张具名豁免清单。判据守着这条，新的违规进不来。

## Scope

### In

- 三个 `IDomainStore` 实现按其含义改名，使「领域 store」这一类可由名字辨认
- 消除两处硬违规
- 处置十四次名字/所在项目不到位的注入，使其可被归类；其中包含对五个 `Application` 内部服务
  跨层状态的裁决
- 退役三个零消费方接缝及其各自的测试，连同只写不读的 `ActiveCharacterState` 副本
- 把白名单落进判据，并把这套词汇表与「违反时哪条判据会红」写进仓库约定
- 消解或如实记载本成果触及的 `AGENTS.md` 与代码冲突

### Out

- `ISessionPresenceRegistry` / `IExpertStoreRegistry` / `IPvpLobbyRegistry` 转领域服务
- 拆解 `SelectedSessionRuntimeDirectory` 与大聚合；角色实例收编等级、经验、SP
- 让那 8 个直接调用写操作的 socket handler 改经命令总线
- 普通 socket handler（非命令处理器）的注入面——本成果的判据只覆盖命令处理器

## Constraints

- 改名必须按类型进行。全局字符串替换会误伤 `IExpertStoreRegistry`——那里的 `Store` 指玩家
  摆的摊，不是存储
- 不改变任何可观察行为
- 现有测试的期望值与断言不得为迁就实现而改写；改名带来的符号重命名除外
- 判据命令必须带 `PVF_ARCHIVE_PATH`

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| C-1 | 27 个命令处理器的每个构造器参数都属于领域服务、领域 store、实例工厂，或具名豁免清单中的一项；豁免每项写明理由 | 全部构造器参数的一次完整枚举，逐个标注归类与依据 |
| C-2 | 两处硬违规不再存在：没有命令处理器注入行网关或持久化接缝 | 枚举中 `ICharacterTable` 与 `ISelectedSessionPositionPersistence` 不再出现在任何命令处理器构造器上 |
| C-3 | 归类由类型本身可判定，不依赖一张要人工维护的类型名单（豁免清单除外） | 判据的判定依据说明；新增一个符合白名单的依赖不需要改判据即可通过 |
| C-4 | 违反白名单会让判据变红 | 一次故意引入的违规使判据失败，移除后恢复通过 |
| C-5 | 「领域 store」可由名字辨认；`IExpertStoreRegistry` 的摆摊语义在改名后仍能从名字读出 | 仓库中含 `Store` 的领域类型逐个列出，各自的含义与新名对照 |
| C-6 | 三个零消费方接缝及只写不读的 `ActiveCharacterState` 副本不再存在 | 类型与调用点枚举；被一并移除的测试逐条对账 |
| C-7 | 服务器行为逐值不变 | 全量判据通过；用例数与基线 2015 的差额能与被移除的死件测试逐条对得上；`Server/tests` 的 diff 不含期望值或断言的改写（符号重命名除外） |
| C-8 | 后来的读者能在仓库约定里找到这套词汇表与它的判据；本成果触及的文档冲突不再是冲突 | 被改动的记载逐条核对，含五个 `Application` 内部服务跨层状态的裁决与 `AGENTS.md:403` 的更新 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 沿用根契约 `../../TaskContract.md` 的 `task` 行：所有者认可三批分工后给出「可以，先把任务契约和sdd搞起来把。multi-spec应该就行了。跑完了再和我说」 | 本成果范围内的实现、验证与一次已评审结果的本地提交；不含合并、推送、清理 |

## Assumptions

- 退役三个死件会连同它们自带的测试一起移除，全量用例数因而低于基线 2015。按 C-7 以逐条
  对账代替计数比较
- 基线本身带一个偶发失败：`OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`
  会抛 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，单独复跑该项目即恢复全绿。见到它不要
  当成本成果引入的回归
