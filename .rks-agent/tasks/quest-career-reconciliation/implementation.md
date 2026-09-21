# Implementation round 1

Changed: career reward settlement, CharacterCareerDomainService and transactional grow_type writer; CharacterSkillStateDomainService.ReconcileAsync uses existing career/free-level rules; special ACK branch, current-view reload, ordered skills/ACK/character projection and public peer delivery.

Checks: RED invalid reward test at baseline failed twice as expected (Invalid_career_reward, 0/16). Targeted GREEN: Application 12/12 including real PVF7840 success/failure; Protocol QuestCompletion 27/27. Solution build 0 errors/0 warnings. Later assertions: incorrect test ledger expectation corrected to 1000+5-30-20=955; incomplete handler fixture populated Features. These were test setup errors, not new game mechanisms.

Round-one follow-up (now completed): independent review, broad regression and fresh verification. Manual A21 acceptance explicitly deferred to user; no running-service/player-database changes.

Pre-review self-check: A21 subtype0 does not carry base stats. Added self-only subtype1 (ExtendedUserStateMessage) after identity to send updated stats; peer targets still carry only public subtype0.

Review remediation: reapply authoritative weakness through existing WeaknessProjectionDelivery after self-state frames (28% test). Preserve unmodelled learned rows, their slots and extra values, in accordance with existing incomplete-ledger semantics; only proven career-inapplicable rules may remove a skill. Added preservation/idempotence test.

Final review remediation: public appearance delivery checks the currently selected source identity, exact town membership lease, and the dungeon admission registry generation for both source and recipients. Seven recipient tests cover valid delivery, replaced source/target and private rooms. Final protocol suite: 1026 passed; Host: 17 passed; solution build: zero warnings/errors.
