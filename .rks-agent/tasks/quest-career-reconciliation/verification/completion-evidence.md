---
kind: rks-verification
decision: pass
---
# 完成证据

日期：2026-09-21。结论仅限本任务契约的代码、自动回归和实机步骤交付；不宣称客户端已验收。

## 契约覆盖
- SC-1：真实 PVF 7840 完成后 grow_type=4；普通任务回归保持原行为。CareerReconciliationTests、QuestCompletion 测试通过。
- SC-2：两页技能整理、赠送合并、快捷栏与合法越级技能保留、未知技能保留、幂等及点数检查通过。
- SC-3：注入技能写入故障，任务完成记录、经验、职业及技能全部回滚；重复完成拒绝，不重复奖励。
- SC-4：技能表先于 ACK；职业、属性、库存随后刷新，最后重发权威虚弱值。公共广播只发身份并检查源/目标会话代次与私有区域。特殊 ACK 字节向量通过，实机步骤已交付。

## 检查命令与结果
工作目录为本任务 worktree，PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf。

| 命令 | 结果 | 本地原始证据 |
|---|---|---|
| dotnet test Server/tests/Dfo.Server.Application.Tests --no-restore | 1703/1703，无失败、无跳过 | artifacts/career-verification/application-final.trx |
| dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-restore | 1026/1026，无失败、无跳过 | artifacts/career-verification/protocol-final.trx |
| dotnet test Server/tests/Dfo.Server.Persistence.Tests --no-restore | 361/361，无失败、无跳过 | artifacts/career-verification/persistence.trx |
| dotnet test Server/tests/Dfo.Server.Host.Tests --no-restore | 17/17，无失败、无跳过 | artifacts/career-verification/host-final.trx |
| dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet | exit 0，0 warning，0 error | 当前执行输出 |
| git diff --check | exit 0 | 当前执行输出 |

共3107项通过。Application/Persistence 的验证后未再修改其生产或测试代码；最后的协议接收者修复已重新运行全量 Protocol 与 Host。独立审查见 ../reviews/change-review.md。

## 实机边界
特殊转职 ACK 来自参照服，尚非 A21 ClientVerified；用户负责部署后的职业显示、技能与属性实机验收，见 Document/转职任务验收.md。本轮没有部署、没有修改玩家数据库，也未补偿234已完成任务7840的历史状态。
