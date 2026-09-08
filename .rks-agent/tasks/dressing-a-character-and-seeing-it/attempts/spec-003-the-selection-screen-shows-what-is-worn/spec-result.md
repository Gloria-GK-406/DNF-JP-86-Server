---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: The selection screen shows what is worn

## Delivered

- The character-selection screen draws each character wearing what that character currently wears,
  worked out from the store, instead of from a blob written once when the character was created and
  never updated since.
- The rule that decides which worn slots are drawn, and what each drawn part carries, now lives in
  one place — `DrawnAppearance` — which both the selection screen and the in-world frame reach. That
  the two cannot drift apart is held by mutation, not by assertion: moving the rule reddens checks on
  both sides at once.
- A read that answers what every character on one account wears, behind an Abstractions seam.
- That read happens only on the paths that draw. The other four request paths that share the roster
  listing keep the cheap one, and a summary that carries no worked-out appearance is refused by the
  drawing codec rather than drawn as nothing.
- `behavior-change-list.md`, opening with the enumeration method itself rather than only its results.
- Seven record corrections, including the two screens' new disagreement written down where the next
  reader will find it.

## Verification

- `verification/completion-evidence.md` — decision `pass`, 2392 passed / 2 skipped / 0 failed,
  exit code 0, against a baseline of 2384 / 2 / 0.

## Repository Result

- Changed paths: 26 modified and 3 new. New: `DrawnAppearance.cs`, one check file, this attempt's
  artifacts. Modified across `Dfo.Server.Abstractions`, `Dfo.Server.Application`,
  `Dfo.Server.Persistence`, `Dfo.Server.Protocol`, nine check files, and five records.
- Baseline or accepted result: `ce6839ed1730747d81a71efcb7342d0b8668965b`
- Parent rollback required: no

## Successor Guidance

- **The two roster frames now disagree on purpose.** The selection screen and the refresh pushed
  after a character is created both derive; the roster frame inside the entry burst still reads the
  blob. The boundary is the byte anchor, not a judgement that the blob is right. Whoever moves the
  entry frame across will move anchored bytes and must name them frame by frame.
- **`CharacterSummaryRow.AppearanceBlob` now has no production reader on the drawing paths.** It is
  still selected, still bounded, still written once at creation, and still read by the entry burst.
  Nothing here removes it; a later result that retires the column has to deal with that last reader.
- **`CharacterSummary.Appearances` is nullable and the distinction carries meaning**: `null` is "this
  listing did not work it out", `[]` is "worked out, nothing drawn". The drawing codec refuses
  `null`. A new listing that forgets to derive will fail loudly; do not make it lenient.
- **The enumeration method is the durable part of this result.** Walk up from the change and, at
  every frontier, ask what symbol the next caller up actually writes down — when the answer is
  "nothing, a registry resolves it", go and read that registry. Three consecutive results in this
  task under-reported a newly reachable path, all for the same reason, and this is what the third one
  produced in exchange. The frontier kinds already seen are listed in the change list; the list is
  known to be incomplete and the question is what to carry, not the list.
- **Creating a character now costs an account-scoped worn read**, after the character is committed
  and after its response has gone out. A failure of that read there is a post-commit failure surface
  and is enumerated as such.
- **One coverage loss**: no check now decodes a non-empty appearance blob. Separate from the
  pre-existing gap that `DecodeAppearances`'s two refusals have never been checked at all.
- **One unclassified check failure** happened once during implementation and never again; its
  evidence was not preserved, so it is not claimed as the known flake. If it returns, the retained
  full output is the place to start.

## Artifacts

- `spec.md`, `plan.md`, `behavior-change-list.md`, `reviews/change-review.md` (independent, two
  rounds: `changes_required` on scope, `changes_required` on a missed path, then `approved`).
