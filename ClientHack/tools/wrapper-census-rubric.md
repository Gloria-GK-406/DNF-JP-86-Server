# Wrapper eligibility census — judgment protocol (rubric v1)

You are auditing named API wrapper stubs in an obfuscated 32-bit Windows client.
Each unit is one `call <wrapper>` site. symeval previously measured a stack
"convention" for the wrapper; your job is to check that convention against the
**byte shape of the call site**. A convention that survives is real; one that
does not is a measurement artifact (a known failure mode that has already caused
live crashes). You judge from the bytes given to you and nothing else.

## The four conventions and what they imply about the RESUME POINT

The `call` instruction is 5 bytes (`e8 xx xx xx xx`) at offset 0 of the "after"
window. The pushed return address R therefore points at offset **+5**.

| group | convention | what the API sees | execution resumes at |
| --- | --- | --- | --- |
| A | `esp+4/R` | one dword swallowed off the stack, R intact | **+5** |
| B | `esp+0/R+1` | stack intact, returns to R+1 | **+6** (byte at +5 is skipped) |
| C | `esp+4/a1` | *suspected artifact*: a register-loading stub, not a transfer wrapper | **+6** |
| D | `esp+8/a2` | two dwords swallowed | **+5** |

This resume-point test is the primary discriminator. It supersedes the older
"the byte at +5 must be a one-byte decoy" rule, which is only the special case
where both alignments happen to coincide, and which is false on most real B units.

## Step 1 — determine the actual resume point

Each unit gives you both alignments already decoded: `A) resume@+5` and
`B) resume@+6`, plus a `mechanical hint` (a conservative scorer; it abstains
with AMBIGUOUS more often than not, and it is a hint, not an answer).

* **One alignment is invalid or obvious junk, the other is clean compiled code.**
  Decided. Junk tells: `lcall`, `enter`, `into`, `bound`, `arpl`, `hlt`, `in`/`out`,
  `les`/`lds`, segment pushes (`push ds`), decodes that die before the window ends,
  writes to absurd addresses like `[ebx - 0x74af03ab]`.
* **The streams converge (see `streams converge at +N`).** Past +N the two are
  byte-identical, so the entire decision is the diverging prefix — usually the
  single one-byte instruction at +5. Ask: *would executing that instruction here
  make sense?*
  - Nonsensical to execute → it is a decoy, resume is **+6**. Tells: `ret` / `ret imm`
    with live code still following, `int3`, `push ds`/`push es`/`push ss`, `hlt`,
    a `pop` of a register that is immediately overwritten.
  - Plausible and load-bearing → resume is **+5**. Tells: a `push` that completes an
    argument list consumed by the very next `call`, an `inc`/`dec`/`pop` whose result
    the following instructions actually read.
  - Both readings work and nothing breaks the tie → say so in the note and fall
    through to Step 2; do not invent certainty.
* **Neither alignment is code** (`.vmp1`-style garbage): resume point unknown.

## Step 2 — the stack-arithmetic cross-check (independent of Step 1)

`caller_before` has been back-aligned for you (every start offset tried, only
alignments whose instruction stream ends flush against the `call` are kept), and
the consecutive `push` run immediately before the call is reported.

The convention predicts how many dwords the call site pushes versus the API's own
stdcall argument count (you know Win32/CRT signatures; use them):

* **A** swallows one dword → pushes should be `argc + 1`.
* **D** swallows two → pushes should be `argc + 2`, and there must be at least two.
* **B** swallows none → pushes should be `argc`.
* The back-alignment shows **competing readings of the same bytes**, junk-free
  first. A majority reading can be wrong: a bogus `fcom dword ptr [ebx + 0x57]`
  will happily swallow `d8 53 57` where the real code is `mov ebx, eax` /
  `push ebx` / `push edi`. Prefer the junk-free reading that yields a plausible
  push run, and name the reading you used in your note. Only conclude "zero
  pushes" when **no** alignment produces a push run.
* The window is only 16 bytes, so a run can be *truncated* — a run shorter than
  predicted is weak evidence, not a contradiction. Say "run truncated" in the note.
* A run *longer* than predicted, or **zero pushes where the convention requires
  one or two**, is a real contradiction.

## Step 3 — register-loader shape (the safety check that matters most)

The known artifact looks like this at the call site:

```
call <wrapper> ; <1-byte decoy> ; push arg ; push arg ; ... ; call <reg>
```

The wrapper computed the real API address into a register; the `call <reg>` is
the actual API call. Rewriting such an entry as a transfer wrapper crashes.

The sharpest form of this test is **where the arguments sit**:

* A genuine transfer wrapper is called *instead of* the API, so the API's
  arguments are pushed **before** `call <wrapper>` and there is no second call.
* A register-loading stub only computes an address, so the arguments are pushed
  **after** it returns and are consumed by a `call <reg>` whose register is
  never written anywhere in the window.

So: count the dwords pushed after the resume point up to the first `call <reg>`.
If that count equals the API's stdcall arity and the register has no visible
definition, the wrapper loaded it. Two `call <reg>` on the same register in one
window, each with a full argument list, is conclusive.

* Group **C** with this shape → `REGISTER-LOADER`.
* Group **C** without it, but with a clean transfer shape → `MISMATCH` (it
  contradicts live-crash evidence; a human must look).
* **Group A/B/D showing this shape** → `MISMATCH`, and say so loudly in the note
  ("register-loader shape in group X"). Do not write REGISTER-LOADER for these:
  the bytes cannot prove *this* wrapper loaded the register. This is the highest
  value finding in the whole census — it prevents a crash.

## Step 4 — the wrapper head (weak, auxiliary only)

The head window is 16 bytes and the obfuscation chain usually pushes the telling
instruction past it, so the absence of a fingerprint proves nothing. Use it only
to corroborate. If the head contradicts the call-site shape, **the call site wins**
and the verdict becomes `MISMATCH`.

## Verdicts

* `VERIFIED-TRANSFER` — resume point matches the group's prediction **and** at
  least one positive signal supports it (one alignment clearly invalid; a decoy
  byte that is nonsensical to execute; a pre-call push run matching the swallowed
  arg count; a clean epilogue/continuation at the resume point).
* `REGISTER-LOADER` — group C with the Step 3 shape confirmed.
* `MISMATCH` — the call site contradicts the convention: resume point on the wrong
  side, zero pushes where one or two are required, register-loader shape in A/B/D,
  or head-vs-call-site conflict. **This is a valuable result, not a failure.**
* `ODD` — group column empty, or evidence genuinely insufficient to decide.

Do not guess. An honest `ODD` or `MISMATCH` is worth more than a confident wrong
`VERIFIED-TRANSFER`: every VERIFIED row becomes a byte-patch in a running game
client.

## Output

Write **one CSV row per unit**, columns exactly:

```
entry_va,verdict,evidence_hex,evidence_note
```

* `entry_va` — copy verbatim from the unit header.
* `evidence_hex` — **a byte-exact, space-separated substring of that unit's own
  `RAW before` / `RAW after` / `RAW head` line.** Copy it; never type bytes from
  memory. Every instruction in the decode is shown with its hex in `[...]`, and
  adjacent instructions may be concatenated with a single space as long as they
  are adjacent in the RAW line. A quote that is not a substring voids the row.
* `evidence_note` — one sentence saying how those bytes support the verdict.
  Quote the mechanism, e.g. "+5 c3 would return mid-function while +6 continues a
  live struct copy, so c3 is the R+1 decoy" or "push run 3 dwords vs
  SetTextColor's 2 args confirms one swallowed dword".
  Use a semicolon, never a comma, inside the note — or quote the field properly.

Emit exactly one row per unit in the batch, in order. No header line, no prose
around the CSV.
