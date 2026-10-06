---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: ready
---

# Implementation Plan：骑士马战大竞猜

## Planning Choice

由本计划统一维护详细执行顺序与接口责任；串行交付后端、原生协议、前端，独立只读协议核对可并行。无需另建 Task Unit registry。

## Approach and Shared Decisions

基于 b4bf8d0d，保留经验实例重构。沿用 Events 注册和通用管理；新增 Joust 玩法/规则/持久化契约、typed PVF source 和活动专属 GM 接口。领域服务唯一拥有状态机及规则快照，协议和 HTTP 只做鉴权/命令及投影。UI 弹窗只编辑赛程并展示只读 PVF 内容。

材料扣除、投注记录、轮次结果和邮件使用现有领域事务及 SQLite ambient transaction；不能引入另一个数据库或手工扣库存副本。定时推进走事务命令，查询也可补推进；通知在提交后投影。活动关闭只阻止新投注，不取消已开始轮次。投注幂等以明确稳定请求键保证。

原生阶段补充（ClientStatic）：04BB在比赛阶段开始需要当前阶段结果以构建动画及晋级名单。因此 Quarter/Semi/Final/Result 投影累计4/6/7/7胜者，下注/封盘阶段不投影结果；派奖仍严格等到Result阶段，不能以已提供7名胜者代替结束时间。LinkKey必须保持`joust event/0/0`，客户端要求两个分隔参数；2365为参考/服务器活动ID选择。

## Work and Dependencies

| Work | Covers | Executable work and checks |
|---|---|---|
| T-001 原生证据 | RQ-006 | 只读核对 A21 04E6/04E7/04E8 和 04B8..04BB；结合 RVA、读取调用和参考 builder 闭合布局、事件 LinkKey 与下注序号身份；未知单列。协议实现前提交字段契约。 |
| T-002 后端闭环 | RQ-001..005,007 | 新增 Joust DTO/interfaces；PVF source 读取材料、奖励和 12 骑士，rule 提供名称/时效/校验；同实例 DI。独立保存 schedule revision，不用默认覆盖。建立轮次/名单/投注/结果/历史/派奖表或等效持久化。领域依次实现配置、UTC+8 轮次、阶段推进、64-bit 奖池、库存扣除、请求幂等、同事务邮件派奖和重启恢复。接线通用列表/GM 专属 GET+保存设置接口、后台 tick/角色快照。使用隔离 SQLite 和可控时间测试失败回滚、封盘、等级/限额、快照、重启、关闭后结算、重复派奖；编译 Host。 |
| T-003 原生接线 | RQ-001,003,004,006 | 消费 T-001 布局和 T-002 接口；新增 Protocol slice/codec/messages/CMD->domain，将稳定会话+帧序号传给下注域；角色进入及在线通知投影。更新 protocol 记录及目录。覆盖字节长度/字段/handler 身份和失败响应；不可将静态结论写成已抓包。 |
| T-004 GM 专属 UI | RQ-002,007 | 复用 events 页“设置活动”分派，新增马战弹窗，字段按赛程分组、PVF 内容显示名称(ID)；统一 Noti 提示。保存后读取服务器返回 revision，冲突/错误保留编辑内容；提供 loading/不可用/只读行为。前端交互测试、typecheck/build；需要浏览器验证时仅用 Chrome 和隔离测试服务。 |
| T-005 综合交付 | RQ-001..007 | 主控核对累计 diff 与基线归属，补使用说明/人工验收清单；序列化跑相关领域/真实SQLite/Host/Protocol/经验回归及 UI 检查。独立完整结果审查，修复并复查，最终新鲜验证后一个本地提交，不部署/推送。 |

生产文件仅一名写入者；主控可只读核对，下一阶段先消费实际接口与报告。每阶段产出阶段报告记录 Changed/Why/Checks/Remaining，不能用报告代替真实检查。

## Verification

按 spec 的 AC-001..007 汇总实际证据；.NET 共享输出串行，必要时申请本机隔离构建权限。使用临时数据库，不连真实玩家数据。完整结果由独立 agent 按 reviewing-work 审查，主控按 verification-before-completion 验证结果和剩余客户端边界。没有实机证据时仅声明待验收，不声明 ClientVerified。

2026-10-06审查修正：下注+封盘+三段比赛总时长必须严格小于每期间隔，保留正时长Result展示窗口；刚好占满轮长的赛程在领域/API/UI一致拒绝。查询补派奖标志独立于返回阶段，跨轮或日程外补结算也刷新邮件计数。
