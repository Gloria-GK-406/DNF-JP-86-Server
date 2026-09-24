# Routing Decision

- TaskContract: `TaskContract.md`
- Contract revision: `1`

## SDD Readiness

用户明确要求任务契约，且已对齐不可变邮件构造、统一发送、装备实例引用、堆叠扣减和同事务结算。服务级可观察目标与协议非目标已经分开；邮资、退回、人偶和客户端wire缺口不作为本轮隐含需求。契约ready表示边界可进入规格设计，不表示实现或实机已验收。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Rationale: 投递、附件寄存、领取、持久化与登录查询共享同一资产一致性边界。直接分散修改容易遗漏重复领取、装备被删除、金币截断或旧登录读取冲突；一份Spec、覆盖发送/领取失败路径的Plan和独立结果审阅能够直接约束这些风险。用户已明确接受任务契约投入，控制成本值得。协议UI独立验收属于后续范围，不为了文件多而拆为多个Spec。
- Reroute conditions: 若实施发现现有事务不能覆盖邮件与物品存储，或必须引入未授权的生产数据改写，返回契约/设计核对；若用户纳入独立协议客户端闭环或退回系统，再重新裁定范围与独立验收边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `planned-delivery`（single-spec-workflow共享Profile）
- Rationale: 需要显式设计与实施顺序保护跨域资产转移，Lite缺少所需Plan。当前是可隔离、可回滚的本地服务交付，可用事务回归和独立结果审阅覆盖风险；未纳入线上迁移或不可逆部署，不先增加Governed全套闸门。本轮交付契约与路由，Profile实例化及Spec执行在任务工作区准备后继续。
