---
kind: rks-task-contract-review
contract_path: Document/tasks/guild-system/attempts/spec-002-create-guild-in-a21/TaskContract.md
contract_revision: 1
reviewer: /root/creation_contract_review
selection_basis: 未参与子契约的建模、意图对齐或路径选择的同级独立评审者，适合本期单人建会和身份投影的范围
verdict: approved
---

# A21 公会创建子契约审查

核查了该工作区的子契约第 1 版，SHA-256 为 `5383350C8DE3E6CAED4ACD144452F552B4E867FD69CC94EE22E53EF5BBC49861`。权威来源为用户的实施授权及「完整公会系统分期实现」范围回复、[根任务契约](../../TaskContract.md)、[第一期结果](../spec-001-persist-guild-membership/spec-result.md)、仓库 `AGENTS.md` 与 `Document/FeatureDevelopment.md`。

| 根意图 | 归属核对 |
| --- | --- |
| 公会身份使用同一份成员关系，覆盖创建后、换角、重登与角色查看 | RI-1、AC-3 |
| A21 原生建会和一次扣费，包括失败、取消与陈旧许可 | RI-2、AC-1、AC-2 |
| 本期可构建并保留 A21 实机与协议来源 | RI-3、AC-4 |
| 申请、完整名册、管理、扩展玩法和正式用户验收 | RI-4，仍由根任务契约负责。本期的 A21 原生验收仍在本期，项目级正式验收另计 |

原生完成画面、金币与关系前后值、拒绝路径和身份投影提供了可观察的完成条件。已有参考行为可指导实现，当前 A21 客户端和匹配 PVF 的真实结果用于判定本期通过。隔离客户端、端口和数据库符合仓库测试边界。协议和实机取证仍属后续执行。

| 维度 | 证据类别 | 依据 | 实际影响 |
| --- | --- | --- | --- |
| quality | observed | AC-1 至 AC-4 覆盖成功、拒绝、身份延续和本期证据，并对应根成功条件 | 后续能据此区分界面可用、关系已提交和状态可恢复 |
| efficiency | proxy | 第一期已封存创建事务与身份查询，第二期把会话与客户端接线限定在单人建会 | 可复用既有写入和查询路径，具体实施效率仍待观察 |
| cost | proxy | 契约只要求已授权项目范围内的独立 A21 采样与隔离测试，无额外证据设施 | 主要成本来自真实客户端操作与联调，尚无时间测量 |
| cognitive burden | observed | 单个子契约列出范围、四项验收及根任务映射 | 阅读者能识别本期客户端范围与后续任务 |
| actual human participation | observed | 用户明确选择完整分期实施 | 用户决定了总范围，后续正式验收仍保留用户反馈 |
