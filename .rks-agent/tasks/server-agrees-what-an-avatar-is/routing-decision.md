# Routing Decision

- TaskContract: `.rks-agent/tasks/server-agrees-what-an-avatar-is/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 所有者当场接受了一段含两半的具体建议（修 8／1 种类不一致；
  补上缺席的时装槽位键），随后以「你来实施吧」授权执行。目标可观察：服务器对「时装是什么、
  能放在哪」三处说法一致，且时装能装回自己的槽。范围的上下界都由所有者的下一步（实机验证）
  钉住——本刀交付的是让实机能开始的前提，不含实机本身。
- Requirement stability: 八条成功条件全部可证，且每条的证据形状不依赖研究结果的走向。
  尤其 SC-3 写成「有出处的补、无出处的不补并记下缺席」，无论档案能查出几个键都成立。
- Execution direction: 中心方向已知且唯一——根因在 `WornPieceMetadata` 一处三元表达式，
  槽位键在 `PvfEquipmentSlots.ByName` 一张字典。两处都已实查坐实，不是猜测。
- Remaining uncertainty: 时装类型名到槽号的映射。这是**工作量所在而非语义歧义**：
  三个可查到的数（12／11／11）对不齐，但对齐它们不改变目标、范围或任何成功条件的措辞。

## Marginal Control Value

- User precision signal: **「慢慢验证，不急于快速推进」**——所有者本轮明确表达精度优先于速度。
- Direct failure mode: **一处可观察行为变化被改掉而没有被记下来。** 这不是假想：
  上一个成果 items-own-their-identity 的完整变更评审以 `changes_required` 拦下，
  阻塞项 F-1 就是行为变化清单漏掉了 13 条新增的生产拒绝路径，根因是核法只从授权变化的起因
  往下游追、再加一次全量判据——**而全量判据全绿恰恰意味着没有用例走到那些新路径上**。
  本刀的暴露面比上一轮更大：上一轮那个矛盾靠「档案里 0 个时装可发放」兜着，
  **本刀的目的就是让它可达**，兜底随之消失。同一失败模式这次会直接落到实机上，
  表现为所有者花时间去 debug 一个我们自己引入且已被诊断过的缺陷。
- Useful control: 一份枚举全部可观察行为变化的 Spec；一次 scope 为完整变更集的独立评审
  （它上一轮正是抓住 F-1 的那道闸）；一次不复用执行方断言的新鲜验证。
- Time-for-quality rationale: 本刀的产物是所有者下一步实机验证的**输入**。
  输入里带一处未记载的行为变化，代价不是一次返工，而是把错误转移到一个反馈慢得多、
  且只有所有者能操作的回路里。相比之下多花的流程时间很便宜。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 两半共享同一个验收边界——「服务器对时装的说法一致」。
  种类一致（kind 8）与槽位可达（ByName 补键）是同一个不对称的两面：
  上一轮修契约兽时，正是这两样一起改才让「取得下、装不上」消失。
  它们改的是同两个文件、共用同一份行为变化清单、共用同一个字节锚，
  也无法各自独立接受——只补槽位键而不修种类，会让时装第一次存盘就被守卫拒绝回滚。
- Why Direct is insufficient: Direct 无法提供「完整变更集的独立评审」。
  上一轮的 F-1 是独立评审者发现的，执行方自己两轮都没发现；
  而本刀的失败模式与那次同型，且失去了档案不可达这层兜底。
- Why Multi is unnecessary: 只有一个连贯的验收边界与一个回滚边界。
  两半不能分别接受（见上），因此不构成两个独立成果。
  实机验证虽是所有者接受的更大意图的一部分，但它由所有者操作、不产生本仓的代码改动，
  不是一个可由本工作流接受或回滚的 Spec。
- Reroute conditions:
  1. 若档案研究表明时装槽号映射需要先做一次实机采样才能确定（即 SC-3 的出处只能来自实机），
     则「补槽位键」这一半的验收边界与「修种类」不再相同，应停下重新切分而不是硬做完。
  2. 若修种类的改动牵动了入场帧之外的协议字节布局，超出本契约 Constraints，停下返回。
  3. 若发现两半之外还存在必须同时改动才能自洽的第三处，重新评估边界。

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-bugfix`
- Why this is sufficient: 本刀有一个硬顺序——**槽号的出处必须先立住，才能动那张表**。
  Standard 的 `plan.md` 是唯一能把这个顺序钉成可检查步骤的产物（`code-bugfix` 的 plan
  阶段 `done_when` 恰是「covers cause investigation, fix, regression, and verification」）。
  另两个 Standard 控制也各自对上一条真实风险：可观察行为变化的连带面跨
  `WornPieceMetadata` → `AddAsync` 写入 → `SaveAsync` 守卫 → `AllowsQuickSlot` → 入场字节锚，
  显式的依赖排序防止「改完 kind 才发现快捷栏规则跟着变」；durable sequencing 让这轮
  可跨会话恢复，而本会话已经很长。
  Profile 选 `code-bugfix` 而非 `code-feature`：本刀的性质是修一处三方互相矛盾的缺陷，
  新增的可观察行为是修复的**后果**而不是目的；其 spec 阶段 `done_when`
  「defines the failure and accepted corrected behavior」正是本刀要交代的两件事。
- Why lighter is insufficient: Lite 没有 `plan.md`，因而**没有任何产物规定「先立出处、再写键」**。
  契约 SC-3 只写了验收条件（每个键都要有出处），不写顺序；Lite 下的自然做法是先把表写出来
  再回头找依据——这正是本仓已被烧过两次的推理方向（写下记载后不回头核；
  挑了一个测不到目标的量具还信了它的读数）。Lite 的完整结果评审能在**事后**发现出处不足，
  但那时表、判据、行为变化清单与字节锚都已按错误映射写完，返工面覆盖整轮。
- Why heavier is unnecessary: Governed 的硬底线一条都不触及——不涉及人身或系统安全、
  凭据与访问控制、隐私或受保护数据、财务法务合规、不可逆的外部变更，
  也没有无恢复手段的持久数据丢失通路（旧库已由上一轮裁定作废重建）。
  最坏后果是某个时装槽号填错，而**紧接着的实机验证正是为发现它而做的**，
  回路短且由所有者亲自把关。独立挑战集与独立产物审批不会缩短这条回路，只会加长它。

## Reroute Record（reroute condition 1 已触发并处置）

**触发**：spec 阶段的两路调查穷尽了槽号的三类可能出处，结论是**一格都查不出**，
只能来自实机采样（证据见 `findings-slot-provenance.md`、`findings-archive-measurement.md`）。
这正是本决策 Reroute conditions 第 1 条写下的情形。

**处置**：**不重选工作流。** 结构判断未变——本刀仍是一个连贯的验收边界，
只是边界的内容按所有者裁定收窄：交付「种类一致 + 发放路径可用 + 供实机量出槽号的量具」，
把「补槽位键 / 时装能装回槽」移交实机之后的后继成果。
契约随之升至 revision 2，Scope Out 与 SC-3/SC-4 已改写，作废的假设已就地标注。

**为什么不改成 Multi Spec**：收窄之后本刀只剩一个成果，更不需要第二个 Spec；
被移出的那一半今天**不可执行**（缺实机输入），不是一个可以并行接受或回滚的兄弟成果。

**控制器选择不变**（`sdd-standard` / `code-bugfix`）。理由反而更强了：
「先立出处、再动表」这条硬顺序正是本次拦住我们的东西——
调查在写任何一行表之前就把「无出处」变成了结论，这是 Plan 阶段
`cause investigation` 要保护的价值，而它已经兑现了一次。
