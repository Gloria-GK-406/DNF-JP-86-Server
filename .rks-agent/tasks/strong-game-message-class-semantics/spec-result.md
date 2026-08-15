---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Strong Game CMD and NOTI Semantics

## Delivered

- Added Protocol-owned `IGameCmdMessage` / `IGameNotiMessage`, category-fixed encoder bases,
  category-safe direct sends and Game-typed explicit delivery plans.
- Renamed the raw first Game header byte below Protocol to opaque `Selector`; Networking continues
  to route by selector plus type without interpreting either Game family.
- Made every C→S Game decoder registration explicitly CMD and classified every ordinary, state and
  supplemental outbound Game message from its established wire bytes.
- Converted synchronization codecs from complete-frame creation to payload descriptions. Accepted
  entry, PremiumService and CloneTitle remain CMD; established state pushes remain NOTI.
- Split admission rejection into adjacent CMD rejection and NOTI notice messages inside one required
  ordered delivery, preserving recipient and failure behavior.
- Preserved the supplemental roster's exceptional offset-7 checksum value `2` through an opaque
  generic framing input and added a full-frame regression for it.
- Updated maintained architecture, framing and protocol implementation guidance.

## Success Evidence

| Condition | Result |
|---|---|
| SC-1 | Every registered concrete Game message has exactly one category; category-safe sender calls compile and architecture coverage passes. |
| SC-2 | The encoder hierarchy alone fixes selector `0x01`/`0x00`; exact shared-type and state transcript regressions pass. |
| SC-3 | All ten C→S Game decoders are CMD-only and retain selector-plus-type routing. |
| SC-4 | Networking contains no CMD/NOTI semantics or Protocol reference and treats selector/checksum/reserved as opaque. |
| SC-5 | Complete state/supplemental category inventory and rejection order pass, including roster checksum `2`. |
| SC-6 | Build has 0 warnings/errors; 526 tests passed, 65 existing environment skips, 0 failed; GameOption and unrelated content are unchanged. |

## Repository Result

- Product changes are limited to the Game protocol classification boundary, opaque lower-layer
  naming/framing support, corresponding handlers/tests and maintained guidance.
- Game Login/GameOption field meanings, Channel semantics, payloads, type numbers, ordering and
  domain workflows were not changed.
- The result is based on pulled baseline `f29123b` and is ready for the one authorized local commit.
- No push, PR, release, branch deletion, history rewrite or unrelated cleanup was performed.

## Artifacts

- `TaskContract.md` revision 2
- `routing-decision.md`
- `spec.md` revision 2
- `plan.md`
- `reviews/change-review.md` (`approved`)
- `verification/completion-evidence.md` (`pass`)
