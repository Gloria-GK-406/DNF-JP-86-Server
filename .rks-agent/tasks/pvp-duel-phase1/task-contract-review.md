---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-pvp\.rks-agent\tasks\pvp-duel-phase1\TaskContract.md
contract_revision: 2
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐与路由选择）
selection_basis: 用户与项目未要求最强评审者；按控制方传入的政策使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：决斗场一期（revision 2）

## 权威来源集

1. 用户原话（2026-11-14，控制方逐字转述）：「我知道了，拍个子代理去看看pk场功能要怎么实现吧」；「用codex cli让astra high审核一下，给出他的看法意见。然后给我一个修改后的版本，以及astra 提了什么意见，你接受了哪些」；「可以，然后先写任务契约后完成一期内容吧」（回复修订版 r1 与「17 条定下来之后就可以开一期」的汇报）。
2. 修订版 r1：`scratchpad/pvp-rev1.md`（本轮重点核对 §3.2、§3.3、§5、§8、§9、§11）；参照调研 `scratchpad/pvp-plan.md`（§二期清单第 91 行、§三期第 168 行）。
3. 仓库政策：`AGENTS.md`、`Document/FeatureDevelopment.md`（§4 第 46、58–61 行）、`Document/server-ci-release.md`。
4. 项目约定：进主干只走 PR，合并、push 前须用户确认。
5. 现场核实：`gh issue view` 81/82/83/84/85/298 均为 OPEN，标题与 r1 分期一致；工作区 HEAD `ef20398a3`（chore: release 0.3.2），仅新增未跟踪的 `.rks-agent/tasks/pvp-duel-phase1/`；`OfferedOn` 调用方 grep 结果与契约一致。
6. 上一轮评审（revision 1，changes_required，F1–F4）。

## 上轮阻断项复核

| 项 | r2 处理 | 核对结论 |
|---|---|---|
| F1 用户实测闭环无验收 | 新增 SC-13：用户反馈、失败修复复测、实测结果与证据路径与未覆盖项记入 `Document/tasks/pvp-duel/`、抓包结论（`0x0037`、`0x0039` 后解除帧、含密码进房、建房/准备是否回 CMD）回写协议页与 catalog 并按实测改证据标签 | 已解决。与 `FeatureDevelopment.md` §4 第 58–61 行逐项对应；规则 13「含密码进房抓包后才算完成」由 SC-13 承接。 |
| F2 规则 9 无验收 | SC-5 补：不发 `0x003A`/`0x0039` 时确认超时后照常推进；延后离场投影按接收者在其 `0x0039`、下一局开局、离房或会话替换时释放，确认超时不释放；迟到 `0x0039` 不作用于下一局 | 已解决。与 r1 §3.3 四条逐条一致（r1 的「房间销毁时全部丢弃」未写入，属实现细节，非阻断）。 |
| F3 城镇 PK 无 owner | Scope Out 单列并注明 70 没有、需单独立项；新增 RI-9 `not_applicable` | 已解决。依据与 `pvp-plan.md` 第 40、52、168 行一致；用户「pk场」指房间制决斗场，城镇 PK 不在本轮意图内，`not_applicable` 有证据。 |
| F4 `OfferedOn` 事实错误 | Current Situation 改为 `Offer`/`Withdraw` 无生产调用方、`OfferedOn` 被两处读取；Scope In 与 SC-1 写明快照改由新 owner 提供 | 已解决。grep 确认 `SelectCharacterLifecycleServices.cs:427`、`SessionAdjacentEntryDomainService.cs:74`、接口 `ISessionPresence.cs:200`。 |

上轮非阻断建议：Assumptions 已补 r1 类名/接缝冲突时的出口条款（以架构约束为不变量、在任务文档记录差异）；SC-12 证据已补「迁移后旧版本无法打开新库时的恢复方式」；Scope Out 已补 `0x0265`。

## 意图处置核对

| Intent | 契约处置 | 核对结论 |
|---|---|---|
| RI-1 调研 | not_applicable（已交付） | 成立。 |
| RI-2 Astra 审核与修订版 | not_applicable（已交付） | 成立。 |
| RI-3 认可 17 条规则 | in_scope | 成立。Constraints 1–16（含 5a/5b）再次逐条比对 r1 §8，建议值一致，无篡改。 |
| RI-4 先写契约 | in_scope | 成立。 |
| RI-5 完成一期 | in_scope | 成立。Scope In 覆盖 r1 §5 协议清单与 §11 落地顺序；验收闭环由 SC-13 补齐。 |
| RI-6 二期、三期 | preserved_upstream → #81、#82–#85 | 成立：均 OPEN；#81 是母 Cycle，仍承接二期；城镇 PK 已移出此行。 |
| RI-7 仓库与项目政策 | in_scope | 成立：独立工作区、证据标签、主干库用户测试、反馈复测与记录（SC-13）、PR 前须用户确认均有落点。 |
| RI-8 跨 NAT | preserved_upstream → #298 | 成立：#298 OPEN。 |
| RI-9 城镇 1v1 PK | not_applicable | 成立，见 F3 复核。 |

无重复、无遗漏、无 `unresolved`。

## Scope Out 交叉检查

- 二期清单与 `pvp-plan.md` 第 91 行一致（含新补的 `0x0265`），由 #81 承接。
- 三期由 #82–#85 承接；城镇 PK 单列为 not_applicable；伤害校验为 r1 §10.4 刻意不做；中继由 #298 承接。
- 推送、PR、合并、版本号：列入 Requires Confirmation，与项目约定和 `server-ci-release.md` 版本规则一致，不构成范围洗白。

## 其余检查

- 授权：任务授权证据与用户原话一致；停服/替换进程、写正在运行的库、push、PR、合并均需确认。无缺口。
- 内部一致性：SC-5 与 SC-8（终局后离开结果不变）不冲突；`0x0037` 先按上游 5 字节、抓包后改为实测长度，与「只接受一种长度」不冲突；SC-13 依赖用户反馈，与 FeatureDevelopment 一致，不由技术评审代替。
- 比例：新增内容均为结果级验收，没有引入多余机制。

## 非阻断建议

- RI-5 的落点写的是 SC-1–SC-12，RI-7 写的是 SC-12；SC-13 已是二者的验收，可顺手把 SC-13 写进这两行，避免读者以为闭环不在 RI 覆盖内。
- SC-13 若因抓包结果需要改变已定规则含义，按 Assumptions 第一条回到用户决定；实测汇报里建议重申 17 条生效值，便于用户发现对「可以」的误解。
- 一期 PR 不要用 `Closes #81` 一类关键字，#81 仍承接二期。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | proxy | 逐条比对 r1 §3.3/§5/§8/§9/§11、`pvp-plan.md` 分期、`FeatureDevelopment.md` §4 与契约；源码 grep 核实 `OfferedOn`；`gh` 核实 issue 状态 | F1–F4 均已正确修复，规则与范围忠实，验收可观察；剩余仅为文字层面的交叉引用小瑕疵 |
| efficiency | proxy | r2 相对 r1 只增补 SC-13、扩展 SC-5、调整一行 Scope Out 与一行 RI、改正一处事实 | 可直接进入结构路由，不影响一期排期 |
| cost | proxy | 新增要求均来自既有政策（反馈复测记录）或已定规则（确认超时），只增加少量自动测试与文档回写 | 不增加实质实现成本 |
| cognitive burden | proxy | 契约约 134 行，分节清楚；SC-12（准备测试）与 SC-13（实测闭环）分开，职责明确 | 读者负担适中 |
| actual human participation | observed | 用户亲自回复「可以，然后先写任务契约后完成一期内容吧」；未见用户逐条表态 17 条规则，也未见用户审阅本契约 | 用户参与限于方向认可；逐条规则确认、实机验收与 push/PR/合并仍需用户，契约以 SC-13 与 Requires Confirmation 保留了这些人工关口，本评审不替代它们 |
