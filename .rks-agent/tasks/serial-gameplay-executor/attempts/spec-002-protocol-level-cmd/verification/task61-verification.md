# TASK-61 验证记录

正式证据采用组合来源：`verification/logs/task61-final-r3` 的 App、Protocol、Networking，及 `task61-final-r2` 的 Persistence、Infrastructure、Host、HttpApi、Pvf。r2 已完成八项目；随后仅增加只读 `HasOnlyFailure` 与 Select 的动作错误来源判断，故按实际影响新鲜重建并重跑三项目，不称八项目来自同一 r3。两次 Release Rebuild 均为零警告、零错误；所有正式运行实际使用 require_escalated，保默认运行时、原并行、5m、detailed、独立 DFO/TRX，串行自然结束。

| 项目 | 来源 | 通过 | 失败 | 跳过 |
|---|---|---:|---:|---:|
| App | r3 | 5962 | 2 | 16 |
| Protocol | r3 | 1795 | 0 | 0 |
| Networking | r3 | 203 | 0 | 0 |
| Persistence | r2 | 484 | 0 | 0 |
| Infrastructure | r2 | 974 | 0 | 2 |
| Host | r2 | 203 | 0 | 0 |
| HttpApi | r2 | 57 | 0 | 0 |
| Pvf | r2 | 80 | 0 | 0 |

共 9778＝9758 通过＋2 失败＋18 跳过。两失败仍为 `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`、`Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`，message 均保持 `Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`。跳过身份及原因与 task60-final-r4 一致。未出现登记的 IllusionTower PVF 例外。

相对 task60-final-r4，App 新增 16（清理能力 8、精确注入正例 4、PvP 绑定守卫 3、独立失败观察 1），Protocol 新增 5（两真实清理注册、三拒绝反例），零删除。原选角、补偿、回选、设置及线级断言保留；隔离 stage driver 调用真实生产阶段核心，事务与 footprint 假适配不冒生产 runner 证据。没有新增业务旧表征，因此没有 old-green purepatch。

原 DFO 的 67 个负例、7 个程序集正对照保持，outside／exe／orphan 均为零。执行者库存和 DFO 检查输出在本机 `verification/logs/task61-final-r3-executor-*`，供主控独立复核。r1 的四个新失败来自两旧夹具接管 Exit route 后遗漏移除其生产 CMD 注册；修正替换范围后相关 53 项通过，r1 不用于最终通过结论。

三消息为 65539／65540／65543；删除 14 个实际失去最后生产消费者的 fine，三日票及 ItemUsage 的 timer 消费者保留。选角原独立事务、回选准备提交、无外层事务的逐项尽力清理、精确 Release、通知与末尾关闭顺序保持。失败状态始终保留，清理能力只执行固定动作，不提供正常写入恢复、第二 CMD／Job 或 raw 权利；过期物品动作只有实际同 cause 且唯一失败发生时保原业务拒绝，独立清理错误按 VC-B40。F-124 由精确通知／handler／composer 配对及缺失、合法错 handler 反例守护；F-131 仅保既有登记关闭精度，未称修复。能力与注入裁定见 phase2-backlog；无专项性能或 A21 实机验收声明。

迁移账 strict 与八个坏例均通过。消息 pending 归零暴露 selftest 原来依赖 pending message 的假设；只把 premature-closed 与 closed-transition 两样本明确设为无真实 destination，其余六例和 validate 不变。此为离线工具修正，没有运行时或测试源码变化，不重跑 dotnet。账仅改 3 message＋14 fine 共 17 行，原 ID／original／顺序及非目标保持；消息 159 closed／0 pending／14 retained，fine 152 closed／52 pending，其余来源、保护及过渡账保持。
