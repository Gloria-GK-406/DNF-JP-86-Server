---
kind: rks-review
verdict: approved
---

# Independent Final Complete Re-review — Spec 009

## Reviewed Change

- Scope: the current complete diff from baseline
  `501c2ca330fc6f506261dc9016e82327dccc73fe`, including R4, the complete
  TaskContract/spec/plan, current product/tests/docs and the authoritative legacy
  builders.
- Fresh focused verification passed:
  - production and synthetic creature-lifetime source tests: 8/8;
  - Application marker-precedence tests: 5/5;
  - select-character payload/production projection tests: 18/18.
- Fresh complete verification:
  `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`
  passed 429 tests with exactly two expected archive-present negative checks skipped.
- `git diff --check`, stale lifetime/raw-delivery scans, invented creature-expiry schema
  scan, Networking concrete dependency scans and Application concrete
  PVF/SQLite/Infrastructure scans passed.

## Finding Dispositions

- F-001 remains closed. Production skill and collection protocol values are completed
  from typed Application-owned inputs before active-character publication.
- F-002 row 42 remains closed. Its typed lottery source, fixed populated layout and
  loaded-empty behavior are unchanged.
- F-003 remains closed. Supplemental composition and delivery remain typed; Networking
  owns frames and preserves direct/explicit-target order and isolation.
- F-002R remains closed. Row 38 has the authoritative NOTI-2 item shape, four ownership
  gates, required-state failures and the genuine two-byte empty body.
- F-002R2 remains closed. Dynamic Chronicle and random membership use the legacy key
  predicates, and the final creature marker crosses into Networking only as a completed
  typed value.
- F-002R3 is closed. The content boundary now returns separate direct and detail-policy
  remaining values, including `[usable period]`, without leaking PVF types or queries
  beyond Infrastructure.

## F-002R3 Exactness Review

The implementation now preserves all three authoritative stages:

1. A positive direct `[expiration date]` value wins
   (`PvfCreatureEquipmentLifetimeSource.cs:66-78`;
   `SupportCreatureMarkerProjection.cs:14-15`). Direct parsing retains the legacy
   relative-duration versus absolute-time distinction.
2. The effective detail fallback selects positive core `ExpireTime`, then an absolute
   core marker, then the equipment expiration policy
   (`SupportCreatureMarkerProjection.cs:17-25`). The Infrastructure policy reads both
   `[usable period]` and `[expiration date]`, rejects the same malformed declarations,
   gives a positive usable period priority, and preserves the legacy Int32 expiry clamp
   (`PvfCreatureEquipmentLifetimeSource.cs:33-56,81-135`).
3. If the detail value is not positive, stored fallback prefers any core marker
   (absolute or relative) and then core expiry
   (`SupportCreatureMarkerProjection.cs:27-32`).

This matches the legacy `ResolveCreatureExpireDate` plus
`ResolvePetCreatureMarker16`/`ResolveStoredPetRemainSeconds` flow. Application resolves
the final value using the single selection `PreparedAt`, and Networking only validates
and writes `Noti2CreatureMarker`; it performs no content discovery
(`SelectCharacterCommand.cs:278-310`;
`SelectCharacterStaticBodyFamilies.cs:240-247`).

The supplied real-PVF test executes the production source against a positive
usable-period/absent-expiration definition. As an additional independent archive audit,
the current `Script.pvf` contains 12 relevant `creature/` equipment definitions with a
positive usable period and absent/zero direct expiration, confirming that the repaired
branch is exercised by real creature equipment rather than only unrelated equipment.

## Complete Contract Review

- Static rows 1-58 retain exact descriptor identity, order, duplicate occurrences and
  the documented row-10/row-58 conditional behavior.
- X-01-X-18 retain typed deterministic seams; target discovery and lifecycle mutation
  remain outside Networking.
- Owner/dependency/failure semantics, row 38/42 populated and loaded-empty distinctions,
  public typed composer/sender behavior and direct/target delivery show no regression.
- G6 still performs no session/registry/inventory/position lifecycle mutation.
- Protocol documentation remains honest that structures are code-reversed and await
  client/capture confirmation.

## Verdict

Approved. No blocking finding remains in the complete Spec 009 change.
