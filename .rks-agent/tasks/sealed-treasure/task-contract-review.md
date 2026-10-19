---
kind: rks-task-contract-review
contract_path: E:/Project/DNF/DNF-JP-86-Server/.rks-agent/tasks/sealed-treasure/TaskContract.md
contract_revision: 1
reviewer: Codex /root/sealed_contract_review
selection_basis: 按调度请求使用 gpt-5.6-sol high；审查者未参与意图建模、契约起草或路由选择，能力与本任务风险相称
verdict: approved
---

# TaskContract 独立审查：封印的藏宝地

## 绑定身份

- 审查对象：`E:/Project/DNF/DNF-JP-86-Server/.rks-agent/tasks/sealed-treasure/TaskContract.md`
- 审查 revision：`1`
- 结论仅适用于上述精确路径与 revision；Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization、Assumptions 或来源权威发生实质变化后必须重新审查。

## 权威来源集

1. 用户前置授权：“你先派子代理完成全部信息收集吧”。其已完成成果为：
   - `Document/SealedTreasure-Research-20261019/README.md`
   - `Document/SealedTreasure-Research-20261019/content.md`
   - `Document/SealedTreasure-Research-20261019/protocol.md`
   - `Document/SealedTreasure-Research-20261019/history.md`
2. 用户最新决定：“那就给spicies dungeon也定义为不消耗疲劳。其他的就按当前设计实施，后续再说要不要改。这样的话可以开始实施流程了嘛？”
3. 工作区 `E:/Project/DNF/AGENTS.md` 与仓库 `E:/Project/DNF/DNF-JP-86-Server/AGENTS.md`，包括分层、事务、测试、文档归属及证据等级约束。
4. 候选契约自身仅作为待审对象，不反向充当授权或技术事实来源。

## 实质意图处置核查

| Intent | 契约处置 | 审查结果 |
|---|---|---|
| 所有声明 `[spicies dungeon]` 的地下城不消耗疲劳 | `RI-1 / in_scope`，落在 Scope In 与 SC-1 | 准确承接用户显式决定；验收同时覆盖零疲劳准入、首房、新房、重访及普通图回归。 |
| 其余按已形成的当前设计实施 | `RI-2 / in_scope`，落在 Scope In 与 SC-2..5 | 入口、等级/模式限制、双票、元素路线、中央箱与奖励闭环均有可观察结果；未偷换成恢复全部历史运营规则。 |
| 规则可在后续再调整 | `RI-3 / in_scope`，落在 Scope Out 与 Assumptions | 每日三次、06:00 补给和退出退票等未闭合规则被明确留作后续；当前采用项被标为暂定策略。 |
| 现在进入实施流程 | `RI-4 / in_scope`，落在 Goal 与 Authorization | 授权覆盖实现、自动验证、文档和本地工作流提交；部署、推送、合并及真实客户端验收未被扩张进来。 |
| 不把孤儿任务或未知协议编造成已还原功能 | `RI-5 / in_scope`，落在 Scope Out 与 Constraints | 20747..20749、专属商店、新 opcode、PARTY SYMBOL native 编码均保持证据边界。 |
| 遵守仓库分层、事务与无现场 PVF 测试约束 | `RI-6 / in_scope`，落在 Constraints 与 SC-6 | 与仓库规则一致，并要求保护普通副本和既有活动行为。 |

六项实质意图各出现一次；没有 `unresolved`、`preserved_upstream`、`authorized_change` 或 `not_applicable` 行，也没有重复处置或遗漏。

## Scope Out 交叉核查

| 交叉项 | 支持与边界 | 结论 |
|---|---|---|
| 历史“每日三次 / 06:00”规则 | `Historical-CN` 支持旧国服玩法，但研究明确指出当前实现为补到 1、北京时间自然日，A21 原服规则未闭合；用户同意先按当前设计实施并后续再改。 | 排除独立次数计数与 06:00 改造有来源支持，不是静默删减。 |
| 失败扣票与退出退票 | Scope In 的失败位于准入、选票、扣票、建局事务语境；Assumptions 明确退出与结算沿用既有生命周期且不另行退票。 | 与研究中的异常退出 Unknown 不冲突；不得在下游把“失败不扣票”扩张为死亡、放弃或断线自动退票。 |
| 孤儿任务、兑换店、额外奖励 | 三个任务未注册且无实际奖励行，研究未找到专属商店证据。 | 排除有直接证据，避免从资源存在推导可达玩法。 |
| 部署、数据库、发物品、客户端/PVF、推送/合并 | 用户授权是开始实现流程，未授权部署或外部状态变更。 | 授权边界准确。 |
| A21 UI、协议与客户端可见行为 | 研究没有新增 `WireCaptured` 或 `ClientVerified`。 | 契约只要求自动化与真实 PVF 证据，并把原生客户端验收留到部署后，证据等级正确。 |

## 一致性与比例性

- Goal 以玩家可见闭环为主：能入场、票据正确、元素路线可持续、中央箱可领取、普通副本不回归。Scope 中的实现机制只用于约束已知高风险边界，没有引入额外通用框架、协议或运营系统。
- 双票“活动票优先、普通票回退”来自 `70Reference`，契约在 Assumptions 中明确它是本次服务端暂定策略，不冒充 A21 原服事实。
- 四元素 `1/2/4/8` 只作为 `ContentStatic` 原始参数；契约允许私有命名状态并禁止预设 native 位编码或发明同步包，符合证据等级。
- 中央箱要求销毁前不可领取、重复领取幂等，并复用现有物件奖励与白金箱开箱链；这是结果约束，没有把尚未实测的客户端显示时序写成原服事实。
- SC-1..6 均有可观察证据类型；自动测试、真实 PVF 集成与完整构建不会被写成 `WireCaptured` / `ClientVerified`。原生 A21 UI、跨房表现和协议差异保留人工验收项。
- 默认常驻开启、现有补给周期、双票优先级和退出不退票均被集中列为 Assumptions，未伪装成研究已确认事实；若实施证据推翻承载能力，契约要求报告限制而不是新增无证据协议。

## 五维评价

| Dimension | Evidence level | Evidence | Practical consequence |
|---|---|---|---|
| quality | observed | 精确读取 revision 1 后，Goal、Scope、六项 Root Intent、Constraints、SC-1..6 与四份研究报告逐项闭合；证据等级和未知项均有明确边界。 | 契约足以约束实现与结果审查，不需补充根意图或重写验收条件。 |
| efficiency | proxy | 契约复用现有副本、疲劳、物件掉落、背包与开箱链，只新增本图所需的票据替代、活动投影和 run 状态；尚未执行实现。 | 预期避免另建运营次数、商店或新协议的无授权工作，但实际工期和返工率需执行后确认。 |
| cost | proxy | Scope Out 排除了部署、客户端/PVF、数据库变更和历史运营系统；SC-6 要求有针对性的回归与一次完整解决方案验证，但没有实测成本数据。 | 成本边界可控且与风险相称，不能据此宣称已有准确工时或运行成本。 |
| cognitive burden | observed | 契约以 6 个意图、6 个成功条件和集中 Assumptions 表达关键选择；机制细节仅覆盖票据事务、元素状态和奖励幂等三个高风险面。 | 下游可按单一玩家闭环理解任务，且能清楚区分暂定策略、静态证据和待实机项。 |
| actual human participation | observed | 人类用户先明确授权完整信息收集，随后亲自决定所有 `[spicies dungeon]` 免疲劳、采用当前设计先实施并保留后续调整权。 | 核心产品取舍和实施授权来自真实用户；本审查不替代未来部署授权或 A21 人工验收。 |

## 结论

`approved`。精确路径的 revision 1 已完整、获授权、比例适当，且没有未解决的根意图保真问题；可进入结构路由。后续若改变疲劳适用范围、补给/次数策略、退出退票语义、双票优先级、元素通路条件、中央箱领取链或部署边界，须提升 contract revision 并重新独立审查。
