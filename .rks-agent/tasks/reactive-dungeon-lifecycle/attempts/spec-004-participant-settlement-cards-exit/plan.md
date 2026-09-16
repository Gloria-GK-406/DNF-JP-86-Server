---
kind: rks-plan
spec:
  path: ./spec.md
mode: direct
state: complete
---

# Implementation Plan

1. RED：补 ClearOpen 结算门、共享 seat/column 约束与完整 change 测试。
2. GREEN：扩展 DungeonRun participant progress 与 shared card selections；让成功 card commit 返回 change。
3. RED/GREEN：扩展 `SelectCardResponse` 为完整快照，增加逐接收者 card projection，保留 actor 私有 inventory/gold。
4. RED/GREEN：增加可控的 card presentation scheduler/coordinator；统一客户端 layout 请求、2 秒自动 layout、4 秒 auto-free 和手动竞态。
5. 将 SetPlayResult、ScoreScroll、CardSelectRightState、SelectCard、EPLP/GiveUp 接到 coordinator；离场后旧回调自然失效并主动取消句柄。
6. 增加双人、单人、ordinary 回归；完成独立 change review、Release build 和完整测试。
