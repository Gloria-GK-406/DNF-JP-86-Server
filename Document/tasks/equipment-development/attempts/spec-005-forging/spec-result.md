---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result：装备锻造

## Delivered

- 普通 `0x01B7` variant0 的武器锻造按真实 PVF 上限、概率、材料 grade 与稀有度权重结算；成功增加独立 `GenuineUpgrade`，概率失败消耗材料而保级，不推断金币费用。
- 所有权、槽位、模板、锁定、过期、武器类型、耐久、上限及材料拒绝均不产生部分结算；同连接同序号重放与 SQLite 提交失败不双扣或半结算，原 UID、普通强化及其它装备字段保持不变。
- 提供 ACK/错误与提交后权威装备、材料刷新、真实 PVF/领域/字节/handler/真实 TCP 测试及集中实机 `Human-Test-List.md`。卷券变体和畸形动态尾安全隔离；未获得 A21 实帧/GUI 验收，不标 `ClientVerified`。

## Verification

- `verification/completion-evidence.md`：自动成功条件通过；真实 PVF Application 全量仍有两项既有宠物基线失败，明确排除于通过声明。
- `reviews/change-review.md`：独立完整增量复审 `approved`，记录两项中严重度发现、修正及残余边界。

## Repository Result

- Changed paths：本子项 `Document/tasks/equipment-development/`、`Server/`、`protocol/` 增量；接受前由完整状态核对。
- Baseline or accepted result：`c15e6add5590d5f957210763a0b1c118a93982c3`；接受提交由父控制器记录，不在本文件写自己的提交号。
- Parent rollback required：no
