# Task: 普通地下城

## Task Background

GitHub Feature Cycle #66 要求普通地下城形成地区/地图选择、难度、入场条件、疲劳、单人/组队、完成记录和开放难度的完整玩家闭环。

当前服务端已经具有普通地下城选择、PartyKey、共享 Run、Dungeon/Cell、过图、战斗、掉落、结算、翻牌和回城底座，但准入规则仍不完整：`character_dungeon_permissions` 只有读取路径，疲劳服务固定返回 `(188, 0)` 且不持久化，入场流程也无法正确表达按首次进入有怪房间扣除、逐成员准入和失败原子性。因此继续开发循环地下城会复制这些缺口。

本任务支持 M16，并以普通地下城作为后续循环地下城、镜像阿拉德、深渊和其他独立玩法复用的准入基线。

## Task Scope

本任务必须完成以下 GitHub 子任务：

- #101 普通地下城准入、难度与疲劳规则取证；
- #102 普通地下城地图开放与难度进度；
- #6 疲劳值真实账本、房间消耗、恢复和每日重置；
- #103 普通地下城组队准入与原子入场；
- #104 普通地下城单人/组队端到端验收与文档收口。

业务边界包括地图和难度开放、最低等级、普通地下城疲劳、全队逐成员检查、共享 Run 原子创建、通关进度写入、重登恢复和客户端拒绝投影。

本任务不实现循环地下城随机池与自动任务、深渊/塔/安徒恩专属次数或材料、独立掉落隔离、客户端拾取动作恢复，也不借机重写已经工作的通用战斗、掉落、结算和翻牌系统。

## Spec (SDD Backfill)

> Backfill this section during the SDD phase. When creating the plan, leave this slot in place and do not invent a spec.

- Spec path: To be backfilled
- Core approach: To be backfilled
- Acceptance criteria: To be backfilled

## Completion Notes

Backfill after completion: describe what was actually completed and whether it differs from the expected result. If there is a difference, describe the difference, cause, and follow-up.
