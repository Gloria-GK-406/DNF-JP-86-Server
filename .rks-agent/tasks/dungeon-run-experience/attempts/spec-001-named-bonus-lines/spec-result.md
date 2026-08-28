---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 每一笔加成走到客户端为它画的那一行

## Delivered

- `ExperienceBonusKind` 从两个成员扩到十个：`Unlisted` 加上实测确认「既画又减」的九行，每一个带客户端叫法与出处。五个不可用/惰性槽刻意没有成员，并在注释里写明为什么它们看起来可填。
- `MonsterDeathPayloadCodec.NamedLines` 声明 kind → 偏移 → 客户端叫法三元组。叫法进生产代码，是因为偏移单看无法被人核对，而「赛丽亚的祝福奖励 挨着 `+0x1A`」可以回查文档或回查屏幕。
- 经验帧改为接收 `ExperienceBonusLines`（按种类归并整份 award），不再只接收频道那一个 `uint`。同种类多笔相加且饱和；`Unlisted` 不写任何槽。
- 四处 `ExperienceNotification` 调用点全部交代清楚：击杀传整份 award，交任务、教程、通关结算传 `None` 并各自注明为什么此刻确实没有可申报的行。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md`；`The_declared_lines_are_pair_for_pair_the_measured_ones` |
| SC-2 | 同上；`Every_named_line_is_written_at_the_offset_the_client_draws_it_from` |
| SC-3 | 同上；`Two_reasons_on_one_line_are_added_together` |
| SC-4 | 同上；`A_bonus_belonging_to_no_line_writes_nothing` + `CharacterExperienceGrantTests.ContributionsAreAddedAndCarriedBackNamed` |
| SC-5 | 同上；`The_slots_that_do_not_balance_are_never_written` |
| SC-6 | 同上；既有全帧扫描断言未改 |
| SC-7 | 同上；build 0/0，全量 1506 通过 / 2 跳过 / 0 失败 |

## Repository Result

- Changed paths: `Server/src/Dfo.Server.Abstractions/World/IExperienceBonusService.cs`、`Server/src/Dfo.Server.Protocol/Game/MonsterDeath/MonsterDeathSlice.cs`、`Server/src/Dfo.Server.Protocol/Game/DungeonClear/DungeonClearSlice.cs`、`Server/src/Dfo.Server.Protocol/Game/QuestCompletion/FinishQuestHandler.cs`、`Server/src/Dfo.Server.Protocol/Game/TutorialLevelUp/TutorialLevelUpSlice.cs`、`Server/tests/Dfo.Server.Protocol.Tests/Game/MonsterDeath/MonsterDeathCodecTests.cs`，以及 `.rks-agent/tasks/dungeon-run-experience/` 下的流程制品
- Baseline or accepted result: 基线 `ff413e0e`
- Parent rollback required: no

## Successor Guidance

- **可申报的行现在是九个，后继成果不要再往帧里加参数。** 新的加成来源只需给它的 `ExperienceModifier` 标一个 `ExperienceBonusKind`，帧会自己把它放到对的偏移上。
- **`+0x2F` 与 `+0x33` 永远不要建模。** 上游对它们都有名字，看起来完全像可填的槽；它们的不对称是客户端自身的，写进去就是让玩家看到的明细算不平。
- **测试锚 `Measured` 只允许由一次新的实测来改。** 把它跟生产表一起改成同一个错误配对，测试是全绿的——这层防不住重写判据。
- **`ExperienceChannelPolicy` 目前把 5% 当作服务端政策，这是错的。** `channel_info.etc` 的 `[server]` 段逐行声明了 `频道号 / 显示名 / 频道类型 / 副本组名 / 经验加成百分比`，`[dungeon]` 段声明每个组含哪些副本；抓包那 5% 出自档案而不是我们。这是 `spec-002` 的题目。
- **给 `spec-003` 的预先侦察（本轮顺手取得，尚未使用）**：`Etc/ServerParameter.etc` 在 A21 档案里确有 `[clear rank exp bonusrate] = 0.05 0.1 0.12 0.15 0.2`（上游代码的兜底 `0/0.01/0.02/0.03/0.05` 与本档案不符，不可抄）、`[clear exp bonusrate] = 1`、`[pc room party exp bonus rate] = 0.05`；`n_quest/questParameter.etc` 的 `[exp reward table]` 前几项为 `100 100 126 187 293 456 …`（`-1` 是分隔符）。上游通关基数取该表在副本 `[basis level]` 处的值，我们的 `DungeonLayout.BasisLevel` 与之对应。

## Artifacts

- `.rks-agent/tasks/dungeon-run-experience/attempts/spec-001-named-bonus-lines/spec.md`
- `.rks-agent/tasks/dungeon-run-experience/attempts/spec-001-named-bonus-lines/reviews/change-review.md`（`verdict: approved`，两轮，第一轮 `changes_required` 已修）
- `.rks-agent/tasks/dungeon-run-experience/attempts/spec-001-named-bonus-lines/verification/completion-evidence.md`
