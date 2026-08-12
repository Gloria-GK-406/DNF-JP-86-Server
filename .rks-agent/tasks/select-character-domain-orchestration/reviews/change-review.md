---
kind: rks-review
verdict: approved
---

# Change Review: Select Character domain orchestration

## Reviewed Change

- Scope: latest complete change from baseline `3b32ca494c3f384c9d2f29dd63bcd1bbe3b2d0ce` through the current worktree, including tracked and untracked product/test files.
- Authority: `TaskContract.md` revision 1, `spec.md`, and `plan.md` in this task directory.
- Final confirmation focus: the latest complete path set, remediation of former finding B-001, and the two final semantic file renames.

## Verdict Evidence

- Former B-001 is resolved at `Server/src/Dfo.Server.Application/Commands/Game/SelectCharacterCommand.cs:94-109`: the complete `SelectCharacterResult`, including the fallible `TimeProvider.GetUtcNow()` call, is constructed before `IActiveCharacterService.Publish`; publication occurs exactly once immediately before returning the already-built result.
- `Server/tests/Dfo.Server.Application.Tests/Characters/CharacterEntryInventoryTests.cs:243-266` supplies a `FailingTimeProvider`, requires the failure to propagate, and asserts both that the previous active character remains current and that publication count stays zero.
- The focused failure-atomicity test passes against the current implementation. In an isolated temporary copy, restoring the former publish-before-composition order made the same test fail at the previous-character assertion, independently demonstrating meaningful RED/GREEN protection. The temporary copy was not the reviewed worktree and was moved to Trash after the check.
- Path/hash confirmation proves the two final renames are content-only. `Server/src/Dfo.Server.Application/Inventory/InventoryEntryServices.cs` is byte-identical to the previously approved `InventoryEntryService.cs` with SHA-256 `e16db285274dfe3475780a2a6a65e29bc0adcbae2e7661f522203b180fc16063`; `Server/tests/Dfo.Server.Application.Tests/Characters/EquipmentEligibilityCharacterFactsTests.cs` is byte-identical to the previously approved `CharacterEquipmentFactsTests.cs` with SHA-256 `96922f6106b77901917ca6e341f215044853e1abbc67e2003aa56d372ff2498c`.
- Both obsolete paths are absent, both semantic replacement paths are present, no source/project/task reference retains either obsolete filename, and SDK compilation discovers the replacement `.cs` paths without an explicit include.
- Complete diff inspection confirms the former `ICharacterEntryService`/`CharacterEntryService` facade remains removed, the command handler explicitly coordinates separated domain contracts, and the application-owned result remains in `Dfo.Server.Data.Game`.
- Static inspection confirms Select Character retains `AccountId` and exclusive command registration, and its handler has no repository, SQLite, command-bus, or `OnlineUserId` dependency.
- `IEquipmentEligibilityCharacterReader` remains purpose-limited to `CharacterId`, level, job, and growth type without exposing character mutation or entry orchestration.
- Networking production changes remain limited to result namespace/type integration; protocol constants and projection behavior are unchanged.
- Fresh full `Server/86JP.Server.slnx` test run: 355 passed, 2 skipped, 0 failed. Both skips explicitly report absent PVF archives and preserve the approved unavailable-source behavior.
- `git diff --check` reports no whitespace errors.
