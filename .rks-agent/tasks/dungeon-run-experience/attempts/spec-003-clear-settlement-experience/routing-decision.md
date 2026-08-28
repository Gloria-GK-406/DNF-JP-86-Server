# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-experience/attempts/spec-003-clear-settlement-experience/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 结果在实机上直接可见——清完一个副本，结算屏上出现非零的经验，角色总量随之上涨。
- Requirement stability: 三张表在 A21 档案里已经实地读到，公式的形状取自上游而其中每一个数取自档案。判据能事先写死。
- Execution direction: 已知。发放件、加成收集、事务命令、跨等级线后续处理四样都已在击杀路径上跑通，本成果是把同一套接到结算上。
- Remaining uncertainty: 只剩局部选择（命令放哪、内容源怎么组织）。不改变成果的含义。

## Marginal Control Value

- User precision signal: 要求 SDD 流程，全部完成后一次性实机验收。
- Direct failure mode: 这里有两个已知的静默错法，且都已在本任务的前两个成果里各出现过一次同形态的版本。其一是**抄上游常量**：`[clear rank exp bonusrate]` 的兜底值与本档案实际值差一个数量级，抄了不报错，只是每次通关的评分奖励小十倍。其二是**付了报不出来**：结算帧有二十几个固定槽，上游旧版把频道奖励写在第 24 个，而那份帧的槽数与我们核对过的这份不同——照着填会让结算屏的合计与实际到账对不上，而玩家只会觉得"数字有点怪"。
- Useful control: Spec 把「付出去的每一分都必须报得出来」写成一条一等判据，加上完整结果评审。评审读的是完成后的整体，能抓到「某一项付了但没有申报」这种单看一处看不出来的问题。
- Time-for-quality rationale: 这是这条路径上最后一块，也是数字最大的一块。错了不会崩、不会红，只会让实机读数不可信——而实机读数是整条经验路径唯一的检验手段。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——清一次副本会涨经验且屏上说得清。基数、评分奖励、发放、申报都服务于这同一件事，拆开任何一块都不能单独验收。
- Why Direct is insufficient: 见失败模式；两种错法都不产生任何报错。
- Why Multi is unnecessary: 只有一个可独立验收的结果。
- Reroute conditions: 若实机结算屏上的数字与我们支付的对不上（位置错、量级错），停下按截图重新取证，而不是往公式里加一个能把数凑对的系数。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制是稳定的验收边界、完成后的整体评审、新鲜验证，Lite 全都有。
- Why lighter is insufficient: 小任务流程没有独立评审，而这里要防的正是「付了却报不出来」这种单看一处看不出来的问题。
- Why heavier is unnecessary: 公式的形状与每个数的来源都已确定，没有需要提前决议的实现分歧。
