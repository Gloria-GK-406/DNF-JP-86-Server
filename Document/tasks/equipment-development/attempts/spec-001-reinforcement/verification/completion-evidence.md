---
kind: rks-verification
decision: pass
---

# 普通强化自动验证

验证声明：普通强化 Spec 的服务端实现、静态协议解释与人工验收包已经按本子项合同交付；**不声明 A21 客户端实机通过**。基线 `a790a243`，独立审查 `reviews/change-review.md` 第二轮 `approved`。

| 条件 | 当前证据 |
|---|---|
| SC-1 / AC-001、002 | `evidence-and-design.md` 与 `protocol/game/*/0x0050` 区分 70Reference、UpstreamReference、ClientStatic、CurrentImplementation；真实 PVF 内容测试 12/12；强化领域/handler 定向测试 38/38，包括锁定、损坏、金币/材料不足、错误虚拟槽、+19 权重缺项。 |
| SC-2 / AC-003—005 | 确定随机源覆盖成功、保留、降级、破坏、保护；真实 SQLite 断言装备 UID 和 item_core、材料/金币/保护券/破坏产物，提交失败回滚、同序列重放及切角色；`0x0050` 成功、破坏、错误字节测试和通知集成测试。协议测试全量 1176/1176。 |
| SC-3 / AC-006 | `Human-Test-List.md` 有 R-01 至 R-07 的样本、操作、结果、重登、抓帧和停测条件，均明确为待用户执行。 |

最终检查：`dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet` exit 0，0 警告、0 错误；设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后 Infrastructure.Tests 403 通过、2 跳过，Protocol.Tests 1176/1176；Application.Tests 强化定向 38/38；不设置 PVF 时 Application.Tests 2026 通过、171 跳过；`git diff --check` 无输出。

广域回归限制如实保留：带真实 PVF 的 Application.Tests 全量是 2205 通过、3 失败，分别为两项 `CreatureGoesBackOnTests` 的宠物种子写库失败及一项 `PaidCardFlipCostTests`；后者单独重跑通过，根因未证，不能当作全量通过。前两项可单独重现，发生于未修改的宠物持久化路径。普通强化定向、内容及协议测试均通过；这些其他领域失败不用于宣称整套服务已通过验收。A21 `0x0050` 尚无实帧或用户 GUI 结果，需按 Human Test List 后续验证。
