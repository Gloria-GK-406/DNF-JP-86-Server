# TASK-62 — HTTP 与 GM 四十二路由

承接 T-010 与用户流程 §8；在 TASK-61 检查点后开始。只迁迁移账现存四十二个 HTTP／GM 来源，统一路由过滤器提交同一种内部 CMD，按实际路由保住业务阶段；不能只用 typed 外壳包旧命令总线。

## 合同与计划

保三十九个 group-enqueued 与三个 self-enqueued 的原边界。JSON 在入队前缓冲，保原失败回放、大小限制与过滤顺序；口令计算、限流及重验证在执行器外，入队不取消、body 使用原取消令牌。各路由原事务、拒绝码、HTTP 响应、鉴权、审计及 NOTI 顺序不变，目标角色不能变成失败来源连接。

先给主控统一路由 CMD 的最小实现方案：显式传递本次 CommandExecutionContext，不借 HttpContext.Items、scoped 当前请求、service locator 或隐藏 Func 业务能力；路由依赖与领域调用须真实吸收。跨组件接缝与新注入由主控裁定并记 backlog 后实施，不修 Spec／Plan。只补现有测试未覆盖的业务，旧行为新增测试先在旧产品运行并独立提交。

完成功能簇后 fresh Release 构建和八项目正式全量（require_escalated、原并行／5m／DFO），逐用例核对原两条 Dimension 失败及跳过；严格迁移账及八反例。只闭合真实迁完来源和失去最后消费者的旧命令。AGENTS 只更新名字；Result 三行，不独立评审，不外部写。

## Result（Outcome: done）

迁移账行：42个HTTP／GM来源与33个最后消费者fine，共75行闭合；来源46 closed／27 pending／12 retained，fine185 closed／19 pending，原ID／original／顺序及非目标不变。
行为变化：原事务、拒绝／HTTP响应、独立审计、幂等与提醒保持；失败保sticky并只在外侧映本次签发结果，罕见基础设施按VC-B40。隔离domain driver只证持久化／前置prepared事实，不充HTTP验收。
新增能力或注入类别：主控批准固定终止审计、RO unit回滚／取消及真实AbortAction只读事实；不扩raw／Self／正常写恢复。fresh Release零警告错误；最终八项复合来源9783通过、2原Dim、18跳过，新25删0，DFO67＋7、strict及八坏例闭合，详task62-verification。
