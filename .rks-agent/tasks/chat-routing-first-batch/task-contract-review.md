---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-chat\.rks-agent\tasks\chat-routing-first-batch\TaskContract.md
contract_revision: 3
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐与路由选择；revision 1、2 也由本评审者评审）
selection_basis: 用户与项目未要求最强评审者；按控制方传入的政策使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：聊天单入口路由与第一批频道（revision 3）

## 权威来源集

- 用户原话 a–f（2026-11-11 同一对话，由控制方逐字转述）。
- 模型第 4 版 HTML：`C:\Users\Gloria\AppData\Local\Temp\claude\3337c86ee32cecf6\35735657-2e51-4ac8-9f8a-22b56156bcc8\scratchpad\chat-model\chat-routing-model.html`。
- 调查方案：`E:\Project\DNF\Document\聊天通信与服务器广播-A21方案-20261111.md`（其中「第一期 GM 公告」已被用户 b 推迟）。
- 仓库政策：`AGENTS.md`、`Document/FeatureDevelopment.md`（第 4、5 节）；工作区 `E:\Project\DNF\AGENTS.md`。
- 协议知识库：`protocol/game/client-to-server/0x0011.md`、`0x01A9.md`；`protocol/game/server-to-client/` 下已有 `NOTI-0x000C.md`、`NOTI-0x0041.md`、`NOTI-0x0076.md`。
- GitHub：#62「[Cycle] 公会系统」OPEN；#61「[Cycle] 聊天与通信」OPEN。

本评审对 revision 3 全文重新评审，不沿用前两个 revision 的结论。

## revision 3 的改动核对

- **私聊改用 NOTI `0x0041`。** 方案文档 1.2 表记录了 A21 `0x0041` handler `01173D70` 的布局（`u8 mode, u8 状态, dstr 名字(≤30), u8 srv, dstr(≤255)`，Confirmed），附录地址表也写明「`0041` 私聊/公会回推」。方案文档 1.1 原来写的是私聊「000C / 0041（错误回执）」。revision 3 依据补查的 70 行为（成功时给目标发 `0x41`）改了这一处，并在 Assumptions 写明依据和证据标签。这只改了投递机制。用户关心的结果是私聊发给在线对象、尽量可达、不报错，SC-3 与 Constraints 第 2 条未变。投递用哪种 NOTI 不是用户亲自定下的事项，模型第 4 版的 mode 对照表也注明「投递」一列只是服务端设计，所以这次修订不越过用户决定。
- **成功应答。** 拒绝回 `0x0011` 失败应答；成功时「按客户端期待处理」，依据是 A21 不等成功应答、70 成功时不回。与 SC-6、SC-10 一致。
- **协议记录范围。** Scope In 与 SC-8 加上 `0x01A9`、`0x0041`，与实际涉及的协议一致。
- **普通发言 mode 的开关说明。** 新增的 Assumption 把 8 和 5（类型 4 地下城）可能出现的情况摆了出来。若实机出现，就按「未映射」拒绝并回到用户决定，没有让 Agent 自行扩大范围，符合「改要求必须问用户」的边界。

## 已核对的根意图处置

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 调查与方案 | not_applicable | 前序已交付，成立 |
| RI-2 公告 | authorized_change | 用户 b 推迟，成立 |
| RI-3 单入口与分流 | in_scope | Goal、Scope In、SC-1/SC-6，成立 |
| RI-4 群组成员建模 | in_scope | 范围服务承接，成立 |
| RI-5 公会人员 | preserved_upstream | #62 是独立 owner，仍 OPEN；GuildOf 与 mode 6 静默留在本契约，成立 |
| RI-6 子类与单参数 MembersOf | in_scope | Constraints 第 1 条，成立 |
| RI-7 禁言、黑名单、限频推迟 | authorized_change | 请求 4 加第 4 版「已定」第 3 条，成立 |
| RI-8 频道／服务器喇叭范围 | in_scope | SC-4、SC-5，成立 |
| RI-9 服务器专用 mode 只拒绝 | in_scope | Scope In 列出完整集合，SC-6 承接，成立 |
| RI-10 私聊尽量可达 | in_scope | SC-3；换 NOTI 不影响这一结果，成立 |
| RI-11 第 4 版已定项 | in_scope | 与 HTML 一致，成立 |
| RI-12 实机验收 | in_scope | SC-9，成立 |
| RI-13 仓库流程政策 | in_scope | Constraints、SC-8/SC-9、Authorization、Requires Confirmation 一致 |

没有重复、遗漏或 `unresolved` 行。Scope Out 交叉核对与 revision 2 相同，各项都有依据。

## 阻塞性发现

无。

## 非阻塞建议

1. **地下城内普通发言要进测试流程。** 第 2 条 Assumption 的 mode 5／8 只有实机才能暴露。建议 SC-9 的编号流程明确写一步「在地下城内用普通频道发言」，以便及时把问题交回用户。否则用户可能把「地下城普通发言被拒」当成缺陷。
2. **成功应答的取舍要写进协议记录。** 「按客户端期待处理」措辞偏宽。实现后在 `0x0011.md` 写清最终做法（不回，或回 ok）及依据，SC-8 已覆盖记录更新。
3. **协议文档的说法要统一。** 更新 `0x0011.md` 时，要统一 u32 字段的说法：现记「目标角色 id」，契约是「喇叭写背包槽位」。更新方案文档引用时，私聊回推一栏也要从「000C / 0041（错误回执）」更正过来，避免两处说法冲突。
4. SC-10 排在 SC-6 与 SC-7 之间，编号不连续，不影响验收。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 对照用户原话、第 4 版 HTML、方案文档 1.1/1.2 与附录、协议页清单；私聊协议的更正有 A21 handler 布局支撑，70 行为标为 70Reference | 可作为可靠的 Spec 输入；私聊发送方回显仍要靠实机确认 |
| efficiency | proxy | 修订只动了投递机制与假设，结果、范围与授权未变 | 不需要重新对齐用户意图 |
| cost | proxy | 协议记录多了两页，没有引入新机制 | 规模合理 |
| cognitive burden | proxy | 新增的两条假设都写明了实机出现偏差时怎么处理 | 实现与验收时决策点清楚 |
| actual human participation | observed | 用户 a–f 的设计决策与授权（由控制方逐字转述）；revision 3 的技术更正没有经过用户，但它不改用户定下的结果 | 实机验收、停服、写库、推送、PR 仍需用户亲自确认；mode 5／8 若出现要交回用户决定 |

## 结论

`approved`：本结论只适用于 revision 3。之后对 Goal、Scope、Root Intent Coverage、Constraints、Success Conditions、Authorization 或 Assumptions 做实质修改，都需要重新评审。
