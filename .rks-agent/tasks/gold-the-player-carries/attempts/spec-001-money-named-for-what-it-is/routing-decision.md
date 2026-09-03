# Routing Decision

- TaskContract: `.rks-agent/tasks/gold-the-player-carries/attempts/spec-001-money-named-for-what-it-is/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 结果是「名字与文档说的就是代码在做的事，且行为不变」，可观察且不含未决语义。命名方案由用户亲自定下，最容易返工的那个决定已经封死。
- Requirement stability: 高度可枚举。`purse` 79 处 10 文件、`Gold` 的两类非货币借用、金库服务及其实现与存储层与两个消费方的文档、三处走样的文档记述、主背包前三个虚拟槽位的说明——每一条都能用检索或逐处核对单独查证。
- Execution direction: 已知且低风险。改动是名字与文字，写路径与协议一律不动；全量判据 1993 条是现成的行为 oracle。
- Remaining uncertainty: 仅剩非语义的局部选择——金库服务与相关类型各自的确切新名、六个魔方碎片字段的具体拼法、主背包槽位说明写在哪一处最合适。这些不改变结果含义，留给 Spec 定。

## Marginal Control Value

- User precision signal: 明确偏精度。用户拒绝了先改了再说的做法，坚持先查清结构；发现根因后主动要求走 SDD；并接受了「拆两步、第一步行为零变化」这个更慢但更安全的路径。
- Direct failure mode: 「改到一半却看起来完成了」。这一步的具体形态是：79 处 `purse` 里 62 处在测试类名与用例名中，**漏改测试名会让下一个人以为随身金币已有回归覆盖**；漏改文档里那句「已有」则陷阱原封不动——那句话正是这次事故的源头。而全量判据在这两种漏改下**全都是绿的**：编译通过、行为不变、无任何运行时征兆。
- Useful control: 一份把每一类漂移写成可独立核对条目的 Spec，加一次独立评审。判据能证明「行为没变」，证明不了「改全了」；补上后一半的只能是枚举加复核。本仓在同类工作上，独立评审已多次抓到自查漏掉的东西。
- Time-for-quality rationale: 值。改动本身机械且量大，正是注意力最容易漏项的形态；而漏项的代价是这次事故再来一遍。相对于返工与再次误导，写一份可核对清单的时间是小头。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——「名字与文档不再指错，且行为逐值不变」，一个 oracle（全量判据加全仓检索），一个回滚边界。所有改动落在同一批文件上，没有任何两部分可以互相独立地接受或回滚。
- Why Direct is insufficient: Direct 无法廉价提供覆盖完整性的证据。这里的失败不是做错而是做漏，漏掉的部分在运行时完全不报错，只有下一个读代码的人会被误导。
- Why Multi is unnecessary: 只有一个可接受结果。名字改一半、文档改一半都不构成任何有意义的交付，也不存在需要各自回滚的独立部分。
- Reroute conditions: 若发现某处改名会改变序列化、反射或判据行为（即无法在行为不变的前提下完成），则本 Spec 的验收边界失效，须退回根控制器重新定边界，而非在 Spec 内放宽 CSC-6。若发现走样文档的修正需要先确定随身金币的实现细节才能写对，则该部分应移交下一个结果，本 Spec 的范围相应收窄。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-refactor`
- Why this is sufficient: 本 Spec 的失败模式是**遗漏**而非误判。需要的控制恰好是 Lite 的三件：把「必须成为真的」写成可独立核对的持久 Spec（每一类漂移一条）、对**完整结果**而非逐任务做评审、以及一次新鲜验证。机械正确性另有两道现成的网——编译器（改名漏改即编译失败）与 1993 条判据（行为逐值不变的 oracle）。
- Why lighter is insufficient: 不能退到 Direct 或无 Spec 的形态。79 处 `purse` 里 62 处在测试类名与用例名中，漏改它们与漏改文档里那句「已有」在**判据全绿、编译通过、无任何运行时征兆**的情况下都能通过——正是这次事故的复现路径。没有持久枚举，「改全了」无从核对。
- Why heavier is unnecessary: Standard 的独有控制是 `plan.md` 带来的顺序与依赖裁决，而本 Spec 没有需要前置裁决的决定——命名方案已由用户定死，不存在归属未定的共享消费方。唯一的顺序性（先摘出 `Gold` 的非货币借用，再做全仓收敛）是实现内的局部次序，由编译器保证安全，不构成会向下传播的早期错误。对照本仓前一个同类全仓重构 `rule-services-own-content`：它选 Standard 的全部理由是「多个消费方共享同一内容源，必须先定归属否则成批返工」，那种前置裁决这里不存在。Governed 无硬性下限：本 Spec **不改变任何行为**，不触及安全、凭据、授权、隐私、合规、不可逆的外部变更，也没有未经证明可恢复的持久数据丢失路径——改动在隔离工作区内，回滚边界清晰。
