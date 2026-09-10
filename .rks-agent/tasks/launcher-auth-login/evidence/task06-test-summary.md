# TASK-06 fresh Release verification

`dotnet build Server/86JP.Server.slnx -c Release --no-restore` completed with 0 warnings and 0 errors.

The first per-project run intentionally preserved the current shell environment and exposed the missing
`PVF_ARCHIVE_PATH` prerequisite: Infrastructure skipped 208 archive cases, Application skipped 116, and
Protocol reported 42 direct `Script.pvf was not found` failures plus one downstream socket EOF. This is
configuration evidence, not an accepted green run.

The complete run then set
`PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` (72,894,573 bytes) and executed every test
project separately. TRX results:

| Project | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| Dfo.Launcher.Tests | 60 | 0 | 0 | 60 |
| Dfo.Pvf.Tests | 60 | 0 | 0 | 60 |
| Dfo.Server.Application.Tests | 1341 | 0 | 0 | 1341 |
| Dfo.Server.Host.Tests | 17 | 0 | 0 | 17 |
| Dfo.Server.Infrastructure.Tests | 301 | 0 | 2 | 303 |
| Dfo.Server.LauncherApi.Tests | 50 | 0 | 0 | 50 |
| Dfo.Server.Networking.Tests | 62 | 0 | 0 | 62 |
| Dfo.Server.Persistence.Tests | 355 | 0 | 0 | 355 |
| Dfo.Server.Protocol.Tests | 714 | 0 | 0 | 714 |
| **Total** | **2960** | **0** | **2** | **2962** |

The two archive-present skips are the deliberately opposite missing-archive cases:

- `PvfTownDefinitionSourceTests.A_missing_archive_fails_rather_than_yielding_an_empty_town_catalog`
- `PvfQuestDefinitionSourceTests.A_missing_archive_fails_with_an_actionable_message`

Both passed in the preserved no-archive Infrastructure TRX, so every test identity has a passing execution
in its required archive-present or archive-absent environment. The archive-present Host TRX includes the
packaged HTTP -> Channel Info -> Game ticket end-to-end test and all 17 Host cases passed.
