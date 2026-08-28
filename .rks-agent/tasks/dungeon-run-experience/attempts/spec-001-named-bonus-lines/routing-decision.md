# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-experience/attempts/spec-001-named-bonus-lines/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 结果在客户端界面上直接可见——一次击杀的通告里，付出去的每一笔各占一行，明细加基础值等于宣布的总数。
- Requirement stability: 判据已经写死在 `Docs/ExperienceNoticeSlots.md` 第四节的十四行表里：哪九个可用、哪两个不可用、哪三个惰性，以及每一个的客户端叫法。要求就是把那张表兑现。
- Execution direction: 已知。种类枚举 + 帧编码按种类填槽，落在既有分层上。
- Remaining uncertainty: 只剩局部表示选择（金额怎么从 `ExperienceAward` 传到编码器）。不改变成果的含义。

## Marginal Control Value

- User precision signal: 用户要求 SDD 流程并在全部完成后一次性实机验收。
- Direct failure mode: 这一处已经错过一次——槽位与叫法照抄上游，被实机证伪两个。直接做最可能重犯的是「顺手把某个种类指到一个看起来对的偏移上」，尤其是两个不可用槽：上游给它们起过名字，看起来完全像是可以填的。填进去不报错、不崩，只有玩家盯着明细做加法才发现对不上。
- Useful control: Spec 把「九个可用、两个禁用」写成可观察判据，加上完整结果评审——评审读的是完成后的整体，能抓到「某个种类指向了禁用槽」这种单看一处看不出来的问题。
- Time-for-quality rationale: 这九个槽是后面两个成果的地基。第三个成果有八个加成来源要往上挂，地基上有一个偏移错了，后面每一次实机读数都被污染，而实机读数是这条路径唯一的检验手段。

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: 一个连贯的验收边界——通告的明细自洽。所有改动集中在种类建模与帧编码两处，没有第二个能独立验收的成果。
- Why Direct is insufficient: 见失败模式：错得看不出来，写的时候没有任何一处会报警。
- Why Multi is unnecessary: 只有一个可独立验收的结果。
- Reroute conditions: 若实测表本身在实现中被发现有内部矛盾（例如某个「可用」槽实机上不画），退回重测而不是照表实现。

## Controller Selection

- Controller: `sdd-lite`
- Profile source: `code-feature`
- Why this is sufficient: 需要的控制是「稳定的验收边界 + 完成后的整体评审 + 新鲜验证」，Lite 全都有。
- Why lighter is insufficient: 小任务流程没有独立评审，而这里要防的正是「单看一处看不出来」。
- Why heavier is unnecessary: 没有需要提前决议的实现分歧，写一份 plan 只是把 spec 抄一遍。
