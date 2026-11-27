---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/guild-system/Document/tasks/guild-system/attempts/spec-007-guild-management/TaskContract.md
contract_revision: 1
reviewer: /root/guild_management_contract_review
selection_basis: 按分期控制器要求，由未参与建模的独立审查者核对现有 A21、仓库规范及用户指令。
verdict: approved
---

# 公会管理子契约审查

核对用户请求与截图、[子契约](TaskContract.md)、[父契约](../../TaskContract.md) SC-02/03/05/06/07/09/10、[分期图](../../multi-spec-map.md) 和仓库开发规范。A21 公会详情与宣传、职级权限、名称、购买分别对应 NOTI 0x0046 和 CMD 0x02E3、0x02EB、0x02EC、0x02F8。现有公会详情编码器单独发送会长身份，六档权限及名称仍为空。截图说明会长操作被拒，但画面不足以判定触发拒绝的具体控件。AC-1 的实机步骤应逐一核对本期宣传与等级配置入口，按钮归属在实测中确认。

会长身份投影、宣传和职级配置属于子契约。普通成员拒绝、越权输入及故障阻止写入，旧库保留公会等既有数据，均有明确验证。购买与加成实际生效属父契约 SC-05，分期图仍列为未完成。该边界保留商品资格、GM 扣费、实际收益与到期责任，本期证据不能关闭购买验收。审批、任命、设施与社交仍按父契约各自验收。线上发布超出本轮实施。

| 维度 | 证据 | 依据与实际影响 |
| --- | --- | --- |
| quality | observed | AC-1 至 AC-3 对应会长与普通成员、重登、失败请求及数据库保留，给出可验收结果。 |
| efficiency | proxy | 现有 0x0046 与成员身份是可复用入口，实际减少的交付时间尚未测量。 |
| cost | proxy | 单独验收扣费和权益降低本期误扣风险，尚无实际成本度量。 |
| cognitive burden | observed | 子契约明确会员身份、等级配置和商品权益的归属，读者可以判断每项由谁收尾。 |
| actual human participation | observed | 用户提供会长实机截图并授权继续。新管理功能仍需要用户验收。 |
