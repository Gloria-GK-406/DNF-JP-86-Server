---
kind: rks-verification
decision: pass
---

# Completion Evidence: Strong Game CMD and NOTI Semantics

## Claim

The completed change makes every outbound Game message intrinsically and exclusively CMD or NOTI,
constrains Game sends and encoders to that category, keeps client-originated Game routing CMD-only,
and leaves Networking protocol-neutral while preserving all established frame bytes, ordering,
recipients and failure behavior.

## Condition-to-Evidence Mapping

| Condition | Fresh evidence |
|---|---|
| SC-1 | Protocol architecture tests enumerate every concrete registered Game message and require exactly one of `IGameCmdMessage` / `IGameNotiMessage`; all direct Game handlers use the category-safe facade and only its adapter references the generic sender. |
| SC-2 | Category-fixed encoder bases are the sole Game frame-composition authority. Exact tests cover CMD and NOTI sharing type `0x0001`, the full accepted transcript, rejection CMD→NOTI order, and opaque nonzero header values. |
| SC-3 | Protocol registration inspection proves all ten C→S Game decoders use selector `0x01`; Networking registry and integration tests preserve selector-plus-type routing and silent unknown-key behavior. |
| SC-4 | Lower-layer scans find no CMD/NOTI terminology or Game category API below Protocol; Networking references only Abstractions and Data and carries opaque selector/checksum/reserved values. |
| SC-5 | Complete state/supplemental registration inventory passes. Accepted-entry, PremiumService and CloneTitle remain CMD; rejection is two adjacent intrinsic messages; the roster `00/0002` frame retains checksum `2`. |
| SC-6 | Fresh solution build/test passed, diff hygiene is clean, `GameLoginOptions.cs` has no diff, and unrelated `Diagnostics/` remains untracked and untouched. |

## Commands and Results

1. `dotnet build Server/86JP.Server.slnx --no-restore`
   - Exit 0; 0 warnings, 0 errors.
2. `dotnet test Server/86JP.Server.slnx --no-build --no-restore`
   - Exit 0.
   - Application: 259 passed, 10 skipped.
   - Pvf: 51 passed, 9 skipped.
   - Protocol: 91 passed, 0 skipped.
   - Infrastructure: 75 passed, 46 skipped.
   - Networking: 46 passed, 0 skipped.
   - Host: 4 passed, 0 skipped.
   - Total: 526 passed, 65 existing environment/archive-dependent skips, 0 failed.
3. Raw-category composition scan under `Server/src/Dfo.Server.Protocol/Game`:
   - Only `GameMessageSemantics.cs` calls `GameServerFrameEncoder.Encode`.
   - No message-level `Command`, raw `Frame(0/1)`, mixed rejection message or `EncodeFrames` seam remains.
4. Lower-layer semantic scan under Data.Networking, Abstractions.Networking and Networking:
   - No CMD/NOTI symbol or obsolete Game command-byte ownership remains.
5. Sender scan under Protocol/Game:
   - Only `GameMessageSender` adapts `IGameMessageSender` to `INetworkMessageSender`; handler `SendAsync` matches are domain command-bus dispatches.
6. `dotnet list Server/src/Dfo.Server.Networking/Dfo.Server.Networking.csproj reference`
   - Networking references only Abstractions and Data; it has no Protocol dependency.
7. Diff and workspace checks:
   - `git diff --check` exited 0.
   - `git diff --name-only -- Server/src/Dfo.Server.Networking/Configuration/GameLoginOptions.cs` produced no output.
   - `Diagnostics/` remains a separate untracked path and is excluded from the task.

## Review Gate

The independent complete-change re-review is `approved`. Its initial byte-regression finding for
the supplemental roster header was corrected, covered by a complete-frame regression and approved
on re-review.

## Workspace Hygiene

The task was performed directly on the user-selected branch after a fast-forward pull, as explicitly
requested. No worktree was created. No push, PR, history rewrite or unrelated cleanup was performed.
