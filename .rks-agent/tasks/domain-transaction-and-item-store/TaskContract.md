---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 领域事务与物品存储抽象

## Context

### Source Request

用户在勘察存储层现状后判定：「对的，这是我们的一个真问题。也就是持久化层没有真正的抽象化，让业务逻辑依旧能感知到持久化层的样子。事务先在 moveitem 和使用里验证一下。看看写完以后代码漂不漂亮，那些奇怪的结构是不是已经处理掉了。」随后授权「ok，可以起 taskcontract 了」。

更早的一轮里用户提出了要达成的形状：「我想我们要有一个领域层的事务概念，也就是直接在领域层开启某种事务。然后让持久化层能实现。……这里 A 包的载体是内存还是 sql 都无所谓，只要确保要么同时完成要么不完成的约束就行。」并在追问失败语义后定下：「事务失败时候先静默吧，然后后续看到如果某个协议在失败时需要一个失败提示那时候再说。」

### Current Situation

存储层没有共享抽象。11 个 SQLite 仓储各自持有数据库路径、各自开连接（38 处 `new SqliteConnection`）、各自开事务（15 处独立 `BeginTransactionAsync`），代码库中不存在连接工厂、工作单元或基类。后果是**跨仓储的改动不可能原子**。

这条现状已经在两个地方结账：

- 角色实体各写各的行，曾导致建出来的角色进不去游戏（`Docs/领域结构重构设计.md` §9 记录，修法是把四行写进同一个事务）。该文档同时论证了持久化决定权应当上提，但其 §12 的执行顺序里没有为这件事留位置。
- 根任务 `inventory-container-refactor` 的约束 C-8（一次道具使用的效果作用于同一个存储）只能靠「当前只有一个持久存储被写」这条读法勉强成立，下一刀发经验与扩仓库时任何读法都会被违反。

领域与存储之间的边界也已经反向渗漏。`ContainerSlotItem`、`ContainerMovePlan`、`ConsumeFromContainerEffect` 都携带 `long StorageEntryId`，即 SQLite 的 `item_uid`；后果是纯内存、永不落库的 `TransientItemContainer` 必须自行发号伪造一个数据库主键（`ItemContainers.cs:150`）。同一方向上，`SqliteEntryInventoryMutationRepository.ApplyDailyRefillAsync` 在仓储内部承载了 UTC+8 日界、领取幂等、叠加还是新开一格的决策、按段查找空位，并从 SQLite 仓储回调 PVF 内容源；`GrantItemAsync` 同样承载「先叠后放」。这些规则因此既无法脱离 SQLite 测试，也无法被内存容器复用。

命令管道已经把位置留好但一直空着：`CommandExecutionKind.Transactional` 在枚举中存在，`CommandExecutionPolicyBehavior` 对它抛出 “requires Transactional execution, but no unit-of-work policy has been registered.”，而 `CommandBus` 已为每条命令建立独立的 DI scope。

.NET 自带的 `System.Transactions` 提供了同形状的原语，且 1 个持久资源加 N 个易失资源无需 MSDTC。但实测（net10.0 + `Microsoft.Data.Sqlite` 10.0.10）确认 `SqliteConnection` **不参与** ambient transaction，且既不入伙也不报错——写入活过了被中止的 `TransactionScope`。因此该库不能直接使用。

## Goal

业务代码不再感知持久化：一次用例内的多处改动要么全部生效要么全部不生效，这个保证由用例边界统一提供，业务代码中不出现任何事务操作，也不因数据落在内存还是数据库而分支。物品的持久化机制归拢到一个存储抽象之后，其中不再夹带业务规则，也不再向领域泄漏数据库行标识。

该形状在道具移动与道具使用两条协议上落地并验证。

## Scope

### In

- 领域事务的边界与参与者抽象：用例边界开启与结束事务，参与者以「准备 / 提交 / 回滚」参与。
- 物品存储抽象及其持久与内存两种实现，寻址词汇为「容器与槽」。
- `MoveItemSpaceCommand` 与 `UseStackableCommand` 迁至该接缝并在事务保护下执行。
- 把 `ApplyDailyRefillAsync` 与 `GrantItemAsync` 中的业务规则（日界、领取幂等、叠加还是新开格、按段查找空位）迁至 Application 层，存储侧只留下写行。
- 消除领域侧的数据库行标识。

### Out

- 背包坐标系统的重构：协议槽位映射表、按类型划分背包、快捷栏成为独立容器。
- schema 集中化，以及 5 处 `PRAGMA table_info` + `ALTER TABLE` 补列器的清理。
- 归属校验上提到命令管道，及其三份实现语义不一致的修正。
- 其余 9 个仓储与其余命令：保持现状与 `Direct` 执行方式不动。
- 入场快照改由容器供给。
- 跨持久存储的事务。
- 协议层两处直连仓储（`TaggedIdReportHandler`、`SelectCharacterPhaseCommands`）中与物品无关的那一处。

## Constraints

- 不得改变任何对外可观察行为。`0x0013` 与 `0x002C` 的线上帧字节须逐字不变，时序不变。
- 既有测试语义不得修改，只允许因类型或依赖变化做机械调整。
- 保证的边界必须是明示的，不得靠巧合成立：一次事务中提交阶段仍可能失败的参与者最多一个；出现第二个持久参与者时以失败暴露，而不是降级为非原子。
- 在事务之外改动持久物品状态必须以失败暴露，不得静默完成。此约束的授权来自实测发现的同类缺陷：`Microsoft.Data.Sqlite` 在 ambient transaction 下既不入伙也不报错，静默的非原子写入不可检测。
- 事务提交失败不向客户端回任何提示。
- 新增的存储实现不得自建任何表。同名表已在 6 个仓储中有 58 处定义、其中 4 张定义互相冲突，再添一处会加深该缺陷。
- 不得改动 `Tool/` 与 `.worktrees/legacy-reference/`。
- 不得手工编辑客户端可执行文件与 DLL。
- `protocol/` 目录内不得出现凭据、令牌、聊天隐私或个人可识别信息。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 两条道具协议的业务逻辑中不出现任何事务操作，也不因参与者是内存还是持久而分支 | 命令处理器源码不含提交、回滚或事务对象；同一段业务逻辑分别在内存容器与持久容器上执行并得到相同结果 |
| SC-2 | 一次用例内的多处改动要么全部可见要么全部不可见 | 令参与者在准备阶段与提交阶段分别失败，断言持久侧与内存侧均无任何改动可见 |
| SC-3 | 提交阶段仍可能失败的参与者最多一个，第二个持久参与者被拒绝 | 登记第二个持久参与者时失败，且该失败可被测试观察 |
| SC-4 | 在事务之外改动持久物品状态会失败，而不是静默成功 | 事务外调用持久容器的改动操作时失败，且该失败可被测试观察 |
| SC-5 | 物品持久化机制归拢到一个抽象之后，其中不含业务规则 | 日界、领取幂等、叠加决策、按段查找空位不再出现在存储实现中；每日刷新与教学奖励的既有行为覆盖仍然通过 |
| SC-6 | 领域词汇中不再出现数据库行标识 | 内存容器不再发号任何伪造标识；效果与容器的对外类型上不再携带存储行标识 |
| SC-7 | 对外可观察行为一字未变 | `0x0013` 与 `0x002C` 的编码字节与改动前逐字一致；全套自动化测试保持全绿（基线 848 通过 / 2 跳过 / 0 失败，加新增用例）；实机走查拖拽与喝药表现与改动前一致 |
| SC-8 | 既有的耦合结构确已移除 | 逐条核对为零命中：`TransientItemContainer.nextEntryId`；`PersistentItemContainer.ToStorageKind`；`MainInventoryMovePlan` / `MainInventoryMoveKind` / `MainInventorySlotPair` / `MainInventoryOccupant`；`ContainerMovePlan.DisplacedEntryId` 与 `ItemMoveStatus.SlotsChanged` 这条拒绝路径；`ConsumeFromContainerEffect` 上的存储行标识。每一处移除都有替代覆盖 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 「ok，可以起 taskcontract 了」，承接同一轮的「事务先在 moveitem 和使用里验证一下。看看写完以后代码漂不漂亮，那些奇怪的结构是不是已经处理掉了」、上一轮的「事务失败时候先静默吧」，以及更早的「我们都要解决掉，特别是存储层问题」 | 为服务端引入领域事务与物品存储抽象，并在道具移动与道具使用两条协议上落地验证；含相关业务规则的外迁与领域侧存储行标识的消除 |

## Requires Confirmation

- 合并、推送、创建 PR、分支与工作树清理。
- 若「容器与槽」被证明不足以寻址本范围内的物品操作（见 Assumptions 第一条），需回到本契约重议，不得在实施阶段自行引入替代的实例身份。

## Assumptions

- 「容器与槽」足以表达本范围内的全部物品操作——交换、合并、拆堆、消耗——附魔与随机属性随物品核心一同移动，不需要额外的实例身份。该前提已按现有四种操作核对，但未被穷尽证明。
- 每日刷新与教学奖励的规则外迁后行为等价，且这两条路径的现有覆盖足以证明等价。若覆盖不足，须先补齐覆盖再外迁。
- 本范围内只有一个持久参与者（SQLite 物品存储），因此不会触发跨持久存储的场景。
- `ItemMoveStatus.SlotsChanged` 在读与写进入同一事务、且用例被账号粒度闸门串行化之后不可达。该判断是 SC-8 中移除它的依据；若实施中证明它仍可达，则应保留该拒绝路径并在结果中记录。
