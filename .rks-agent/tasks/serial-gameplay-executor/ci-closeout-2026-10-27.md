# PR #211 CI closeout — 2026-10-27

This is new verification evidence for the final branch. It does not amend the sealed A/B acceptance records or claim A21 client validation.

## Failures and corrections

1. Windows checkout exceeded the default Git path limit in retained acceptance evidence. The checkout step now supplies `core.longpaths=true` through process configuration. The next Windows run completed checkout. Version remains 0.2.8, one patch above target main 0.2.7.
2. Linux without Script.pvf exposed 162 Application fixture failures. Business fixtures now declare their synthetic content dependencies; archive cases explicitly restore the real content registrations. Existing PVF skips and business assertions remain. With the archive enabled, two previously hidden fixture issues were corrected: marriage party writes enter the gameplay executor, and the illusion tower public-flow fixture uses archive rewards and placement together.
3. Host fixtures released a probed port before Kestrel could bind it. The ephemeral GM listener now retains the bound, listening socket and passes that same socket to Kestrel. Explicit port behavior remains covered. Networking probes now match IPv4 Any/non-reusable listener semantics and never reissue a port within the test process; startup guards report actual bound endpoints and original bind errors. TouchSocket still requires releasing its probe before binding, so an external process can race that handoff.
4. TouchSocket 4.3.1 native close can discard flushed outbound bytes, reproduced on Linux with real socket backpressure. The pinned transport adapter owns the close phase, stops the native receive/send loops, drains remaining native input after those loops stop, and performs ordinary socket close. A close-phase barrier prevents the framework callback from issuing its abortive close or returning pooled state early. Physical close and framework cleanup have separate bounded deadlines. This adapter intentionally depends on the exact TouchSocket assembly/private members and must be revalidated on dependency upgrades.

## Final local evidence

| Scope | Result |
| --- | --- |
| Application, isolated checkout without PVF | 5,453 passed; 493 existing PVF skips; 0 failed |
| Application, explicit real PVF | 6,048 passed; 2 previously documented Dimension failures; 0 skipped |
| Host, Linux without PVF | 200 passed; 12 PVF skips; 0 failed |
| Host, macOS with PVF | 212 passed; 0 failed |
| Networking, final macOS with PVF | 210 passed; 0 failed |
| Networking, final Linux without PVF | 189 passed; 20 PVF skips; 0 failed |
| Real socket close with blocked receive callback, with/without unread kernel input | Both cases passed on macOS and Linux; all bytes followed by FIN and bounded close |
| Other full solution project results with PVF | Protocol 1,798; Persistence 484; Infrastructure 974 + 2 skips; HttpApi 59; Pvf 80 passed |

The two remaining archive failures are `DimensionDungeonTests.Fusion_real_content_builds_six_reachable_rooms_and_the_actual_boss_resource` and `All_six_real_cells_materialize_with_the_boss_and_personal_chronicle_loot`: dungeon 7100, maze 0, cell (1,0) has no compatible Left entrance. These were recorded before this repair, and are not suppressed here.

Independent review approved the close adapter, Host fixture correction, and Application fixture corrections. Final Networking port helpers received direct review and full macOS/Linux regression. Logs and TRX files are retained with the closeout artifacts. The original full-platform merge gate was subsequently changed by the user as recorded below.

## Final Windows correction and user-directed CI scope

The full CI run [40439120725](https://github.com/Gloria-GK-406/DNF-JP-86-Server/actions/runs/40439120725) at `1ee60233` completed before changing the gate, as requested. Linux passed all tests and package smoke. Windows passed every other test but the new unread-input close regression: only 73,728 of 921,600 bytes arrived, followed by reset. Its package step did not run; this is not a full Windows package acceptance.

Winsock forbids further receive after `Shutdown(Both)`, so it cannot use the Unix drain sequence. Commit `bc6f6426` preserves its receive direction with `Shutdown(Send)`, cancels the original overlapped I/O through `CancelIoEx`, waits for the original transport tasks to exit, and drains only currently available input under the same close deadline. Independent review approved the source and ownership/error boundaries. The temporary Windows-only Networking run [40445397974](https://github.com/Gloria-GK-406/DNF-JP-86-Server/actions/runs/40445397974) passed, including the unchanged strong backpressure regression. Both strong cases also passed again on macOS and Linux. See `Document/ProtocolCommands.md` for the bounded guarantee and dependency seam.

The user then directed that PR testing be smaller and full cross-platform testing run only at Release. The final `ci.yml` therefore runs one Linux job on PR, main push, and manual dispatch: frontend unit tests, the report guard, a full compilation fence, and the positive `Category=Unit` filter. The temporary Windows workflow is removed. `release.yml` and its reusable `validate.yml` retain the complete Windows/Linux suite, native package creation, smoke, and publication dependencies.

110 explicitly reviewed classes in 105 files received only a Unit trait; no test body or assertion changed. The selection covers byte codecs, pure rules and in-memory mechanisms, excluding real SQLite, sockets, Host, archive/file fixtures and mixed classes. Local filtered execution passed 1,286 tests (Pvf 37, Application 726, Networking 20, Protocol 503), with no failures or skips. Other projects may have zero selected tests, but the aggregate must execute tests; three report-guard controls and all 14 Release-tool tests passed. Independent review approved the final CI boundary and classification. The new Linux unit CI is the merge gate; the earlier full run and focused Windows run remain separate evidence with their stated limits.
