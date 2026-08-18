---
kind: rks-task-contract
revision: 2
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-2, SC-3, SC-4, SC-5]
depends_on: []
---

# TaskContract: 容器抽象与角色绑定（行为零变化）

## Context

### Source Request

由根任务 `.rks-agent/tasks/inventory-container-refactor/TaskContract.md`（revision 2）
的成果一派生，覆盖其 SC-1 至 SC-5。根授权原话：

> 写multispec吧，把第二刀也记录一下。这样省的后面上下文多了，到时候忘了第二刀干啥。
> 这样我们就能用两个subspec来完成这个任务了。

### Current Situation

`MoveItemSpaceHandler` 与 `UseStackableHandler` 目前直接依赖
`IEntryInventoryMutationRepository`，也就是直接绑死在 SQLite 上。这两条路刚建立
（`db22416`、`0af6e0b`），只有两个调用点。

`0x000D` / `0x000E` 的投影已经由 `InventoryContainerState` 驱动
（`Dfo.Server.Protocol/Game/StateSynchronization/StateSynchronizationMessages.cs`），
线上格式不需要改动，只需改变这个状态的来源。

本子任务只搬迁接缝，不引入任何新玩法。

## Goal

道具的读写经由一层可替换的容器完成，且角色对「当前使用哪个容器」有显式绑定，
使后续的临时容器与多容器切换只需提供新的容器实现；完成后对外可观察行为一字不变。

## Scope

### In

- 容器抽象：自报身份、容器类型、客户端可见的形状（类型与容量参数）、是否允许移除；
  支持按槽读取、写入、删除，以及枚举已占用的槽
- 持久容器实现，其存储行为与现有 `SqliteEntryInventoryMutationRepository` 等价
- 内存容器实现
- 按既有容器的形状创建临时容器的能力
- 角色维度的容器注册与绑定：注册、移除、按类型列出可用容器、按类型取得当前容器、
  切换当前容器
- `MoveItemSpaceHandler` 与 `UseStackableHandler` 改为经容器操作
- `0x000D` / `0x000E` 投影所用的 `InventoryContainerState` 可由容器供给

### Out

- 任何对外可观察行为的变更
- 道具可用性判定、效果模型、冷却（属根任务成果二）
- 多容器切换的持久化结构变更与触发协议
- 线上帧格式与既有编码器的改动
- 每日补给、选角入场等其余仓储路径的迁移
- `Tool/` 与 `.worktrees/legacy-reference/` 的任何改动

## Constraints

- 不得改变任何对外可观察行为，包括线上帧的字节内容与时序
- 容器自行决定自身是持久的还是内存的，其使用方不区分二者
- 某类型下第一个被注册的容器成为该类型的当前容器
- 移除当前容器时回落到该类型下第一个不可移除的容器；不存在可回落容器时视为程序错误
- 既有测试的语义不得修改；仅允许因类型或依赖变化而做的机械调整

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| AC-1 | 道具移动与道具使用两条协议不再直接依赖持久化仓储 | 两个协议处理器的构造依赖中不含 `IEntryInventoryMutationRepository` |
| AC-2 | 当前容器可作为背包投影内容的来源 | 在同一份存储上，容器供给的容器状态与入场加载路径供给的容器状态，编码所得帧字节一致 |
| AC-3 | 同一类型下可注册多个容器，可列出、可切换、可移除，首个注册者为初始当前容器 | 覆盖注册、列出、切换、移除的自动化测试通过 |
| AC-4 | 移除当前容器后角色仍绑定到一个可用容器；无可回落容器时以异常暴露 | 覆盖这两种情形的自动化测试通过 |
| AC-5 | 对外行为一字未变 | 既有测试基线 780 通过 / 2 跳过保持全绿且既有测试语义未变；实机走通教学关，拖拽物品到快捷栏与饮用药水的表现与改动前一致 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 沿用根契约 `../../TaskContract.md` 的 `task` 行：「写multispec吧，把第二刀也记录一下……这样我们就能用两个subspec来完成这个任务了」 | 在根任务工作区内完成上述成果，含任务内工件、实现与验证、一次经验证的本地结果提交 |

## Assumptions

- 现有 `0x000D` / `0x000E` 编码器接受的 `InventoryContainerState` 足以表达容器的投影，
  无需改动线上格式；若实施中证伪，则本子任务的边界需要重新评估
- 每日补给与选角入场路径继续直接使用仓储，不在本次迁移范围内；容器与这些路径共享同一
  底层存储，二者对同一角色的并发写入不在本次处理
