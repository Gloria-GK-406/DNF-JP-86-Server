# Routing Decision

- TaskContract: `.rks-agent/tasks/dungeon-run-experience/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: 三件后续在上一轮交付的结尾被逐条列出并按该顺序被用户接受（「后续的按你的想法实现」）。每一件的可观察结果都在客户端界面上：一条通告的明细、一行加成的有无、一块结算屏上的数字。
- Requirement stability: 三件的判据都能事先写死。槽位语义已经实测完（`Docs/ExperienceNoticeSlots.md`），频道规则在 `channel_info.etc` 里逐行声明，结算的字段偏移在 `DungeonClearPayloadCodec` 里已经按参照帧核过。
- Execution direction: 已知。三件都落在既有的分层上——加成种类进 `ExperienceBonusKind` 与帧编码，频道归属进内容源与 `ExperienceBonusRules`，结算走已经存在的 `ICharacterExperienceGrant`。
- Remaining uncertainty: 只剩局部选择：频道归属表放在哪个内容源里、结算基数用哪张表的哪一行。都不改变成果的含义。

## Marginal Control Value

- User precision signal: 明确要求「依旧是sdd流程」，且要「完全实现后」一次性实机验收——偏精确，且容忍工作流时间。
- Direct failure mode: 直接做最可能丢的是**证据归属**。这个题目已经在同一处摔过两次：槽位标签照抄上游，错了两个；频道比率从一次抓包反推成常数，把档案声明当成了服务端政策。两次都不报错、不崩、看起来完全正常，只有在实机上盯着那一行字才看得出来。第三次的形状是一样的——通关结算有八个加成来源，逐个都能「看起来合理地」填一个数进去。
- Useful control: 每个子成果自己的 Spec 把「这个数从哪来」写成可观察判据，加上完整结果评审与新鲜验证。评审是这里的关键控制：它读的是完成后的整体，能抓到「某个槽被填了但出处只是上游」这种单看一处看不出来的问题。
- Time-for-quality rationale: 三件都要改动同一条已经验收过的经验路径。一处发明的数字会污染后面所有实机读数，而实机读数正是这条路径唯一的检验手段——返工的代价是重跑一整局游戏并重新怀疑之前所有结论，远高于工作流开销。

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: 三个成果各自独立可验收、独立可回滚，且第三个带着一个真实的阻塞风险。(1) 九个具名加成行：验收判据是通告明细自洽，单独交付即有价值——它让帧不再把付出去的加成算进基础值。(2) 频道奖励按归属：验收判据是组内付、组外不付，与 (1) 无依赖，因为它要用的槽已经存在。(3) 通关结算发经验：最大的一块，且它依赖一张我们尚未证实取得的表（`[clear rank exp bonusrate]`、以及结算基数是否真与交任务共用 `[exp reward table]`）。若证据不足，它应当能单独阻塞而不拖掉前两个已经验收的成果——这正是独立回滚边界的意义，不是假设出来的。
- Why Direct is insufficient: 见上面的失败模式。这条路径上「错得看不出来」是常态而非例外，直接做没有任何一处会在写的时候报警。
- Reroute conditions: 若第一个子成果做完后发现后两个其实必须一起改同一批文件、无法分别验收，则退回 Single Spec 合并剩余部分；若通关结算的基数表在实机上对不上且无其他证据来源，该子成果按 `blocked` 封存，根任务如实记录缺口而不是编一个数。
