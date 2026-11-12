---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-chat\.rks-agent\tasks\chat-routing-first-batch\TaskContract.md
contract_revision: 6
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐与路由选择）
selection_basis: 用户与项目未要求最强评审者；按控制方传入的政策使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：聊天单入口路由与第一批频道（revision 6）

## 权威来源集

- 契约 Source Request 第 1–7 项用户原话。
- 调查摘要：`.rks-agent/tasks/chat-routing-first-batch/research/conversation-re-20261112.md`（70Reference 与 ClientStatic，无实机抓包）。本版新增的第 35 行「特殊状态」条目已核对。
- 仓库政策：`AGENTS.md`（功能开发流程、用户测试通过前不建 PR、版本号规则）、`Document/Architecture.md`（同一事实只由一个 owner 保存）、`Document/FeatureDevelopment.md`。
- 上游 owner：GitHub issue #62「[Cycle] 公会系统」，本次用 `gh issue view 62` 核实为 OPEN。
- `verification/completion-evidence.md`：只用来核对测试库的实际情况，见非阻塞建议 3。
- 上一版评审（revision 5，changes_required）只用来核对修正项。本评审对 revision 6 全文重新评审，不沿用旧结论。

## 上一版发现核对

| revision 5 发现 | revision 6 处理 | 结论 |
|---|---|---|
| 阻塞：公会 mode 6 同时适用「静默」和「拒绝」 | Scope In 的拒绝规则改为「不含公会 6 与 1:1 的 43」；SC-6 改为「不含 6、43」；SC-2 补上「公会 mode 6 不投递、不回拒绝，连接保持可用」 | 已解决 |
| 非阻塞 1：mode 33／37 的「决斗场」说法缺少证据 | mode 33 标注了 `[game+0xF48]` 特殊状态与 ClientStatic High；调查摘要第 35 行补上了状态集合 {8, 0xD, 0x14, 0x16, 0x17, 0x18}、地址 `0163D300`／`0163D150` 与 High | 已解决（mode 37 的措辞见非阻塞建议 2） |
| 非阻塞 2：SC／RI 编号顺序 | 未改 | 不影响核对 |
| 非阻塞 3：`0x016A` 成功 result 取值 | 契约仍留给实现决定 | 合理，属实现细节 |

## 根意图核对

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 调查与方案（请求 1） | not_applicable | 成立：方案文档 `Document/聊天通信与服务器广播-A21方案-20261111.md` 存在，Artifact 已在此前交付 |
| RI-2 服务器广播／公告（请求 1） | authorized_change | 成立：请求 2 明确说「先不管公告」 |
| RI-3 单入口与分流（请求 2） | in_scope | 成立：所有发言（含 mode 6、43）都经 `0x0011` 入口；`0x01A3`／`0x01A5`／`0x01A6` 是会话管理 CMD，`0x01A9` 只做拒绝，这些都不是第二个聊天发言入口 |
| RI-4 频道、服务器、小队成员建模（请求 2） | in_scope | 成立：SC-7 |
| RI-5 公会人员（请求 2） | preserved_upstream | 成立：issue #62 是独立的 owner，已核实仍为 OPEN；本契约只承担 GuildOf 恒为空，以及 mode 6 静默（SC-2） |
| RI-6 基类子类、单参数 MembersOf（请求 3） | in_scope | 成立：Constraints 第 1 条；会话范围作为新子类加入 |
| RI-7 禁言、黑名单、限频推迟（请求 4、6） | authorized_change | 成立：本期只留检查点，建会话同样经过检查点 |
| RI-8 喇叭范围（请求 4） | in_scope | 成立：SC-4、SC-5 |
| RI-9 服务器专用 mode 只拒绝（请求 4） | in_scope | 成立：SC-6；6 和 43 已排除在拒绝集合之外，不再冲突 |
| RI-10 私聊尽量可达、不报错（请求 5） | in_scope | 成立：SC-3；1:1 部分沿用这一原则（目标不在线不回包，mode 43 失败静默） |
| RI-11 第 4 版已定项（请求 6） | in_scope | 成立：「`Conversation(id)` 暂不加」已注明由 RI-14 改变，有请求 7 支撑 |
| RI-14 完成 1:1 对话窗口（请求 7） | in_scope | 成立：70 没有实现，改以客户端逻辑为准，符合「对比 70server 和 client」；收窄部分已写明交回用户的条件 |
| RI-12 实机验收（请求 6） | in_scope | 成立：SC-9；验收结论由用户给出 |
| RI-13 仓库流程政策 | in_scope | 成立 |

每个意图都只出现一次；没有重复、遗漏或 `unresolved` 行。请求 5 的「剩下的在测试计划就行」由 SC-9 的编号测试流程承接，不需要单独一行。

## Scope Out 交叉核对

- **多人群聊、复用旧窗口、mode 33／37**：与 RI-14 相交。理由是用户反馈说的是「1v1聊天」，70 也没有实现，而且 mode 33 的触发条件现在有证据。RI-14 已写明这是 Agent 的收窄，用户如有异议就交回用户决定；SC-9 也要求在测试流程里写明本期的表现。处置明确且有依据。
- **换频道后保留会话**：与 Scope In「一方下线或换频道时结束其所有会话」、SC-12 一致。
- **公会系统本身**：由 RI-5 的上游 owner 承接。
- **禁言、黑名单、限频、违禁词**：由 RI-7 承接。
- **公告与 `Channel(id)`**：由 RI-2、RI-11 承接。
- **GM 喇叭、攻坚队、其他喇叭、物品链接**：与 SC-6 的拒绝路径一致。
- **PR、推送、合并、版本号**：与仓库政策和 Requires Confirmation 一致。

## 阻塞性发现

无。

## 非阻塞建议

1. **通用正文检查与 mode 6、43 的先后关系可以写得更明。** Scope In 有一条通用规则：「正文为空或超过客户端对该回推协议的显示上限时回 `0x0011` 拒绝」，SC-10 也没有写例外。这条规则对 mode 43 已被具体条款覆盖：Scope In 1:1 条款用粗体写明正文为空或超长时静默、不回拒绝，SC-11 也写了「mode 43 不回拒绝」，所以不构成冲突。但 mode 6 正文为空或超长时该拒绝还是静默，契约没有直接说。建议在通用规则和 SC-10 后补一句「mode 6、43 除外（静默）」，或者写明对 mode 6 先判断范围；也可以在 Spec 里定下来。
2. **mode 37 的措辞比证据宽。** 调查摘要第 33、35 行写的是特殊状态下「跨服情形」才发 37，契约写成「客户端在决斗场等特殊状态下的本地 1:1 窗口也发出」，少了「跨服情形」这个限定。因为 37 本来就在拒绝集合里，不影响行为；建议改成与摘要一致的说法。
3. **SC-9 与用户后来对测试库的选择不一致。** 契约（Constraints 第 5 条、SC-9）仍写「当前主干实际使用的数据库」，`verification/completion-evidence.md` 则记录：主检出的库是 schema v0，用户在 2026-11-12 选择「复制 #308 副本」作为测试库。这是用户自己的决定，不算授权缺口；但复测时 SC-9 又会按字面不满足。建议在 SC-9 或 Authorization 里记下用户的这项选择（附原话出处），免得验证阶段再次登记偏差。
4. SC 编号仍是 1–6、10、7、8、11、12、9 的顺序，RI-14 排在 RI-12 前面。不影响核对，可以顺手整理。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 对照请求 1–7 原话、调查摘要（含新增的第 35 行）和仓库 owner 规则逐行核对；revision 5 的阻塞项已在 Scope In、SC-2、SC-6 三处一致修正；issue #62 已核实为 OPEN | 可以作为可靠的 Spec 输入。1:1 行为全部来自 ClientStatic，仍要实机确认；正文检查的先后关系可在 Spec 定下来 |
| efficiency | proxy | 本轮只改文本：拒绝例外、SC-2、mode 33 证据，外加调查摘要一行 | 不需要重新对齐用户意图，可以直接进入路由 |
| cost | proxy | 范围仍限定在第一批频道和 1:1 最小闭环，多人群聊与特殊状态 1:1 已排除 | 规模和用户诉求相当，没有过度建设 |
| cognitive burden | proxy | 例外集合 {6, 43} 在 Scope In 与 SC-6 中写法一致；Assumptions 写明了客户端不去重、不复用、拒绝会弹框等表现 | 实现与测试的决策点清楚；剩下的歧义只有建议 1 和 3 两处，范围很小 |
| actual human participation | observed | 用户在请求 6、7 中的原话分别授权实现和范围扩展；mode 33／37 的拒绝、多人群聊的排除由 Agent 依据证据决定，未经用户确认，契约写明了交回用户的条件；测试库的选择只见于 Agent 写的验证记录（这一项是 proxy） | 实机验收、停服或写库、推送、PR 仍要用户亲自确认；本评审不代替这些决定 |

## 结论

`approved`：revision 6 解决了 revision 5 的唯一阻塞项，对请求 1–7 的根意图承接完整、处置有据，范围适度，可以进入路由。本结论只适用于上述路径的 revision 6；Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization 或 Assumptions 再有实质修订，都需要重新评审。
