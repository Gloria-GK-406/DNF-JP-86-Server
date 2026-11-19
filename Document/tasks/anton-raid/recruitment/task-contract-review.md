---
kind: rks-task-contract-review
contract_path: 'E:\Project\DNF\DNF-JP-86-Server-anton-raid\Document\tasks\anton-raid\recruitment\TaskContract.md'
contract_revision: 1
reviewer: /root/contract_review
selection_basis: 独立子代理，未建模契约、对齐意图或选择路径；项目无指定评审模型。
verdict: approved
---

# 安图恩招募任务契约评审

## 依据

- 用户原始请求授权按修订设计交付招募，并用 A21 实机确认。
- [攻坚服务设计](../design.md) §4.1、§5.1、§7、§8.2「入场与招募」及[领域设计](../../../AntonRaidDesign.md)。
- 工作区与仓库 AGENTS、[新功能开发流程](../../../FeatureDevelopment.md)及会话中隔离开发实机与正式用户测试的边界。

## 意图与范围

RI-1 至 RI-6 分别覆盖招募切片与任务契约、进频道、建团、申请、编组及真实 A21 验证。SC-1 至 SC-4 给出对应的角色条件、实际结果和证据。拒绝申请和同账号限制有设计依据，编组 F–J 的开放范围由实机证据确定。开团准备和后续攻坚、重入、跨频道招募及 PR 发布属于独立阶段。真实 A21 招募闭环仍是本契约的责任。当前玩家库和现行服务切换保留仓库要求的授权边界。

## 比例评估

| 项 | 证据等级 | 依据 | 实际影响 |
| --- | --- | --- | --- |
| quality | proxy | SC-1 至 SC-4 要求真实客户端双端结果与相关帧 | 可以区分服务端单方状态与客户端招募成功；实现效果待验证 |
| efficiency | proxy | 目标局限于招募，沿用已有频道、普通离队与协议证据 | 减少同开团和出战的耦合，节约程度待实现后观察 |
| cost | proxy | 取证需两个 A21 会话与隔离环境，题设未给用时数据 | 成本集中在用户要求的实际招募路径 |
| cognitive burden | observed | 契约按四项可观察结果组织，设计链接明确限定职责和场景 | 评审者可将四项结果分别对照请求，无需从完整攻坚方案重建范围 |
| actual human participation | observed | 用户直接决定本次先做招募、采用任务契约并要求 A21 取证 | 人工参与已明确当前范围；实际操作结果及正式验收仍待后续 |
