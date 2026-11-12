---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-friends\.rks-agent\tasks\friend-relations-first-batch\TaskContract.md
contract_revision: 1
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐与路由选择）
selection_basis: 用户与项目未要求最强评审者；按控制方传入的政策使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：角色关系系统与好友第一批（revision 1）

## 权威来源集

- 契约 Source Request 第 1–5 项用户原话。第 5 项中 Agent 提议的内容是转述，不是原文；本评审按转述核对。
- 调研依据：`E:\Project\DNF\Document\好友系统可行性方案-20261112.md`（协议选型、三方规则对照、E1–E4 证据缺口、分期 P0–P4）。
- RI-12 的来源：`.rks-agent/tasks/chat-routing-first-batch/TaskContract.md` revision 6（请求 4、RI-7、Scope In「发送前预留检查点……黑名单结果码 77 随黑名单一起做」）。
- 仓库政策：`AGENTS.md`、`Document/FeatureDevelopment.md`（独立工作区、证据标签、当前主干库做用户测试、用户通过前不建 PR）、`Document/Architecture.md` 第 113 行「同一事实只由一个 owner 保存」与第 114 行「领域服务不认识具体协议的字节布局」。
- 上游 owner：用 `gh issue view` 核实 #60「[Cycle] 好友系统」与 #61「[Cycle] 聊天与通信」均为 OPEN。
- Current Situation 抽查（本工作区代码）：
  - 分支 `feature/60-friend-relations`，HEAD 即 `17eb734d4`。
  - `CharacterDatabaseSchema.Version = 4`；`characters.delete_flag` 存在。
  - `UnansweredReportSlices.cs:107` 的 `0x0078` 只收不回。
  - `ChatPreSendChecks.Passes` 是恒真的预留检查点。
  - `protocol/game/client-to-server/0x0122.md` 有 11 字节真实样本。
  - `Research/third-party-s4a21-v3.3/` 含 README 与 `decompiled/`。

  以上均与契约描述一致。

## 根意图核对

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 调研与可行性方案（请求 1） | not_applicable | 成立：方案文档存在，本契约只承接实现 |
| RI-2 第三方服务端反编译成源码（请求 2） | not_applicable | 成立：`Research/third-party-s4a21-v3.3/decompiled/` 与 README 存在 |
| RI-3 好友、最近联系人、师徒、黑名单进入同一关系记录系统（请求 3） | in_scope | 成立：Goal 与 Constraints 第 1 条明确只有一个角色关系领域服务；本期只实现两种关系，另两种由 RI-6、RI-7 处置 |
| RI-4 只记录角色 ID、不记名字（请求 3） | in_scope | 成立：Constraints 第 2 条写明名字只在协议入口解析；用语统一见非阻塞建议 2 |
| RI-5 偏向纯存储的领域服务：含规则、不主动推送（请求 3、4） | in_scope | 成立：Constraints 第 3 条、SC-8「不依赖任何发送或推送能力」可从依赖观察 |
| RI-6 师徒只做关系存储、后面再说（请求 4） | authorized_change | 成立：请求 4 原话明确推迟 |
| RI-7 最近联系人后置（请求 3 → 请求 5） | authorized_change | 成立：请求 5 认可「待 `0x02A3` 逆向后再接」 |
| RI-8 先做好友系统（请求 4） | in_scope | 成立：SC-1–SC-5 |
| RI-9 婚姻与关系系统分开（请求 4） | in_scope | 成立：Constraints 第 4 条、Scope Out |
| RI-10 黑名单与好友同批（请求 5） | in_scope | 成立：SC-6、SC-7。方案文档第 2.4 节「全量 `0x0111` 清掉黑名单显示」也说明两者须同批 |
| RI-11 GM 等入口也须经关系服务写入（请求 5） | in_scope | 成立：Constraints 第 1 条；本期不新增 GM 写入口，不构成越界 |
| RI-12 聊天侧交来的黑名单判定与结果码 77 | in_scope | 成立：聊天契约 RI-7 与 Scope In 原文把黑名单判定交给好友系统；禁言、限频仍归 #61，且 #61 为 OPEN |
| RI-13 频道跟随 `0x0323`、黑名单接入组队／交易／决斗／邮件 | preserved_upstream | 成立：#60 是本批次的父 cycle 且为 OPEN；本契约是第一批，#60 在本批之后仍负责。注意见非阻塞建议 5 |
| RI-14 #60 初始范围中的接受／拒绝、最近联系人、跨频道 | preserved_upstream | 成立：同上；跨频道在线状态已由 SC-3、SC-4 承接，没有把本契约自身的结果推给上游 |
| RI-15 仓库流程政策 | in_scope | 成立：Constraints 第 6 条，SC-9、SC-10 |

每个意图都只出现一次；没有重复或 `unresolved` 行。

请求 5 中两项已认可的决定没有单独成行：「读写接口拆开」和实现顺序「客户端证据 → 关系领域服务 → 在线投影 → 协议切片」。
- 读写拆分已写入 Scope In 第 2 项。
- 顺序已体现为 Scope In 第 1 项和 Assumptions 第 1 条（实现前须取得客户端读取证据）。

两者都属于机制或排程，不是独立的结果意图，内容没有丢失，不构成遗漏（见非阻塞建议 1）。

## Scope Out 交叉核对

- **最近联系人、团队最近玩家**：由 RI-7 处置；团队最近玩家在方案文档中「随团队副本」后置，用户也没有要求。
- **师徒**：由 RI-6 处置。
- **好友确认流程、亲密度、分组、备注、推荐、礼物、PVP_BUDDY、旧式 `0x003C`／`0x003D`**：三方参照都没有，客户端 handler 是默认 handler（方案文档 2.1、2.2 节），接受／拒绝由 RI-14 处置。Assumptions 第 2 条写明：若客户端证据显示需要确认，交回用户决定。处置有据。
- **`0x0323`、黑名单在组队／交易／决斗／邮件上的生效**：由 RI-13 交给 #60。
- **婚姻**：由 RI-9 处置。
- **角色改名**：本仓无此功能，有据。
- **推送、PR、合并、版本号**：与仓库政策及 Requires Confirmation 一致。

## 阻塞性发现

无。

## 非阻塞建议

1. **SC-8 可以更直接地验收「统一关系系统」。** 调研依据方案文档第 4.1 节写的是用户请求 3 之前的设计：`IFriendDomainService`／`IBlacklistDomainService` 按种类拆分、两张独立的表。它与 Constraints 第 1 条相反，而 Spec 作者会读这份文档。SC-8 的「关系只有一个 owner」字面上可以理解为每种关系各有一个 owner。

   建议做两处修改：
   - 把 SC-8 写成「好友与黑名单同属一个角色关系领域服务（读、写接口分开），没有按关系种类拆分的服务」。
   - 在 Context 注明方案文档 4.1 节的拆分设计已被请求 3 取代。

   这样也顺带承接了请求 5 的读写拆分。
2. **「角色仓库 ID」与「角色 ID」用语统一。** Goal、Constraints 第 2 条用的是「角色仓库 ID」，SC-8 用的是「角色 ID」。从方案文档和用户对请求 5 的认可看，两者都指 `characters.character_id`。建议在 Constraints 第 2 条写明这一对应关系，避免被误读为账号金库之类的 ID。
3. **SC-7 的聊天过滤范围可以写全。** Scope In 写的是「聊天投递（含私聊与 1:1 窗口内发言）」，SC-7 写的是「普通聊天、私聊与 1:1 窗口内发言」，都没有说队伍、频道喇叭和服务器喇叭是否也按收件人的黑名单过滤。建议在 Spec 中列明第一批的每个聊天频道是否过滤，并写进测试流程。
4. **上限取值属于玩法语义。** Assumptions 第 3 条规定：客户端没有可见上限时，好友与黑名单各取 64，即上游值。70 的取值是 32／10。这项选择没有经过用户确认。按 `FeatureDevelopment.md` 第 2 节，证据不足且影响功能语义时须向用户提问。建议 E3 有结论后，在测试流程或提问中告诉用户最终取值。
5. **不要让本批 PR 关闭 #60。** RI-13、RI-14 依赖 #60 继续承担后续批次。PR 描述里请用「Refs #60」而不是「Closes #60」，否则 `preserved_upstream` 的 owner 会随合并消失。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 对照请求 1–5 原话、方案文档、聊天契约 revision 6 与仓库政策逐行核对；Current Situation 的六项事实在代码中抽查一致；#60、#61 已核实为 OPEN | 可以作为可靠的 Spec 输入。协议布局目前仍是参照实现加一份样本，契约已要求先取得客户端证据；方案文档 4.1 节与契约冲突，须按建议 1 防止误用 |
| efficiency | proxy | 范围相当于方案文档 P0+P1+P2，P3、P4 后置；实现顺序沿用用户认可的顺序 | 可以直接进入路由，不需要重新对齐意图 |
| cost | proxy | 方案文档估算 P0–P2 约 7–11 天；本契约合为一批、一次用户测试 | 规模与「黑名单与好友同批」的要求相当；没有过度建设。未来关系种类只要求结构上能容纳，没有要求预先实现 |
| cognitive burden | proxy | Scope In／Out 与 RI 一一对应；不确定项（上限、条目末尾 4 字节、全量列表是否清除黑名单显示、单向关系）都在 Assumptions 中写明了处理规则 | 实现与测试的决策点清楚；剩余歧义集中在建议 1、3 两处 |
| actual human participation | observed | 用户以原话完成以下决定：关系系统定位（请求 3、4）、师徒与婚姻处置（请求 4）、对 Agent 提议的认可与实现授权（请求 5）。请求 5 的提议内容只有转述（这部分为 proxy）；上限 64 与过滤范围由 Agent 决定，未经用户确认 | 停服或写库、推送、PR、实机验收仍须用户亲自确认；本评审不代替这些决定 |

## 结论

`approved`。revision 1 对请求 1–5 及聊天契约移交的黑名单意图承接完整，处置有据，验收可以观察，范围与用户诉求相称，可以进入路由。

本结论只适用于上述路径的 revision 1。Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization 或 Assumptions 有实质修订时，都需要重新评审。采纳建议 1–3 若只调整措辞、不改变范围，按控制方判断是否构成实质修订。
