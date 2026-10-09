# 最终自动化检查

执行日期：2026-10-07，Release；测试目标为本工作区相对 `b596b06bcef121e6444127850980032115a1b0fb` 的最终产品与测试变更。

```powershell
dotnet build Server/86JP.Server.slnx -c Release --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol2/Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-build --no-restore -m:1 /nodeReuse:false --verbosity quiet --logger trx --results-directory Document/tasks/manual-hell-entry/verification/FinalTestResults
git -c core.whitespace=cr-at-eol diff --cached --check
```

构建退出0，0警告 / 0错误；完整测试退出0；完整暂存diff检查退出0。此Windows工作区保留原始CRLF日志与文档，检查以一次性的cr-at-eol识别CRLF，未修改仓库配置。构建和全量测试串行执行，使用当前绑定的真实 PVF。全量测试在沙箱外执行，既有 EventLog provider 未修改。

| 项目 | 通过 | 失败 | 跳过 |
| --- | ---: | ---: | ---: |
| Pvf | 80 | 0 | 0 |
| Application | 3118 | 0 | 0 |
| Host | 155 | 0 | 0 |
| HttpApi | 50 | 0 | 0 |
| Infrastructure | 783 | 0 | 2 |
| Networking | 90 | 0 | 0 |
| Persistence | 459 | 0 | 0 |
| Protocol | 1340 | 0 | 0 |
| 合计 | **6075** | **0** | **2** |

两项跳过为既有 `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog` 与 `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`；原因均为 `An archive is reachable from this working tree.`。不是深渊测试跳过，不将它们计入通过。

证据：[最终构建](final-build.log)、[最终全量日志](final-tests.log)、[逐项目 TRX 汇总与原始文件哈希](final-trx-summary.json)。原始 TRX 在本地 `FinalTestResults/` 保存并忽略，不作为需要跨机器恢复的唯一证据。

塔入口保护的有效失败复现见 [RED](tower-red.log)，修复后副本/塔相关103项见 [GREEN](tower-green.log)。首次全量运行因沙箱 EventLog 权限拒绝而中断，保留 [原日志](tests.log)；它不构成最终通过证据。相同Host启动定向检查在沙箱外4/4通过，见 [环境定位验证](host-environment-check.log)。

本记录证明 CurrentImplementation 的自动化行为，不证明主动深渊的真实客户端表现。柱交互、怪物波次和奖励闭环未纳入本次验收。
