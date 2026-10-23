---
kind: rks-review
target: .rks-agent/tasks/serial-gameplay-executor/attempts/spec-001-executor-outbox-lock-removal/TaskContract.md
target_revision: 2
parent: .rks-agent/tasks/serial-gameplay-executor/TaskContract.md
parent_revision: 4
verdict: approved
rounds:
  - round: 1
    target_revision: 1
    parent_revision: 3
    verdict: changes_required
  - round: 2
    target_revision: 2
    parent_revision: 4
    verdict: approved
reviewer: independent subagent (fresh context; reviewing-work deliverable + modeling-task-contracts read-only/readiness + multi-spec-workflow child shape)
---

# 子契约审查：spec-001（结果 A），revision 1

## 范围与依据

- 目标：`attempts/spec-001-executor-outbox-lock-removal/TaskContract.md`，`revision: 1`、`state: ready`，未提交（sha256 前缀 `89a58012`）。
- 父契约：`TaskContract.md` revision 3，未提交（sha256 前缀 `1176dc63`）；worktree HEAD `06ca5e0f`。
- 背景：`verification/reroute-t008.md`（含文末「主控方裁定（2026-10-23）」）、`routing-decision.md` 末尾改道记录、`plan.md` 修订记录 R3、`tasks/TASK-01～28.md`、`git log a02bce46..06ca5e0f`。
- 准则：`reviewing-work`（交付物）；`modeling-task-contracts` 的 Create or Revise 第 5～9 步与 Readiness；`multi-spec-workflow` 的子契约形状（`parent`／`contributes_to`／`depends_on`、确定路径）。
- 只读审查，本文件是唯一写入。根契约 rev 3 的审查（`task-contract-review-r3.md`）尚不存在，本审查不替代它。

## 结论

`changes_required`。结构、边界、收编与 RI-2 的处置都成立；有三处需要在 `state: ready` 之前改掉，都是小改。

## 发现（按严重度）

### F-1（阻断）RI-13 被归为「根契约流程项」而漏处置

- 位置：Root Intent Coverage 表后一段：「根契约的 RI-10～RI-14 管的是整个任务的流程、授权与结构……由根契约承担，本契约不重复处置」。
- 问题：RI-13（#182 统一加锁原语、#204 两塔并发声明的死锁陷阱）不是流程或授权意图。根契约把它的位置写作「结果 A：Goal；SC-4」，`reroute-t008.md` §2 的分配表也把 RI-13 归给结果 A。子契约既没有处置它，又给了一个与事实不符的归类。
- 后果：A 的封存验证与最终汇报不会去证明「#182 原先的加锁原语建议被执行器方向替代、#204 的陷阱随锁消失」。根收尾时更新 issue 依赖的正是这条结论，到时就没有 A 的证据可引。
- 修法：加一行 `RI-13 | authorized_change | Goal；SC-4 | 同根契约；issue 更新属根收尾`，并把表后那段改成「RI-10、RI-11、RI-12、RI-14」。

### F-2（阻断）RI-16 的理由与主控方裁定第 5 条相反

- 位置：RI-16 行，理由列「第一次送审的对象就是本结果」。
- 问题：裁定第 5 条写的是：B 在 A 封存后就开始，不等 Astra 对 A 的结论；两者同一分支、一次收尾，Astra 审核放在最后；A 封存时只向用户汇报一次。`reroute-t008.md` §2 也把 RI-16 归给根契约本身。子契约仍沿用改道提案阶段「先审 A」的说法。
- 后果：执行方可能把「Astra 审核通过」当成 A 封存或 B 开始的前置，与已定结构冲突。它也会把根级的收尾门（Requires Confirmation 第 2 项）读成 A 自己的结果义务。
- 修法：按裁定改理由，例如「A 的最终汇报是外部审核的输入之一（SC-11）；审核在 B 完成后一次进行，收尾门属根契约 Requires Confirmation 第 2 项（主控方裁定第 5 条）；A 不推送、不开 PR」。在 Context 里引用用户 2026-10-23 的原话（「继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出」），说明 Multi Spec、同一分支、一次收尾的依据。
- 附注（不在本目标内）：根契约 rev 3 的 RI-16 理由「主控方按授权把第一次送审的对象定为结果 A」有同样的问题，根契约也没有吸收 2026-10-23 的用户决定。这两点交给根契约 r3 审查一并处理。

### F-3（阻断，修在根契约或在子契约里明示）SC-11 的证据有一半已经拿不到

- 位置：SC-11 行，证据为「根契约 SC-11」，根契约同号行的证据是「规划停点汇报；A 的最终汇报」。另有总述「两处不一致时以根契约为准」。
- 问题：`reroute-t008.md` §8 第 7 条已经查明，任务目录里没有 rev 2 规划停点的落盘记录。裁定第 7 条的处理是「在 A 的最终汇报中复述，不追补历史记录」。可是根契约 SC-11 和子契约都没有据此改写证据。
- 后果：A 封存时用新证据做验证，SC-11 要么判不过，要么要临场重新解释它的证据。这正是成功条件要求「可观察、证据可行」时要排除的情况。
- 修法：首选在根契约 rev 3 的 SC-11 证据里注明「规划停点一项以 A 最终汇报中的复述为证据（主控方裁定第 7 条）」，子契约照引。如果根契约不改，子契约的 SC-11 证据要写明这个替代，并把「以根契约为准」的总述对这一行设一个明确例外。

### N-1（非阻断）路径覆盖整目录归 A，但目录里混着根级和 B 的文件

- 位置：Current Situation 最后一条：`../../spec.md`、`../../plan.md`、`../../tasks/`、`../../reviews/`、`../../verification/`。
- 问题：`reviews/` 里有根契约的审查（`task-contract-review*.md`，将来还有 r3），`verification/` 里有 `reroute-t008*.md` 和 B 的草案 `reroute-t008-spec-002-draft.md`。按裁定第 4 条，B 的草案要在 A 封存后移出。`verification/follow-ups.md` 里也有归 B 的 11 项。A 封存后它的产物不可变，B 再动这些文件，审计时就分不清是改了已封存的 A，还是 B 自己的工作。
- 建议：在子路由决定的「Artifact path overrides」里列出这几个目录中不属于 spec-001 的文件（根契约、根路由、改道汇总、B 草案、根审查、`research/`），并写明 A 封存后 B 新的跟进事项记在 `attempts/spec-002-…/` 下，不改 A 的 `follow-ups.md`。契约层面现在这样写已经足够找到产物，不必改契约。

### N-2（非阻断）「一次到位、删净」的本地解释可能读得比根契约窄

- 位置：Constraints 第 1 条：「只对 Spec 附录 F『删除』栏与本结果引入后又不再需要的东西成立；过渡机制按 SC-12 列明」。
- 问题：「只对……成立」可以读成「附录 F 删除栏之外的旧门、旧管线行为不用删，也不用上清单」。根契约 SC-12 写的是「清单之外，A 没有留下旧门、旧管线行为、转发层或 `[Obsolete]`」。
- 建议：改成「附录 F『删除』栏各项，以及 A 引入后不再需要的东西，在 A 内删净；其余仍在的旧机制必须进入 SC-12 清单，不在清单上就违反 SC-12」。

### N-3（非阻断）Scope Out 的子列表比标题窄

- 位置：Scope Out 第 1 条，「根契约『In — 结果 B』的全部各项」下的七个子项。
- 问题：子列表没有写 B 的测试改写（假总线加直接 new handler 的用例）、B 的测量、B 的文档、「迁移保留项」。标题已经说「全部各项」，所以不构成漏项。
- 建议：子列表前加「包括」，或补全这几项。

## 已核实无问题的项

1. **frontmatter**：`parent: ../../TaskContract.md` 从 `attempts/spec-001-…/` 解析到根契约，正确。`contributes_to` 是 SC-1、SC-3～SC-12，与根契约「结果」一栏标 A 的 11 行完全一致。`depends_on: []` 正确：A 没有未完成的外部前置契约。路径和编号符合 `spec-NNN-<kebab>` 的确定形状。
2. **边界**：Goal 与 Scope In 逐项对应根契约的「结果 A」和「In — 结果 A」，没有超出。Authorization 沿用根契约的 task 一行，范围收窄到 A 的实施、验证与检查点提交，不含合并、推送、PR、issue、部署和清理，没有越权，也没有另立授权。
3. **RI-2 的 `preserved_upstream` 合规**：所有者是父契约 rev 3 的结果 B（SC-2、SC-13），具体、仍然有效，而且不是本契约。根契约明写「A 只让执行层面的串行单位成立，编程模型上的吸收全部在 B」，执行层面那一半由本契约的 RI-3／SC-1 承担。A 交出的只是 SC-12 清单作为 B 的输入，没有借这条推掉自己的结果义务。
4. **其余 RI**：RI-1、3、5、6、7、9 是 `in_scope`，RI-4、8、15 是 `authorized_change`，位置都指向本契约的 SC 或根 Scope Out，与根契约一致，没有 `unresolved`。RI-10、11、12、14 是流程、授权和结构意图，留在根契约承担，符合「不引入无关根义务」。RI-13 除外，见 F-1。
5. **SC 摘要**：11 条摘要都与根契约同号行一致，「以根契约为准」写得明确。A 的封存不依赖 B：Constraints 写明「验收不以 B 的任何工作为前提」，SC-12 是 A 自己产出的清单，SC-11 只要求 A 的汇报作为输入，不要求审核结论（解释按 F-2 修正）。
6. **SC-12 的定义**：根契约 SC-12 要求每项写位置、存在理由和由 B 删除的方式，并检索「清单之外没有残留」。根 Goal 已经列出已知的四项（handler 在作业内编排细粒度命令；`SendAsync` 在执行器上内联执行；请求级帧以 AsyncLocal 在作业内设置；细粒度命令及其 0／45／58／60／100 管线）。定义可观察，可检索。
7. **收编与已提交证据**：
   - `git log a02bce46..06ca5e0f` 实为 31 个提交，提交信息全部以 `serial-gameplay-executor:` 开头：规划 `b76504bb` 1 个，修订 `c3115f9a`、`5f7dba11` 2 个，TASK-01～TASK-28 共 28 个。1 ＋ 2 ＋ 28 ＝ 31，与 Assumptions 一致。
   - `plan.md` 修订记录 R3 记 T-004、T-006 完成、M-2 通过，T-007 不变，T-009 收窄。这与子契约所说「剩下 T-007 与 T-009」一致。
   - Spec 基准 `a02bce46`、规划检查点和 HEAD 都写明了。「证据原样有效，不需要重做」没有重开任何已完成的结论。
   - 收编带来的三处偏离由主控方裁定第 2、3 条在映射中登记，不属于契约层面的事。
8. **Readiness 的其余项**：没有 Open Questions，Assumptions 可见，没有把实现机制升级成硬约束，SC 行都有证据形式（SC-11 见 F-3）。

## 改完之后

F-1～F-3 改完后，状态可以保持 `ready`。按 `modeling-task-contracts` 第 10 步，这是对 Root Intent Coverage 和 SC 证据的实质修订，revision 要递增到 2，并重新审查一次（只审 F-1～F-3 及其直接影响即可）。如果 F-3 选择修在根契约里，子契约要等根契约 r3 审查定稿之后再复核引用。

---

## Round 2 — verdict: approved

```yaml
kind: rks-review
round: 2
target: .rks-agent/tasks/serial-gameplay-executor/attempts/spec-001-executor-outbox-lock-removal/TaskContract.md
target_revision: 2
parent_revision: 4
verdict: approved
```

### 范围

- 定点复审，只看三类：第 1 轮的 F-1～F-3、N-1～N-3；父契约 SC-11 的证据改写；子契约对根契约审查定稿的引用。另看这些修正带来的直接影响。
- 目标：子契约 revision 2（sha256 前缀 `6894d41f`），`state: ready`。
- 父契约：revision 4（sha256 前缀 `d3d1bc4d`）。
- 落点对照：`verification/reroute-t008.md` 的「主控方处置」「spec-001 子路由决定的待落地内容」「第 1 轮审查发现的落点」三节。
- 未重读的部分沿用第 1 轮「已核实无问题的项」：frontmatter、边界、RI-2、收编、提交计数。这些部分本轮没有改动。

### 第 1 轮发现的处置

| 发现 | 结论 | 核实依据 |
|---|---|---|
| F-1 RI-13 | 已解决 | 新增 RI-13 行，处置为 `authorized_change`，落点为 Goal、SC-4，理由与根契约 rev 4 的 RI-13 行一致，issue 更新归根收尾。表后一段改为「RI-10、RI-11、RI-12、RI-14」，现在每条根 RI 只出现一次 |
| F-2 RI-16 | 已解决 | 理由改为：「输入之一」；审核默认在 B 完成后一次进行；先看 A 时 A 照常封存、不重开；审核结论不是 A 封存或 B 开始的前提；收尾门属根 Requires Confirmation 第 2 项。这与「主控方处置」更正后的第 5 条及根 rev 4 的 RI-16 一致。Context 与 Authorization 都引用了 10-23 原话。Constraints 第 2、3 条也同步改了 |
| F-3 SC-11 证据 | 已解决（修在根契约） | 根 rev 4 的 SC-11 证据改为「A 的最终汇报。规划停点一项以 A 最终汇报中的复述为证据」，并注明出处（裁定第 7 条）。子契约 SC-11 的摘要与证据栏照引，「以根契约为准」不再与可行证据冲突。根 SC-11 的条件文字没有改，只改了证据，没有放宽 A 要交付的内容 |
| N-1 路径覆盖排除项 | 已解决（待落地内容可接受） | 子契约加了一句「不属于本结果的文件由子路由决定的路径覆盖逐项排除」。排除表列了根审查三份及以后的根审查、`reroute-t008.md`、B 草案，并另列根级文件与 `research/`。还写明 A 封存后 B 不改 A 的 `follow-ups.md` 和 spec-001 的审查文件。对照目录实况：`reviews/` 下其余文件（Spec、Plan、挑战、TASK 结果审查、`spec-plan-r3-review.md`、本文件）和 `verification/` 下其余文件都是 spec-001 的产物，排除表完整 |
| N-2 删净 | 已解决 | 改成两类在 A 内删净，其余仍在的旧机制必须进 SC-12 清单，不在清单上就违反 SC-12。现在与根 SC-12「清单之外没有残留」等价，不再比根契约窄 |
| N-3 Scope Out | 已解决 | 改为「包括」，并补了 B 的判据、测试、测量、文档和「迁移保留项与工作单元对照」，与根「In — 结果 B」逐项对应 |

### 对根契约审查定稿的引用

- 子契约通过 Context「revision 4」和 frontmatter `parent` 绑定根契约，没有声称根审查已经定稿，这样写是对的。
- 截至本轮，`reviews/task-contract-review-r3.md` 只有对 rev 3 的第 1 轮（`changes_required`），还没有对 rev 4 的复审结论。根改道记录（`routing-decision.md` 第 49 行）也把生效条件写成「根契约当前 revision（4）通过独立审查」。
- 所以本轮的 `approved` 绑定根契约 rev 4 的现文。如果根契约复审后为了 SC-11、RI-13、RI-16、Requires Confirmation 第 2 项、Constraints「先 A 后 B」或任何标「A」的 SC 行升到 rev 5，子契约要再做一次定点复核。根契约复审通过而没有实质改动的话，本结论继续有效。
- Multi Spec 的激活顺序要求根审查通过后才建映射、写子路由决定，这个门由流程守着，不需要在子契约里重复。

### 修正带来的直接影响

- revision 只递增了一次（1 → 2），没有 `unresolved` 行，也没有 Open Questions，readiness 成立。
- 子契约与根 rev 4 没有矛盾。根 Requires Confirmation 第 2 项多出的两个限定（「以及可能出现的修正子 Spec」「要等用户明确要求分支收尾」），子契约 Constraints 第 3 条和 RI-16 是以引用根契约的方式承接的，没有改写或放宽。
- `contributes_to` 与 `depends_on` 都没变，仍然正确。

### 非阻断残余

1. 子契约审查（本文件）放在 `../../reviews/`，没有放在子契约旁边。这是裁定第 8 条接受的做法。写子路由决定时，要显式写出本审查的路径和 `target_revision: 2`，供激活顺序第 5 步「与已批准的子契约审查匹配」比对。
2. RI-16 写的是「或并进 B」，少了根契约的「在不跨边界的前提下」。子契约整体以根契约为准，不构成冲突；以后再改时可以顺手补上。
