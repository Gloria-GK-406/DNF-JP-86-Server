# Implementation round 1

- Strongly typed DungeonDifficulty across entry, run, rooms, quest facts and reward contexts; explicit raw conversion at packet and PVF/table boundaries.
- CharacterDungeonDifficultyDomainService reads the existing table, defaults Normal, groups legacy duplicate dungeon rows by maximum, refuses unknown values and unlocks monotonically inside the existing database transaction.
- Single-player entry validates before preparing any room or Run; existing party policy is unchanged. Production path uses facts.Difficulty as its single input; legacy internal compatibility overload normalizes facts from its explicit difficulty.
- NOTI0005 now uses u32+u8 entries; fresh permission projection is sent before ordinary selection opens. NOTI001C reads the actual Run difficulty.
- Review correction: old 9-byte tutorial test was stale; #101 records tutorial and ordinary as 15 bytes. Decoder now requires15 and rejects truncation9..14. Unknown trailing fields receive no new meaning.

## Checks

- RED: dotnet test Server/tests/Dfo.Server.Protocol.Tests --filter FullyQualifiedName~A21_adventure_request --verbosity quiet (baseline 026c4f9f plus test): exit1, 1 failed due to u16 truncation/offset.
- GREEN: DungeonEntryCodecTests 28 passed, then additional undefined-value and 5-byte-stride tests included in full Protocol 981 passed.
- With PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf: Application1639 passed, Persistence361 passed. Final Protocol987 passed after selection-frame and truncation regression updates.
- Persistence six new tests were written after implementation (not claimed as TDD); cover monotonic/default/isolation/reload, rollback, duplicate legacy rows and invalid states.
- Initial broad Protocol run lacked PVF; reproduction confirmed FileNotFoundException. The only independent byte-baseline change was NOTI0005, hash recalculated from specified bytes.

## Remaining

Independent review approved; user-assisted normal/adventure entry, movement and relogin passed. See verification/completion-evidence.md. Main integration remains a separate decision.
