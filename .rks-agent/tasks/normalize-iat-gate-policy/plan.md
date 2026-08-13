# Plan: Normalize IAT startup-gate policy across system ASLR

Approved spec: `./spec.md`. Controller: SDD Standard, code-feature Profile.

## Design summary

Drop every captured absolute address from the IAT gate. Accept a slot by identity only:

1. `actual == resolved` → RESOLVED (idempotent across boots; `resolved` is the current `GetProcAddress`).
2. `resolved != 0 && actual == 0` → NEEDS_REPAIR (initial unresolved state; repaired to `resolved`).
3. `resolved != 0 && actual` falls inside any currently-loaded module range → NEEDS_REPAIR (legitimate forwarder stub; repaired to `resolved`).
4. `resolved == 0` (DNF data export) → `actual == module_base + module_rva` → RETAIN.
5. otherwise → REJECTED (fail closed; dangling address).

Module ranges are gathered once in `iat_repair.c` (Win32 `GetModuleHandle`/`LoadLibraryEx` + `GetModuleInformation`/`VirtualQuery`) and passed to the pure-logic policy, so `iat_state.c` stays platform-free and unit-testable.

The 28 kernel32-direct slots (previously exact-matched to `0x74xxxxxx`/`0x77xxxxxx`) now pass via rule 1 at gate time on any boot, because their gate-time actual already equals the current `resolved`. The 311 msvcp60 forwarder-stub slots pass via rule 3 (stub inside the msvcp60 module range). idx 341 passes via rule 4. idx 375 (msvcr71 swprintf compat) passes via rule 1 or 3.

## Boundaries and dependencies

- BD-001: `iat_state.c`/`iat_state.h` are consumed by CTest as pure logic. The new signature must remain platform-free (no windows.h in iat_state.c).
- BD-002: `iat_repair.c` is the only caller of `ch_iat_policy_evaluate` in production; CTest is the other. Both must be updated atomically with the signature change or the build breaks.
- BD-003: The five-required-plugin lab launch depends on the policy accepting the supported client. Any rule that rejects a legitimate gate-time state regresses R6002 and fails AC-005.
- CK-001: Rule 3 must use the *current* loaded-module ranges, not captured bases, or the ASLR fix is void.
- CK-002: The dangling-address rejection (rule 5) must be exercised by a test, or fail-closed is unverifiable.

## Steps (ordered)

### T-001 Policy data and signature

- T-001.1 In `iat_gate_policy.h`: delete `g_ch_iat_gate_exact[]` and `CH_IAT_GATE_EXACT_COUNT`. Keep `CH_IAT_GATE_ENTRY_COUNT`. Replace the index-341 absolute handling with `CH_IAT_GATE_RELATIVE_INDEX = 341`, `CH_IAT_GATE_RELATIVE_RVA = 0x000060F0u`. Remove `CH_IAT_GATE_EXTRA_EXACT_INDEX`/`_VALUE` (375); it is now handled by general identity rules, no special constant needed.
- T-001.2 In `include/clienthack/iat_state.h`: add `typedef struct ch_iat_module_range { uintptr_t base; uintptr_t size; } ch_iat_module_range;`. Change the signature to:
  `ch_iat_policy_state ch_iat_policy_evaluate(uintptr_t actual, uintptr_t resolved, const ch_iat_module_range* modules, size_t module_count, uintptr_t module_base, int is_data_export, uintptr_t data_export_rva);`

### T-002 Policy implementation

- T-002.1 In `src/core/iat_state.c` rewrite `ch_iat_policy_evaluate` to the 5-rule design. Add a static helper `ch_iat_address_in_modules(addr, modules, count)` scanning the range array. Keep `ch_iat_slot_evaluate` and `ch_iat_repair_transaction` unchanged.

### T-003 Production caller

- T-003.1 In `src/az/iat_repair.c`: after resolving modules, build a `ch_iat_module_range modules[8]` array (extend the existing `ch_module_cache_entry` cache to also hold module size; use `GetModuleInformation` via `psapi` or fall back to `VirtualQuery` walk to get `size`). Gather distinct module ranges once before the slot loop.
- T-003.2 Replace the captured-value selection block (lines ~95-104) so each slot passes `resolved`, the module range array, and for idx 341 the data-export identity (`is_data_export=1, data_export_rva=0x60F0, module_base=msvcp60 base`). No slot reads `g_ch_iat_gate_exact`. Remove the `captured`/`captured_is_module_rva` locals.
- T-003.3 Keep the transaction/rollback path (`ch_iat_repair_transaction`) and the `VirtualProtect` PAGE_EXECUTE_READWRITE slot writes unchanged.

### T-004 CTest update

- T-004.1 `tests/test_main.c::test_iat_policy_and_transaction`: update all `ch_iat_policy_evaluate` calls to the new signature. Cover: RESOLVED (actual==resolved), NEEDS_REPAIR (actual==0), NEEDS_REPAIR (actual inside a module range via forwarder), RETAIN (data-export RVA), REJECTED (dangling address outside all ranges).
- T-004.2 `tests/test_main.c::test_production_iat_gate_policy`: remove the `g_ch_iat_gate_exact[index]` branch. Replace with identity-based expectations. Add a **relocated-base case**: call the policy with module bases different from any captured value and assert every previously-exact slot is accepted by identity (rule 1/3) while a wrong/dangling value is REJECTED.
- T-004.3 Ensure the required-plugin metadata test still expects five plugins (already true after b68c0e6) and does not regress.

### T-005 Build and CTest (Windows MSVC)

- T-005.1 Configure with `cmake --preset win32-release` using VS18's cmake and vcvars32 (x86). Build `ALL_BUILD`/`clienthack_stage` in Release.
- T-005.2 Run `ctest --preset win32-release --output-on-failure`. Require all suites green, including the new relocated-base case.

### T-006 Isolated lab launch (AC-005)

- T-006.1 Create/reuse an isolated lab copy of the client (not `D:\project\DNFClient`). Deploy the fresh stage (Az.dll, passive ijl15.dll, ClientHack.ini, five plugin DLLs) into it.
- T-006.2 Launch the lab DNF under bounded monitoring. Confirm the Host log records IAT policy acceptance and the five required plugins initialized in numeric order, with no fatal warning/crash, and that the process reaches the game window (past the former R6002 interval).
- T-006.3 Record source-client hashes unchanged after the run.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Rule 3 module-range scan is too loose (accepts a wrong address that happens to fall in a module) | This matches the prior fail-closed strength: the old exact table also only asserted "equals one specific stub". A dangling address outside all modules is still rejected (CK-002 test). |
| `GetModuleInformation` needs psapi linking | Use `VirtualQuery` walk to find the module's committed region extent; avoids a new link dependency. |
| idx 341 module identity (msvcp60 vs msvcr71 in iat_table.h) | Use the runtime `GetModuleHandle` base of whichever module `iat_table.h` names for index 341; the RVA 0x60F0 is normalized against that base. |
| Lab regression (R6002) if a rule wrongly rejects | T-006 is the gate; if it fails, debug via the Host log's per-slot REJECT message before adjusting rules. |

## Verification mapping

- AC-001 (EV-001): T-001, T-002, T-003 — source diff shows identity-only acceptance, exact table removed.
- AC-002 (EV-002): T-004.1 — policy unit cases.
- AC-003 (EV-002): T-004.2, T-005 — relocated-base CTest green.
- AC-004 (EV-003): T-005 — clean build + integrity.
- AC-005 (EV-004): T-006 — isolated lab launch to game window.
