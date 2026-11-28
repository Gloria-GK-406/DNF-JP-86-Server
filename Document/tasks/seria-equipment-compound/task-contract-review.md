---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.worktrees/seria-equipment-compound/Document/tasks/seria-equipment-compound/TaskContract.md
contract_revision: 3
reviewer: /root/contract_r3
selection_basis: 独立审查者，未参与本契约的意图对齐、建模和路由选择，具备核对任务规划及仓库权威资料的能力
verdict: approved
---

# 赛利亚物品合成任务契约审查

## 权威来源

- 用户讨论赛利亚原生装备合成、能否合成传承及配方是否来自 PVF，随后明确要求「好，你来找收集实现功能相关的信息，然后写任务契约完成这个功能吧。随后我实际测试」。
- 已对齐的玩法涵盖 A21 原生单底材入口、随机与定向、PVF 配方与权重、普通稀有可产出传承、传承底材只出传承、现有账号材料及连接内原帧重放保护，用户负责客户端实机验收。
- 工作区 AGENTS.md、[服务端 AGENTS.md](../../../AGENTS.md)与[功能开发流程](../../FeatureDevelopment.md)明确独立工作区、证据、当前主干数据库、备份及测试交接要求。工作区 HEAD 与契约记录同为 `818cbeed7391a9fb6079270a06766fecac2a5ff6`。
- 契约的两个外部引用均解析成功。核对了 [A21 协议条目](../../../protocol/game/client-to-server/0x0197.md)、[证据规则](../../../protocol/evidence.md)与[任务证据](evidence.md)。另核对 ServerS4A21 的 `Handle_REGENERATION_RANDOM_OPTION`、`EquipmentRegenerationConfigProvider` 和 `InventoryEquipmentRegenerationService`，用于识别参照实现的最小载荷、配置读取、加权抽取和结果槽投影。

## 意图覆盖与范围交界

| 契约项 | 核对结果 |
|---|---|
| RI-1 | `in_scope`。SC-1 覆盖资料调查、协议记录及待证边界，任务契约已具备后续验收依据。 |
| RI-2 | `in_scope`。SC-2 承接原生单底材、有效配方、随机与定向、等级和类型限制及传承分支。有效内容约束与受控抽取检查共同约束权重。四类有效配方均纳入证据检查，原版传承权重换算保持待证，候选策略需可追溯。 |
| RI-3 | `in_scope`。SC-3 与 SC-4 覆盖底材和多容器材料、库存投影和持久化、失败和原帧重放，以及随后合法请求仍能结算。 |
| RI-4 | `in_scope`。SC-5 要求验证可连接候选并提供编号用例，客户端操作及实际反馈归用户。 |
| RI-5 | `not_applicable` 用于同号八字节结算和其他制作入口。八字节界面归属仍待证，SC-4 对该载荷及其他非六字节载荷要求安全拒绝与资产无变化，保留了协议交界的必要验收。 |
| RI-6 | `in_scope` 覆盖仓库规定的主干数据库核实、用户测试交接及实机通过这一集成前置条件。PR、合并、发布和清理作为后续操作列在 Scope Out。现用服务及玩家库切换须按 SC-5 与项目流程确认。 |

## 五维评估

| 维度 | 证据分类 | 证据 | 实际影响 |
|---|---|---|---|
| quality | proxy | SC-2 至 SC-4 要求使用真实 PVF、受控抽取、协议和事务检查以及原帧重放验证。 | 验收覆盖配置、产物与资产结果，实机表现待用户反馈。 |
| efficiency | proxy | 原生客户端入口和现有账号材料容器可承担既有交互，任务范围限定为单底材合成。 | 实现和调查可集中于 0197 六字节结算、候选规则和投影。 |
| cost | proxy | 上游提供配置和出站顺序线索，任务以有依据的候选权重策略承接原版资料缺口。 | 可以开展候选实现及验证，复原日服实际传承概率的工时仍未知。 |
| cognitive burden | observed | 契约集中列出五条可检查结果，SC-5 将实机操作和期望结果绑定到编号清单。 | 用户可在收到候选后按样本判断费用、产物和重登数据。 |
| actual human participation | observed | 用户提问确认传承与配方来源，随后授权实现并明确亲自实机测试。 | 用户已参与范围判断和测试分工，A21 测试结果仍待实际操作。 |
