# Routing Decision

- TaskContract: `.rks-agent/tasks/typed-command-dispatch/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 目标形态经过多轮收敛并由编译原型验证——三参数注册、`TCommand : ICommand<TResult>` 与 `THandler : ICommandHandler<TCommand,TResult>` 双约束、错配产生 CS0311。用户已确认这一形态。可观察结果明确：枚举消失、`object` 接缝消失、`CanHandle` 消失、执行语义不变。
- Requirement stability: 显式要求可枚举且可复核——四类删除（枚举、`object` 接缝、`CanHandle`、结果回收判定）加一条不变量（执行语义保持），后者可拆成行为顺序、两种闸门、嵌套拒绝、作用域四项。
- Execution direction: 已知。适配层 `ICommandInvoker<TResult>` 的必要性已从 C# 类型推导规则推出并经原型验证：`SendAsync<TResult>` 在调用点只静态知道 `TResult`，需要一个闭合 `TCommand` 的中间类型才能解析。
- Remaining uncertainty: 仅剩非语义的局部选择（类型参数顺序、适配类型命名、开放泛型的 DI 注册写法）。

## Marginal Control Value

- User precision signal: 用户主动要求走 SDD 并明说「这个重构还是有点危险的」，同时授权使用 subagent。这是本次会话中最强的一次精确度偏好表达。
- Direct failure mode: 危险不在「编译不过」——那是响亮的。危险在**编译通过但执行语义静默改变**。管线从非泛型改为按 `TResult` 泛型后，行为需改用开放泛型注册；若某个行为漏注册或解析不到，链路照常工作、测试可能照常通过，但该行为不再进链。其中 `OnlineUserExecutionBehavior` 与 `AccountExecutionBehavior` 守的是并发不变量——账号互斥闸门一旦静默失效，症状是偶发的并发写冲突，与本次改动的因果链极难关联。同类风险还有 `CommandExecutionPolicyBehavior` 对 Transactional 的拒绝失效。
- Useful control: 一份把「执行语义保持」拆成可复核条目的 Spec，加一次对照契约的完整结果评审——重点检查四个行为是否仍然全部进链且顺序不变，而不只看测试是否变绿。
- Time-for-quality rationale: 改动面覆盖派发主干（8 命令、8 handler、4 行为、注册表、bus、DI 装配、约十处测试替身），且失效模式是静默的。规格化的时间远小于一次并发缺陷的定位成本。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界。四类删除与类型契约改造互为前提，不能分别交付——去掉枚举而不改注册就无法编译，改注册而不改 bus 就仍需 `object` 接缝。不存在可独立接受或独立回滚的子结果。
- Why Direct is insufficient: Direct 无法在实现前把「执行语义保持」固定成可复核清单，而这正是静默失效的落点；仅靠「测试变绿」不足以证明四个行为都还在链上。
- Why Multi is unnecessary: 单一原子改动，无独立验收或回滚边界。
- Reroute conditions: 若开放泛型的行为注册在 `Microsoft.Extensions.DependencyInjection` 下无法可靠工作，则「链路全程静态类型」这一目标需重新评估，退回意图对齐；若发现命令类型存在继承关系，按精确类型建键的前提失效，须重新评估注册表设计。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: default
- Why this is sufficient: 需要的控制是「实现前固定显式要求」加「完整结果评审」，二者正是 Lite 的组成。执行方向已由原型验证，不存在需要事先排序的依赖网络。
- Why lighter is insufficient: Direct 缺少可复核的行为保持清单，静默失效无从拦截。
- Why heavier is unnecessary: Standard 的独立 Plan 在此不增加保护——改动是原子的，无可供排序的阶段；Governed 的独立挑战与漂移控制针对的是高后果不可逆结果，本任务的结果完全可由 git 回退。
