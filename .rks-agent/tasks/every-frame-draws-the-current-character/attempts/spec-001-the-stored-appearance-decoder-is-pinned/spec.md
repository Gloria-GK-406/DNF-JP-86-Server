---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: The stored appearance decoder is pinned

## Why and Outcome

`SelectCharacterPreparationService.DecodeAppearances` turns a stored appearance blob into roster appearance
entries and refuses two malformed shapes. Nothing under `Server/tests` names it, so any of its behaviour can
change without a single check going red.

The consumer of this outcome is the next attempt in the root task, which replaces this decoder's only
production reader. It needs to be able to say whether that replacement changed behaviour it did not mean to,
and it can only say that if the behaviour was written down first.

The deliverable is checks and a record. No production code changes.

## Scope

In: the decoding of a non-empty blob, the truncated-blob refusal, the trailing-bytes refusal, the mutation
that proves those checks are load-bearing, and the record separating the two coverage gaps.

Out: any production change, including visibility; whether this decoder should survive at all; the empty and
null cases as a required deliverable, though covering them alongside is fine.

## The Object Being Described

`internal static IReadOnlyList<CharacterRosterAppearanceState> DecodeAppearances(byte[] blob)` in
`Server/src/Dfo.Server.Application/Characters/SelectCharacterPreparationService.cs`. Read from this
repository at `addbeb52`.

Wire shape of the blob:

| Offset | Width | Meaning |
|---|---|---|
| 0 | 1 | entry count |
| then, per entry | 23 | one appearance entry |

Wire shape of one entry, in read order, with the `CharacterRosterAppearanceState` member each fills:

| # | Width | Read as | Member |
|---:|---|---|---|
| 1 | 1 | byte | `Slot` |
| 2 | 4 | int32 | `DisplayItemId` |
| 3 | 4 | int32 | `ExpansionLength` |
| 4 | 4 | raw bytes | `ExpansionData` |
| 5 | 1 | byte | `State` |
| 6 | 4 | int32 | `LinkItemId` |
| 7 | 4 | uint32 | `EnchantValue` |
| 8 | 1 | byte | `Flag20` |

`BinaryReader` over a `MemoryStream`, so multi-byte members are little-endian.

The two refusals, both `InvalidDataException`, separated only by their message:

- before reading each entry, when fewer than 23 bytes remain:
  `"Character appearance blob is truncated."`
- after the declared entries have been read, when the stream is not exhausted:
  `"Character appearance blob contains trailing bytes."`

## Requirements

| ID | Requirement |
|---|---|
| RQ-001 | The decoding of a non-empty blob is described by a check whose expected values would not survive a member being filled from the wrong place in the entry, or a member being read at the wrong width. Signedness is explicitly outside this: see **What cannot be observed** below. |
| RQ-002 | The truncated-blob refusal is described by a check that identifies *that* refusal rather than the fact that something was thrown. |
| RQ-003 | The trailing-bytes refusal is described by a check that identifies *that* refusal, and that would go red if it were replaced by the truncated-blob refusal. |
| RQ-004 | The checks are shown to be load-bearing: altering the decoder reddens them, and the count and identity of the reddened cases are reported. |
| RQ-005 | The record states that the two refusals never had a check, and separately that the non-empty decoding was covered indirectly until `35aaedf3` replaced the case that covered it -- naming the replaced case and its replacement. |
| RQ-006 | No production file changes, and no existing check's expected value or assertion changes. |

## Acceptance Criteria

| ID | Covers | Criterion |
|---|---|---|
| AC-001 | RQ-001 | A blob of one entry whose eight members carry eight mutually distinct values, none of them zero, decodes to one `CharacterRosterAppearanceState` whose every member equals the value written for it. See **Distinguishability** below for what the values must satisfy. |
| AC-002 | RQ-001 | A blob declaring two entries decodes to two entries in the order written, so a decoder that read one entry and stopped, or that reversed them, is not accepted. |
| AC-003 | RQ-002 | A blob whose count byte declares more entries than the remaining bytes can carry raises `InvalidDataException` whose message is the truncated one. |
| AC-004 | RQ-002 | The truncation case is exercised with at least one entry successfully read before the shortfall, so the refusal survives a partially readable blob rather than only a blob too short to hold anything. It does not distinguish the guard inside the loop from an equivalent up-front length check, and does not claim to. |
| AC-005 | RQ-003 | A blob declaring one entry but carrying 23 + n bytes after the count, n > 0, raises `InvalidDataException` whose message is the trailing-bytes one. |
| AC-006 | RQ-002, RQ-003 | The two refusals are distinguished by their message, so that swapping the two throws in the production code would redden a check rather than pass. |
| AC-007 | RQ-004 | With the checks in place, mutations applied to `DecodeAppearances` redden them; each mutation, the number of reddened cases, and their names are recorded, and the file is restored with a `sha256sum` match against a pre-mutation backup. |
| AC-011 | RQ-004 | At least one recorded mutation reddens a case through a **value mismatch** on the eight member assertions, not through a raised exception. A mutation set that only alters control flow leaves those assertions unproven, however many cases it reddens. |
| AC-008 | RQ-005 | The record contains both statements, marked apart, and names `TheRosterCarriesStoredAppearancePartsAndCopesWithNone` and `TheRosterDrawsWhatEachCharacterWearsAndNothingWhenBare`. |
| AC-009 | RQ-006 | `git status` and `git diff --stat` over the worktree show no change under `Server/src`, and no line removed or altered in any pre-existing check file. |
| AC-010 | RQ-001..RQ-006 | The suite passes: 2392 + the added cases, 2 skipped, 0 failed. Any failure is identified by exception type and stack root before it is judged, and a green rerun does not by itself excuse an unidentified one. |

### Distinguishability

AC-001's values must satisfy all of these, or the check does not do what its name says:

- No member's value equals another's, so a decoder that read members in a swapped order fails.
- `DisplayItemId` and `LinkItemId` -- the two `int32`s -- differ, and neither is zero.
- `EnchantValue` is above `int.MaxValue`, so the four bytes it occupies carry a value no other member
  carries and a member filled from that offset by mistake is visible. This pins the offset, **not** the
  signedness -- see below.
- `ExpansionData`'s four bytes are not all equal, and are distinct from the single-byte members, so a
  decoder that took them from the wrong offset fails.
- No member is zero, so a member left unread and defaulted fails rather than coincidentally matching.

### What cannot be observed

**Signedness cannot be checked through this method and no check here claims to.**
`CharacterRosterAppearanceState.EnchantValue` is a `uint`, so any signed read has to be converted back, and
that conversion is bit-preserving: the same four bytes that read as `3_000_000_000` unsigned read as
`-1_294_967_296` signed and convert back to `3_000_000_000`. Replacing `reader.ReadUInt32()` with
`(uint)reader.ReadInt32()` therefore leaves every case green, which was measured rather than reasoned about
(mutation R2 in the coverage record). It is a property of the member's type, not of the values chosen, and
choosing different values cannot recover it.

An earlier revision of this Spec asserted the opposite. It was wrong, and the check file's remarks repeated
it; both are corrected.

`ExpansionLength` is written and read as an ordinary `int32`; the decoder does not use it to size
`ExpansionData`, which is always four bytes. A value for it that is not 4 is therefore legitimate and is
worth using, because it proves the decoder does not quietly re-derive it.

## Success Evidence

- EV-001 (AC-001..AC-006): the new check file, whose case names say what each proves.
- EV-002 (AC-007): the mutation record -- what was changed, which cases went red, and the `sha256sum`
  before and after restoration.
- EV-003 (AC-008): the record, in the attempt's own artifacts.
- EV-004 (AC-009): `git status --short` and `git diff --stat` output.
- EV-005 (AC-010): the retained suite output file, not only its summary line.

## Revisions after this Spec was marked ready

This Spec was revised once after implementation, in response to blocking findings from the independent
change review. The revisions are recorded here rather than only in the review, so a later reader sees that
the boundary moved and why.

| What changed | Why | Direction |
|---|---|---|
| RQ-001 no longer claims signedness is covered; **What cannot be observed** added | Mutation R2 measured that it is not, and cannot be | Narrowed |
| AC-004 no longer claims the loop guard is proven from inside the loop | An equivalent up-front check would pass the same case | Weakened |
| AC-011 added: at least one mutation must redden through a value mismatch | The original mutation set reddened only through control flow, leaving the member assertions unproven | Strengthened |

**None of this moved the Spec below the TaskContract, which was not edited.** C-1 asks for values that would
not survive a member being read in the wrong order or at the wrong width, and it never mentioned signedness;
the removed clause was something this Spec had added on top of the contract and could not deliver. Both
dimensions C-1 does ask for still have mutation evidence: placement by R3, width by M2.

- `Dfo.Server.Application.csproj` declares `InternalsVisibleTo` for `Dfo.Server.Application.Tests`, so the
  `internal static` method is reachable without a production change. If that turns out to be untrue in
  practice, this Spec's Out boundary is reached and the attempt returns to the root map rather than
  changing visibility.
- The 23-byte entry width is the decoder's own constant, read from its source here. Its agreement with the
  writer in `AccountCharacterRosterPayloadCodec.WriteAppearance` is a repository record from the previous
  round and is not something this outcome re-establishes or depends on.
