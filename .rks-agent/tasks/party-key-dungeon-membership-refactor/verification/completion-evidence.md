---
kind: rks-verification
decision: manual_required
---

# 完成验证：PartyKey 与副本成员真源

声明边界：隔离工作区的代码实现、Release 构建/测试和独立完整审查已完成；TaskContract 的 A21 双客户端成功条件尚无实机证据，因此整个契约**不能标为完成**。

## 已验证

| 条件 | 当前证据 |
|---|---|
| SC-1 | `PartyService` 领域身份由稳定 `PartyKey` 承载，真实/虚拟反查和客户端 ID 换届回归通过；完整审查 approved。 |
| SC-2 服务端/投影部分 | 队长退出后继任与设置保留、旧 ID 清除到新 ID 名册的协议顺序测试通过。交错回归 `TownReadRepairCannotSupersedePendingLeaderRekeyClearForSurvivor` 修前失败、修后通过。 |
| SC-3 服务端部分 | RunId/已创建 Cell 连续、离队者和图外新成员被拒、通知收件人与旧连接排除的领域及协议测试通过。 |
| SC-4 | 新 Cell 使用当前成员、已创建 Cell 不重算、离队专属结果跳过和共享结果保留的测试通过。 |
| SC-5 自动化部分 | 真实/虚拟单人及多人路径的领域/协议回归通过。 |

新鲜检查（2026-09-17，当前差异）：`dotnet build Server/86JP.Server.slnx -c Release --no-restore -p:BaseOutputPath=bin/codex-review/ -v:q` 退出码 0，0 警告/错误；设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol2\Script.pvf` 后 `dotnet test Server/86JP.Server.slnx -c Release --no-restore -v:q` 退出码 0，共 3371 通过、2 跳过；`git diff --check` 退出码 0。独立审查：`../reviews/change-review.md`，verdict `approved`。其中 2 个跳过为 Infrastructure 测试，未计入通过数。

## Unverified Conditions

- SC-2：A21 双客户端是否在 B 端正确显示新队长和“是否保留队伍”提示。
- SC-3、SC-5：A21 双客户端在队长离队后是否保持原地下城连通、继续过图，以及完整单人/多人流程无视觉或 P2P 退化。
- 实机步骤与预期见 `Document/PartyKey-Dungeon-Membership-A21-Acceptance.md`。尚无部署授权，主干及运行中 Host 未变；不得用自动化测试替代这些客户端观察。
