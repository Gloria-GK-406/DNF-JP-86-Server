# Routing Decision

- TaskContract: `.rks-agent/tasks/every-flip-pays-gold/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 使用者一句话定死了语义 ——「金币一定有的，而且有一定波动，
  相当于一个底薪，免费牌一定会给」。可观测、无歧义。剩下的「给多少」由档案回答。
- Requirement stability: 可枚举。三处档案读数（每级基数、每级波动百分比、每难度金币倍率）各自
  可以单独成为一条要求；行为侧的「恒大于零」「同局不变」「一律入账」也各自可独立核对。
- Execution direction: 已知且很窄。补一列解析、新读一节、加一条规则、改两处消费点。
  规则服务已经拿到所需的全部事实（`ClearRewardOccasion` 已带副本基准等级与难度），
  **不需要新增任何运行时状态**。
- Remaining uncertainty: 只剩一条已在契约 Assumptions 里具名的 —— 三个数如何相乘，档案没说。
  它不改变要交付什么，只决定日后若有更好的证据，改动落在哪一处。

## Marginal Control Value

- User precision signal: 极强，而且是刚刚用一次失败换来的。使用者在上一个课题里逐条主持了十四轮
  实机实验，判定前一个 Spec 的模型不成立并要求重开；对「不能抄参照服务端」有过明确表态
  （稀有度分母那次）。本次又亲自纠正了「金币跟着装备走」这个读法。
- Direct failure mode: **两个，都发生过。**
  其一，**抄一个没有出处的常数**：参照服务端的 `175/1000` 出现两次、零注释、档案里查不到 175；
  同一族错误在本仓已造成过「两档稀有度永远摇不到而代码毫无异样」。Direct 之下最省事的做法
  正是照抄那份现成实现。
  其二，**判据钉错对象**：上一个 Spec 的 1964 条判据全绿而真机一律 NONE，因为它们断言的是
  「编码器写了哪些字节」。金币这次同样可以写出一套「断言我算了这个乘法」的判据，与实现互为镜像。
- Useful control: 一份把每处档案读数拆成独立可核对要求的 Spec，加上一次不共享我的结论的
  独立评审。前者让「这个因子的出处是哪一行档案」成为会被单独检查的验收；后者去核对**因子的
  出处**与**判据的对象**，而这两件按定义都无法自查 —— 作者的判据和作者的实现出自同一个误解。
- Time-for-quality rationale: 错金币的代价是长期且无声的（玩家永远拿一个凭空的数，没有任何报错），
  识别它的成本只是把三个因子逐条列出来给人比着档案核一遍。上一轮同一套控制刚跑通，成本已知。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个验收边界 —— 翻牌给不给金币、给多少。三处档案读数、一条规则、两处消费点
  共享同一个答案，拆开会让「这个数从哪来」横跨两个 Spec。
- Why Direct is insufficient: Direct 给不出「有人独立地核对每个因子的出处」这件事，
  而无出处的常数正是本仓吃过两次亏的那一族。
- Why Multi is unnecessary: 只有一个连贯的验收与回滚边界；付费牌与黑砖牌的金币已被契约排除在外。
- Reroute conditions: 若档案被证明另有一处直接声明翻牌金币（而不是靠三个因子相乘），
  公式的形状失去依据，回到意图对齐；若使用者要求采用击杀数缩放，则需新增按局的击杀计数，
  范围扩大到运行时状态，回到对齐重定。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制只有两件 —— 把每处读数写成可独立核对的要求，
  以及一次独立的完整结果评审。两件 Lite 都有。
- Why lighter is insufficient: Direct 没有独立评审。本任务的两个失败模式（抄无出处的常数、
  判据钉错对象）都属于作者自己看不出来的那一类。
- Why heavier is unnecessary: Standard 多出来的是显式 Plan，它的价值在于「有一簇互相牵制的
  实现决定要先定在纸上」。**这一次没有那一簇**：规则服务已有全部事实，不需要新增运行时状态，
  改动是线性的（先补解析，再加规则，再改两处消费点），写 Plan 只会把契约抄一遍。
  Governed 多出来的独立挑战与产物批准更挣不回时间 —— 不触及安全、凭据、隐私、合规或不可逆的
  外部变更，且回滚在上一个课题里已经演练过。
