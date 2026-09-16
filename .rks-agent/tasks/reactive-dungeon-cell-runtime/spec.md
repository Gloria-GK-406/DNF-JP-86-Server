---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Cell-owned dungeon runtime

## Why and Outcome

地下城中的房间事实必须只有一个权威来源。首次访问 Cell 时，系统从静态内容和规则完整构造
运行时 Cell；构造成功后，怪物死亡、掉落出现、拾取、房间清理和重访只读取或修改该 Cell。
Dungeon Run 继续管理参与者、迷宫布局和 Cell 集合，但不再用 `DeadActors`、
`MonsterDrops`、`TakenGroundDrops` 等平行集合重新拼装房间状态。

预期结果是：服务器能够在任何时点从已创建 Cell 取得一致快照；同一 Cell 的状态变更有稳定
标识和单调次序；首次创建失败不会让 Run 提前换房；重访不会重新查询 PVF 或复活已经消耗的
内容。

## Scope

本 Spec 覆盖普通与剧情地下城共享的运行时 Cell 基础设施，以及当前已经存在的首次进房、
组队进房、相邻换房、怪物确认死亡、初始地面物、怪物掉落、拾取和房间清理判断。它保持
A21 现有线格式和背包持久化事务。

本 Spec 不重新定义 Boss 结算、翻牌、回城等待、任务推进、经验计算或掉落概率；这些消费者
只能改为从新的 Cell 状态取得同等事实。建筑和玩家丢弃可使用来源中立的地面物入口，但不得
在缺少现有命令或协议证据时新增行为。由于当前死亡请求没有可信坐标，地面物模型不得伪造
位置；以后获得位置证据时可以在同一 Cell 边界内扩充。

## Definitions and Invariants

- **Dungeon Instance**：一次共享副本运行，按 `DungeonRunId` 标识，拥有冻结的参与者、布局、
  当前 Cell 引用和本次运行已经创建的 Cell 集合。
- **Cell identity**：`(DungeonRunId, MazeCell)`。同一运行中同一坐标至多存在一个已发布 Cell。
- **Cell instance**：一个房间的运行时实体，拥有 Actor、地面物、清理状态和 Cell revision。
- **Cell snapshot**：Cell 在一个 revision 上的不可变只读镜像；协议投影只能消费镜像或由同一
  次提交产生的 change，不得再次访问静态内容来补齐运行事实。
- **Ground item**：来源中立的地面物。稳定编号、物品标识、数量、耐久、种类、归属和来源
  元数据属于该实体；来源只用于协议投影或审计，不决定它存放在哪个集合。
- **Pending loot**：Actor 创建时固化的确定性掉落结果或无需外部访问即可完成的掉落计划。
  它属于 Actor，在死亡提交前不是地面物。
- 未发布的构造草稿不可通过 Run 查询，也不能成为 `CurrentCell`。
- Cell revision 每次成功的运行时提交恰好递增一次；失败、释放预占和幂等重放不得制造已提交
  的新 revision。

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Dungeon Instance 必须按坐标管理 Cell。初始进入和首次换入目标 Cell 都必须先完成 Cell 构造，再以一次受版本保护的提交发布 Cell 并切换当前房间。 |
| RQ-002 | 同一 Run/坐标的并发创建只能采用一个完整实例；失败或被竞争淘汰的草稿不能泄露、覆盖已发布状态或改变当前 Cell。 |
| RQ-003 | Cell 创建必须一次性读取当前房间所需的 Actor、初始声明地面物、物品表现分类及怪物掉落规则输入，并生成稳定 Actor sequence、来源中立的地面物以及每个敌对 Actor 的 pending loot。 |
| RQ-004 | 已发布 Cell 的 Actor 查找、死亡/清理判断、地面物查找和重访不得再次读取 PVF/content；重访必须复用该实例当前状态。 |
| RQ-005 | Actor 死亡必须在 Cell 内预占并提交。成功提交同时把 Actor 标为死亡、把其 pending loot 转入该 Cell 的 GroundItems、更新清理状态并产生一个有序 change；重复报告不得重复发奖或重复生成地面物。 |
| RQ-006 | 房间声明物和死亡掉落必须共享一个 GroundItems 集合与稳定编号空间。来源元数据可区分声明对象和死亡 Actor，但拾取流程不得按来源维护两套库存。 |
| RQ-007 | 拾取必须直接从当前 Cell 查找并预占地面物。背包/金币持久化成功后提交移除；持久化失败释放预占并保留原地面物；重复或过期请求不能取走不存在或不属于当前 Cell 的物品。 |
| RQ-008 | Dungeon Run 对外镜像必须暴露当前 Cell snapshot 及相应版本，同时移除运行态 `DeadActors`、`MonsterDrops`、`TakenGroundDrops` 作为独立真源。Run 级 revision 可继续为跨 Cell/参与者变化排序，但不得替代 Cell revision。 |
| RQ-009 | 首次进入、重访、死亡、拾取和清理投影必须从 Cell snapshot/change 构建，并保持现有客户端 opcode、字段布局、Actor/drop identity 和参与者接收范围。领域状态不得引用 Session、Connection 或发送器。 |
| RQ-010 | 既有经验、任务击杀推进、背包事务和结算消费者必须继续获得等价事实；本重构不能扩大或改变它们的业务规则。 |
| RQ-011 | 领域文档必须说明 Dungeon Instance、Cell 创建器、Cell instance、规则/content 服务、命令事务协调和协议投影之间的职责与数据流。 |

## Acceptance Criteria

### AC-001 — 原子首次创建（RQ-001, RQ-002, RQ-003）

Given 一个 Run 位于 Cell A 且 Cell B 从未创建，When Cell B 所需任一 content/rule 读取失败或
被取消，Then Run 仍位于 A，查询不到 B 的半成品，A 和 Run revision 均没有已提交变化。

Given 两个并发请求为同一 Run 创建 Cell B，When 两个准备过程完成，Then 只有一个 Cell B
被发布并被后续查询复用，Actor 和地面物标识只来自被采用实例。

### AC-002 — 重访复用（RQ-004, RQ-008）

Given Cell B 已经创建且其中一个 Actor 已死亡、一个地面物已成功拾取，When 队伍离开后重返
B，Then 返回的领域镜像保留死亡和缺失状态；Cell 创建相关 content/rule 调用次数不增加，
不得因为重访重新生成 Actor 或物品。

协议层可以依照 A21 的重访线语义省略客户端已经持有的条目，但该投影必须由同一个 B 快照
决定，而不是把“空投影”当作领域状态。

### AC-003 — 创建时固化掉落（RQ-003, RQ-004, RQ-005）

Given 一个敌对 Actor 随 Cell 创建完成，Then 它已经持有确定的 pending loot 或完整的本地
结算计划。When 随后报告死亡，Then 处理过程不调用房间 Actor/drop source、物品分类 source、
普通掉落 content 或任务掉落 content；只使用 Cell 和死亡命令中已有事实。

当前规则下普通掉落的角色上下文使用冻结的运行参与者规则；任务专属掉落继续保持现有
QuestLinked、任务状态、上限和 owner 语义，但这些外部事实在 Cell 创建时取得并固化。

### AC-004 — 死亡原子变化（RQ-005, RQ-006, RQ-009, RQ-010）

Given 一个存活 Actor，When 其持久化击杀效果成功并提交死亡，Then Actor 只死亡一次，其
pending loot 只转移一次，GroundItems 取得稳定编号，Cell revision 递增一次，死亡通知使用
同一提交产生的物品。

When 持久化击杀效果失败，Then 死亡预占可释放，Actor 仍存活，pending loot 仍挂在 Actor，
GroundItems 与 Cell revision 不变；已经按既有规则成功提交的参与者级效果继续遵守当前选择性
重试语义。

### AC-005 — 统一地面物与拾取补偿（RQ-006, RQ-007, RQ-009）

Given Cell 同时包含房间声明物与怪物掉落，When 按各自稳定编号查询，Then 两者都从同一个
GroundItems 快照返回并保留正确来源 identity。

When 拾取写入背包或金币成功，Then 对应地面物只移除一次并产生一个顺序晚于其创建的
change；When 写入失败，Then 释放预占后仍可再次查询和拾取该物品。其他参与者接收的移除
投影必须引用同一 GroundItem identity。

### AC-006 — 单一真源与版本（RQ-008, RQ-009）

静态检查与完整审查确认 Run aggregate/state 不再维护 `MonsterDrops`、`TakenGroundDrops`、
`DeadActors` 或等价的第二套房间事实。所有运行时 Actor、GroundItems 和清理判断均来自 Cell。
创建、死亡和拾取测试证明 Cell revision 单调，且旧 Run/Room/Cell 版本的 claim 不能提交。

### AC-007 — 兼容与回归（RQ-009, RQ-010）

现有进入、组队进入、换房、死亡、掉落编码、拾取、背包事务、任务击杀推进及清理判断测试在
适配新镜像后继续通过；协议 codec 的黄金字段断言不因领域重构改变。完整解决方案构建零警告、
零错误。

### AC-008 — 文档一致（RQ-011）

领域文档给出首次创建、重访、死亡转移和拾取事务的数据流，并明确规则/content 只能在 Cell
创建阶段提供构造输入，运行期协议 handler 不拥有或重建 Cell 状态。文档术语与实际公开模型、
服务职责一致。

## Success Evidence

- **EV-001（AC-001, AC-002）**：Cell 生命周期领域测试，包含构造失败、并发首次创建、重访与
  content/rule 调用计数。
- **EV-002（AC-003, AC-004）**：Actor pending loot、死亡预占/提交/释放、重复死亡和无运行期
  content 访问的领域及应用测试。
- **EV-003（AC-005）**：统一 GroundItems、背包/金币成功提交、失败补偿、重复拾取和组队观察者
  投影测试。
- **EV-004（AC-006）**：聚焦版本/过期 claim 测试及对生产代码旧平行真源的静态检查。
- **EV-005（AC-007）**：相关 Application/Protocol/Networking 测试与完整解决方案构建记录。
- **EV-006（AC-008）**：`Document` 下领域设计文档与完成变更的交叉审查。
