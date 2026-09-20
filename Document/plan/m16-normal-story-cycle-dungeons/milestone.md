# Milestone: M16 普通、场景与循环地下城

## Goal

在共用 Dungeon/Cell 运行时之上，分别完成普通地下城、场景地下城和循环地下城的选择、准入、规则与奖励闭环，使三种入口共享基础设施但不混淆业务语义。

## Current Status

- Status: 🟡
- Prerequisite milestones: M15 通用地下城运行时（现有战斗、过图、掉落、结算底座可用；其余缺口独立跟踪）
- Last updated: 2026-09-20

## Scope

本 Milestone 负责三种地下城的入口选择、地图/难度开放、准入、疲劳或专属消耗、组队进入、完成记录和玩法特有奖励。通用房间战斗、掉落和结算继续由 M15 承载；深渊、塔、异界、安徒恩等独立玩法不并入本 Milestone。

当前唯一激活的 Feature Cycle 是 GitHub #66 普通地下城。场景地下城 #67 和循环地下城 #68 保持未启动，不与当前任务并行展开。

## Target Outcome

- 普通地下城可按等级、地图、难度和疲劳正确准入，单人及组队完整可玩；
- 场景地下城始终由任务和剧情进度决定目标，不被普通选择错误路由；
- 循环地下城从当前区域的合法池中选择地图并处理自动任务和循环奖励；
- 三种玩法共享 Dungeon/Cell、PartyKey 和投影基础，但各自只有一个规则真源；
- 失败路径不创建半局、不提前扣除资源、不污染开放或完成记录；
- 自动化、协议记录与 A21 实机验收能够分别证明实现状态和客户端行为。

## Status Icons

- ✅: Completed
- 🟡: In progress or needs attention
- ⬜: Not started

## Task Board

| Task | Scope | Path | Status |
| --- | --- | --- | --- |
| 普通地下城（GitHub #66） | 地图/难度开放、等级与疲劳准入、单人/组队原子入场、完成记录和验收 | `Document/plan/m16-normal-story-cycle-dungeons/normal-dungeon.md` | 🟡 |
| 场景地下城（GitHub #67） | 任务绑定目标、剧情演出、任务推进和组队共享 | `Document/plan/m16-normal-story-cycle-dungeons/story-dungeon.md` | ⬜ |
| 循环地下城（GitHub #68） | 区域地图池、随机选择、自动任务、奖励箱和组队模式 | `Document/plan/m16-normal-story-cycle-dungeons/cycle-dungeon.md` | ⬜ |
