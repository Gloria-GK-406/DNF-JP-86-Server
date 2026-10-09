---
kind: rks-review
verdict: approved
---

# 任务型称号路线独立结果审查

审查日期：2026-10-09。工作区：`E:/Project/DNF/DNF-JP-86-Server/.worktrees/abyss-research`；HEAD：`9da9b83ee2ad3f42502d575254d8a176e6bbfeac`，审查对象为当前未提交结果。最终文件身份见 [reviewed-target-manifest.json](../verification/reviewed-target-manifest.json)。

依据：工作区及上级 AGENTS、[TaskContract](../TaskContract.md)、[Spec](../spec.md)，以及 `reviewing-work` 的 `references/code.md`、`references/deliverable.md`。由独立于实现者的上下文审查，生产代码只读，未另启动构建或测试。

结论：当前代码及配套说明满足本次审查边界，没有未解决的阻塞缺陷。本结论允许进入后续部署与完成验证；不代表部署已完成或 A21 客户端已通过现场验收。

## 边界及基线

任务开始时已有称号镜像投影与相关协议/测试修改，不能将 HEAD 的整个工作区 diff 当成本次任务增量。[baseline-manifest.json](../verification/baseline-manifest.json) 用于识别这部分既有工作。本次重点审查任务分类、日志受理/完成、被动依赖判定、遗留事件守卫、奖励承接、登录及当前快照过滤，以及对应回归。

`AchievementContracts` 既有镜像换装接口和 `TitleBookDomainService` 镜像操作并非本次路线重构新增；本次新增的是完成事实规则、ResetPending 契约以及称号解锁守卫。旧镜像相关测试保留。恢复证据 [recovered-test-baseline.cs.txt](../verification/recovered-test-baseline.cs.txt) 的 SHA-256 为 `d0d08c0e8dcca45471ecf4a4e840dd2a0f6e1225493632cd7c403f071a76194e`，与任务基线对应测试文件完全相同；本次调整从该基线重新应用。

## 当前代码证据

| 要求 | 已检查文件与结果 |
| --- | --- |
| RQ-001/002：真实受理与任务生命周期 | `PvfAchievementDefinitionSource` 仅收 grade=title；`QuestLog.AcceptAsync`、`QuestCompletionDomainService.FinishAsync` 仅拒绝 Title，不再拒绝 Achievement。001F 命令仍经 QuestLog，放弃清 carried；`QuestProgressDomainService` 仅遍历 carried，普通怪判定检查 Monster 类型及 Ordinary rank，未持有/已放弃不计数。达到 trigger=0 与真实 cleared 写入不同。 |
| RQ-003/006：旧自动计数不能充当完成事实 | `AchievementDomainService.CurrentProgressAsync` 过滤当前目录；`AchievementRuleService.CompletedFromFacts` 将目录内被动依赖与目录外真实 cleared 区分，旧4303自动行不会进入确认集合。Prepare 恢复未激活、无真实依据的 clearquest 完成行；`SqliteAchievementPersistence.ResetPendingAsync` 只恢复 p1，不删除原始行或改写其他列。 |
| RQ-004：事件与唯一奖励 | Finish 在任务效果应用后登记 QuestCompleted；事件登记及 SQLite 效果参与同一事务。AchievementDomainService 据真实 cleared 完成6501，TitleBookDomainService 订阅 AchievementCompleted，解锁唯一键和当前解锁集合拦截重放；称号奖励由称号簿承接。`QuestCompletionEffectRuleService` 对 title 奖励逐项核对 clearquest 与称号槽位映射，映射不明返回 UnmappedTitleReward，原 ItemRewards 发放路径保留。 |
| RQ-005/006：登录、查询与激活状态 | `SelectCharacterPreparationService` 先 prepare/reconcile，再用 Achievements.GetAsync 覆盖最终快照；`CharacterCurrentStateQueryDomainService` 仅调用只读 Get 覆盖当前快照。通知投影也用 Get。已激活标题进入确认集合并跳过重新解锁，读取称号簿不产生奖励写入。 |
| RQ-006：遗留排队事件 | `TitleBookDomainService.UnlockAsync` 新增当前被动定义与持久化完成校验；clearquest 再由真实 cleared/已激活依赖确认。即使旧6501行为0，旧4303自动完成或旧6501完成事件也不能凭此创建26596。 |
| RQ-007：入口与掉落路线 | 001F/0022/放弃/客户端 trigger 仍走既有任务领域；WorldContentEntryRuleService 本来即包含 Achievement 可接分类。`QuestEnemyDropDomainService` 从 carried 获取任务ID，规则读取该任务 EnemyRewardItems，QuestMonsterDropRules 保留目标、概率和持有数量限制，移出被动目录不会断开已接受任务的掉落入口。普通副本 ApplyMonsterKillCommand 和塔奖励路径均继续报任务权威事实。 |
| 说明一致性 | `spec-result.md`、001F、0022、NOTI-0167 的新增段落与当前代码一致，明确标注 CurrentImplementation，保留新客户端验收边界。 |

实际 A21 PVF 路径为 `E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`。用现有 PvfProbe 执行只读扫描，63条 reward type=title 任务均为 grade=achievement；63条均找到相同 ItemId 的称号簿槽位及 grade=title 的单任务 clearquest 条件，未出现多任务条件或前置项的奖励映射。其中19条声明 monster/enemy reward item。逐条结果与复核脚本见 [静态映射证据](../verification/review-static-title-mapping.json) 和 [扫描脚本](../verification/review-static-title-mapping.ps1)。这是内容映射检查，不证明全部63种任务目标都已有完整行为实现。

## 已处理发现

1. 初次审查发现登录与当前查询仍通过 CharacterEntryAssembly 的原始成就表投影发送旧4303行。当前两条出口已经改用成就领域 Get；新增协议测试分别观察登录 prepare 与当前查询只读行为，并检查最终 AchievementStateMessage 不包含4303。该发现已解决。
2. 最终检查发现格式处理期间文件截断异常。实现者已恢复测试基线及配套文件，独立复核恢复基线哈希、旧镜像测试差异、原 D-001..D-005 表行保留与新增 D-006；恢复后再次读取最新108项绿色结果。该发现已解决，最终目标清单已重新记录。

## 验证证据与剩余边界

- [quest-achievement-final.trx](../verification/quest-achievement-final.trx)：108/108通过；真实4303接受、普通怪推进、放弃后停止、正式完成、事件重放、重启、回滚、旧计数及旧事件污染、奖励映射拒绝均通过。新文件 TitleQuestUnlockRoutingTests 与旧 AchievementTitleBookTests 调整已逐项检查。
- [protocol-regression.trx](../verification/protocol-regression.trx)：264/264通过，包括 `Login_and_current_view_use_authoritative_achievement_projection_instead_of_legacy_task_rows` 的两个分支。
- [content-regression.trx](../verification/content-regression.trx)：86通过，1个缺失档案场景在本次有真实 PVF 的环境中跳过；未将此跳过计作通过。
- `git diff --check` 当前通过。此前红色 TRX保留为失败取证，未用于批准当前目标。

已存在的同事务多次写同一任务 trigger 会覆盖首次乐观比较基线，初版批量30次事实用例曾重现；本轮改为与实际单死亡命令相同的30次独立事务，并完成完整生命周期验证。[技术债 D-006](../../../plan/technical-debt.md) 保留问题。当前生产单死亡路径未因此阻塞；后续将多个同任务事实合并到同一事务时必须修复。

运行目录发布、数据库/配置备份、实际进程/监听/健康/hash，以及 A21 的001F受理、任务进度、0022完成和称号簿展示，仍由控制器执行完成验证并交用户实机验收。本代码审查不提前确认这些结果。
