---
kind: rks-spec-result
contract:
  path: ./TaskContract.md
  revision: 1
spec:
  path: ./spec.md
outcome: completed
terminal_stage: verification
---

# Spec Result: Character-scoped PartyService directory

## Delivered

- 建立独立 `IPartyService`/`PartyService`，统一承载队伍行为、成员关系、邀请、端点和目录索引；
  公开 Party 边界只认识 Character 与不可变值对象。
- 目录按逻辑 `ChannelId + TownId + AreaId` 的共享城镇可见范围一次返回全部匹配快照，并支持
  Category 与 ModeTarget 组合过滤；private gate 和非城镇 presence 不进入公共目录。
- 队伍归属随当前队长范围和队长转移即时迁移，解散或离开公共范围后不留下旧目录条目。
- Session 继续留在登录替换、exact teardown 和协议投递层。replacement 注册前完成旧角色清理；
  通知捕获 exact session，不会把旧通知发送给 replacement connection。
- 城镇队伍发现改为调用 `IPartyService.Query`，不再扫描 Session 反推队伍。
- 保持 A21 既有 codec、通知顺序以及建队、改名、解散、刷新消行所依赖的协议行为。

## Verification

- `verification/completion-evidence.md`：`decision: pass`，SC-1 至 SC-7 全部通过。
- `reviews/change-review.md`：独立终审 `verdict: approved`，无阻断项。
- 新鲜完整验证：0 warning、0 error；3142 total、3140 passed、0 failed、2 intentional skips。
- Party 禁止边界扫描无匹配，`git diff --check` 通过。

## Deferred boundary

- 本轮没有再次执行双客户端实机回归，因此没有新增或提升 `ClientVerified` 证据；既有实机行为由
  自动化协议回归守住，仍可在后续客户端可运行时复测。
- 剧情地下城进入 bug、`debug-lab`、跨进程持久化、合并、推送及工作区清理不属于本结果。
- D-001、D-002 两项非阻塞优化记录在 `Document/plan/technical-debt.md`。
