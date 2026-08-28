---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 通关结算发经验

## Delivered

- 清完一个副本会涨经验。通关基数按副本的 `[basis level]` 从 `[exp reward table]` 取值，乘 `[clear exp bonusrate]`、难度率与副本的 `[experience increasing point]`；评分奖励按客户端报来的评分点位在 `[rank grade]` 上定档，取 `[clear rank exp bonusrate]` 对应的比率。四张表全部读自档案。
- 发放走与击杀、交任务同一件 `ICharacterExperienceGrant`；结算跨等级线时刷新可接任务表并补发列表帧，与击杀路径一致。
- `CLEAR_DUNGEON_REWARD` 的头两个字段带上真实数字，其余仍为零；结算的经验帧报出支付**之后**的等级与总量，此前报的是入场值。
- **付出去的每一分都报得出来**，两个方向都守住：发放没发生时结算屏申报零；没有技能镜像因而无法报出新总量时，先拒付再结算。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md`；命令层 552+66=618，处理器层 1000→1618 |
| SC-2 | 同上；三个因子各一例，表值对真实档案断言 |
| SC-3 | 同上；六个评分档，等级表对真实档案断言且只取该段 |
| SC-4 | 同上；唯一写入路径是共享发放件；跨线重算与列表帧位置 |
| SC-5 | 同上；两项落点、40 亿级数值不溢进长度字段、帧长 274 |
| SC-6 | 同上；等级越界、评分越界、两张表缺失各有测试 |
| SC-7 | 同上；`ClearBase + RankBonus == Award.Gained` 四例，反方向两例 |
| SC-8 | 同上；build 0/0，1577 通过 / 2 跳过 / 0 失败 |

## Repository Result

- Changed paths：新增 `Server/src/Dfo.Server.Abstractions/Content/DungeonRankContent.cs`、`Server/src/Dfo.Server.Abstractions/World/DungeonClearExperienceRules.cs`、`Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonRankGradeSource.cs`、`Server/src/Dfo.Server.Protocol/Game/DungeonClear/GrantClearExperienceCommand.cs` 及四个测试文件；修改 `ExperienceParameterContent`、`PvfExperienceParameterSource`、`InfrastructureModule`、`ProtocolModule`、`DungeonClearSlice`、`Docs/ExperienceNoticeSlots.md` 及两个既有测试
- Baseline or accepted result：基线 `3d22d384`
- Parent rollback required: no

## Successor Guidance

- **下一次实机通关先看结算屏的头两个数。** 它们的含义取自上游命名，未经实测。通关基数远大于评分奖励（144 号副本难度 0 是 552 对 27–110），写反了一眼能看出来。以截图为准，不以上游为准。
- **结算帧那二十三个固定槽还没测。** 测法与不测就不付的理由都写在 `Docs/ExperienceNoticeSlots.md` 第七节：往每个槽填一个互不相同的值（2 的幂），跑一次通关，结算屏会把它认得的那几个连名带值列出来。测完之后，结算侧的频道奖励与账号权益加成才有地方申报——`ChannelExperienceCatalog.RateFor` 与 `IExperienceBonusService` 都已就位，缺的只是槽位。
- **共享发放件在经验到顶时有一处同形的不一致**（独立评审在本轮发现，不属本成果）：`CharacterExperienceGrant.Advance` 封顶后 `total == held`，`if (total != held && ...)` 短路不落库，却仍返回 `Gained > 0`。结算是第一个把金额单独画在屏上的地方，所以到顶的角色会第一个看见「屏上报了、总量没动」。击杀与交任务同样如此，只是没有单独的明细屏。修它要动共享发放件，是一件独立的事。
- **时间奖励点位没做，且不应重复计算。** `Etc/RankSystemInfo.etc` 有 `[dungeon clear time reduction bonus]`，但客户端报来的评分点位已经是它算过的结果；服务端再加一次是重复。上游同样把 `timeBonusPoint` 固定为 0。
- **`[clear exp bonusrate]` 在本档案里是 1**，因此任何实测都区分不开「乘了它」与「没乘」。它被读取并参与，理由是与已在用的 `[monster exp bonusrate]` 同形同源；这是一处构造上的选择，不是测出来的。

## Artifacts

- `.rks-agent/tasks/dungeon-run-experience/attempts/spec-003-clear-settlement-experience/spec.md`
- `.../reviews/change-review.md`（`verdict: approved`，两轮：首轮 `changes_required` 抓到「付 0 报 618」，补救后复审通过并独立跑了五组变异）
- `.../verification/completion-evidence.md`
