---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 在线身份一条记录、一把锁；在线角色状态一个无锁运行时

## Delivered

- “他是谁”只有一个真源：`OnlineStateRegistry` 在一把锁内持有在线用户、选中会话（`Selected` / `InWorld`、隔离标记）与端点；`ISelectedSessionDomainService` 新增 `EnterWorld`、`Fence`、`StandingOf`、`IsInWorld`。`SelectedSessionRuntimeDirectory`、`IActiveCharacterDomainService`、`activeByAccount`、`OnlineUser.CurrentCharacterId` 字段、`SelectedSessionSafety` 的会话集合全部删除。“连接已撤销、待清理”是可查询的阶段。
- “他怎么样”只有一个归属：新增 `IOnlineCharacterRuntimeDomainService`，按会话代数持有位置、血蓝百分比、物品冷却、灵药加成，无互斥锁（CAS + 不可变记录）；原四个服务及接口删除，全部调用方迁移。
- “是否存位置”不再存储，拆除时由频道推导。
- 文档与新模型一致。issue #208 记录了范围外的两条发现。

## Verification

- `verification/completion-evidence.md`（decision: pass）

## Repository Result

- Changed paths: `Server/src`（51 个文件）、`Server/tests`（123 个文件）、`AGENTS.md`、`Docs/CharacterEntryFacesInventory.md`、`Document/SelectedSessionCurrentView.md`、`Document/成长契约与精灵秘药经验.md`，以及本任务目录。
- Spec baseline: `7fc8acdc`
- Rollback: not applicable
- Parent rollback required: no

## Artifacts

- `reviews/task-contract-review-r1.md`、`reviews/task-contract-review.md`、`reviews/change-review.md`
- `verification/baseline.md`、`verification/logs/`
