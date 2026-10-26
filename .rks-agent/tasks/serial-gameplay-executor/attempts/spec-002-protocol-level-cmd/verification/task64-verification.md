# TASK-64 验证记录

闭合七个 remaining 来源和五个 scheduled fine。消息159 closed／14 retained，fine202 closed／2 pending，来源73 closed／12 retained；保护项仍2 closed／3 pending、过渡项仍8 pending。剩余 ReadReplayState 与 ReportDungeonQuestFacts 留 T-011。原 ID、original、顺序及非目标行逐项保持。

最终为明确复合来源：`task64-final-r2-application`，加 `task64-final-r1-{protocol,networking,persistence,infrastructure,host,httpapi,pvf}`。r1 八项目自然结束后，仅加强 Application 的同一条 scope 反例，再 fresh Release solution 重建并完整复跑 App；源文件哈希核对只该测试改变，产品与其余七项目测试未变。两次 fresh Release 均零警告、零错误。所有正式运行实际 require_escalated、原并行、5m、detailed、独立 DFO／TRX，严格串行；原始命令、起止、权限与 exit 在各 `verification/logs/` run.json。

| 项目 | 通过 | 原失败 | 原跳过 |
|---|---:|---:|---:|
| Application | 6025 | 2 | 16 |
| Protocol | 1795 | 0 | 0 |
| Networking | 203 | 0 | 0 |
| Persistence | 484 | 0 | 0 |
| Infrastructure | 974 | 0 | 2 |
| Host | 208 | 0 | 0 |
| HttpApi | 59 | 0 | 0 |
| Pvf | 80 | 0 | 0 |

9848＝9828通过＋2原失败＋18原跳过。两失败完整名仍为 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` 与 `Dfo.Server.Application.Tests.World.DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource`；message均为 `System.IO.InvalidDataException : Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'.`。十八 skip 的 display／message／重数未变，未使用 PVF 偶发许可。

相对 TASK-63 新增27、零删除：真实旧业务1＋取消10＋事件编码／scope7＋注入正反例6＋调度scope1＋实际注册名单1＋报告owner1＝27。逐 display／outcome／重数和 DFO 在 `task64-final-combined-inventory.json`、`task64-final-combined-dfo-*.log`；原67负例＋7对照保持，outside／exe／orphan全零。严格迁移账与八坏例均通过。

纯旧表征在隔离 `1aabbe37` 产品的 `task64-old-business-r2` 实际1/1通过：入口是 TestGameplay→真实调度业务体→旧 fine bus＋SQLite，不冒称 hosted 定时入口。首角色真实物品／事件部分写后失败回滚，次账号角色提交，下轮可重试；r1仅夹具使用了认证前代次。独立纯测试补丁为 `/private/tmp/task64-pure-tests.patch`。迁后同业务断言走真实 typed scheduled CMD，保原 exact Aggregate、成功角色无额外 fence；hosted／最新 Requested／RunOnce／Skip／fresh scope／重试／取消／排空由原 scheduler 强例覆盖。

新增共享反例覆盖实际必达编码 occurrence、原 NotificationBus 精确 recipient 隔离、同 cause 的独立再编码，以及 Reliable 成功 Commit／Forget 后释放 scope、失败先释放再回滚的真实阶段。补强的同一调度 scope 例确有首角色健康回滚、次角色提交和待报告 Aggregate，再实际 Dispose 失败：scope cause优先，已提交角色及实际 physical 默认保护，不能走完成桥。固定报告仅在 whole 完整健康关闭后于原 queue body 抛原 Aggregate；不恢复失败请求、不另起 job。具名 WriteResult action 取消仅无既有 commit 时沿原完整观察搬运，LiveOnly 已提交后取消仍默认失败且不重放。

生命周期保原 handler 注册顺序、Begin、初始帧、精确代次、补偿／释放与深度降级；固定清理及投递失败保因但继续撤销匹配在线身份，断开不伪造来源。启动保原准备／初始化／readiness顺序。新增能力均据主控 phase2-backlog 裁定；Spec／Plan未改。领域 fixture只证明领域／单元事实，生产来源、总线与线级由来源及集成测试证明。未作 A21 实机或专项性能通过声明；未独立评审、未写 Git 或外部系统，全部旧验证目录保留。
