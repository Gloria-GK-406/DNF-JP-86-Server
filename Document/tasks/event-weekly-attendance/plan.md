---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：活动管理与周签到

## Planning Choice

采用一份完整计划，按共享数据/协议依赖串行交付；代理负责有边界的后端与前端结果，不建立重复 Task 单元状态。独立客户端与架构调查可并行，生产写入和共享 .NET 构建串行。

## Approach and Shared Decisions

- 活动统一接口从自身状态和 PVF 规则返回领域日历信息；管理服务收集注册实例。周签到实现提供统一接口与玩法接口，DI 复用同一对象。
- 当前期次由服务端持久化；客户端固定 ID 2208 不用作数据库唯一季身份。UTC+8 周界与原规则一致；启停不新建期次。
- 奖励与窗口规则源为 `event/chn_event/chn_totalattendance.evt`，需要核对客户端所读路径；不得混入同名其他脚本或 fallback 物品。
- 结算通过现有领域事务调用周签到进度，不从客户端上传的14B签到记录计算奖励或资格。邮件通过现有官方邮件领域能力创建。
- 普通推荐副本资格按结算前等级±5与副本PVF等级区间重叠判定；本阶段不增加次元副本例外。周界从既有 `IDailyResetRuleService` 的UTC+8业务日回溯到周一，复用配置中的日切换时刻。
- 消费键按账号、活动、期次、RunId唯一；同账号多个角色的同Run只计一次。进度调用位于 `GrantClearExperienceCommand` 的既有事务中，传递RunId、结算前等级和统一结算时间；命令采用账号独占门。
- 活动季身份稳定保存；后台显式新期次才改变它，启停不改变。尚有进度的期次不能悄然修改影响奖品或进度语义的参数，展示字段编辑不重置数据。
- UI沿用每日补给的表格+独立编辑窗口+确认启停+未知结果后重读模式；API固定 `/api/gm/v1/events`，JSON类型以后端实际共享模型为准。
- API返回 `entries`，每项含eventId/type/revision/seasonId/enabled/status、展示文案/startsAt/endsAt、PVF来源/可用性/requiredClears/totalWeeks/两类奖励表和审计更新时间。POST共用eventId/expectedRevision/action，action为configure/enable/disable/new-season；configure/new-season需要完整展示文案和有效期，enable/disable不接受多余字段。只有显式new-season换季身份，有进度时拒绝修改有效期。

## Work and Dependencies

| Work | Deliverable and action | Coverage / dependency |
|---|---|---|
| T-001 | 保存客户端/PVF协议及规则核对证据，确认当前源码结算/邮件/入场接线；再固定后端数据契约 | RQ-003,004,007,009；先于生产 |
| T-002 | 串行后端代理实现 Data/Abstractions、领域注册与生命周期/周签到、SQLite配置进度、PVF源、结算与入场协议；必要行为测试保护重复/回滚/周期 | RQ-001..007,009；依赖T-001 |
| T-003 | 后端接入GM commands/endpoints/审计/在线投影，HTTP验证；回报API实际模型供前端消费 | RQ-008；与T-002同一串行写入者 |
| T-004 | 串行前端实现 `/gm/events` 列表/配置/启停，统一NotiBar和故障重读，更新说明及人工清单 | RQ-008,009；依赖后端定型 |
| T-005 | 独立审查完整积累差异，修复实际缺陷；串行回归及Release构建、Chrome隔离页面检查后记录验收边界 | AC-001..006 |

## Verification

重点为真实存储生命周期、每周/每期资产去重、结算重放、失败回滚与PVF实际奖励；协议静态证据/编码测试不替代GUI。已有游戏协议回归、GM HTTP与前端测试按改动范围运行。行为保护有独立可观测结果时使用TDD；架构/文案/过程元数据使用静态核对，不制造镜像测试。

工作区复用证据：canonical root `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`，Git dir与common dir不同，无superproject，分支 `task/warehouse-storage`，接受基线 `af0715492d9b9850756afdd908f8a93614795ea6`，开始时状态干净。原运行服保持不变；浏览器测试使用隔离数据库与端口。完成独立审查和新鲜验证后，工作区控制器创建一个本地结果提交。
