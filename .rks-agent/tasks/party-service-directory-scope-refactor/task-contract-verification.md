---
kind: rks-verification
decision: pass
---

# TaskContract readiness verification

Claim: `.rks-agent/tasks/party-service-directory-scope-refactor/TaskContract.md` is ready for
structural SDD routing. This decision verifies the contract boundary only; it does not claim that
the refactor has been implemented.

## Evidence

| Readiness condition | Decisive result |
|---|---|
| Goal and scope are unambiguous | The contract separates the PartyService refactor, character-owned presence input, directory filtering and protocol adaptation from session redesign, client work and story-dungeon repair. |
| Authorization is traceable and covers the bounded task | The Authorization table preserves the user's explicit grant and limits merge, push, cleanup, client/debug-lab work and story-dungeon repair. |
| Success conditions are observable | SC-1 through SC-7 each name concrete code, query, transition, protocol or build evidence. |
| Material uncertainty is visible without blocking the task | The current boundary is explicitly limited to the verified same-channel shared-visible-area behavior; broader town/channel visibility is excluded until new evidence exists. |
| No unresolved semantic question remains | The contract contains no Open Questions section and requires no unverified original-server behavior to begin. |
| Artifact structure is valid | Current file inspection found the ready state and all required sections; `git diff --check` reported no whitespace errors. |
