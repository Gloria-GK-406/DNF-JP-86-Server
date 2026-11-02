---
kind: rks-verification
decision: manual_required
---

# 无限疲劳自动验证

结论：实现与自动验证完成。2026-11-02 变基到 `2f5df5a0e` 后，用户在 A21 实机确认疲劳不再扣除、活动显示在客户端活动页，并同意合并；
该次测试以用户结论为准，human-test.md 的逐项记录（零疲劳、组队、竞技、重启等）没有单独记录。

变基后完整 Application 回归：31 失败、6,410 通过、11 跳过；未修改的 `2f5df5a0e` 上失败集合完全相同（31 失败、6,398 通过）。下文数字是变基前基线 `1bc7d38b7` 的记录。

## 环境与范围

- 源码基线：`1bc7d38b7`，独立分支 `task/230-unlimited-fatigue-20261030`。
- SDK：10.0.302。测试只使用临时隔离数据库。
- 内容测试：`E:/Project/DNF/DNFClient/Script.pvf`，SHA-256 `573388731C394A71D5E75ED7DD215124B3D04383631D93116625ED353E06ACAB`。
- 用户明确只完成实现，暂不准备实机环境。玩家库未迁移，主干服务未切换，不创建 PR；A21 验收仍未进行。

## 已验证结果

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx --no-restore` | 0 警告、0 错误 |
| 解决方案 `Category=Unit` | 1,705 通过，0 失败、0 跳过；计数为实际匹配测试，部分项目无该分类 |
| 无限疲劳、入场材料、竞技、疲劳账本及依赖图专项 | 70 通过，0 失败、0 跳过 |
| 完整 Application 回归 | 6,387 通过、12 失败、11 跳过；12 个失败均在未修改主干复现，见下表 |
| GM 活动及宿主启动集成 | 26 通过；另加无限疲劳真实 HTTP 启停与版本冲突用例 1 通过 |
| `DbMigrate.Tests` 全部 | 25 通过，包含 v1→v2 数据保留及迁移/新建结构一致性 |
| `npm test -- src/Events.test.tsx` | 28 通过 |
| `npm run build` | 通过，产出 GM 资源；仍有既有的大 chunk 提示 |
| Git 空白检查 | `git diff --check` 通过 |

专项筛选：

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient\Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~UnlimitedFatigue|FullyQualifiedName~DungeonEntryMaterialTests|FullyQualifiedName~TournamentDungeonTests|FullyQualifiedName~FatigueTotalTests|FullyQualifiedName~SelectedSessionRefresh|FullyQualifiedName~DependencyGraphArchitectureTests' --logger 'console;verbosity=minimal'
```

覆盖：零余额与组队准入、最终消费重查、普通及竞技免扣、真实账本不写、配置修订重新应用、时间窗边界、
重复对账、同步订阅失败整体回滚、真实定时 CMD 逐活动隔离及下一轮重试、重开数据库与启动命令恢复、
停机跨过整个活动期不补发、活动期内才发 EVENT_INFO、控制写入方限定，以及 GM HTTP 乐观锁冲突保持原效果。

## 完整 Application 回归与基线对照

完整回归不报告全绿。使用上述 PVF 时，以下 12 个既有失败在原始 `1bc7d38b7` 的隔离源码上全部复现：

| 用例 | 失败身份 |
|---|---|
| `MarriageCapsuleItemUseTests.Corrected_wedding_guardian_ticket_can_be_picked_up_once_through_the_real_grant_pipeline` | `chn_stk_couple_surprise_ticket.stk` 的 `[stackable type]` 重复 |
| `AvatarCompoundTests.Rejections_leave_all_inputs_unchanged(reason: "expire")` | 预期无合成回执，实际产生回执 |
| `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` | 地下城 7100，cell (1,0) 没有兼容 Left 入口的 map |
| `DimensionDungeonTests.All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot` | 同上 |
| `WorldContentArchiveTests.A_high_level_character_is_offered_a_different_quest_set_than_a_low_level_one` | 任务候选断言失败 |
| `WorldContentArchiveTests.Clearing_a_quest_removes_it_from_the_offered_set` | 任务候选断言失败 |
| `WorldContentArchiveTests.Real_job_restrictions_change_the_offered_set_for_a_supported_subject` | 找不到预期职业限制样本 |
| `GiantDragonDungeonTests.Entry_plan_owns_each_quest_mode_and_directed_maze_policy` 的 3536/3540/3539/3537/3538 五组参数 | `Giant dragon dungeon or native entrance is missing/incompatible` |

对照源码通过 `git archive 1bc7d38b7 Server Tool global.json` 导出到本 worktree 的忽略目录，未修改原主干工作区。
第一组对照运行 13 项，4 失败/9 通过；第二组 11 项，8 失败/3 通过，失败身份与本次扩展回归一致。
这些失败不在无限疲劳功能中修复；不把所有失败统一归因为 PVF，尤其时装到期断言仍是既有问题。

## 失败路径证据

定时 CMD 的回归曾通过以下隔离命令证明：正常业务回滚后没有将 action 失败上报调度器。
当时退出 1，决定性输出为 `Assert.Throws() Failure: No exception was thrown`。

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Scheduled_failure_rolls_back_one_activity' --logger 'console;verbosity=normal'
```

修正保存 action failure 并进入调度完成桥后，同一行为纳入上述 70 项通过的专项。
GM 单次启停的原生帧集成断言也证明了避免重复 EVENT_INFO 刷新的需求；最终 26 项集成已通过。

原始日志保存在本 worktree 的 `artifacts/unlimited-fatigue/verification/`，包括构建、Unit、专项、迁移、
宿主、前端、完整回归及两组基线对照。日志不等同于客户端验收证据。

## Unverified Conditions

- 以下为变基前记录；用户 2026-11-02 的实机结论见文首。
- A21 真实零疲劳选图、换房、重新挑战及双客户端组队显示，按用户要求留待后续。
- 使用当前玩家库的实际迁移、服务切换及重启验收，本轮未执行。
- 完整回归中的 12 个既有失败与 11 个跳过不能视为通过；本功能不修复这些无关问题。
