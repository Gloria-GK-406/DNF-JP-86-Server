# Routing Decision

- TaskContract: `.rks-agent/tasks/select-character-domain-migration/attempts/spec-007-complete-world-and-session-entry-state/TaskContract.md`
- Contract revision: `1`

## SDD Readiness

- Intent and outcome stability: The outcome is fixed by the accepted migration matrix, which
  names G5's exact coverage (rows 12, 14, 17–18, 31–34, 49–53, 56 and the row-58 content
  portion; X-08 through X-10; X-15 through X-18; L-04 and L-06). The observable result is a
  composed world/content and session-adjacent limb of `SelectCharacterResult` replacing a
  one-field placeholder. The operator was shown the smaller static-content-only boundary and
  explicitly chose the complete group, so the acceptance boundary is a decided fact rather
  than an inference.
- Requirement stability: Every requirement is enumerable from the matrix and reviewable
  against a legacy implementation that is present and readable. The four legs — static
  content composition, town/area source, channel admission, session-adjacent read views —
  each reduce to concrete rows with named legacy sources.
- Execution direction: Known for three of four legs and bounded for the fourth. The content
  interfaces already exist and are verified. Admission is small and fully read: one
  level-70 rule on one listener, plus spawn, town-entry and persistence decisions derived
  from the same listener identity. The town/area source reuses a format library that already
  parses `.lst` plus text entries. The session-adjacent leg is the least specified, but its
  shape is visible in the legacy code as queries over a session directory indexed by
  `(townId, areaId, listenerPort)`.
- Remaining uncertainty: Non-semantic and local — naming of the new interfaces and read-view
  records, where the registries live within Infrastructure, and how the unconfirmed rows are
  spelled as carried-but-unnamed data. None of these change the acceptance boundary.

## Marginal Control Value

- User precision signal: The operator has consistently asked for SDD on risky work and, for
  the immediately preceding refactor, said 「这个重构还是有点危险的」 and authorized subagents.
  Here they also declined the smaller boundary in favour of the whole group, which raises
  the amount of work riding on one acceptance decision.
- Direct failure mode: The specific, already-realized failure mode for this exact outcome is
  **dishonest completeness**. `spec-006` did not fail by crashing; it produced a working
  implementation whose content was empty, and the honest thing was to refuse the result and
  roll it back. The same trap is still open on the runtime leg: town/PvP/rejoin/expert-store
  views will legitimately be empty on the production path until G7 wires their writers, so a
  green suite proves nothing about whether the registries genuinely exist or whether the
  emptiness is fabricated. A second failure mode is scope: G5 borders G7 along X-09 through
  X-18, and without a fixed boundary the attempt drifts into lifecycle mutation and stops
  being independently acceptable.
- Useful control: A Spec that fixes, before implementation, (a) which rows are modeled versus
  carried as unconfirmed, (b) the exact meaning of "registry exists but is empty" versus
  "source absent" for each of the four read views, and (c) the query/mutation line against
  G7. Then a complete-result review by a fresh reviewer whose primary question is whether any
  part of the composed state is fabricated rather than loaded — the question `spec-006`
  answered honestly and only at the end.
- Time-for-quality rationale: The prior attempt at this same outcome consumed a full
  implementation cycle and was discarded. The cost of specifying the honesty boundary up
  front is far below the cost of a second rolled-back attempt, and unlike last time the
  boundary is now decidable rather than dependent on an absent input.

## Structural Selection

- Selected workflow: `single-spec-workflow`
- Boundary evidence: One coherent acceptance boundary. The four legs all terminate in the
  same observable — the world/content and session-adjacent limbs of one business result
  composed before publication — and none of them is separately acceptable in a way the
  operator wants: they declined exactly that split. Rolling back any one leg would leave the
  composed limb partial, which the no-fabricated-state constraint forbids.
- Why Direct is insufficient: Direct cannot fix the honesty boundary before implementation,
  and that boundary is precisely what a passing test suite fails to protect. It also has no
  place to record the G5/G7 line, which is the scope risk.
- Why Multi is unnecessary: This is one attempt inside an existing Multi Spec chain; nesting
  is not permitted and not needed. The root controller already owns the group sequencing.
- Reroute conditions: If the session-adjacent read views turn out to require session
  *registration* to be meaningful at all — that is, if a registry cannot be defined without
  the G7 writer — then the runtime leg is not independently acceptable and the attempt must
  return to intent alignment for a re-split. If the town/area archive structures do not parse
  with the delivered format library, the town source becomes its own bounded problem and this
  attempt's boundary must be reconsidered rather than widened silently.

## Controller Selection

- Controller: `sdd-standard`
- Profile source: `code-feature`
- Why this is sufficient: The control Standard adds over Lite is a durable Plan with explicit
  sequencing and dependency mapping, and this outcome has real ordered coupling across its
  four legs. Channel identity must be resolved before admission, spawn, position persistence
  and PvP-profile applicability can be decided from it — one decision feeding four
  consumers. A session-registry substrate must be settled before any of the four read views
  can be shaped, and the shape of that substrate is the single most propagating choice in
  the attempt: the legacy directory is indexed by `(townId, areaId, listenerPort)`, and
  choosing a different key would silently distort town presence, PvP lobby, expert store and
  rejoin alike. The town/area content source must land before spawn resolution can be
  written at all. A wrong early choice in any of these surfaces only at the composition step,
  by which point the corrective edit spans every leg — precisely the propagation the Plan
  exists to prevent.
- Why lighter is insufficient: Lite reviews the complete result but offers no place to fix
  the substrate decisions before they are consumed four times over. It also gives the
  honesty boundary — which read views are backed by a real registry versus carried as
  unconfirmed — no durable ordering relative to the composition work, and that boundary is
  the failure this attempt exists to avoid repeating. The four sibling entry-state loads in
  this same chain (`spec-003` through `spec-006`) each selected Standard for a strictly
  smaller surface; this attempt spans all of theirs plus a runtime leg none of them had.
- Why heavier is unnecessary: Governed buys independent challenge, independent artifact
  approval and drift control. None of the hard-floor categories apply: this is read-only
  business-state composition on a local branch with no credential, privacy, financial, or
  irreversible-external reach, and no durable-data-loss path — the entire result is
  recoverable by `git` reset to baseline `86ba115`. The concrete failure mode here is
  dishonest completeness, which a fresh complete-result reviewer asking "is any of this
  fabricated rather than loaded?" catches as well as an adversarial challenge would, at
  materially less time. `spec-006` demonstrates the point: that exact failure was caught and
  honestly sealed under Standard.
