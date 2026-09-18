# TASK-004 — Cell 当前成员输入与专属待落实结果

## Task Unit Contract

- Revision: 1.
- Outcome: 首次创建后续 Cell 使用绑定 PartyKey 查询到的当前入图成员/队长；既有 Cell 绝不因换队长重建。怪物/建筑的个人专属待落实结果在落地或发放时若归属人已离队就跳过，共享结果照旧一次完成。
- Owned boundary: DungeonVisitDomainService、DungeonRunDomainService 的 Cell 提交、ApplyMonsterKillCommand 的直接任务物授予，以及相关领域/协议测试。现有可事务化库存与地面物语义保持。
- Checks: 换届后首次开格传入继任者和当前人数；已生成 Cell 再访不重算；离队者个人预掉落不落地、共享掉落照常；直接入包任务物不发给已离队归属人。
- Depends on: TASK-003 当前成员镜像。

## Detailed Plan

1. 在 `MoveAsync` 准备新 Cell 前通过 `RunId` 取得最新 PartyKey 成员镜像；保留房间版本控制提交，但不以队伍整体版本不变作为重试前提。初始 Cell 由入口当前入场队列生成，提交时确认队列仍与 PartyService 一致。
2. 死亡提交将 PendingLoot 按共享/归属个人两类逐项处理；个人项只在所属角色仍为当前入图队员时进入地面 Cell。建筑掉落若有归属字段同一规则。
3. 直接任务物在实际发包前的授予业务中按 PartyKey 再查一次归属；不改变任务进度和经验的既有事务边界，不把任务物转赠其他队员。
4. 对首次创建、重复访问、个人跳过和共享保留加回归；跑相关领域/协议项目与全方案测试。

## Result

- State: code complete, 2026-09-17; runtime效果留待 T-005。新 Cell 前重新读取当前 RunId 视图，取得 PartyService 当前成员/队长；沿用房间版本守护提交，不对队伍整体版本做复核或重新摇掉落。
- 怪物死亡将已离队归属人的 Ground PendingLoot 跳过，共享掉落仍落入 Cell；通用 Cell 地面追加也过滤无当前资格的归属人。直接任务物在执行授予业务时按绑定 PartyKey 查询归属人；未在队则不入包。
- 已有 Cell 的首开/再访测试扩展到换届场景，重复进入不再造。保留入场队长的剧情任务目的归属字段，它是本次运行的规则上下文，不是当前成员真源。
- Evidence: 新 Cell 当前成员与再访、共享/专属地面掉落、直接任务物离队跳过的回归均通过；最近一次完整解决方案测试 0 失败。此后新增了目的归属字段，需要 T-005 再跑新鲜完整测试。
