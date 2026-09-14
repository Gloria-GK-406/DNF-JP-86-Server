---
kind: rks-review
verdict: changes_required
---

# Independent change review — round 1

Baseline: `623054bae44820c456adbec72b7f90f3868bb305`

## Blocking findings

1. `IPartyService.TransferLeadership` was not implemented by all test doubles, so the current
   target did not compile and the earlier TRX files predated the change.
2. Pending invitations and their delivery had been reduced to `CharacterId` without an outer
   exact-session delivery token. A reconnect could accept an old invite, and a delayed old invite
   could be redirected to the replacement connection.
3. Presence mutation and the Party directory callback were not serialized. An older relocation
   callback could run after a newer relocation/admission callback and overwrite the Party index
   with a stale scope.
4. The command-handler architecture predicate treated `IPartyService` as if it satisfied the
   canonical suffix rule instead of recording the intentional name as a reason-bearing exact
   exemption and updating the authoritative convention.
5. AC-003 lacked a direct integration test proving that identical numeric private gates stay out
   of Party query results while shared normal/dungeon-gate presences are discoverable.

## Non-blocking risk

Presence migration advances Party revision without advancing the protocol projection stream. A
delayed pre-move Party plan could republish its old observer scope unless delivery checks the
current Party revision. Movement read-repair covers the new scope but does not itself make the old
plan harmless.

## Disposition

`changes_required`. The controller must repair all blocking findings, address or explicitly
contain the stale-plan risk, rerun current build/tests, and request a fresh review of the updated
target.
