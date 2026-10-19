---
kind: rks-task-contract-review
contract_path: 'E:\Project\DNF\DNF-JP-86-Server\.worktrees\collection-box\Document\tasks\collection-box\TaskContract.md'
contract_revision: 2
reviewer: 'Codex /root/collection_contract_review (fresh independent reviewer)'
selection_basis: '用户保存偏好允许 Sol medium/high，复杂有界审核采用 high；适用政策不强制最强模型。本 reviewer 未参与意图对齐、合同建模或结构路由，具备本次 intake 审核所需能力。'
verdict: approved
---

# TaskContract intake review

## 绑定身份与评审边界

- 已重新读取上述绝对路径，文件声明 `revision: 2`、`state: ready`。
- 本次只审核根意图保真、Scope Out 处置、授权、可观察验收与比例性；不审核规格、计划、实现、测试结果或完成度。
- 本结论只绑定该路径的 revision 2；Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization、Assumptions 或来源权威发生实质变更后须重新审核。

## 权威来源集

1. 用户将下一阶段置于新工作分支，并以剩余活动地图优先；随后明确本轮预期包含收集箱与剩余活动地图。
2. 用户把地图总范围收敛为“先完成当前客户端有入口的全部活动地图，历史活动另列”，并接受先收集箱、后续其他地图的顺序，已建立 #164 与本轮 #170。
3. 用户决定收集箱“常驻启用，取消历史到期限制”。
4. 用户先授权收集信息与给出可行设计，并连续追问 PVF 预设模板、动态系列生成及持久化方式；随后确认按稳定系列 ID 与 slot 保存原物、系列扩张安全而既有系列修改需兼容处理的理解。
5. 用户以“可以，按这个流程写任务契约后实现吧”授权当前收集箱实施。
6. 执行中用户授权按更新后的 rks-agent 自适应处理既有持久化过程记录冲突，按 reviewer/执行者判断继续推进。
7. `E:\Project\DNF\AGENTS.md` 的仓库边界、协议证据等级、分层与人工实机验收约定。
8. 已接受的 `E:\Project\DNF\DNF-JP-86-Server\.worktrees\collection-box\Document\CollectionBox-Design-20261019.md`，其中收集箱玩家规则、PVF/存储路线、原物事务、协议投递、兼容限制与验收边界构成当前技术设计来源。

## material intent dispositions checked

| Material intent | Contract disposition | Intake finding |
|---|---|---|
| 实施当前收集箱并形成可供实机验收的软件结果 | `in_scope` | Goal、Scope In 与 SC-1 至 SC-7 形成完整且可观察的交付边界。 |
| 系列由 PVF 动态枚举，按稳定系列 ID 与 slot 保存原物；既有系列变化必须兼容处理 | `in_scope` | Scope In、Constraints、SC-1、SC-2、SC-6 覆盖扩张、实例状态和定义变化，没有把“动态”夸大为任意资源变化都安全。 |
| 收集箱常驻并取消历史到期限制 | `in_scope` | Scope In、Constraints、SC-3、SC-4 明确区分箱子永久与物品自身期限。 |
| 当前客户端有入口的全部活动地图优先，历史活动另列 | `not_applicable` | 该意图约束本轮总排序；本合同是已获授权的独立收集箱子任务，明确不把本结果声明为完成其他地图，处置有来源支持。 |
| 在新工作分支继续并保持可审核结果 | `in_scope` | 合同限定隔离工作区、本地检查点与审核，不直接修改主干。 |
| 接受设计中的单箱选择、原物事务、协议与重放恢复 | `in_scope` | SC-2 至 SC-5 提供结果级判据，具体实现机制仍留给规格和实现，比例适当。 |
| 用户保留实机验收和实际运行变更决定权 | `in_scope` | SC-7 与 Scope Out 明确保留 ClientVerified、真实库、部署、PVF 替换和远端收尾边界。 |
| 自主适应更新后的 skill 并协调旧过程记录冲突 | `authorized_change` | 来源授权支持调整过程和本地检查点；合同未借此扩大业务目标、生产变更或远端权限。 |

信息收集、设计请求以及关于 PVF/持久化的提问已在后续接受的设计和实施授权中收敛，属于上述设计与持久化意图的形成证据，不另造重复交付义务。未发现遗漏、重复、矛盾或 `unresolved` 的 material intent。

## Scope Out crossings

| Scope Out item | Crossing disposition |
|---|---|
| 其他活动地图 | 与本轮总目标相交，但有明确 `not_applicable` 处置：由后续任务承担，当前合同不宣称完成。没有把总目标悄然缩写成“收集箱即全部地图”。 |
| 客户端 EXE/DLL 改造 | 接受设计认为当前路线可先使用原生 A21 能力，且是否需要兼容补丁留待实机结果；当前排除有依据，不妨碍收集箱服务端交付。 |
| 全服权威战斗属性重构 | 接受设计明确本期由客户端原生单箱链应用效果，避免重复叠加；排除属于结果所需的比例控制。 |
| 真实玩家库、部署/重启、远端推送/PR/Release、实际 PVF 替换 | 用户授权是实现与本地工作流授权，未授予这些运行或远端变更；排除正确保留人类决定边界。 |
| 根据旧 itemId 制造原物或未经确认迁移旧数据 | 接受设计证明 itemId 不足以恢复实例，要求保留诊断并取得明确迁移规则；排除防止资产制造或丢失。 |

## 五维评价

| Dimension | Evidence level | Evidence | Practical consequence |
|---|---|---|---|
| quality | proxy | Goal 与 SC-1 至 SC-7 覆盖 PVF 枚举、原物事务、选择恢复、协议、重放、兼容和实机边界；接受设计提供对应技术依据。 | 合同为实现和结果审核提供了可追踪质量门槛，但 intake 文档本身不证明实现质量或 ClientVerified。 |
| efficiency | proxy | 一个独立收集箱边界复用已接受设计，其他地图留到后续，且把机制细化留给规格/实现。 | 可减少当前任务的上下文扩张和返工；实际开发周期与吞吐尚未观察。 |
| cost | missing | 权威来源没有时间、算力、金钱或人力预算，也没有本任务的实际消耗记录。 | 无法量化成本收益；因用户未设置成本上限，缺失不阻塞本合同 intake。 |
| cognitive burden | proxy | Root Intent Coverage、Scope Out 与七项 SC 把关键决定集中表达，且不要求用户重复确认已决定的常驻、持久化与实施授权。 | 预计能降低执行时重新解释的负担，但没有实际用户负担测量，不能声称已观察到降低。 |
| actual human participation | observed | 用户本人决定了活动地图总范围与顺序、常驻规则、系列/slot 持久化理解，并明确授权当前收集箱实施；实机验收仍保留给用户。 | 合同建立在真实人类决策上，没有用自动审核替代授权或最终实机接受。 |

## Verdict

`approved`。revision 2 对当前独立收集箱子任务保真、获授权、可验收且比例适当；其他活动地图被明确保留为本轮后续工作，没有被本合同吞并或错误宣称完成。未发现阻止结构路由的 intake 缺陷。
