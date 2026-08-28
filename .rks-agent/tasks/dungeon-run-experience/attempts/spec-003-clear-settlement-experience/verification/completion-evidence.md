---
kind: rks-verification
decision: pass
---

# 终验：通关结算发经验

**被评价的主张**：`spec-003-clear-settlement-experience` 的八条成功条件全部成立，改动可以验收。

**新鲜度**：证据取自复审通过并补上那条前提注释之后的最终状态。命令在 `.worktrees/dungeon-run-experience/Server` 下执行，`PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`。

| 条件 | 证据 |
|---|---|
| SC-1 通关后总经验增加，增量等于通关基数加评分奖励 | `GrantClearExperienceCommandTests.AClearPaysItsWorthAndItsRank`：552 + 66 = 618；`SetPlayResultHandlerTests.TheExperienceFrameCarriesTheStandingAfterTheAward` 两例：入场 1000 → 报出 1618 |
| SC-2 通关基数取自档案表在基准等级处的值，乘世界级通关率、难度率、副本权重 | `DungeonClearExperienceRulesTests.AClearIsPricedAtTheDungeonsOwnLevel`（293 × 1 × 1.3 × 1.45 → 552）、`AWeightBelowOnePaysLess`（0.1 权重付 38）、`ADifficultyTheArchiveDoesNotListIsWorthOne`；表值本身由 `PvfExperienceParameterSourceTests.TheClearTablesAreReadFromTheArchiveAndNotFromTheReferenceServer` 对真实档案断言 |
| SC-3 评分奖励按档案的评分等级表定档，比率取自档案 | `EachRankBandPaysTheRateTheArchivePairsWithIt` 六例覆盖 100/99/90/80/60/50；`PvfDungeonRankGradeSourceTests` 对真实档案断言 `99 90 80 60 50 30 20 10` 且只取该段 |
| SC-4 经由同一发放件；跨等级线时可接任务表重算并补发 | `GrantClearExperienceCommandHandler` 唯一的写入路径是 `ICharacterExperienceGrant.AwardAsync`（无第二份加经验/落库实现）；`SetPlayResultHandlerTests.ALevelCrossedAtSettlementRefreshesTheOfferList` 断言按新等级 4 重算并把列表帧插在正确位置；`ASettlementThatCrossesNoThresholdSendsNoQuestList` 断言不跨线时不发 |
| SC-5 结算帧的两项等于实际支付，其余字段仍为零，帧长 274 | `DungeonClearCodecTests.What_the_clear_paid_is_written_at_the_frames_first_two_figures`；`Paying_leaves_every_other_field_exactly_as_an_empty_run_leaves_it`（含 40 亿级数值，证明不会溢进被当作长度读的字段）；`Nothing_else_in_the_reward_breakdown_carries_a_value` 未改 |
| SC-6 表缺失、越界、点位超范围不发或按边界发，且不让结算失败 | `ALevelTheTableDoesNotReachPaysNothing`、`AScoreBelowEveryPayingBandEarnsNothing` 三例、`AClearWorthNothingEarnsNoRankBonus`、`WithoutTheRankTablesTheClearStillPays`、`ADungeonTheTableCannotPricePaysNothing`；`PvfDungeonRankGradeSource` 表缺失时返回空而不抛 |
| SC-7 没有一分钱是付了却报不出来的 | `GrantClearExperienceCommandTests.NothingIsPaidThatTheResultScreenCannotAccountFor` 四个评分档各一例，断言 `ClearBase + RankBonus == Award.Gained`；`SetPlayResultHandlerTests.NothingIsPaidThatTheScreenCannotAccountFor`；反方向由 `AnAwardThatDidNotHappenDeclaresNothing` 与 `WithoutASkillMirrorTheClearIsNeitherPaidNorReported` 守住 |
| SC-8 全套通过、构建零警告零错误 | `dotnet build`：0 警告 0 错误。`dotnet test`：Protocol 330、Networking 51、Pvf 60、Host 4、Application 732、Infrastructure 160(+2 跳过)、Persistence 240 —— 合计 **1577 通过 / 2 跳过 / 0 失败** |

## 判据强度是量出来的

独立评审第一轮判 `changes_required`，抓到的是本成果核心约束的**反方向**：发放失败（角色行读不到，或写回行数不是 1——两条都不抛异常）时，handler 仍把算出来的 552 + 66 报在结算屏上，而总量一分没动。付 0 报 618。

修法是把申报的判据从「命令算出来多少」换成「发放是否发生」。同形的另一半也一并修了：`mirror` 为空时原本先付款再跳过经验帧，等于付了却没有任何帧报出新总量；现在与击杀路径一致，先看 mirror，为空就不付。

复审独立跑了两组变异确认：把申报改回无条件 → `AnAwardThatDidNotHappenDeclaresNothing` 红；在早退前插一次真实的 `commandBus.SendAsync` → `WithoutASkillMirrorTheClearIsNeitherPaidNorReported` 红。另外三组也验过：配对方向写反 → 5 红；`[rank grade]` 的段隔离削掉 → 读到 13233 个整数而非 8 个；塞一路无人申报的加成 → 2 红。

## 实机验收时该看到什么

副本 144 是红色魔石森林，档案里 `[basis level] = 5`、`[experience increasing point] = 1.45`。难度 0（率 1.3）下：

* **通关基数 = 293 × 1 × 1.3 × 1.45 = 552.3 → 552**
* **评分奖励**按客户端报来的评分点位：≥99 得 20%（110）、≥90 得 15%（82）、≥80 得 12%（66）、≥60 得 10%（55）、≥50 得 5%（27）、低于 50 不得

一次通关约等于八次击杀（单杀 65–75），经验条应当明显跳一段。服务端日志里 `Set play result … clearBase= rankBonus= award=` 那一行可以逐项对照。

## 需要如实标注的三处

1. **结算帧头两个字段的含义没有实测。** 位置是结构性的（前八字节），但哪个是通关基数、哪个是评分奖励取的是上游命名。基数远大于评分奖励，所以第一张结算屏截图就能看出有没有写反。若写反，按截图改，不要按上游改。
2. **结算侧不付频道奖励与账号权益加成。** 那二十三个固定槽在 A21 一个都没测过，付了报不出来。测法与本次实现都记在 `Docs/ExperienceNoticeSlots.md` 第七节。
3. **`mirror` 为空时拒付这条分支对已发布会话不可达**（进入世界时若没有 mirror 就不会发布）。代价为零才敢守死不变量；这层依赖已写进分支注释。

## 未由本次终验覆盖的

`spec.md` 的 EV-004（一次实机通关的结算屏截图，并与服务端日志逐项对照）属根契约 SC-5/SC-6 的实机验收，由用户在三个子成果全部完成后一次性执行。本次终验不代替它。
