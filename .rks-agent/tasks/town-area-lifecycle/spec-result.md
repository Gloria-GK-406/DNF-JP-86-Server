---
kind: rks-spec-result
outcome: achieved
terminal_stage: verification
---

# Spec Result：城镇区域生命周期与主动退本归一

## Delivered

- 新增独立的 `ITownAreaDomainService`，以逻辑 `ChannelId + TownId + AreaId` 惰性维护成员、lease、
  共享可见性和实时位置；领域对象不保存连接、端点、listener 或 selected session。
- 选角、区域内移动、跨区、选图离开、取消选图、实际进本、EPLP/GiveUp 回城和 teardown 统一接入
  区域生命周期；生产旧 `SessionPresence` 模型与注册已删除。
- 城镇投影按区域串行并在发送前重读最新权威快照；多人批量回城从最终状态构造全部 direct roster，
  避免迟到旧名单覆盖新成员。
- 回城和进本 commit boundary 使用 account transition gate 与 exact-session 复核；进本 preparation
  失败先回滚 run，成功后先同步提交全员离区，再交付首房间和区域投影。
- 活跃 run 的 `GiveUpGame` 复用现有 `LeavePartyCommand` 与 Party 通知；EPLP 和取消选图保持队伍。

## Verification

- [`verification/completion-evidence.md`](verification/completion-evidence.md)：`decision: pass`。
- [`reviews/change-review.md`](reviews/change-review.md)：Round 4 `verdict: approved`，全部 finding 关闭。
- Release build：0 warning、0 error；完整串行 solution 测试：3264 passed、0 failed、2 expected skip。
- 领域/应用定向测试：26/26；协议生命周期与投影定向测试：53/53；`git diff --check` 通过。

## Repository Result

- Baseline：`1e75dc85`。
- Result：本文件所在的单一任务提交，分支 `task/town-area-lifecycle`。
- 尚未合并、部署或清理工作区；这些操作需要用户另行确认。
- Parent rollback required：no。

## Scope Boundary

地下城内远端角色模型/P2P 可见性、登录票据 180 秒语义、SQLite→MySQL、奖励和任务触发规则均未改动。
