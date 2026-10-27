---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 「会话仍有效」「角色在线」收成具名判定

## Delivered

- `ISelectedSessionDomainService` 提供三个具名判定，都由 `OnlineStateRegistry` 在一次加锁内回答：
  - `IsSelected`：当前选中，并且连接仍归它，不论阶段；
  - `IsInWorld`：在此基础上已从它自己的游戏连接进入世界，比原来更严；
  - `TryGetInWorld`：按角色、线上号或连接查找，三个重载。

  `IsActiveExact` 已删除。
- 生产代码里的当前性写法全部换成具名判定，并按用途分级：
  - 「查出来再比较」和在线用户 `CharacterActive` 标志组合，0 处；
  - 「查不到就当不在线」的写法，改为 `TryGetInWorld`；
  - 纯当前性 helper，0 处；
  - 选角工作流用 `IsSelected`，共 5 处；
  - 清理路径用 `StandingOf` / `ReleaseExact`；
  - 其余玩法、投影、投递一律用 `IsInWorld` / `TryGetInWorld`；
  - 剩下的 `Find…` 都归入 Spec 中的 (a)～(f) 保留类别。
- 收紧：玩法路径不再接受尚未入世或连接已撤销的会话。涉及旁人的查找，原来查不到会抛异常，现在改为跳过这个人。清理路径（包括宠物离场结算）照旧接受连接已撤销的记录。
- `AGENTS.md` 已更新，并新建 issue #209，记录写操作中按数据库再查角色归属的问题。

## Verification

- `verification/completion-evidence.md`（decision: pass）

## Repository Result

- 改动范围：`Server/src`、`Server/tests`、`AGENTS.md`，以及本任务目录。
- 提交：
  - 实现 `97c5af5d`；
  - 第一轮审查修复 `7b55d4bf`；
  - 第二轮审查修复 `a29d7586`；
  - 测试断言收紧 `b01f6841`。
- Spec 基线：`ea6a1544`
- 回滚：不适用
- 是否需要父级回滚：否

## Artifacts

- `reviews/task-contract-review.md`（r1）、`reviews/task-contract-review-r2.md`
- `reviews/change-review.md`：完整审查、修复复审、第二轮修复复审
- `verification/baseline.md`、`verification/logs/`
