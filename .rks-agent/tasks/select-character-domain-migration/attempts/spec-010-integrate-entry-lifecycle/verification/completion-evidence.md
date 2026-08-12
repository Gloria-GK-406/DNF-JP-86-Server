---
kind: rks-verification
decision: pass
---

# Completion Evidence: Integrated Select Character Lifecycle

## Claim

G7 executes Select Character as one account-exclusive, generation-safe lifecycle over the
accepted G1-G6 result and projection boundaries. It performs truthful entry mutations,
publishes only a successfully prepared exact session, and compensates a failed registered
attempt without clearing a newer generation.

## Condition Mapping

| Condition | Fresh evidence |
|---|---|
| SC-10.1 | Admission and invalid-wire behavior tests keep cleanup, registration, full preparation, mutation and publication at zero; admission rejection emits the ordered X-08 pair. |
| SC-10.2 | Replacement, same-connection refresh, stale disconnect and stale rollback tests prove exact connection/generation ownership across selected, active, presence, PvP and cleanup state. |
| SC-10.3 | The integrated success ledger covers registration, durable preparation, static/X delivery, publication, PvP direct/ready/targets, trailing locks, quest, adjacent pet packets and rejoin order. |
| SC-10.4 | Real-PVF refill and quest facts plus SQLite inventory, outbox, quest and pet tests cover commit, no-op, reload, poison, ownership and rollback behavior. The dirty lease is explicitly limited to character item containers and avatar details. |
| SC-10.5 | Registry and sender tests cover listener-local X-17/X-18, required PvP direct order, ready-after-direct, ordered rejoin participants, invalid/expired release and isolated disconnected targets. |
| SC-10.6 | Fault-injection tests cover late preparation, required send and cleanup failures; exact compensation continues independently, transport close occurs after lifecycle-gate release, and stale generations remain intact. |
| SC-10.7 | Independent complete review is approved and plan drift is aligned. Scans find no concrete SQLite/PVF dependency in Application or Networking, no repository dependency in Networking, no downstream-domain `OnlineUserId`, and no nested command dispatch. |
| SC-10.8 | Fresh real-PVF solution verification passes 488 tests with two intentional archive-present skips. Host builds with zero warnings/errors, reaches `Application started`, and shuts down cleanly. G7 documentation remains explicitly code-reversed. |

## Fresh Commands and Results

- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`: exit 0; PVF 60, Application 245, Networking 64, Infrastructure 119; 488 passed, 2 skipped, 0 failed.
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore`: exit 0; 0 warnings, 0 errors.
- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet run --project Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-build`: reached `Application started`; Ctrl-C produced clean shutdown and exit 0.
- Application/Networking concrete peripheral scan: no `Microsoft.Data.Sqlite`, `Dfo.Pvf`, `PvfArchive` or `PvfToken` matches.
- Networking repository scan, downstream `OnlineUserId` scan and Select Character nested-command scan: no matches.
- `git diff --check`: exit 0.
- Independent complete change review: `approved`.
- Fresh plan-drift review: `aligned`.

## Skips

The two Infrastructure skips deliberately exercise missing-archive behavior. They skip
when the supplied real archive is present; the positive real-PVF paths ran and passed.

