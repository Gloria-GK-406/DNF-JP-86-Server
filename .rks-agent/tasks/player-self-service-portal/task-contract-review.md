---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server\.rks-agent\tasks\player-self-service-portal\TaskContract.md
contract_revision: 2
reviewer: 独立审查子代理（Claude Opus 5.5，未参与意图对齐、契约建模或路由选择；revision 1 的审查者，见 task-contract-review-r1.md）
selection_basis: 用户与项目策略未要求最强审查者；按风险选用新鲜、独立、有能力的审查者
verdict: approved
---

# TaskContract 审查：玩家自助后台（#329），revision 2

## 权威来源集（直接读取原文）

1. 用户原始需求（逐字）、"先给我一个可能的UI设计图在右侧"，以及授权答复"ok，写任务契约直接跑吧"（回应"D1–D6 按方案里的建议定吗？"）。
2. GitHub issue #329、#305（均为 OPEN；#305 的"建议修复"与"验收"两节）。
3. 设计方案 `E:\Project\DNF\Document\玩家自助后台设计方案-20261113.md`（§4–§14，含 §12 第 0 步"修 #305"）。
4. UI 设计图 `E:\Project\DNF\Document\玩家自助后台UI设计图-20261113.html`。
5. 仓库规则：服务端仓 `AGENTS.md`、`Document/FeatureDevelopment.md`、`Document/DatabaseSchemaMigration.md`；工作区 `E:\Project\DNF\AGENTS.md`。
6. 现状核对（沿用 r1）：`main` 为 `20bcaf37c`；`Server/src` 中无 `ForwardedHeaders`；GM 限流取 `Connection.RemoteIpAddress`；`GmAccessBoundary` 把回环来源视为安全连接。

## r1 发现的复核

| r1 发现 | revision 2 处理 | 结论 |
|---|---|---|
| F1 条目号错位 | Scope In / Out 改为显式编号；逐条核对 RI-1～RI-23 引用的 Scope、Constraints、SC，均指向正确条目 | 已解决 |
| F2 RI-21 标签误用 | 改为 RI-23 `not_applicable`，理由是源需求未要求发布，并引用 FeatureDevelopment §5；Requires Confirmation 保留 | 已解决 |
| F3 #305 拆分 | 选方案 (b)：#305 整体纳入 Scope In 8、RI-16、SC-9，SC-9 逐条对应 #305 验收（真实 IP 限流、`Secure`、同源检查、伪造头无效、未配置时不变），SC-11 与 Goal 改为"除 #305 / SC-9 规定的变化外不变"；Assumptions 1 写明依据方案 §12 第 0 步 | 已解决；与用户认可的方案一致，SC-9 和 SC-11 之间不再冲突 |
| F4 迁移与用户测试 | 新增 Scope In 10、11，RI-21、RI-22，Constraints 5，SC-12、SC-13；停用户服务列入 Requires Confirmation | 已解决 |
| F5 重新开启 | RI-15 与 SC-6 补上"重新开启后可再登录" | 已解决 |

## 已核对的意图处置

- 原始需求四项（RI-1～RI-5）、D1–D6（RI-6～RI-11）、方案 §5.2 踢会话（RI-12）、issue #329 全部验收（RI-2、RI-13～RI-15，以及 Constraints 4 中的越权条款）都只出现一次，处置均为 `in_scope`，并有可观测 SC。
- RI-8、RI-9 指向 Scope Out，执行侧由 SC-5（拒绝高级选项）与 Constraints 3（沿用 GM 物品校验）落实，属于有依据的排除。
- RI-16 由 r1 的 `preserved_upstream` 改为 `in_scope`：本契约直接负责，不再依赖外部承接者。
- RI-20～RI-22 的仓库强制规则都有落点与证据要求。
- RI-23 `not_applicable` 有依据，相关的不可逆动作都留在 Requires Confirmation。
- 没有 `unresolved` 行，没有重复或遗漏的实质意图。

## Scope Out 交叉检查

| Scope Out | 交叉意图 | 结论 |
|---|---|---|
| 1 改密码 / 注册 / 看他人 / 模板草稿 | 方案 §1"不做" | 有依据 |
| 2 装备高级选项 | D3 | 有依据 |
| 3 物品黑名单 | D4 | 有依据 |
| 4 启动器内嵌入口 | D6 | 有依据 |
| 5 合并 / 推送 / PR / 发版 / 部署 / 改线上 Caddy | RI-23；#305 提到的"代理上的临时处理可以删掉" | 有依据。删代理临时处理属于部署动作，#305 验收不要求在本仓完成 |

## 阻塞性发现

无。

## 非阻塞建议（交给 Spec 落实，不要求再修订契约）

1. **用户测试用库的迁移方式。** 表结构升到 v7 后，主干服务端（只认 v6）将无法再打开迁移过的库。SC-12 写的是"用当前主干实际使用的数据库（先一致备份）"，没有说是原地迁移还是在主干库的一致副本上迁移后再测试。建议 Spec 默认采用后者，即在主干库一致快照上运行 DbMigrate，既能保留用户数据，又不影响主干服务。如需原地迁移，应与停服一并按 Requires Confirmation 第 3 条先问用户，并记录用备份回滚的步骤。
2. **#305 的"`SecureTransport` 语义"待确认项。** #305 列为开放问题：配置可信代理后，回环来源是否仍视为安全连接。它不在 #305 验收中，契约也未裁定。Spec 应给出明确取舍并配测试，因为这会影响 GM 与玩家两侧的远程 HTTPS 判定。
3. #305 将随本功能分支一并关闭；PR 说明里应同时关联 #329 与 #305。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 逐条对照原始需求、issue #329 / #305 验收、方案 D1–D6 与 §12、UI 设计图及仓库规则：意图齐全、引用正确、SC 可观测，SC-9 与 SC-11 已不冲突 | 可作为 Spec 的可靠输入；剩余两处由 Spec 决定的取舍已列在非阻塞建议中 |
| efficiency | proxy | 范围与方案 §12 的 0–5 步一一对应，没有额外机制；#305 并入以后，不需要再为代理问题单开一轮 | 减少后续返工 |
| cost | proxy | 由方案推断：领域核心剥离、新表与 v6→v7 迁移、两套边界、前端两套页面、#305 管道改动、全量测试与用户测试准备；规模大于 r1，但每项都有来源依据 | 成本与已认可的方案相称，不属于镀金 |
| cognitive burden | proxy | 约 130 行，编号显式，表格可直接对账 | 读者负担低 |
| actual human participation | observed | 用户亲自认可方案与设计图，并授权执行；本契约本身没有经过人工审阅。SC-12 把用户测试交给用户，且在用户反馈前不声称验收；推送、PR、合并、上线、停用户服务都需要用户确认 | 技术审查不替代用户测试与发布授权；契约已为人工决策留出节点 |

## 结论

`approved`。本批准只对上述路径的 revision 2 有效。Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization、Assumptions 中任何一项的实质修订，都需要重新审查。
