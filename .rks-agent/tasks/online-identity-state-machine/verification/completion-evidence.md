---
kind: rks-verification
decision: pass
---

# 完成验证：在线身份一条记录、一把锁；在线角色状态一个无锁运行时

**验证对象**：分支 `task/refactor-session-20261021`，HEAD `e9498353`（实现提交 `e337660b` + 审查修复提交 `e9498353`），Spec 基线 `7fc8acdc`。

**结论**：TaskContract revision 2 的 SC-1～SC-7 都有当前证据，判定 pass。

## 条件与证据

| 条件 | 结果 | 证据 |
|---|---|---|
| SC-1 平行存储删除、“是否存位置”不再存储 | 通过 | `logs/ac001-grep.txt`：`Server/src` 中 `SelectedSessionRuntimeDirectory`、`IActiveCharacterDomainService`、`activeByAccount`、`SelectedSessionSafety` 的会话集合、`TryGetPositionPersistence`、`IsPublishedExact` 全部 0 命中；`[Obsolete]` 0 个（基线同为 0）。拆除时按频道推导，见 `SelectCharacterLifecycleServices.cs` 与 AC-009 测试 |
| SC-2 查询与阶段判定由一个组件在一致性边界内回答 | 通过 | 实现：`OnlineStateRegistry` 单一 `sync` 持有在线用户、选中会话、端点；`SelectedSessionDomainService` 无自有状态。测试：`Sessions/OnlineIdentityTests`（10 个，全过） |
| SC-3 各路径阶段转移正确，“连接已撤销、待清理”可查询并在清理后消失 | 通过 | `OnlineIdentityTests`（选中→在世界→隔离→撤销、进入被拒、释放、替换、撤销待清理）；`OnlineUserServiceTests.A_rejected_connection_is_cleaned_up_by_its_disconnect_and_leaves_nothing`；既有选角/顶替/拆除/断线流程测试（Application、Protocol、Networking）全部通过 |
| SC-4 旧会话不影响新会话，拆除后迟到更新不复活 | 通过 | `Characters/OnlineCharacterRuntimeTests`（替换会话读写隔离、迟到写入不复活）；`SessionGenerationOwnershipTests`；`ApplyEffectsTests.Replaced_session_does_not_receive_an_old_cooldown_commit` |
| SC-5 四类在线状态单一归属、原服务删除、无互斥锁、并发不丢失 | 通过 | `logs/ac001-grep.txt`（旧四个服务与接口 0 命中）；`logs/ac008-no-lock.txt`（实现文件无锁原语）；`OnlineCharacterRuntimeTests.Concurrent_updates_of_one_session_are_all_kept`、`Ending_while_writes_are_in_flight_leaves_no_record`、`The_runtime_takes_no_lock` |
| SC-6 构建、测试、文档 | 通过 | `logs/build-test.txt`：构建 0 警告 0 错误；全量测试仅基线已有的 2 个 `DimensionDungeonTests` 失败（迷宫内容问题，见 `baseline.md`）。测试数相对基线：Application.Tests 4301 → 4323（+22：OnlineIdentityTests 10、OnlineCharacterRuntimeTests 8、SessionGenerationOwnershipTests 2、OnlineUserServiceTests 1、ApplyEffectsTests 正向对照 1），其余 7 个项目数量不变。文档：`AGENTS.md`、`Document/SelectedSessionCurrentView.md`、`Document/成长契约与精灵秘药经验.md`、`Docs/CharacterEntryFacesInventory.md` 已更新 |
| SC-7 向用户汇报 | 待最终汇报完成 | 由控制者在封存后发送 |

## 审查

`reviews/change-review.md`：完整改动审查 approved；修复提交复审 approved。

## 有意的行为与断言调整（已经审查确认）

- 冷却、灵药补齐代数隔离；持有自己会话的读取改为按会话精确读取（Spec RQ-012）。
- `OnlineUserServiceTests.Disconnect_OnlyAcceptedOwnerRemovalClearsActiveCharacter`：断线后选中会话记录保留到拆除（RQ-009），断言改为“不再活跃、显示连接已撤销”。
- `OnlineExpiredItemCleanupTests.Queued_cleanup_revalidates_published_session_after_account_gate`：“已取消发布但仍在线活跃”的中间状态不复存在，排队清理改为在入口被拒；物品与待投递断言不变。

## 限制

- 自动化测试不替代 A21 客户端实机验收（选角、顶替、断线、城镇移动、组队进图）。
- 运行 `Host.Tests` 时曾有一次 `LauncherHttpHostStartupTests` 超时，之后连续三次全量运行通过，判断为负载下的偶发超时，与本改动无关。
