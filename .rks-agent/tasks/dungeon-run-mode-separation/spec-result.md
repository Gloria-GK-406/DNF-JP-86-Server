---
kind: rks-spec-result
spec:
  path: ./spec.md
  revision: 2
outcome: completed
terminal_stage: verification
round: 6
baseline: 2b323fb628b90faa3549fab24e0dd3c47901dd73
---

# 实施结果：普通与任务关联地下城运行模式分离

## Changed

- 内容层保留 dungeon/maze `[quest connection]`，并保留 hunt-enemy 五元组；A21 PVF 的 dungeon 144/quest 1776 已由实际内容测试读出。
- 新增绑定精确角色连接代次的一次性 `DungeonEntryIntent`：`0x000F=0` 记录 Interactive，非零记录 Directed；`0x0010` 原子消费并拒绝跨代、缺失、重复或目标错配。
- `DungeonLayoutRuleService` 先按意图分区候选，再按活动/完成任务和难度过滤连接；随机种子只在同一候选集合内选。选中结果携带 `DungeonRunPurpose`，随布局进入 `DungeonRunState` 后冻结。
- 首次可信 actor 死亡把任务推进放在同一 transactional command；type 1 怪物同时结算经验/掉落，type 2 AI 只推进任务。事务失败释放精确 death claim，成功后重复报告不再推进。
- 混合 hunt-enemy 任务按本次 selector 实际选中的通道判权：纯 type 1/2 通道由服务器推进，纯 type 3 通道在绑定运行中接受客户端报告；跨权威、缺失通道和无效目标全部拒绝。
- A21 实机证明 quest 1776 的 `144/-1/13099/type3/count1` 是被动对象目标：其 `selector=0x10 decrement` 只有在冻结为同任务、同 dungeon/difficulty 的 Active QuestLinked 运行中才可由 trigger 1 到 0；普通运行和错配上下文拒绝。
- 完成/结算规则同时检查冻结目的和关联任务状态：普通运行保持原判据，活动任务连接要求 trigger 到 0，完成态连接要求任务在 cleared log；随后按冻结目的投影 `PLAY_RESULT.questMaze`。没有 A21 证据的卡牌、奖励和展示字段保持共享。
- GiveUp 结束运行后复用 EPLP/取消选图的 `TownReturnProjection`，同一响应批次发送确认及完整城镇状态。
- 任务奖励改为固定、job/growType 条件、可选与金币的结构化定义；完成命令读取当前持久化身份，先生成唯一计划，再把任务、物品、金币和经验放入同一事务。后置失败越过 CommandBus 触发回滚并由 Handler 映射 code 22。
- 真实 PVF 1776 的 job=0/growType=0 计划和实际发放均只有 `101010437 x1`；金币 marker 和仅有 `[gold multiple]` 的任务均按公式处理，实际增加量以 ItemId=0 投影。
- 更新地下城入口、`0x000F`/`0x0022`/`0x0027`/`0x002A` 协议记录，明确已证实内容与仍未 ClientVerified 的边界。

## Why

- 普通与剧情使用同一 `0x0010`，差异来自前置选择上下文；把活动任务直接当剧情意图会使普通入口误选任务 maze。
- 运行目的放在入口结果并冻结，使移动、击杀和结算复用同一运行机制，同时避免中途任务状态变化改写本局性质。
- 任务计数按内容声明分权：Monster/AI 由服务器可信 actor 事件产生；Passive Object 是客户端地图脚本事件，但必须由当前 QuestLinked 运行授权，不能脱离副本上下文推进。
- 奖励定义不能压成整数对：`[job]` 改变后续字段含义，`[reward selection int data]` 是另一组真正的多选一候选，itemId=0 和 `[gold multiple]` 表达金币而不是物品。
- 完成链的普通返回值会被 UnitOfWork 提交，因此任何写入后的失败必须以专用异常穿过命令边界；Handler 只在事务外把它映射为客户端拒绝。

## Checks

- 入口/目标/结算/GiveUp 的既有 RED/GREEN 均保留；A21 实机已确认普通 `Interactive -> Ordinary maze0`、剧情 `Directed -> QuestLinked(1776) maze1`、trigger `1 -> 0`、boss 出口、`questMaze=1`、任务完成与正常回城。
- 奖励 RED：真实 PVF 1776 在旧扁平模型下生成 13 项错误 grant；结构化投影后 job=0/growType=0 的计划与实际背包均为 `101010437 x1`。
- 奖励矩阵覆盖固定/职业/选择、过滤后索引、缺失/越界/禁止选择、completionCount、金币 marker、GoldMultiplier-only 任务 130 和 ItemId=0 响应投影。
- 原子性晚失败测试使用真实 CommandBus、同一 SQLite 的 quest/item/gold/experience 入口：事务内先观察到物品、金币与经验均已写，再注入异常；边界外断言 active quest、clear flag、背包、金币、level/exp 全部恢复。
- AI type 2 测试覆盖首次事务失败释放、重试成功、成功后的重复不推进，以及无经验/无掉落；混合目标测试覆盖单通道分权、跨权威与无效目标拒绝。
- 新鲜 Release 构建为 0 warning / 0 error；新鲜完整解决方案测试的最终计数记录于 `verification/completion-evidence.md`。
- `git diff --check`：通过。

## Historical Data Boundary

本任务不删除或补偿实机测试时已经错误发放的装备；修复只改变后续任务完成的计划和事务行为。
