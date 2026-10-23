---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/TaskContract.md
target_revision: 4
verdict: approved
rounds: "Round 1 (revision 3): changes_required; Round 2 (revision 4): approved"
reviewer: independent subagent (fresh context; reviewing-work deliverable + modeling-task-contracts read-only, Create or Revise steps 5-10, Readiness)
---

# TaskContract 复审：serial-gameplay-executor（revision 3）

## 范围

- 目标：`.rks-agent/tasks/serial-gameplay-executor/TaskContract.md`，frontmatter `revision: 3`、`state: ready`，未提交改动；相对 revision 2 的差异按 `git diff HEAD`（HEAD `06ca5e0f`）读取。
- 重点：revision 3 是否只做结构改道（结果 A／B 拆分）；来源意图覆盖；SC 拆分与 A→B 交接；「先 A 后 B」与确认边界是否与用户决定一致；能否保持 `ready`。
- 只审根契约。子契约 A、结果 B 草案、`spec.md`／`plan.md` 的 R3 修订不在本轮（见 `verification/reroute-t008.md` §7、裁定第 8 条）。

## 依据

- `reviewing-work` 的 Deliverable 参考；`modeling-task-contracts` 的 Operation Gate（read-only）、Separate Outcomes from Mechanisms、Create or Revise 第 5～10 步、Readiness。
- 上两轮：`reviews/task-contract-review.md`（r1）、`reviews/task-contract-review-r2.md`（r2，approved）。
- 背景：`verification/reroute-t008.md`（含文末「主控方裁定（2026-10-23）」）、`routing-decision.md` 末尾改道记录、`verification/reroute-t008-spec-002-draft.md`、`spec.md` RQ-091、`tasks/TASK-15.md`、`tasks/TASK-28.md`。
- 用户原话按任务目录内的记录采信，审查者看不到会话原文。

## 发现（按严重度）

### F-1（阻断）用户 2026-10-23 的结构决定没有进入根契约；送审与收尾的写法和它不一致

**位置**：Context「revision 3」一段第 1 条；Root Intent Coverage RI-14、RI-16；Requires Confirmation 第 2 项。

**问题**：

1. 改道的最终依据是用户本人的决定。`verification/reroute-t008.md`「主控方裁定」记录的原话是：「继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出」。据此定为 Multi Spec，两个子 Spec 都在 `task/refactor-session-20261021` 上完成，收尾只推送一次，A 不单独开 PR，Astra 审核放在最后（裁定第 5 条）。根契约里完全没有这句话：Context 说依据「有两条」，RI-14 把拆分只归为主控方依「你来做主吧」作出的决定。来源集合漏了一条有约束力的用户决定。
2. 契约里描述送审与收尾的文字仍然是裁定之前的提案：
   - Context：「其余部分在 T-007、T-009 之后收口，交外部审核；审核通过前不推送、不开 PR」。拆分理由里还有一句「外部审核与分支收尾都要等 T-008 全部做完」，可是按用户的决定，收尾本来就要等 B 做完。
   - RI-16：「主控方按授权把第一次送审的对象定为结果 A」。裁定第 5 条的默认安排是 Astra 放在最后审；先给 Astra 看 A 只是用户可选的做法。
3. Requires Confirmation 第 2 项只写了「等用户告知 GPT Astra 的审核已经通过」，没有写「A、B 都封存之后一次收尾」。如果用户选择先让 Astra 看 A，这一条照字面读，A 审核通过就可以推送、开 PR、收尾。这与「同一分支、一次收尾、A 不单独开 PR」直接矛盾。RI-12 里的「改道不改这条约定」只是理由栏里的一句话，压不住确认边界的字面意思。

**后果**：A 封存后，控制器、外部审核者或用户如果按根契约行事，可能把 A 单独送审、等 A 的结论再开始 B，或者在 A 审核通过后就推送、开 PR。这些都违背用户已经作出的决定。根契约是后面各层的依据，子契约 A 与 B 草案的 RI-16 也照抄了「第一次送审的对象就是本结果」。

**最小修法**（只改措辞，不改授权与结构）：

- Context revision 3 一段补上用户 2026-10-23 的原话及其含义：Multi Spec，同一分支，A、B 都完成后一次收尾，A 不单独推送或开 PR，Astra 默认审最终结果。改正「交外部审核」和「分支收尾都要等 T-008」两处理由。
- 在 RI-14 的来源中写入这句原话（或者新增 RI-17，处置 `in_scope`，落点为 Constraints／Requires Confirmation）。每条意图仍只出现一次。
- RI-16 的理由改为与裁定第 5 条一致：Astra 默认审最终结果；用户可以先让 Astra 看 A，但这不触发收尾。
- Requires Confirmation 第 2 项改为：A 与 B 都封存，并且用户告知 Astra 审核通过之后，才推送、开 PR、合并与分支收尾；只推送一次，A 不单独开 PR。同时注明这些动作在本契约的授权之外（Scope Out 第 6 项、Authorization），由用户按 AGENTS.md 明确要求「分支收尾」后进行。

### F-2（非阻断）B 的成功条件没有明说 A 的结果在 B 封存时仍然成立

**位置**：SC-8B、SC-13；RI-5 的理由栏。

**问题**：SC-1、SC-3～SC-7（执行器唯一、发件箱语义、无门、事件可靠性、深度上限覆盖全部来源、投递结果承接）只有 A 一行。B 封存时它们是否仍成立，只能间接推出：Constraints 写了「A 建立这些判据，B 不得削弱」和「各自对自己改动所涉及的保护负责」，SC-8B 要求全量测试通过、A 的表征测试与线级测试断言不变。但 SC-8B 允许 B 改动其余测试并逐条说明，所以事件分发、深度上限这类非线级测试即使被改弱，也只是记录下来，不会阻断。RI-5 写「SC-5 的语义要继续成立（SC-8B）」，可 SC-8B 并不包含 SC-5。

**后果**：B 引入新的 CMD 来源，例如协议级 CMD 的开始点。这时 SC-6 的「覆盖全部 CMD 来源」、SC-3 的「只在提交后发出」可能被悄悄放宽，验收却挡不住。风险中等，可以检测，也可以回退，所以不阻断。

**建议**：在 SC-8B（或新增 SC-14，结果 B）写明：A 的 SC-1、SC-3～SC-7 在 B 封存状态下仍成立，对应的判据与测试照常通过，断言不放宽。B 的 Spec 列明的可见变化除外。

### 残余风险（不是本契约的缺陷，交主控方）

- 裁定第 5 条写着「Astra 发现的问题在 A 上追加修正提交」。A 封存后不可重开（multi-spec／reviewing-work：sealed terminal Spec 不复活）。Astra 对 A 的意见应当作为新的子 Spec 处理，或者并进 B，并触发 `routing-decision.md` 的再改道条件。根契约目前对此没有表态，这样是对的；修 F-1 时不要把「在 A 上追加修正」写进根契约。
- `routing-decision.md` 改道记录「理由」一节也写着「A 的外部审核与收尾要等 B 全部做完」。按用户的决定，这一条不再是拆分理由，建议与 F-1 一起改正（不在本轮审查对象内）。子契约 A 第 79 行、结果 B 草案第 80 行的 RI-16 要随 F-1 同步改。

## 已核实无问题项

1. **只做结构改道，授权未扩大**：
   - Authorization 一行与 revision 2 逐字相同。
   - Source Request（第 11～30 行）不在任何 diff hunk 内，原文保留。
   - 基准 `a02bce46` 时的 Current Situation 原样保留，新增的「修订时的进度」另起一节。
   - Goal 首段四段原文保留，只追加 A／B 两个结果的说明。
   - Scope Out 第 1～6 项不变，第 7 项（A 不承担 B）只收窄边界。
   - revision 只递增一次（2→3），审查 r2 与下游路由据此失效，改道记录也标为提案、待审。
2. **revision 2 的 Scope In 无遗漏**：逐项对照，每项都落在 A 或 B，没有两边都不管的：
   - 「协议级 CMD」整项、「handler 只解包入队／协议级 CMD 注入面」判据、「假总线」测试、handler 规则文档，移入 B；
   - 「保留并迁移」拆成 A「保留」与 B「迁移保留项」；
   - 其余各项留在 A；
   - 吞吐测量与文档两边各有一份。
3. **RI-1～RI-16**：
   - 编号唯一，每条恰好一次；
   - 处置只用 `in_scope`／`authorized_change`，没有 `unresolved`，没有 `preserved_upstream`。根契约同时承担 A 与 B，用 `preserved_upstream` 本来就不合适。
   - rev 2 的 RI-1～RI-13 一条不少，原意未改；RI-5、RI-11、RI-12 只补了改道后的落点。
4. **RI-2 归 B 成立**。用户原话「CMD直接把其他的都吸收了。命令变成唯一实体。不过这里的命令含义就是以协议为单位的了」，是对上一句「CMD内部再根据channel做分流」的纠正，讲的是编程模型：以协议为单位，handler、投递、补偿都并入 CMD。执行层面的串行单位已由 RI-3（push CMD 的执行器）承担，在 A 里完成。所以 RI-2 的实质结果只在 B 中兑现，落点是 Scope In B「协议级 CMD」「删净过渡机制」、SC-2、SC-13。它在根契约中仍是 `in_scope`，没有被 Scope Out 抹掉（Out 第 7 项只约束 A）。
5. **RI-14～RI-16 有依据**：
   - RI-15 的两句原话见 `spec.md` RQ-091「用户修订」、`tasks/TASK-15.md:982`、`tasks/TASK-28.md:74`。
   - RI-14、RI-16 的原话「你来做主吧，结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。」见 `routing-decision.md:62`、`verification/reroute-t008.md` 第 5 行。这两处都是主控方写的，任务目录内没有更早的独立记录，按采信规则接受。
   - 三条都用 `authorized_change` 或 `in_scope`，并写明授权来源；RI-15 把原阈值降为对照值，与 RQ-091 的用户修订一致。
   - 缺的是用户 10-23 的那句决定，见 F-1。
6. **Scope Out 交叉**：
   - 第 1 项对应 RI-4，第 2 项对应 RI-8，第 3 项对应 RI-9，第 6 项对应 RI-12，第 7 项对应 RI-2，各有处置；
   - 第 4、5 项不与任何来源意图冲突，与 r2 相同。
7. **SC 拆分**：
   - 每行都有「结果」列和可观察的证据；
   - A 为 SC-1、SC-3～SC-12，B 为 SC-2、SC-8B～SC-11B、SC-13，与 `reroute-t008.md` §2 一致；
   - SC-9／SC-9B 按 2 秒接受线判定，有测量记录作证据。
8. **SC-12 与 SC-13 的交接完整，A 的封存不依赖 B**：
   - Goal 结果 A 与「修订时的进度」点名的过渡机制有四项：handler 在作业内编排细粒度命令、`SendAsync` 内联执行、请求级帧走 AsyncLocal、细粒度命令及其管线（0、45、58、60、100 五层）。这四项都要求列入 SC-12，并写明位置、存在理由与 B 的删除方式。
   - SC-13 要求 SC-12 的清单在 B 封存时检索为零，B Scope「删净过渡机制」不留转发层或 `[Obsolete]`，SC-10B 移除 A 记下的已知缺口。
   - A 的已知缺口由 SC-10 写成今天的状态，再由 SC-10B 清除，因此 A 留下的过渡态全部有接收方。
   - SC-12 的证据是清单与代码检索，SC-11 只要求 A 的最终汇报作为审核输入，两者都不以 B 的工作或 Astra 的结论为前提。Constraints「先 A 后 B」与 Scope Out 第 7 项也明确了这一点。
   - SC-12、SC-13 落实的是 revision 2 已有的约束「一次到位、删净」，不是新加的机制约束。
9. **「先 A 后 B」**：Constraints 写明 B 在 A 封存后开始，以 A 的提交状态为起点。这与裁定第 5 条一致：B 在 A 封存后开始，不等 Astra 对 A 的结论。
10. **Readiness（除 F-1 外）**：
    - Goal 与 Scope 无歧义；
    - 一条 task 授权可追溯，覆盖 A 与 B 两个结果。A、B 都在原授权的 Scope 内，不需要扩大授权；
    - 每行 SC 都有证据；
    - 新增的 Assumption 第 3 条（A 可独立运行、独立审核）是可见的前提，并且有再改道条件兜底；
    - 没有 Open Questions，没有 `unresolved`；
    - 根契约没有 child 字段，这是正确的。

## 结论

`changes_required`。阻断项只有 F-1：根契约没有记下用户 2026-10-23 的结构决定（同一分支、一次收尾、A 不单独开 PR、Astra 默认放在最后），Context、RI-16 与 Requires Confirmation 第 2 项的措辞和这个决定不一致。修法只动措辞，不改授权，不改 A／B 边界，不改 SC 结构。F-2 建议一并补上。F-1 修好、revision 递增后，根契约可以保持 `state: ready`；复审只需核对 F-1（以及 F-2，如果采纳）的改动和这些改动直接带来的回归。

---

## Round 2（revision 4）：approved

```yaml
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/TaskContract.md
target_revision: 4
verdict: approved
```

### 范围

- 定点复审。只核对 Round 1 的 F-1、F-2、两条残余风险，以及这些修正直接带来的影响。
- 修正落点按 `verification/reroute-t008.md` 文末两节读取：「主控方处置（三路审查第 1 轮之后，2026-10-23）」和「第 1 轮审查发现的落点」。`routing-decision.md` 的改道记录也一并核对。
- 未改动的部分沿用 Round 1 的结论，不重审。

### 逐项核对

| Round 1 项 | revision 4 的落点 | 结论 |
|---|---|---|
| F-1 ①来源集合漏记用户 10-23 的决定 | Context「revision 3」一段：依据改为三条，第 2 条引用原话「继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出」，并写出它的含义：Multi Spec、同一分支、A 与 B 都封存后一次收尾、A 不单独推送也不单独开 PR、Astra 默认放在最后审、先看 A 时怎样承接。RI-14 的来源同时列出 10-22、10-23 两句原话，落点加了 Requires Confirmation 第 2 项。 | 已解决。原话与 `reroute-t008.md`「主控方裁定」、`routing-decision.md:62` 一致。RI-14 仍只出现一次，处置 `authorized_change` 合法 |
| F-1 ②两处过时理由 | 删去了「交外部审核；审核通过前不推送、不开 PR」；「外部审核与分支收尾都要等 T-008」改为「A 的验收与封存要等 T-008」 | 已解决。契约里已经没有「A 单独送审」或「A 先收尾」的写法（全文检索「外部审核」「收尾」「送审」） |
| F-1 ③RI-16 | 理由改为：Astra 默认在 B 完成后对整个分支审一次；用户先让 Astra 看 A 不触发收尾；Astra 对 A 的意见作为新的修正子 Spec 处理，或在不跨边界的前提下并进 B，封存后的 A 不重开 | 已解决。与「主控方处置」对裁定第 5 条的更正一致 |
| F-1 ④Requires Confirmation 第 2 项 | 改为：A、B 以及可能出现的修正子 Spec 都封存，并且 Astra 审核通过之后，才推送、开 PR、合并、收尾；只推送一次，A 不单独开 PR；这些动作不在本契约的授权内，要等用户按 AGENTS.md 明确要求「分支收尾」 | 已解决。字面上不会再被读成「A 审核通过就可以收尾」。与 Scope Out 第 6 项、Authorization 的排除项一致，授权没有扩大 |
| F-1 附带 | RI-12 的理由补了一句：10-23 的决定再次确认一次收尾；Constraints「先 A 后 B」补「不等外部审核；A 封存之后不重开」 | 与用户决定、裁定第 5 条一致 |
| F-2 | SC-8B 写明：B 封存时 A 的 SC-1、SC-3～SC-7 仍然成立，对应判据与测试照常通过，断言不放宽；证据栏加「逐条对照」 | 已解决。RI-5 理由栏引用 SC-8B 守 SC-5，现在有了实际依据 |
| 残余风险：「在 A 上追加修正」 | 根契约 Context、RI-16、Requires Confirmation 都写成「新的修正子 Spec，或并进 B；封存后的 A 不重开」。裁定第 5 条原句在 `reroute-t008.md` 中标为作废。子契约 A（revision 2）第 93 行与 B 草案第 87 行的 RI-16 已同步 | 已消除 |
| 残余风险：改道记录的理由 | `routing-decision.md`「理由」一节改为「A 的验收与封存要等 B」「每一次审查的对象都过大」，并写明收尾不因拆分而提前；补了 10-23 原话；再改道条件改为「Astra 对 A 的意见既不能作为修正子 Spec，也不能并进 B」 | 已消除 |

### 修正带来的直接影响

- **「修正子 Spec」是新出现的可能结构**。它只承接 Astra 对 A 的意见，没有超出根契约的 Goal 与 Scope，所以仍在原 task 授权内，不需要扩大授权。它是否出现、编号多少、边界在哪，由主控方届时判定，并写进自己的子契约。根契约不需要为它预设 SC 行。Requires Confirmation 第 2 项已经把它列入收尾前必须封存的对象。
- **Readiness 仍然成立**：
  - Source Request、Goal 首段、Scope In／Out、Authorization 都未被本轮修改；
  - RI-1～RI-16 每条恰好出现一次，没有 `unresolved`；
  - 每行 SC 都有可观察的证据。SC-11 的证据改为「以 A 最终汇报中的复述为证据」，与裁定第 7 条一致；
  - 契约头写着 revision 4，`state: ready`，没有 child 字段。
- **新的阻断问题：无。**

### 非阻断意见（可选）

- Assumptions 第 3 条写的是「可以独立运行、独立审核」。这里的「独立审核」指 A 自己的验收审查，不是 Astra 审核。想彻底消除歧义，可以改成「独立验收」。不改也不影响任何决定。
- `reroute-t008.md` §8 第 5 条和 §9 第 1 条正文里，仍留着裁定前的建议（「等 Astra 对 A 的结论再开始 B」「先审 A」）。两处都已加注「已由……定下」。这是带日期的过程稿，保留原文符合 AGENTS.md「过期的记载怎么办」的规则，不需要改。

### 结论

`approved`。F-1、F-2 和两条残余风险都已解决，修正没有带来新的缺陷，也没有扩大授权。根契约 revision 4 可以保持 `state: ready`。改道记录可以按 `routing-decision.md` 的生效步骤绑定本审查，`target_revision: 4`。
