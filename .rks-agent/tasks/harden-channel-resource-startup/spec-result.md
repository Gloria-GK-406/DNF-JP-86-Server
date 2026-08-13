---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Harden channel resource startup

## Delivered

- The Host now roots default configuration and relative resources at the executable directory,
  independent of the caller's current working directory.
- The required channel script is resolved once, validated before TouchSocket listeners start, and
  served as an immutable startup snapshot.
- Missing, empty, and unreadable/non-file resources fail startup with the effective path instead of
  failing on the first client `0x09` request.
- Startup logs expose effective roots, advertised addresses, and listener ports without payloads or
  secrets.
- Real-process regression coverage proves the complete existing Channel bootstrap and invalid-startup
  cases; operator documentation matches the verified run and publish layout.

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | Real built and published Hosts started from unrelated working directories. |
| SC-2 | Real TCP process test passed the ordered `0x0B→0x0C→0x09→0x0A→0x11→0x12` flow. |
| SC-3 | Missing, empty, and unreadable/non-file resources failed before the listener was reachable. |
| SC-4 | Executable-root relative resolution and absolute override/snapshot tests passed. |
| SC-5 | Published startup log contained the effective root, script, addresses, and ports. |
| SC-6 | Fresh full real-PVF solution passed 500 tests with 2 intentional skips; Host build and diff check passed. |
| SC-7 | `Docs/HostStartup.md` matches verified startup and publish behavior. |

## Repository Result

- Changed paths: bounded Host startup/configuration, channel resource source, Host/Infrastructure
  project metadata, focused integration tests, solution registration, SDD artifacts, and startup
  runbook.
- Baseline or accepted result: baseline `b73a838c74989a80eced2dc9c4a4db2677e83942`; accepted result is
  the single local commit that seals this candidate.
- Parent rollback required: no.

## Artifacts

- `.rks-agent/tasks/harden-channel-resource-startup/TaskContract.md`
- `.rks-agent/tasks/harden-channel-resource-startup/spec.md`
- `.rks-agent/tasks/harden-channel-resource-startup/reviews/change-review.md`
- `.rks-agent/tasks/harden-channel-resource-startup/verification/completion-evidence.md`
- `Docs/HostStartup.md`

