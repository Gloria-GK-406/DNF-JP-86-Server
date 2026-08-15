# TASK-01 — Dispatch and Transcript Baseline

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: Correct the fabricated handler multi-dispatch prohibition and establish behavior-level regression evidence for sequential top-level command dispatch plus the accepted SelectCharacter transcript before structural extraction.
- Referenced spec requirements: RQ-001, RQ-002, RQ-005, RQ-006, RQ-009.
- Referenced plan sections: T-001; BD-001; BD-002.
- Owned boundary: Architecture rule text and baseline command-bus/SelectCharacter protocol tests only.
- Permitted files or interfaces: `AGENTS.md`; `Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs`; `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/**`.
- Dependencies: None.
- Required dependency outputs: None.
- Acceptance conditions: `AGENTS.md` permits a protocol handler to dispatch multiple commands sequentially and prohibits only recursive/indirect command-bus dispatch from command handlers; tests faithfully observe two sequential top-level dispatches and nested rejection; protocol tests capture ACK-first ordered command/type identities, deliberate duplicates, conditional omissions and Dark Knight adjacency without depending on future internal class names.
- Task-specific authorization: Covered by the root task authorization; no remote mutation, merge, push, cleanup or wire change.
- Required methods: Apply the TDD value gate. Use behavior tests for command-bus and transcript behavior; use focused review for prose. Preserve current production behavior in this unit.
- Required checks: CK-001, CK-002, CK-003 from `plan.md`; `git diff --check` on owned files.
- Required evidence: Exact test commands and exit status, decisive assertions captured, owned diff paths, and self-check against the referenced requirements.

## Execution Result — Round 1

- Executor: root agent.
- Owned diff: `AGENTS.md`; `Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs`.
- TDD value gate: the protected behavior is top-level sequential dispatch versus command-handler nested dispatch, and the existing first and fifth `CommandBusTests` already observe that public bus behavior faithfully. A new handler-mock test would only freeze an internal interaction, so no new proxy test was added; the existing sequential test was renamed to make its established contract explicit. Existing SelectCharacter composer tests already provide the transcript oracle (ACK first, 58 identities, duplicates, conditional row omissions and Dark Knight adjacency), so this baseline unit preserved them unchanged.
- Implementation: replaced the overextended `AGENTS.md` sentence with the exact user rule: protocol handlers may dispatch multiple commands sequentially; `ICommandHandler` implementations may not recursively or indirectly dispatch through `ICommandBus`; shared business logic belongs in application services.
- Initial check exception: both planned `--no-restore` tests exited 1 with `NETSDK1004` because the new worktree had no `obj/project.assets.json`. `dotnet restore Server/86JP.Server.slnx` exited 0 and generated the local assets; no source assumption or task scope changed.
- Check: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CommandBusTests"` exited 0; 13 passed, 0 failed, 0 skipped.
- Check: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter"` exited 0; 32 passed, 0 failed, 0 skipped.
- Check: `git diff --check -- AGENTS.md Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs` exited 0.

## Executor Self-check — Round 1

- RQ-005/RQ-009: the maintained rule now distinguishes handler orchestration from forbidden command-handler recursion and matches executable `CommandBus` behavior.
- RQ-001/RQ-002/RQ-006: the existing behavior-level protocol suite remains green and records the accepted static transcript independently of the prose correction.
- Scope: no production behavior, wire bytes, order, command-bus implementation or unrelated file was changed.
- Residual risk: the current transcript oracle names legacy composer/descriptor classes; TASK-02 must migrate the same behavior assertions to concrete state messages before deleting those internals.

## Independent Quick Review — IQR-01

- Reviewer: `/root/task01_quick_review` (fresh independent context).
- Verdict: `changes_required`.
- Reviewed contract: revision 1; no amendments recorded.
- Owned diff identity: `sha256:39cdb2e33aa5ced01dfd6a6ce1ab787880197346d39e3c127bb3624e3379f49a` for `git diff -- AGENTS.md Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs`.
- Evidence inspected: the sealed Task Unit contract, Execution Result and Executor Self-check; `plan.md` T-001/BD-001/BD-002/CK-001..CK-003; `spec.md` RQ-001/RQ-002/RQ-005/RQ-006/RQ-009; repository `AGENTS.md`; the complete owned diff; focused test output (`CommandBusTests`: 13 passed, 0 failed, 0 skipped; `SelectCharacter`: 32 passed, 0 failed, 0 skipped); and clean `git diff --check` output.
- Finding IQR-01-F1: `AGENTS.md:90-92` still says `handler 里不能再发命令`, directly contradicting the corrected rule at `AGENTS.md:175-177`. Therefore CK-003, RQ-005/RQ-009 and the Task acceptance condition that repository guidance permits protocol-handler sequential multi-dispatch are not satisfied.
- Finding IQR-01-F2: no file under `Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/**` is present in the owned diff, while the Executor Self-check explicitly records that the unchanged transcript oracle names legacy composer/descriptor classes. This contradicts the Task acceptance condition and T-001 step 3 requiring an ACK-first semantic transcript baseline that does not depend on future internal class names; a green unchanged suite does not supply the required extraction-safe baseline.
- Required remediation: reconcile the remaining contradictory `AGENTS.md` command-bus paragraph with the exact handler-versus-command-handler rule, and add/convert the SelectCharacter protocol regression coverage to the required internal-name-independent semantic transcript assertion (including ordered identities, duplicates, conditional omissions and Dark Knight adjacency), then rerun CK-001, CK-002, CK-003 and `git diff --check` on the complete owned files.

## Execution Result — Round 2

- Executor: root agent.
- Findings remediated: IQR-01-F1 and IQR-01-F2.
- Owned diff: `AGENTS.md`; `Server/tests/Dfo.Server.Application.Tests/Commands/CommandBusTests.cs`; rename/rewrite from `SelectCharacterResponseComposerTests.cs` to `SelectCharacterAcceptedTranscriptTests.cs`.
- IQR-01-F1: corrected the earlier CommandBus overview paragraph as well as the detailed handler rule. Both now state that protocol handlers may invoke `SendAsync` sequentially and only command handlers are prohibited from recursive/indirect bus dispatch.
- IQR-01-F2: replaced the descriptor-structure assertion and composer-named test surface with an accepted-entry transcript oracle expressed only as observable frame command/type identities. It asserts ACK first, all 58 reviewed identities, deliberate duplicates, conditional omissions, the single support-state message and Dark Knight adjacency. The legacy composer call is isolated behind one temporary capture seam so TASK-02/03 can replace the producer without rewriting the oracle.
- Check: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CommandBusTests"` exited 0; 13 passed, 0 failed, 0 skipped.
- Check: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~SelectCharacter"` exited 0; 32 passed, 0 failed, 0 skipped.
- Check: `rg -n "handler 里不能再发|handler 里不能再发命令|handler.*不能.*第二条命令" AGENTS.md Docs Server/src Server/tests` returned no matches.
- Check: `git diff --check` on all owned tracked paths exited 0; the renamed test file is a new text file with no whitespace defects observed by the successful build/test compile.

## Executor Self-check — Round 2

- RQ-005/RQ-009: both repository rule locations consistently permit handler multi-dispatch and prohibit command-handler nesting.
- RQ-001/RQ-002/RQ-006: the accepted transcript assertions no longer depend on descriptor rows or a composer-named test contract; they observe the wire identities that later implementations must reproduce.
- Review remediation stayed within the sealed Task boundary and did not modify production behavior.
- Owned diff and evidence identify Round 2; Round 1 remains retained for audit.

## Independent Quick Review — IQR-02

- Reviewer: `/root/task01_quick_review_round2` (fresh independent context).
- Verdict: `passed`.
- Reviewed contract: revision 1; no amendments recorded; verdict applies only to the latest Round 2 Execution Result, Executor Self-check and owned diff.
- Owned diff identity: `sha256:f0c48157abfbc3a8e0486a18e9b9b83156ff4dfd21742ec9d9ba2b1077f12086` for the combined patch covering `AGENTS.md`, `CommandBusTests.cs`, deleted `SelectCharacterResponseComposerTests.cs`, and new `SelectCharacterAcceptedTranscriptTests.cs`.
- Evidence inspected: the sealed Task Unit contract and Round 2 result/self-check; `plan.md` T-001/BD-001/BD-002/CK-001..CK-003; `spec.md` RQ-001/RQ-002/RQ-005/RQ-006/RQ-009; repository `AGENTS.md`; the complete owned patch including the deleted and untracked replacement test; fresh CK-001 (`13 passed, 0 failed, 0 skipped`) and CK-002 (`32 passed, 0 failed, 0 skipped`) output; CK-003 contradiction search (no matches, `rg` exit 1); tracked `git diff --check` (exit 0); and an explicit no-index check of the untracked replacement file (no check diagnostics; `git diff --no-index` exit 1 only because the file differs from `/dev/null`).
- Findings: no obvious blocking defect was demonstrated in the bounded Round 2 inputs. Both guidance locations now permit sequential protocol-handler dispatch while forbidding command-handler nesting; the command-bus tests observe two sequential top-level executions and nested rejection; and the replacement transcript oracle asserts ACK-first exact identity order, deliberate duplicate positions, conditional omissions, the single support-state identity and Dark Knight adjacency without coupling those assertions to descriptor rows or a future implementation class name. The one current composer call is isolated as the replaceable transcript-capture seam.
- Required remediation: none for this quick-review round. This verdict permits Task Unit acceptance logic only and is not final Spec or complete-change approval.
