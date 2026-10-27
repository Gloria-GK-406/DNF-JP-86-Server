# TASK-65 — 删除旧模型与最终迁移判据

承接 T-011 和用户流程 §8，仅在 TASK-64 提交且全部真实消息及来源闭合后开始。先核当前迁移账，删除最后无生产消费者的 fine、过渡机制及其保护替代，完成第一阶段全部五类闭合。

## 合同与计划

删除 legacy 路由／重载、细粒度处理器与注册类别、旧 45／58／60／100 管线、请求级 AsyncLocal、重复 Error 和 T1～T6、两项适配。不能仅删名字或留测试专用生产入口；保真实事务、精确参与者保护、足迹重放与单元失败收口。仅保 executor 当前 job 与领域事务的必要 ambient，命令不得借新包装重建 request current。

将 F-93 的 DiscardExact 和 recipient 当前性保护绑定现行 whole 路径，保真实 SQLite／替换代际、查询先于序号消费等强断言；补或迁移 Channel 业务失败降级、空条目失败关闭与 rootless 不误关。F-131 保既定关闭因果精度限制；F-132 延迟 Compose 仍禁用，结构判据保 async／构造器等反例。不提前处理第二阶段的结构美观问题。

先给主控删除图与实际保护替代方案；新能力或未写边界由主控裁定并记 backlog，不改 Spec／Plan。复用已有强测试，缺口才补旧行为测试并先旧 green、单独提交。冻结后 fresh Release 与八项目正式全量，实际 require_escalated、原并行／5m／detailed／DFO，精确失败及跳过、库存、strict 与八坏例；按语义及检索共同证明清零。迁移账随本 Task 提交，原始证据不进 git，无独立评审或外部写。

## Result

Outcome: done

迁移账行：仅闭合最后2 fine、3保护、8过渡共13行；ID／original／行序及非目标不变，五类pending均0。
行为变化：旧模型与请求ambient实际删除，保显式单元／事务／精确参与者／recipient先于ordinal／DiscardExact。重复传播Error按VC-B40归执行器，独立业务或清理错误保留；F-131限制与F-132禁用不变。
新增能力或注入类别：无新业务能力／类别。主控裁定固定未注册消息技术拒绝归job-entry、旧频道别名归现有正式领域singleton。fresh r4 Release零警告；八全量9810＝9790pass＋2精确原Dimension失败＋18原skip，DFO68负＋7controls且outside/exe/orphan0。库存／strict／8坏例及旧测试去向见 verification/task65-verification.md。STOP交主控；未开始第二阶段或封存B。
