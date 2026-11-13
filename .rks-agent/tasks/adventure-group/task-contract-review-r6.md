---
kind: rks-task-contract-review
contract_path: E:\Project\DNF\DNF-JP-86-Server-adventure\.rks-agent\tasks\adventure-group\TaskContract.md
contract_revision: 6
reviewer: Claude Opus 5.5 子代理（全新上下文，未参与建模、意图对齐、路由选择，也未参与 revision 5 及更早的评审）
selection_basis: 用户与项目政策没有要求最强评审者；按控制方要求使用新鲜、独立、胜任的评审者
verdict: approved
---

# TaskContract 评审：冒险团按账号派生、通关经验加成与升级推送（revision 6）

## 权威来源集

- 契约 Source Request 第 1–4 项的用户原话。拿不到对话原文，以契约引文和控制方给出的背景为准。第 4 项是本轮新增：
  - 选人界面右侧应显示冒险团；
  - buff 栏应有冒险团 buff；
  - 每次登录都出现冒险团升级提示；
  - 用户选择「这次一起修」。
- revision 5 的评审 `task-contract-review.md`（approved），只作背景。revision 6 改了 Goal、Scope In、Root Intent Coverage、SC、Authorization 与 Requires Confirmation，属于实质修订，原批准不能沿用，本评审独立判断。
  - 原评审的 `contract_path` 指向主仓 `E:\Project\DNF\DNF-JP-86-Server\...`。本次绑定的是任务工作区 `DNF-JP-86-Server-adventure`。它是同一仓库的 worktree，分支 `feature/adventure-group`。
- `git diff HEAD` 中 TaskContract 的修订内容（revision 5 → 6，未提交）。
- 工作区里未提交的配套代码，用来核对契约描述与实际改动是否一致：
  - `GameMercenaryInfoCommand.cs`：`0x01BA` 应答改为按 `IOnlineUserQueryDomainService` 取已登录账号，不再按已选会话取；
  - 对应的 `LightweightProtocolCommandFlowTests.cs`；
  - `protocol/catalog.md` 的一行措辞。
- `Document/outputs/notice-flags-20260924/investigation.md` 第 3 节：
  - 当时服务端没有发出任何 `01A0`，用户仍看到登录后底部出现「冒险团已升级」；
  - 列出了候选原因：管理器首次创建时生成 `Mercenary_levelup.act` 实体，这个实体是否默认播放尚未证明；
  - 「底部冒险团升级动画另案处理」。
- 仓库政策：
  - 服务端仓 `AGENTS.md`；
  - `Document/FeatureDevelopment.md` §4；
  - `protocol/evidence.md`。工作区 `AGENTS.md` 的约定沿用上一轮。

## 根意图核对

| Intent | 处置 | 核对结论 |
|---|---|---|
| RI-1 至 RI-10 | 同 revision 5 | 内容没有变化，处置仍然成立 |
| RI-11 选人界面右侧显示、buff 栏显示、登录不再误报升级、「这次一起修」 | in_scope | 成立。三项诉求各有落点：<br>• 选人界面与 buff：Scope In 中 `0x01BA` 一条补了「包括选人界面（尚无已选角色）的询问」，对应 SC-3 字节断言与 SC-9 实机反馈；<br>• 登录误报：Scope In 新增「登录升级提示」条目，对应 SC-10；<br>• 「这次一起修」：写入 Authorization 证据列。<br>工作区代码改动（按已登录账号应答）与契约描述一致 |

没有重复或遗漏的意图，没有 `unresolved` 行，也没有 `preserved_upstream` 行。用户先前对 subtype2 写实值的试验已撤回，契约在 Current Situation 中如实记录，Scope Out「subtype2 写非零」保持不变，二者一致。

## Scope Out 交叉核对

- 新诉求与 Scope Out 没有冲突：
  - 选人界面显示靠 `0x01BA`，不靠 subtype2 头部；
  - buff 由客户端按等级显示，不涉及「服务端计入四维」的禁止项；
  - 登录提示的修正限定在服务端。
- 如果修正必须改客户端（ClientHack 是另一个仓库，不在 Authorization 范围内），Requires Confirmation 新增的一条会把处理方式交给用户决定，没有被悄悄扩权。investigation 第 3 节的候选原因正好指向这种可能，这个出口是必要的。

## 阻塞性发现

无。

## 非阻塞建议

1. **SC-5 与 SC-10 的措辞。** SC-5 的证据列写着「登录后本来就会出现的『冒险团已升级』字样不算证据」，这是以误报会继续存在为前提写的。SC-10 修好后，这句话就过时了。建议改成「登录时出现的升级字样不算 SC-5 证据」，或者注明「SC-10 修正前」。语义没有冲突，不影响路由。
2. **RI-10 的落点列。** SC-10 也需要用户按编号流程实测反馈，建议把 SC-10 加进 RI-10 的 SC 列。RI-11 排在 RI-10 前面，只是顺序问题。
3. **SC-10 实测覆盖面（在测试流程中写明即可）。** 这个现象在冒险团恒为 0 时就存在。修正方式可能与账号的冒险团等级有关，SC-10 的实测建议覆盖：
   - 一个冒险团等级 > 0 的账号；
   - 一个 0 级账号（例如新账号）；
   - 「返回选人界面后换角色进入世界」，确认它是否也算「登录进入世界」。
4. **修正与 SC-9 的回归。** 如果登录误报与选人界面或进城时的 `0x01BA` 应答有关，修正时可能会动到让 SC-9 显示出来的那条应答。建议编号测试流程在 SC-10 修正后重跑 SC-9 的选人界面与 buff 两项。如果服务端修正和 SC-9 的显示不能兼得，属于 Requires Confirmation 最后一条的同类情形，应回到用户。
5. **取证方式的边界。** SC-10 允许使用客户端运行时证据。如果需要在用户的客户端目录里挂调试器或改 `ClientHack.ini`，这些操作超出了服务端仓的授权，按工作区惯例应先向用户说明。只做静态分析（正在进行的那条）不受影响。

## 五维评估

| 维度 | 证据类型 | 证据 | 后果 |
|---|---|---|---|
| quality | observed | 我核对了以下各项：<br>• revision 6 的 diff，新增诉求逐条都有 Goal、Scope、SC 落点；<br>• 工作区未提交代码与 Scope In 的 `0x01BA` 描述一致；<br>• investigation 第 3 节支持「服务端无 `01A0` 仍出现提示」和「原因未明」，也说明修正可能落在客户端，契约为此留了用户决定的出口；<br>• SC-10 的结果和反例都可以观察。<br>只有非阻塞的措辞与覆盖面建议 | 可以作为后续 Spec 修订与执行的可靠输入 |
| efficiency | proxy | 用户明确选择在本任务中一起修，没有另开任务；`0x01BA` 的改动只是把查询来源从已选会话换成已登录账号 | 改动面小。登录误报的工作量取决于静态分析结果，契约用结果条件加确认出口限定了范围 |
| cost | proxy | 新增工作：<br>• 一项原因调查（静态分析已在进行，可能还需要运行时取证）；<br>• SC-10 的两轮重登实测；<br>• SC-9 新增的两项观察。<br>如果原因在客户端内部，可能白跑一轮调查，但会在 Requires Confirmation 处止损 | 成本可控，最大的不确定性有出口 |
| cognitive burden | proxy | 契约把三条实机发现写进 Current Situation，并区分了已证实的（`0x01BA` 在选人界面询问、按账号应答后两处都显示）、已排除的（subtype2）和未查明的（登录误报）。只有 SC-5 的一句措辞与 SC-10 略有张力 | 执行者容易理解，修正建议 1 后更清楚 |
| actual human participation | proxy | 用户在实机中的反馈与「这次一起修」都只以契约引文和控制方背景的形式出现，我拿不到对话原文。留给用户的事项：<br>• SC-4a、SC-5、SC-9、SC-10 的实机验收；<br>• 原地迁移、停服、改角色进度；<br>• `0x01A0` 降级；<br>• 登录误报无法由服务端修正时的处理；<br>• 合并与 PR | 本评审不替代这些人工决定与实机反馈。用户明确反馈通过前，不得声称实机验收通过 |
