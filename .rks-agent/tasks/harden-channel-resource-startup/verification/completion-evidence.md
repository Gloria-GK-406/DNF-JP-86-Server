---
kind: rks-verification
decision: pass
---

# Completion Verification: Harden channel resource startup

## Claim

The completed change makes the packaged Host configuration and required channel script independent
of the process working directory, rejects an unavailable script before listeners start, preserves
the existing Channel wire exchange, and packages and documents the resulting startup behavior.

## Condition-to-Evidence Mapping

| Condition | Fresh evidence |
|---|---|
| SC-1 | `Dfo.Server.Host.Tests` launched the real built Host from an unrelated temporary working directory and passed 4/4; a separate published Host smoke also reached `Application started` from an unrelated directory. |
| SC-2 | The positive real-process test completed `0x0B→0x0C→0x09→0x0A→0x11→0x12`, decrypted the unchanged responses, and verified the packaged script and configured Game port. |
| SC-3 | The three negative real-process cases (missing, empty, and non-file/unreadable override) exited nonzero with the resolved path in the error and left the chosen listener unavailable. |
| SC-4 | The real-process default path resolved from the executable root; `ChannelSourcesTests` passed 1/1 for an explicit absolute override and immutable snapshot behavior. |
| SC-5 | The published Host log contained the effective canonical content root, packaged channel-script path, Channel/Game advertised addresses, and Channel/Game listener ports. |
| SC-6 | Real-PVF full solution: 500 passed, 2 intentional missing-archive skips, 0 failed. Host build: 0 warnings, 0 errors. Baseline diff check: clean. |
| SC-7 | `Docs/HostStartup.md` records the executable-root rule, authoritative packaged path, validation/snapshot behavior, startup summary, run/publish commands, and code-reversed protocol caveat. |

## Commands and Results

- `PVF_ARCHIVE_PATH=/Users/puxianguo/Downloads/Script.pvf dotnet test Server/86JP.Server.slnx --no-restore`
  - Passed: Pvf 60, Application 250, Networking 67, Infrastructure 119, Host 4.
  - Skipped: 2 intentional missing-archive negative cases.
  - Failed: 0.
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore`
  - Succeeded with 0 warnings and 0 errors.
- Publish smoke from a separate working directory:
  - `appsettings.json` and `Resources/Channel/channel_info.etc` were present and nonempty.
  - The Host reached `Application started`, logged the publish directory as Content Root and the
    packaged resource as ChannelScript, then handled `SIGTERM` with `Application is shutting down`.
- `git diff --check b73a838c74989a80eced2dc9c4a4db2677e83942`
  - Passed.
- Process cleanup check found no remaining `Dfo.Server.Host.dll` process.
- Main checkout remained at baseline `b73a838c74989a80eced2dc9c4a4db2677e83942`; its pre-existing
  packet-diagnostics and documentation changes remained untouched.

