# Implementation Report: Character-scoped PartyService directory

## Changed

- Replaced the public `IPartyDomainService` boundary with `IPartyService`. Party commands,
  results, member state, and notification plans now carry `CharacterId`, immutable
  `PartyCharacterSnapshot`, and party value objects rather than selected-session identities.
- Replaced `PartyDomainService` with one process-local `PartyService`. Its private `PartyState`
  owns party membership, invitations, endpoints, revision/generation state, and a
  `PartyDirectoryScope -> PartyId` index. Directory queries return detached, stably ordered
  `PartySnapshot` values with optional category and mode-target filters.
- Added a logical directory scope of `ChannelId + TownId + AreaId`. Presence only produces that
  scope while a character is standing in a shared town visibility area; private gate and
  non-town states produce no public scope.
- Connected presence admission, exact relocation, and exact withdrawal to the party directory.
  Directory ownership follows the current leader; stale withdrawal cannot remove a replacement
  connection's presence or party state. Per-character ordering now covers each presence write and
  Party scope callback, preventing an older callback from overwriting a newer scope.
- Moved current-connection validation, session-to-character projection, observer resolution, and
  network delivery outside `PartyService`. Cleanup only calls the character-scoped service when
  the exact selected session still owns that character. Replacement selection now waits for the
  displaced cleanup before registering the new selected session, and notification delivery uses
  exact recipients captured while lifecycle gates are held.
- Added service-level leadership transfer. It swaps the leader/member slots, advances the Party
  revision, reindexes from the old leader scope to the successor scope, and returns one detached
  notification snapshot.
- Changed town party projection to discover parties through `IPartyService.Query` for the
  recipient's scope. Represented sessions remain only for synchronizing already selected streams.
- Migrated existing Party Application/Protocol tests and added directory filtering, scope
  migration, private/non-town isolation, stale-withdraw, immutable snapshot, and architecture
  boundary regression tests.

## Why

The former boundary mixed party behavior with connection lifetime and inferred the directory by
scanning sessions. The new boundary makes party state and directory lookup a single coherent
domain service while retaining Session only in the application/protocol concurrency and delivery
layers where connection replacement still matters.

## Checks

- The focused TDD test first failed because the directory query returned no party, then passed
  after directory indexing was implemented.
- `dotnet build Server/86JP.Server.slnx`: passed with zero warnings and zero errors.
- Application Party and replacement-order focused tests: 46 passed.
- Full Application tests after review remediation: 1420 passed. One prior run had a single
  unrelated transient SQLite `ObjectDisposedException`; that exact test then passed alone and the
  complete Application project passed on rerun.
- Protocol Party and cleanup focused tests: 94 passed; final full Protocol project: 814 passed.
- A fresh post-approval full-solution run with
  `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` reported 3142 results,
  3140 passed, 0 failed, and 2 intentional missing-archive skips across 9 TRX files.
- `git diff --check`: passed.
- Forbidden Party-boundary type/name scan: no match for selected-session identity,
  network connection identity/generation, or listener port in the public Party service/model
  boundary.

An earlier full test attempt without `PVF_ARCHIVE_PATH` produced 43 Protocol failures rooted in
the missing `Script.pvf`; it was treated as an environment failure, not as product evidence. The
same full suite passed after supplying the existing A21 protocol archive.

## Remaining

- No new dual-client runtime pass was performed in this implementation turn. The previously
  observed A21 create/rename/disband/refresh behavior remains runtime evidence, while this change
  is supported by codec and notification regressions only and is not newly promoted to
  `ClientVerified`.
- Story-dungeon entry behavior, `debug-lab`, merge, push, and worktree cleanup remain outside this
  task.
- Two non-blocking long-run optimization items are tracked as D-001 and D-002 in
  `Document/plan/technical-debt.md`.
