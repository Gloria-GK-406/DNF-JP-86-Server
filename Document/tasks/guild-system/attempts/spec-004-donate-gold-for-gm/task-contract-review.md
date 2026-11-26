---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system/attempts/spec-004-donate-gold-for-gm/TaskContract.md
contract_revision: 2
reviewer: /root/guild_donation_contract_review
selection_basis: 独立审查者未参与建模、意图对齐和路由选择，按本期金币事务及客户端交付风险审查
verdict: approved
---

# 金币捐赠子任务契约审查

## 来源与覆盖

- 用户选择「完整公会系统分期实现」，本轮指定「金币捐赠换GM点」。金币捐赠、持久化及界面同步、协议与隔离实测分别纳入本期，对应[根契约](../../TaskContract.md) SC-05、SC-09、SC-10。
- GM 消费、排行榜、月度结算及其他成长玩法继续由[根契约](../../TaskContract.md)和[分期图](../../multi-spec-map.md)承接。正式环境切换、PR 和发布超出已授予的本地实施与隔离验证权限。
- 工作区与服务端 `AGENTS.md` 要求记录证据等级。A21 [0x015A 协议页](../../../../../../protocol/game/client-to-server/0x015A.md)仅确认请求体为四字节；ServerS4A21 开发规范第 403 行支持成员捐赠及累加两类贡献，属于上游参照。当前开发 PVF `etc/(r)guild.etc` 的 `[guild donate]` 为 1,000。

## 复审结果

- 第 1 版缺少捐赠者累计贡献及当前面板即时刷新的明确成功条件。第 2 版 AC-1 已补齐，验收同时检查重登恢复及另一成员的个人累计保持原值。AC-2、AC-3 分别覆盖拒绝和回滚、旧库兼容。当前范围、验证与授权一致。

## 五维评估

| 维度 | 证据 | 依据 | 后果 |
| --- | --- | --- | --- |
| quality | observed | 第 2 版 AC-1 至 AC-3 与本期范围表及根 SC-05 对读 | 可分别判断两类积分、即时刷新、恢复、回滚和迁移是否通过 |
| efficiency | proxy | 分期图和隔离授权 | 可独立验证本次事务和界面，预期减少与其他成长功能的联调 |
| cost | proxy | 一次数据库升级和正反向事务及 A21 验证要求 | 工程成本可随单期控制，耗时尚未测量 |
| cognitive burden | proxy | 单一捐赠入口、PVF 比例和分期范围表 | 依靠一个动作核对余额、两类积分和一个面板 |
| actual human participation | observed | 用户亲自选择完整分期，并在上一期验收后指明金币捐赠 | 用户已决定本期功能，当前 A21 验收结果仍待实际参与 |
