---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server-pvp/Document/tasks/pvp-duel/business-domains/TaskContract.md
contract_revision: 2
reviewer: /root/pvp_domains_contract_review
selection_basis: 由未参与契约起草、意图对齐和工作流路由的独立子代理审核。
verdict: approved
---

# 决斗业务拆分与巅峰日常契约审核

## 依据与覆盖

- 用户要求自由与普通归属同一业务服务，传统决斗的业务与任务归属另一服务，底层控制通用。用户在收到具体设计后说「可以，这个任务就有点大了。起个任务契约吧，实现后和我说」。
- RI-1 覆盖契约与本地实现授权。RI-2 覆盖两业务服务、共用控制，以及继续使用各自结算权益。RI-3 覆盖传统服务内部任务、回大厅更新和一次发奖。RI-4 将实机操作归在用户侧，程序回归仍由本契约承担。
- [仓库约定](../../../../AGENTS.md) 要求区分自动化与 A21 实机证据。[决斗场当前结构](../../../PvpDuel.md) 支持复用现有事务和房间状态生命周期。[任务卡记录](../../../../protocol/game/server-to-client/NOTI-0x0143.md) 确认下发可展示，PVF 任务目标为三场，进度字段和奖励到账行为留待实机复验。
- 篇幅与范围相符。普通和自由无每日任务。额外的任务条件、运行中的测试环境切换及 PR 不属于本轮交付。跨代表共享进度是可验证的实现假设，验收要求限定为相同任务归属和游戏日幂等，符合用户已确认的边界。

## 五维评估

| 维度 | 证据类型 | 依据 | 后果 |
|---|---|---|---|
| quality | observed | SC-1 至 SC-4 给出路由、原结算与任务进度验收。 | 可分别验证现有链路以及从整局结算到任务投影的行为。 |
| efficiency | proxy | 共用对局控制及沿用现有自动化回归。 | 预计降低对局控制重复修改的成本，尚未有实施数据。 |
| cost | proxy | 任务边界限于首条巅峰日常，保留角色与战绩数据。 | 可将开发验证集中于事务、持久化和协议投影。 |
| cognitive burden | proxy | 每日重置、发奖和匹配入口都有明确归属。 | 预计可从对应业务服务追踪玩法，跨代表假设可单独复核。 |
| actual human participation | observed | 用户提出两业务服务，确认方案并授权本地实施。 | 这些决策覆盖总体设计，本契约用分开的证据记录后续 A21 确认。 |
