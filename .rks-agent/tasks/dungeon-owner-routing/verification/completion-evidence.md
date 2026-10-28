---
kind: rks-verification
decision: pass
---

# 完成验证：副本归属命令先定位、再单次路由（#203）

## 声明

分支 `task/dungeon-owner-routing-20261028` 在 `b6c17eb9` 满足 TaskContract（revision 2）的 SC-1～SC-6，以及 SC-8 中本地完整测试的部分；Spec 基准为 `7adec858`。

以下各项不在本次封存时的证据范围内：
- SC-7 的 issue 评论与 SC-8 的 PR、CI、Codex 审核：这三项在封存之后的 PR 流程中完成，结果在 PR 与 issue 上可查；
- A21 客户端实机行为：不是契约成功条件。

整体审查覆盖 `7adec858..b6c17eb9`，结论 approved（`../reviews/change-review.md`）。

## 条件与证据

| 条件 | 结论 | 决定性证据 |
|---|---|---|
| SC-1 入口判一次陈旧，通道不判 | 满足 | 结构守护 `DungeonOwnerRoutingSourceTests` 规则一与顺序检查，及其反例（TASK-006）；变异 G3、G5 被守护拒绝；整体审查第 1 项代码检查 |
| SC-2 定位一次、单通道，0x000F 唯一例外 | 满足 | 守护规则二～四（含 0x000F 固定一次定位），变异 G1、G2、G2b、G4、G6 被拒绝；`DungeonLocatorTests` 17 例（AC-003）；整体审查第 1 项 |
| SC-3 陈旧帧在各位置丢弃 | 满足 | `InRunCommandStaleFrameTests`（30 格）、`ExitAndSelectionStaleFrameTests`（20 格）、`PartyCommandStaleFrameTests`（10 格），以及 TASK-001 中“保持丢弃”的 52 格；授权变化格逐格标注见 TASK-003～005 Result，整体审查确认与 Spec 清单一致 |
| SC-4 新鲜帧结果与基准一致 | 满足 | TASK-001 表征测试 155 例在 `68c34f8e` 提交，之后 TASK-002～006 没有修改这些文件（整体审查用 `git log` 核对），并在下方完整运行中通过 |
| SC-5 窗口、整队归属、0x000F 例外 | 满足 | TASK-001 中的会话代次窗口用例（单人 22 格、组队 16 格）与整队 Boss 优先用例；`EnterSelectionBossHandOverTests` 与 TASK-001 的 0x000F 释放后续选图用例 |
| SC-6 陈旧定义唯一且中性 | 满足 | `GameRequestFreshness.IsStale`；`git grep TowerProtocol -- Server` 无结果（TASK-006）；守护规则一 |
| SC-7 文档唯一归属 | 文档部分满足；issue 评论在 PR 流程中完成 | `Document/ProtocolCommands.md`“副本归属分发与陈旧帧”；三处旧顺序注释已删除（TASK-006 grep 与整体审查第 5 项） |
| SC-8 完整自动测试 | 本地满足（附归因）；CI 与 Codex 在 PR 流程中完成 | 见下节 |

## 完整测试（`b6c17eb9`）

构建 `dotnet build 86JP.Server.slnx -warnaserror`：0 错误（`logs/build.log`）。

**不带 PVF**（`logs/full-test-no-pvf.log`，`exit=0`）：

| 项目 | 通过 | 跳过 | 失败 |
|---|---|---|---|
| Pvf | 73 | 9 | 0 |
| HttpApi | 59 | 0 | 0 |
| Infrastructure | 655 | 348 | 0 |
| Protocol | 1990 | 60 | 0 |
| Networking | 189 | 20 | 0 |
| Persistence | 484 | 0 | 0 |
| Host | 200 | 12 | 0 |
| Application | 5512 | 493 | 0 |

**带真实 PVF**（`artifacts/refactor-session-20261021-closeout/preserved/Script.pvf`；`logs/full-test-pvf.log`，`exit=1`）：

- Pvf 82、HttpApi 59、Protocol 2055、Persistence 484、Networking 210、Host 212：全部通过，无跳过。
- Infrastructure：1001 通过，2 跳过。
- Application：6106 通过，3 失败，均与本次改动无关：
  - 2 个 `DimensionDungeonTests`：`Dungeon '7100' maze '0': cell (1,0) has no compatible map for entrance 'Left'`。这是既有的内容缺陷，#202 完成验证已在基准上复现（`.rks-agent/tasks/dungeon-run-party-roster/verification/logs/baseline-1cff7218-dimension-pvf.log`），本次与副本路由无交集。
  - 1 个 `AvatarCompoundTests.Rejections_leave_all_inputs_unchanged(reason: "expire")`。用同一 PVF 在基准 `7adec858` 的临时工作树上单独运行，以相同断言失败（`logs/baseline-7adec858-avatar-compound-pvf.log`）。
    - 原因：#200 之后装扮的期限只读 `AvatarDetail.ExpireDate`，该用例仍给装扮设置核心字段 `ExpireTime = 123`，所以输入不被视为过期。
    - 该用例只在有 PVF 时运行，#200 当时在本机被跳过。这是 #200 遗留的测试夹具问题，见 `../spec-result.md` 的后续事项。
- 此前各 Task 中因缺 PVF 而跳过的 `IllusionTowerPublicFlowTests`，这次随完整运行执行。另外单独带 PVF 运行一次，2 例都通过（`logs/illusion-public-flow-pvf.log`）。

## 未纳入本验证

- PR 当前提交的 CI、Codex 审核，以及 #203、#187 的范围说明评论：在 PR 流程中完成。
- A21 客户端实机行为：陈旧帧统一属于授权的行为变化，实机确认由用户进行。
