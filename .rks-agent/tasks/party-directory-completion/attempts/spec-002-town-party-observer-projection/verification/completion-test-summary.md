# Fresh completion test summary

日期：2026-09-13。执行目录：spec-002 当前工作树，HEAD `2530f35bb0b5fb6d82603fbfaa9a492e37fc6375`
加上本 Spec 未提交结果。

## Command

```powershell
$env:PVF_ARCHIVE_PATH = 'E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-build --maxcpucount:1 `
  --results-directory '.rks-agent/tasks/party-directory-completion/attempts/spec-002-town-party-observer-projection/verification/completion-trx' `
  --logger 'trx'
```

命令 exit `0`。生成的 9 份 TRX 已逐条解析 `UnitTestResult`；原始 TRX 是约 8 MB 的临时输出，完成统计后
不纳入结果提交。

## Parsed results

| Test project | Passed | NotExecuted | Failed | Other |
|---|---:|---:|---:|---:|
| Dfo.Launcher.Tests | 63 | 0 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 | 0 |
| Dfo.Server.Application.Tests | 1410 | 0 | 0 | 0 |
| Dfo.Server.Host.Tests | 17 | 0 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 301 | 2 | 0 | 0 |
| Dfo.Server.LauncherApi.Tests | 50 | 0 | 0 | 0 |
| Dfo.Server.Networking.Tests | 62 | 0 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 353 | 0 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 811 | 0 | 0 | 0 |
| **Total** | **3127** | **2** | **0** | **0** |

两个 `NotExecuted`：

- `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`
- `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`

二者专门要求 archive 缺失；本轮为验证正常 PVF 路径显式设置 archive，故与本轮环境互斥。其余 3127 条均
为 `Passed`，没有遗漏 outcome。
