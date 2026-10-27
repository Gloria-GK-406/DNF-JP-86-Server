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

Independent review approved the final close adapter, Host fixture correction, and Application fixture corrections. Final Networking port helpers received direct review and full macOS/Linux regression. Logs and TRX files are retained with the closeout artifacts. PR Windows/Linux CI, including publish/package smoke, remains the merge gate; local evidence alone does not establish that gate.
