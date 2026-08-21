# Routing Decision

- TaskContract: `.rks-agent/tasks/domain-orchestrates-table-repositories/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 成果在本轮对话中逐条收敛并由用户确认——领域层与持久层分界清晰，仓储退回单表读写，编排上移。用户还主动否掉了两个更大的方案（泛型 `Repository<T>`、拆 BLOB），说明边界是想清楚的而不是含糊的。
- Requirement stability: 可枚举且可观察：九个仓储、26 处边界外自开连接、31 张表的入场装载组装、一组已确认无生产调用方的退役目标。每一项都能在改动前后清点。
- Execution direction: 中心方向由用户自己定：按表拆、取环境连接、组装集中一处。Agent 只在机制细节上有裁量。
- Remaining uncertainty: 仅剩局部选择——每张表对应的仓储命名与签名、编排落在哪个领域服务上。这些不改变任务语义。

## Marginal Control Value

- User precision signal: 用户明确要求「按 multispec 来」，并在此前两个同类任务上都选了 SDD 治理流程。同时要求「全部完成后再和我说」，即接受流程时间成本换取一次交付。
- Direct failure mode: 本任务的主要风险是**不产生红灯的失效**。三条已识别：其一，仓储拆分时把跨表组装顺手复制到多个调用方，测试全绿但存储结构泄漏到各处，与目标相反且难以事后收回；其二，改为环境连接时漏掉某条路径仍自开连接，读撕裂只在并发下暴露，测试环境构造不出来；其三，退役内存载体时连带删掉仍被间接依赖的行为，直到运行期才发现。
- Useful control: Spec 把「组装唯一」「边界外自开连接为零」「同一用例读到同一快照」写成可清点的验收条件，使上述三条各自有一个能被检验的观察点，而不是靠执行者自觉。整体变更评审在两个 Spec 之间提供一次独立视角。
- Time-for-quality rationale: 改动面 26 处连接、9 个仓储、31 张表的组装，一次性做完且中途不与用户交互。没有可清点的验收条件，「做完了」将无法与「大部分做完了」区分开。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 用户明确给出两个各自可接受的结果，且它们的接受边界确实独立。Spec 1（九个仓储按表拆分并接入环境连接）交付后即是一个完整、可用、可回滚的状态——item 路径原样保留，系统照常运行。Spec 2（item 载体收敛与无用构造退役）依赖 Spec 1 的环境连接就位，但它的失败不使 Spec 1 的成果作废，回滚边界也不重叠：前者改仓储与领域编排，后者删容器与工厂。
- Why Direct is insufficient: Direct 无法提供「组装唯一」「自开连接为零」这类需要跨整个改动面清点的验收条件，而这正是本任务三条静默失效各自的唯一观察点。
- Reroute conditions: 若执行期发现内存载体存在未被识别的生产调用方，Spec 2 的退役前提失效，须回到路由重新划分；若九个仓储中出现无法按单表拆分的（例如某张表的写必须与另一张表在同一语句内完成），则该仓储脱离 Spec 1 的接受边界，须判断是并入 Spec 2 还是另立；若按表拆分后入场装载出现可观测的延迟退化，则吞吐量假设失效，须回到用户重新确认边界。
