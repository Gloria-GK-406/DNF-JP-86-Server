# Baseline verification

- Revision: `9c63059147ebccac99bcfb9ca0bfec63f64ddb1f`.
- Initial `dotnet test --no-restore` could not start because the new worktree had no NuGet assets; this was an environment prerequisite, not a product failure.
- `dotnet test Server/86JP.Server.slnx -v quiet --nologo` restored assets and exited 0.
- Passed 3828; skipped 475; failed 0. PVF_ARCHIVE_PATH was not set, so PVF-dependent cases were skipped. This is not full content verification.
- Implementation verification must set `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf` and recheck relevant PVF cases.
- Output: [baseline-tests.log](baseline-tests.log).
