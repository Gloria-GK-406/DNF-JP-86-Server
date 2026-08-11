# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The user explicitly authorized a separate SDD branch with checkpoints to migrate the complete set of services needed by `SELECT_CHARACTER`, using the legacy implementation as evidence while tolerating justified corrections.
- Requirement stability: The domain separation, AccountId identity boundary, service-driven repositories, no nested commands, completed-result Networking projection, honest missing-data semantics, and final verification expectations are explicit and reviewable.
- Execution direction: The current refactor branch already supplies the command, account gate, typed message, first selection workflow, and TouchSocket response seam; the remaining work can proceed by mapping and replacing legacy dependencies in bounded slices.
- Remaining uncertainty: Individual legacy field meanings and packet confidence may remain code-reversed. The contract requires them to be recorded rather than invented, so this does not change the task meaning.

## Marginal Control Value

- User precision signal: The user explicitly requested SDD and checkpoints for this large feature while granting implementation discretion for defects in the legacy code.
- Direct failure mode: A direct migration could omit one of the 58 static entries or conditional packets, put account/static/session data back into character state, or accept a partial packet sequence without noticing the gap.
- Useful control: A durable migration matrix, serial child Specs with complete-result review, accepted checkpoint commits, rollback boundaries, and final root verification keep source coverage and domain ownership visible.
- Time-for-quality rationale: The legacy behavior spans dozens of builders and data sources; the cost of the controls is lower than diagnosing an incomplete or cross-domain character-entry state after integration.

## Structural Selection

- Selected workflow: `multi-spec-workflow`
- Boundary evidence: The migration matrix, character/account/world-session service outcomes, and Networking projection each have independent acceptance evidence and rollback boundaries. Later packet projection depends on accepted service results but does not need to share an unreviewed implementation attempt with them.
- Why Direct is insufficient: A conversation-only checklist cannot cheaply prove complete legacy coverage or preserve accepted service checkpoints while a later packet-projection attempt is corrected.
- Reroute conditions: Block and return to intent alignment if packet semantics require external capture to choose between materially different business meanings, or if legacy data proves unavailable locally. A child may choose a lighter or heavier Single Spec controller according to its own risk, but the root remains Multi while independent accepted outcomes remain necessary.
