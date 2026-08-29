# Routing Decision

- TaskContract: `.rks-agent/tasks/finishing-and-leaving-a-run/attempts/spec-001-boss-die-check/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 结果在屏幕上——打死 boss 之后结算界面出不出得来。用户已经用一句话把它定死了。
- Requirement stability: 判据能事先写死：界面出现、`0x0075` 不再挂在 `Unhandled` 上、既有清关与结算路径不变。取证的**结论**未知，但要求不因结论而变。
- Execution direction: 已知的部分是它落在既有的 `DungeonClear` / `MonsterDeath` 分片上；未知的部分是取证，那是执行内的工作。
- Remaining uncertainty: 只剩"客户端到底要什么"这一个技术问题，它不改变成果的含义。

## Marginal Control Value

- User precision signal: 要求 SDD 流程，并要求先写契约与 spec、压缩后一次性执行。
- Direct failure mode: 上游对这一帧**不发应答**，而我们的清关顺序与它相反。直接做最可能发生的是照着上游写一个"解析并触发清关"的处理器，然后发现清关早就触发过了，于是补一个看起来合理的应答字节——日志变干净、测试全绿、实机界面依旧不出来。这个仓库已经两次栽在"照抄上游看起来能跑"上。
- Useful control: Spec 把「判据是实机界面，不是日志不再 warn」写成一等要求，加上完整结果评审。评审读完成后的整体，能抓到"用日志代替界面作为验收"这种自证式收尾。
- Time-for-quality rationale: 这一件是后面三件的前置观察窗口——玩家要先能站在结算界面上，才谈得上翻牌与回城。这里若用错的判据收尾，后面三件的实机读数全部不可信。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——结算界面出得来。取证、解码、应答（若需要）都服务于这同一件，拆开任何一块都不能单独验收。
- Why Direct is insufficient: 见失败模式：错误的收尾方式（拿日志当判据）不会产生任何报错。
- Why Multi is unnecessary: 只有一个可独立验收的结果。
- Reroute conditions: 若取证发现界面不出来的原因根本不在这一帧、而在别的子系统（例如结算帧本身的某个字段），停下重新界定成果，而不是在这里补一个能让日志变干净的应答。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制是稳定的验收边界、完成后的整体评审、新鲜验证，Lite 全都有。
- Why lighter is insufficient: 小任务流程没有独立评审，而这里要防的正是"用日志代替界面"。
- Why heavier is unnecessary: 取证是执行内的工作，不是需要提前决议的实现分歧；写一份 plan 只会把 spec 抄一遍。
