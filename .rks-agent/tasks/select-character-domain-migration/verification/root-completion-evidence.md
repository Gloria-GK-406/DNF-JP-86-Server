---
kind: rks-verification
decision: pass
---

# Root Completion Evidence: Select Character Domain Migration

## Claim

The accepted G1-G7 checkpoints together replace the legacy Select Character initialization
dependency with a service-backed, domain-separated, generation-safe workflow whose complete
business result is projected by Networking into the code-reversed static and lifecycle
stream without repository lookup or business mutation.

## Root Condition Mapping

| Root condition | Fresh accepted evidence |
|---|---|
| SC-1 | The reviewed matrix mechanically contains 58 static, 18 supplemental and 10 lifecycle identities with sources, owners, conditions and confidence. |
| SC-2 | Accepted G1-G5 results compose character, inventory, account, world/content and session siblings with explicit inapplicable/loaded-empty/error semantics. |
| SC-3 | Accepted service and adapter boundaries keep normalized SQLite and PVF implementations in Infrastructure; fresh dependency scans are empty in Application and Networking. |
| SC-4 | Exact slot/account selection, account-exclusive command execution, generation-aware session ownership, late publication and stale cleanup/rollback protection are accepted and covered. |
| SC-5 | Accepted G6 provides the descriptor-driven 58-row stream and typed X-01-X-18 projections; Networking owns framing and explicit-target delivery only. |
| SC-6 | Accepted G7 completes preparation, mutation, delivery, publication and compensation. Fresh real-PVF tests, Host smoke, review, drift, dependency and documentation checks pass. |

## Fresh Root Verification

- Accepted baseline: `248abac2356a4a5275f00dde2134ae9da5782e22`; all accepted checkpoint commits are ancestors.
- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`: 488 passed, 2 intentional archive-present skips, 0 failed.
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore`: 0 warnings, 0 errors.
- Host run reached `Application started`; Ctrl-C shut it down cleanly with exit 0.
- Mechanical matrix counts: static 58, supplemental 18, lifecycle 10.
- Application/Networking concrete SQLite/PVF scan, Networking repository scan and downstream-domain `OnlineUserId` scan: no matches.
- `git diff --check`: exit 0.
- Independent root review: `approved`.
- Spec-010 drift review: `aligned`.

## Remaining Validation Boundary

Real-client execution and packet capture are not required for this code-reversed migration
completion. They remain an explicit post-completion validation activity and no current
document claims client-confirmed protocol behavior.

