---
kind: rks-task-contract
revision: 1
state: ready
---

# TaskContract: 任务日志成为自己的领域

## Context

### Source Request

> 对于任务，我们看看要怎么建模？和人物一样，应该有个任务桶仓库。这里是类似于某个人物的任务背包，不过还包括完成了哪些任务的记录。也就是这个实例记录了一个人物完成了什么任务，还在做什么任务。做的任务里要记录奖励，目标是什么。这个任务实例本身可以是个record或者可修改的DO。领任务就接近于，从任务仓库拿任务桶，从任务列表这类的服务拿对应任务的DO或者record。对任务桶进行领取动作。可能会失败也可能会成功。任务更新也是一样，找任务桶，找对应任务，更新任务实例等。不过这里和人物不同的是有任务追踪和完成后的奖励发放等业务流程。

随后确认：

> 对，是挪出去。要用sdd来实现

「挪出去」指前置对话中给出的两个选项里的第一个：把任务行从
`ICharacterStoreSession.Quests` 移到自己的存储，与背包完全同构，而不是留在人物存储上
只加一层领域外壳。

### Current Situation

任务在本仓**没有被建模成领域概念**，只有两类互不相干的数据：

- **静态内容** `QuestDefinition`（`Abstractions/Content`，PVF 解析）——不可变、与角色无关。
- **角色的几行**：`ICharacterStoreSession.Quests`，
  `IStoreSession<OwnedCharacterId, short, StoredActiveQuest>`。也就是任务被建成
  「角色自己的一批行」，与「角色站在哪儿」同级，不是独立聚合。

没有 `IQuestLog`，没有任务领域服务，`Application/` 下没有 `Quests/` 目录。
而 `ICharacter` 的注释已经写明这不是它想要的形状：

> "It is an entry point, not a container. What belongs here is what belongs to the character's
> own body; a bag, **a quest log** and a skill page are each their own thing, reached through
> their own repository rather than bolted on here."

背包是已经按这个形状建好的那一个，可作同构模板：`IItemContainer`（带动词、不持状态）
+ `ICharacterContainerDomainService.FindContainer` + `ContainerId.ForCharacter` 铸出的身份
+ 独立的 `IItemStore`。

**当前写入方与读取方**（均需在本任务中被照顾到）：

| 角色 | 位置 |
|---|---|
| 写 | `SelectCharacterPostEntryServices` 入场后修正 seeking 任务的 trigger |
| 写 | `Protocol/Game/QuestAcceptance/AcceptQuestCommandHandler` 领取任务 |
| 读 | `SqliteCharacterStore` → `ICharacterStoreSession.Quests`（用例内） |
| 读 | `CharacterEntryStateTables` → `CharacterActiveQuestStorageRow`（入场投影） |

同一张 `character_active_quests` 现有两个读法且互不知道对方；背包对同类双读有明确交代，
任务这边没有。

领取任务的线级行为**已经实现并逐字节验证过**（应答与 `NOTI 0x0015` 均与参照会话相同，
重复领取回 `00 12`，两行以槽 0/1 落库）。本任务是**行为不变前提下的建模重构**。

已知缺口：库里**没有已通关任务表**，因此「已通关且不可重复」与「前置未满足」两条受理判据
现在评估不了，本仓比上游宽松（记于 `Docs/ProtocolScaffoldDissonances.md` 二十二）。

### 与既有根任务的关系

现有根任务 `a21-world-entry-protocol` 的目标是「客户端从启动走到站进城镇」。任务日志建模
不在其范围内，故另立根任务。本任务的基线是该分支的当前实现。

## Goal

任务成为自己的领域：一个按角色取得的任务桶，同时持有**在做的**与**已通关的**任务，
受理／推进／放弃／完成／追踪这些动作由桶自己判定与落库；协议层只做解码、编排与回包。
搬迁完成后，任务行不再挂在人物存储上，同一份任务状态不再有互不知情的两个读法。
已验证的线级字节与落库结果不发生任何变化。

## Scope

### In

- 任务桶的领域接口与实现：在做的任务、已通关的任务、以及两者上的动作。
- 按角色取得任务桶的查找服务。
- 任务行从人物存储迁出，成为自有存储；与既有存储共享同一参与者身份。
- 已通关任务的持久化。
- 现有两个写入方改为经由任务桶。
- 入场投影与用例内读取对同一份任务状态的关系被理清（合并，或明确交代为何仍是两条路）。
- 受理规则（查重、占槽）从协议层命令 handler 移入领域。
- 任务追踪状态纳入任务桶的表述范围（已有 `character_quest_notify_selections` 承载）。
- 相关文档同步：领域说明与 `Docs/ProtocolScaffoldDissonances.md` 中受本次改动影响的条目。

### Out

- `FINISH_QUEST`、`GIVEUP_QUEST`、任务追踪各自的**协议实现**（本任务只让领域能表达它们）。
- 完成任务时的奖励发放**流程**本身。
- 补齐「已通关且不可重复」「前置未满足」两条受理判据 —— 它们要等任务完成协议落地才有判据。
- 任务实例中快照目标与奖励（前置对话已裁定：存引用 + 进度 + 选择，不存内容快照）。
- 服务架构调整（模块边界、依赖方向、命令总线与管线均不动）。

## Constraints

- 不改变已验证的线级字节：领取任务的应答、其后的 `NOTI 0x0015`、以及入场批次中与任务
  相关的帧，改动前后必须一致。
- 不改变服务架构。分层依赖方向、命令总线与管线、事务参与者规则均按 `AGENTS.md` 现状。
- 开发阶段的库变更采取重建而非迁移。

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 存在一个按角色取得的任务桶，在做的与已通关的任务都由它作答；它不缓存状态。 | 领域接口与实现；针对同一角色先写后读、以及用例回滚后再读的测试 |
| SC-2 | 任务行不再挂在人物存储会话上；任务状态由自有存储承载，且与既有存储同属一个事务参与者。 | 人物存储会话上不再有任务分区；一次用例内同时改任务与物品并整体提交／回滚的测试 |
| SC-3 | 已通关任务可被记录与读回。 | 写入后读回的测试；库中对应结构存在 |
| SC-4 | 受理规则（查重、占槽）由领域判定，协议层命令 handler 不再包含它们。 | 领域层针对查重、占槽、槽位耗尽的测试；handler 中不再出现这些判断 |
| SC-5 | 现有两个写入方均经由任务桶写入。 | 两处调用点改造后仍通过既有测试 |
| SC-6 | 同一份任务状态的读取路径关系已理清：或合并为一条，或在文档中交代为何仍是两条及如何保持一致。 | 代码或文档中的明确结论 |
| SC-7 | 线级字节与落库结果不变。 | 改动前后对同一次领取的线级重放结果一致，且与参照会话逐字节相同；活动任务落库槽位与 trigger 不变 |
| SC-8 | 全量测试通过。 | 测试套件运行结果 |
| SC-9 | 受本次改动影响的文档已更新。 | 更新后的文档 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 紧接「要不要我把它补成和背包同构的 `IQuestLog` + 查找服务，把规则从命令 handler 挪进去？」与随后的建模讨论，用户答：「对，是挪出去。要用sdd来实现，不过你的上下文已经很多了。先完成sdd的任务契约和spec，plan阶段。我再压缩执行任务？这样可以？」 | 在 `a21-world-entry-protocol` 工作树内完成上述搬迁与建模，含代码、库结构、测试与文档；本轮只产出契约、spec 与 plan，执行阶段在上下文压缩之后继续 |

## Assumptions

- 本任务的基线是 `a21-world-entry-protocol` 分支的当前实现，其中包含尚未提交的领取任务
  实现。开始执行前需要一个可证明的已接受基线；该实现如何入基线由用户决定。
- 一个数据库即一个事务参与者（身份为连接字符串），因此任务的自有存储与物品、人物同库时
  是同一参与者的又一个 facet，不会触发「一次事务只收一个可失败参与者」的限制。
- 客户端读取的是与服务端同一份 `.qst`，因此不为在做的任务快照内容是安全的。
