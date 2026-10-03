---
kind: rks-verification
decision: pass
---

# 迷妄之塔最终验证

2026-10-03；对象为 `task/warehouse-storage`、基线 `762d0cb4` 上的完整迷妄之塔实现，工作区 `E:/Project/DNF/DNF-JP-86-Server/.worktrees/warehouse-storage`。声明范围是 [TaskContract](../TaskContract.md) SC-1 至 SC-8 的服务端交付与自动化验证，不包含 A21 实机验收。

## 新鲜验证

所有 .NET 命令串行执行；restore、build、test 均 exit 0。

```powershell
dotnet restore Server/86JP.Server.slnx
dotnet build Server/86JP.Server.slnx -c Release --no-restore -p:ContinuousIntegrationBuild=true
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 --logger trx --results-directory artifacts/test-results-illusion-tower-final-20261003 --blame-hang-timeout 3m --blame-hang-dump-type none
```

Release 构建零警告、零错误。PVF SHA-256 为 `3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06`。完整测试输出见 [release-test.log](release-test.log)；本地 TRX 位于上述 results-directory。

| 项目 | 通过 | 失败 | 跳过 |
|---|---:|---:|---:|
| Pvf | 80 | 0 | 0 |
| Application | 2889 | 0 | 0 |
| Host | 118 | 0 | 0 |
| Http | 50 | 0 | 0 |
| Infrastructure | 768 | 0 | 2 |
| Networking | 87 | 0 | 0 |
| Persistence | 446 | 0 | 0 |
| Protocol | 1273 | 0 | 0 |

8 项通过数相加：80 + 2889 + 118 + 50 + 768 + 87 + 446 + 1273 = 5711。两个跳过项是 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` 和 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`：检测到当前档案可用时按设计跳过缺文件情景。另起无 PVF 环境的进程补跑：

```powershell
Remove-Item Env:PVF_ARCHIVE_PATH -ErrorAction SilentlyContinue
dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~A_missing_archive_fails --logger trx --results-directory artifacts/test-results-illusion-tower-missing-pvf-20261003
```

补跑 exit 0，2 通过、0 失败、0 跳过。因此全量的 5713 项均在对应环境中执行通过；不把原全量结果改写成零跳过。

其他新鲜检查全部通过：WireSmoke Python 测试 5 项、Release 工具测试 11 项；协议目录检查器 `--self-test` 与 `--repo-root .` 均零失败，覆盖 123 个 C2S、178 个 S2C（81 CMD、97 NOTI）及 30 行显式映射。目录检查证明源码身份映射，不证明实帧语义。

## 成功条件证据

| 条件 | 决定性证据与当前结果 |
|---|---|
| SC-1 | [独立评审](../reviews/change-review.md) 确认 `IllusionTowerDomainService → IllusionTowerDgn → TowerRoom` 独立所有权和生产调用路径；本次 Application 全量含架构回归通过。 |
| SC-2 | [TASK-01](../tasks/TASK-01.md) 的真实 PVF/APC 规则及 [TASK-07](TASK-07-verification.md) 的生产内容公共入口完整 15 层测试均进入本次全量通过集合。 |
| SC-3 | [TASK-02](../tasks/TASK-02.md) 的扣券/拒绝/事务回滚/归属互斥及 [TASK-06](TASK-06-verification.md) 的真实双成员入场与 intent 消费回归通过。 |
| SC-4 | [TASK-07](TASK-07-verification.md) 通过 0010、0025、009F、0027、002D 串联实际 15 层；重复/迟到动作、房间版本及未清场推进保护的领域与协议回归通过。 |
| SC-5 | 领域各结束原因、0084 返回、再次扣券入场及旧 sweep 捕获 context 后的新 run 保护均通过；公共入口测试使用真实领域与隔离 SQLite。 |
| SC-6 | [TASK-03](TASK-03-verification.md) 的结算与资产事务、任务物集成及 [R3 修正](review-remediation.md) 的首败次成/持续失败/替代会话均在全量通过集合；经验、金币、装备不重复提交。 |
| SC-7 | Protocol 编解码/注册、0092 的 0/1 向量与 Networking 塔帧 TCP 测试通过；知识库更新和目录检查通过。公共 15 层 handler 测试记录投影，不冒称全程 TCP 测试。 |
| SC-8 | 8 个测试项目及缺文件补跑零失败；完整结果 [独立评审批准](../reviews/change-review.md)；[human test list](../human-test-list.md) 集中列出待实机项目。 |

## 交付限制

没有新增 WireCaptured 或 ClientVerified。A21 首层、跨层画面、结算界面、按钮和队伍表现仍待实机；70Reference 奖励和返回时序不提升为原服已确认语义。D-001 的慢连接持有同局账号门风险仍保留。未部署、修改玩家数据库或客户端二进制。
