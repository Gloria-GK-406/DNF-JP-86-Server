---
kind: rks-verification
decision: pass
---

# Completion Evidence: Complete G6 Entry Projection

## Claim

G6 converts the completed G1-G5 selection result and typed lifecycle projections into the
complete code-reversed Select Character wire stream without performing business queries,
target discovery or lifecycle mutation in Networking.

## Condition Mapping

| Condition | Fresh evidence |
|---|---|
| SC-9.1 | The source sequence contains 58 descriptors with row identities exactly 1 through 58; the coverage table contains the same 58 rows. Composer and sequence focused checks pass. |
| SC-9.2 | Production composition fills skill mirrors, collection protocol state, row-38 support state and row-42 lottery state before publication. Populated source-to-result and payload vectors pass, including exact NOTI-2 dynamic option and creature-lifetime precedence. Independent complete review is approved. |
| SC-9.3 | Positive/negative tests cover Knight, pet-welcome, Dark Knight, item containers, duplicates, mailbox, supplemental batches and required-state failures. Networking focused checks pass 35/35. |
| SC-9.4 | The coverage table contains X-01 through X-18 exactly once. Public typed composer and delivery contracts accept packet-ready projections and explicit targets; raw frames and target discovery stay inside Networking. |
| SC-9.5 | Fresh full solution verification passes 429 tests with two expected archive-present skips. Dependency scans find no SQLite, PVF or repository dependency in Networking and no SQLite/PVF dependency in Application. |
| SC-9.6 | `SelectCharacterG6Coverage.md` maps all 58 rows and 18 X identities to typed sources, codecs, conditions and G7 ownership, while retaining code-reversed/no-capture confidence. Complete review is approved. |

## Fresh Commands and Results

- `PVF_ARCHIVE_PATH="$PWD/Script.pvf" dotnet test Server/86JP.Server.slnx --no-restore`: exit 0; PVF 60, Application 206, Networking 64, Infrastructure 99 plus 2 expected skips; 429 passed, 2 skipped, 0 failed.
- Infrastructure source-focused selection checks: 38/38 passed.
- Application selection/composition checks: 16/16 passed.
- Networking selection and delivery checks: 35/35 passed.
- Mechanical extraction: coverage static rows 58; source descriptors 58 with row sequence 1-58; supplemental coverage entries 18.
- `git diff --check`: exit 0.
- Networking and Application dependency scans: no prohibited matches.
- Independent complete change review: `approved`.

## Skips

The two skipped Infrastructure checks deliberately exercise a missing archive. They skip
when the supplied real archive is reachable; all positive PVF paths, including the
usable-period-only creature equipment path, ran and passed.
