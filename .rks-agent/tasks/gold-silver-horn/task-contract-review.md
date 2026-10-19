---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.rks-agent/tasks/gold-silver-horn/TaskContract.md
contract_revision: 1
reviewer: /root/gold_silver_contract_review (GPT-6.1-sol, independent)
selection_basis: 用户长期偏好 Sol medium/high；适用政策未要求最强审查者，本任务的契约忠实度与授权风险可由未参与建模、对齐或路由的独立 competent reviewer 覆盖
verdict: approved
---

# TaskContract 独立审查

## 绑定身份

- 精确契约：`E:/Project/DNF/DNF-JP-86-Server/.rks-agent/tasks/gold-silver-horn/TaskContract.md`
- 审查 revision：`1`
- 契约状态：`ready`
- 独立性：本审查者未参与该契约的建模、意图对齐或结构路由，也未替控制器选择后续 workflow。

## 权威来源集

1. 用户原始要求：“金角银角还是和之前的一样，如果没有什么特殊机制之类的就直接按藏宝地一样的顺序实现就行。先派子代理看看什么情况”。
2. 已向用户披露并作为后续授权上下文的调查结论：当前候选为四房线性玩法、每日三次；`0x042B` 两个 `u16` 的具体映射仍需闭合；兑换与开罐存在材料重复扣除风险。
3. 用户后续任务授权：“那你先继续收集然后开始创建功能后部署，我再验收。部署后给我一个验收清单”。
4. `E:/Project/DNF/AGENTS.md` 与仓库内 `E:/Project/DNF/DNF-JP-86-Server/AGENTS.md` 的项目边界、证据分级、协议、持久化、部署和文档约束。
5. 审查绑定与选人政策：只审上述精确路径的 revision 1；无需最强审查者，但必须由未参与建模、对齐或路由的独立 competent reviewer 执行。

## Material Intent 核对

| Material intent | 契约处置 | 核对结果 |
|---|---|---|
| 先继续收集证据并确认是否存在特殊机制 | `RI-1` / `in_scope` | 恰好一次；由 Scope In 1 与 SC-1 承接，未知字段与奖励语义必须在受影响实现前闭合。 |
| 没有额外特殊机制时，沿用藏宝地的实施节奏交付金角银角玩法 | `RI-2` / `in_scope` | 恰好一次；由 Scope In 2-3 与 SC-2-3 承接，同时保留本玩法差异和当前 PVF 规则。 |
| 功能完成后部署到当前测试服务 | `RI-3` / `in_scope` | 恰好一次；Scope In 4、SC-4 和 Authorization 对部署范围一致。 |
| 部署后提供验收清单，并保留用户最终实机验收权 | `RI-4` / `in_scope` | 恰好一次；Goal 与 SC-5 同时覆盖清单和人工验收边界。两项共享同一处置与交付边界，无需拆成重复意图。 |

未发现缺失、重复或 `unresolved` 的 material intent；没有使用 `preserved_upstream`、`authorized_change` 或 `not_applicable` 来转移本契约应承担的结果。

## Scope Out 交叉检查

| Scope Out 项 | 与根意图的关系 | 核对结果 |
|---|---|---|
| 旧版灵符插房、旧金银角双图、历史限时纪念任务 | 用户要求的是在当前调查结果上确认特殊机制并实现当前玩法，不构成恢复历史版本的授权 | 不删除任何已授权结果；当前四房候选和差异取证仍在 Scope In / SC-1 至 SC-3 内。 |
| 客户端二进制或 PVF 修改，以及无证据的掉率、奖励或其他活动规则变更 | 用户授权的是功能交付，未指定这些手段；仓库证据规则禁止凭未知扩张 | 属于比例适当的机制与范围边界，不妨碍通过服务端与现有资源实现授权结果。若后续证据证明必须扩张，应先修订契约并重新审查。 |
| 把自动化回归当作实机验收、自动关闭 `#155` | 用户明确保留部署后的亲自验收 | 与 `RI-4`、Goal 和 SC-5 一致，保护而非排除用户意图。 |

## 忠实度与比例性结论

Goal、Scope、Constraints、Success Conditions、Assumptions 与 Authorization 相互一致。契约没有臆造 `0x042B` 的具体 wire 值或奖励材料语义，而是要求在受影响代码落地前用可复核证据闭合；这允许实施阶段完成必要技术取证，同时避免把未知写成协议事实。

“成功入场扣一次、拒绝或建图失败不扣”、持久额度、数据保护及回退输入对应当前已知的每日次数、失败消耗和部署数据风险，属于保护可验收结果所需的边界，未把通用审计机制或特定实现结构强加为根契约要求。任务授权覆盖调查、实现、验证、当前测试服务部署和验收清单；推送、PR、主干合并以及代替用户实机验收被正确排除。

## 五维评估

| Dimension | Evidence class | Evidence | Practical consequence |
|---|---|---|---|
| quality | observed | revision 1 的 Goal、Scope、四条 Root Intent Coverage、五项 Success Conditions、两条 Assumptions 与授权范围可直接核对；协议未知和人工验收边界被明确保留 | 契约质量足以支持后续路由；此结论不等同于功能质量或客户端行为已经验证。 |
| efficiency | proxy | 契约要求复用现有四房地图/战斗/结算链，并将新调查集中在计数映射、准入、额度和奖励扣费差异 | 预期减少重复实现与无关调查，但尚无实际执行数据证明工期或吞吐。 |
| cost | proxy | Scope Out 排除了客户端/PVF改造和历史玩法恢复，Scope In 将共用修复限制为闭环所必需 | 预期控制开发、部署和回归成本；实际成本需在实施后观察。 |
| cognitive burden | proxy | SC-5 要求一份覆盖资格、计数、战斗、奖励、退出和重登观察点的可执行验收清单，且只把最终实机判断留给用户 | 预计降低用户验收时的记忆与组织负担，但清单尚未产出和被用户实际使用。 |
| actual human participation | observed | 用户先给出条件化实现方向，收到调查结论后又明确授权继续调查、创建、部署，并保留部署后的亲自验收 | 已有真实人工决策和任务授权；本审查不会替代用户最终实机验收。 |

## Verdict

`approved`。精确路径下的 revision 1 已具备完整、授权充分且比例适当的执行边界，可以作为后续结构路由的契约门禁证据。Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization、Assumptions 或来源权威发生任何实质变化时，本批准立即失效，必须对新 revision 重新独立审查。
