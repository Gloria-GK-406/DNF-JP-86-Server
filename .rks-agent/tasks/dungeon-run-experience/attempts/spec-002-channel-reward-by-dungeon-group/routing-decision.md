# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-experience/attempts/spec-002-channel-reward-by-dungeon-group/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 结果在实机上直接可见——同一张图，在一个频道上多一行「赛丽亚的祝福奖励」，在另一个频道上没有。
- Requirement stability: 规则逐行写在 `channel_info.etc` 里，本仓已经在发这个文件；判据就是把那两段解析出来并照办。
- Execution direction: 已知。落在既有分层上：内容源解析、纯规则决定、同步的加成收集服务读常驻状态。
- Remaining uncertainty: 只剩局部选择（解析器放哪个项目、频道号怎么从监听端口同步取到）。不改变成果的含义。

## Marginal Control Value

- User precision signal: 要求 SDD 流程，全部完成后一次性实机验收。
- Direct failure mode: 这一处的具体失败已经发生过一次，就是现在要修的东西：从一次抓包的四个数反推出一个常数，写进代码，还在注释里断言「档案没有声明它」——而档案里逐行写着。四个样本全部吻合，测试全绿，实机看起来完全正常，唯一的错是**归属条件不存在**：在任何频道跑任何图都付这 5%。直接做最可能重犯的形状是同一个：把「组内」这半条规则实现掉，然后用一个恰好在组内的样本验证通过。
- Useful control: Spec 把「组外不付」写成与「组内付」并列的一等判据，加上完整结果评审。评审读的是完成后的整体，能抓到「只测了正例」这种自证式验收。
- Time-for-quality rationale: 这条规则的错法是静默的——付多了或付少了都只是经验数字变化，没有任何一处会报错。而实机读数是这条路径唯一的检验手段，一旦被污染，后面通关结算的每一个数都无法判断对错。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——频道奖励按归属发放。解析、归属、撤配置、部署可见性都服务于这同一件事，拆开任何一块都不能单独验收（只解析不接线什么也看不出来）。
- Why Direct is insufficient: 见失败模式；这正是直接做已经错过一次的地方。
- Why Multi is unnecessary: 只有一个可独立验收的结果。
- Reroute conditions: 若实机测出「组外也付」或比率与 `[server]` 行不符，说明档案不是权威来源，停下重新取证而不是往代码里塞一个能对上的数。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制是稳定的验收边界、完成后的整体评审、新鲜验证，Lite 全都有。
- Why lighter is insufficient: 小任务流程没有独立评审，而这里要防的正是「只验了正例」。
- Why heavier is unnecessary: 没有需要提前决议的实现分歧。
