# Post-review full-suite summary

Command:

```powershell
$env:PVF_ARCHIVE_PATH='E:\Project\DNF\DNFClient-A21-protocol\Script.pvf'
dotnet test Server/86JP.Server.slnx -c Release --no-build --maxcpucount:1 --logger trx
```

Nine fresh TRX files under `PostReviewResults/` were parsed by `UnitTestResult.outcome` after the independent Round 2
approval.

| Test assembly | Passed | Skipped | Failed |
|---|---:|---:|---:|
| Dfo.Launcher.Tests | 63 | 0 | 0 |
| Dfo.Pvf.Tests | 60 | 0 | 0 |
| Dfo.Server.Application.Tests | 1410 | 0 | 0 |
| Dfo.Server.Host.Tests | 17 | 0 | 0 |
| Dfo.Server.Infrastructure.Tests | 301 | 2 | 0 |
| Dfo.Server.LauncherApi.Tests | 50 | 0 | 0 |
| Dfo.Server.Networking.Tests | 62 | 0 | 0 |
| Dfo.Server.Persistence.Tests | 353 | 0 | 0 |
| Dfo.Server.Protocol.Tests | 811 | 0 | 0 |
| **Total** | **3127** | **2** | **0** |

The two skipped results are the PVF-present mutually exclusive negative tests
`PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message` and
`PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`.
