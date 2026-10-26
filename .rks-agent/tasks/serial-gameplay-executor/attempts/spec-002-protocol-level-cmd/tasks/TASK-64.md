# TASK-64 — 生命周期、事件、启动与计划调度

承接 T-010 和用户流程 §8，在 TASK-63 提交后开始。按实际迁移账核对并迁完余下两个生命周期、两个事件、两个启动和一个计划调度来源；同时删除失去最后消费者的 fine。Direct 事件仍同步直接执行，不另排队。

## 合同与计划

将真实业务与阶段纳入 typed CMD，生产者只调度；不能把旧 raw callback、旧 bus 或隐式 current 藏在 typed 包装内。保连接 observer 顺序、精确代次、清理补偿、关闭因果链与超深降级；连接已断不能成为新失败来源。保 Reliable 的 claim／消费／完成事务、失败后的独立重试记录与退避，LiveOnly 先落完成再发提示且不重放，超深按原策略终结并关闭原链根。

两条启动写保内容准备和 readiness 次序；计划调度五个真实任务连同阶段一并迁入，保到期先于完成、实际开始时读取合并时间、RunOnce／Skip、重试、fresh scope、取消和排空。禁止为构造 typed 入口传入任意业务 Func 或 service locator；注册分发机制与业务调用的边界先给最小方案，由主控按真实路径裁定并记 backlog，不改 Spec／Plan。

复用原强测试，只给未覆盖业务加旧 green 并单独提交；新机制用共享正反例，不铺罕见失败矩阵。冻结后 fresh Release 和八项目正式全量，实际 require_escalated、原并行／5m／detailed／DFO、精确原失败与跳过、库存和严格账／八坏例。只闭合真实来源和旧命令，AGENTS 只名字，Result 三行，无独立评审或外部写。

## Result

Outcome: done

迁移账行：七来源＋五fine闭合；消息159/14retained、fine202/2pending、来源73/12retained，原ID/original/顺序及非目标不变，保护与过渡留T-011。
行为变化：保原阶段、精确代次／帧序／事务／重试；基础设施沿VC-B40。固定调度完成桥保原Aggregate及成功角色可重试，scope失败仍默认保护；LiveOnly提交后不授quiet取消。
新增能力或注入类别：具名分发／启动owner、编码事实、取消声明及固定完成桥均由主控裁定。fresh Release零警告错误，正式复合9828通过＋2原Dim＋18原skip，新增27删0；DFO67＋7、strict及八坏例通过。详task64-verification。
