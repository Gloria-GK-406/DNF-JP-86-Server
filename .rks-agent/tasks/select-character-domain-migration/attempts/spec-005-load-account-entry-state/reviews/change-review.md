---
kind: rks-review
verdict: approved
---

# Change Review

Review scope: complete Spec 005 change from baseline `927e6ed997c5b1b093060ab4334b41a6c1421c80`, including remediation of the prior independent findings.

## Verdict Basis

- G4 account state is separated into cohesive settings, wallet/entitlement, progression/event and relation/compatibility views, keyed by `AccountId`, with SQLite confined to Infrastructure.
- Normalized premium types retain signed 32-bit width through repository, Application and Data contracts. Focused repository and service tests exercise Devil Contract type 586, so row 41 no longer rejects or narrows valid types; byte-sized row-1 presentation remains a later Networking concern.
- The account hotkey boundary is now one nullable 16-bit prefix. Empty storage remains absent, one byte is rejected as malformed, and historical oversized blobs are reduced to the demonstrated first account-scoped pair, leaving the complete character layout in accepted G3 state.
- Mandatory account ownership, scalar widths, roster slot uniqueness, relation endpoint ownership, lottery uniqueness/ranges, Unknown730 whole-roster consensus and row-52 whole-roster consensus are validated before composition.
- Account inventory/cargo remains the accepted `SelectCharacterResult.AccountInventory` reference and is not duplicated into `AccountEntryState`.
- Honor, adventure and growth retain raw account progression plus ordered roster levels; no static-derived level, threshold, bonus or presentation value is fabricated.
- `CharacterEntryService` loads inventory, character entry and account entry before active-character publication. Failure-path coverage proves an account load failure preserves the prior active state.

## Evidence

- `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore`: 53/53 passed.
- `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests/Dfo.Server.Infrastructure.Tests.csproj --no-restore`: 36/36 passed.
- `dotnet test Server/86JP.Server.slnx --no-restore`: 118/118 passed.
- `git diff --check 927e6ed997c5b1b093060ab4334b41a6c1421c80`: passed.
- Dependency scan found no SQLite reference in Application, Networking or Abstractions.
- The G4 coverage document traces every listed account-owned matrix portion and records the neutral Unknown730/row-52 promotion rules and account-hotkey legacy-tail policy.
