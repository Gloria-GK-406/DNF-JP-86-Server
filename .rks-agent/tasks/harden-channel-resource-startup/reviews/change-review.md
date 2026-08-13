---
kind: rks-review
verdict: approved
---

# Change Review: Harden channel resource startup

## Reviewed Change

- Baseline: `b73a838c74989a80eced2dc9c4a4db2677e83942`
- Scope: the complete tracked and untracked worktree change against the accepted
  `TaskContract.md` revision 1 and `spec.md`.
- Boundary: Host configuration/root selection, channel resource validation and snapshot,
  startup diagnostics, production-shaped tests, and the operator runbook. No Channel/Game
  wire codec, encryption, packet-diagnostics, Patch, PVF semantics, or domain behavior changed.

## Verdict

Approved. The complete change satisfies the TaskContract and Spec with no unresolved blocking
findings.

## Requirement Review

- Stable Host root: `Program.cs:11-15` fixes the Content Root to `AppContext.BaseDirectory` before
  default configuration is loaded. The real Host process test launches that built output from an
  unrelated temporary directory, while the independent publish smoke does the same with a publish
  output.
- Relative and absolute paths: `InfrastructureModule.cs:137-165` resolves the packaged default and
  all relative values from the executable directory while normalizing, but not re-rooting, absolute
  overrides. `ChannelSourcesTests.cs:12-50` exercises an absolute override.
- Fail before listeners: the resource `IHostedService` is registered by the Infrastructure module
  at `InfrastructureModule.cs:29-35`; module order in `Program.cs:17-20` and
  `ServerModuleRegistrationExtensions.cs:25-29` places it before both TouchSocket services registered
  by `NetworkingModule.cs:58-66`. Missing, empty, and non-file inputs each make the actual Host process
  exit nonzero, name the effective path, and leave the selected listener unavailable.
- Immutable serving: `InfrastructureModule.cs:222-276` loads once under a lock at startup, rejects
  empty input, wraps read failures with the effective path, and returns a clone per request. The
  focused source test mutates both a returned array and the backing file and still receives the
  startup snapshot.
- Protocol compatibility: `ChannelHostStartupTests.cs:31-47` drives the actual TCP stack through
  `0x0B→0x0C→0x09→0x0A→0x11→0x12`, decrypts the unchanged responses, verifies a
  nonempty packaged script and the configured Game port. No production framing or cipher file is in
  the change set.
- Diagnostics and packaging: `Program.cs:28-40` logs only effective roots, resource/address and
  listener settings. It does not log keys, accounts, or payloads. Host project metadata packages the
  script under its sole authoritative path; the independent publish inspection found both
  `appsettings.json` and `Resources/Channel/channel_info.etc`.
- Documentation: `Docs/HostStartup.md` matches the tested stable-root, validation, snapshot,
  diagnostics, run/publish behavior and retains the code-reversed protocol caveat.

## Fresh Validation Evidence

- `dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --no-restore`:
  4 passed, 0 failed.
- Focused `ChannelSourcesTests`: 1 passed, 0 failed.
- `PVF_ARCHIVE_PATH=/Users/puxianguo/Downloads/Script.pvf dotnet test Server/86JP.Server.slnx --no-restore`:
  500 passed, 2 intentional missing-archive skips, 0 failed.
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore`:
  succeeded with 0 warnings and 0 errors.
- Independent `dotnet publish` smoke: published configuration and channel resource were nonempty;
  the published Host reached `Application started` from a separate working directory and logged the
  publish directory as Content Root and packaged script root.
- `git diff --check b73a838c74989a80eced2dc9c4a4db2677e83942`: clean.
