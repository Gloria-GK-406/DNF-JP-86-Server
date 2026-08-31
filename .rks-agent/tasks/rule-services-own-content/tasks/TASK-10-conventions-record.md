# TASK-10 — The conventions record describes the layering as a stack

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: `AGENTS.md` states that a business service consults a rule service and a rule service calls a pure rule function, replacing the reading in which `*RuleService` and `*Rules` are parallel alternatives. It states that a rule input is read by a rule service and by nothing else, and that host deployment configuration is not a rule input.
- Referenced spec requirements: RQ-007, RQ-009
- Referenced plan sections: T-010; boundary BD-016
- Owned boundary: BD-016 `AGENTS.md`.
- Permitted files or interfaces: `AGENTS.md` only.
- Dependencies: TASK-09
- Required dependency outputs: the completed structure from TASK-01 through TASK-09, since the record must describe what the code now is.
- Acceptance conditions: every statement in the revised record matches the code; no statement permits a business service or handler to read a rule input; the existing record of the deployment-configuration exclusion and of the four sanctioned handler capabilities is unchanged; host deployment configuration consumers are unchanged from the baseline.
- Task-specific authorization: the root task grant. Implementation only. The user's standing instruction forbids revising the deployment-parameter exclusion note.
- Required methods: none beyond reading the revised record against the completed structure.
- Required checks: CK-020 non-command reading of the revised record against the completed structure; CK-021 non-command comparison of host deployment configuration consumers against the baseline.
- Required evidence: the CK-020 reading, the CK-021 comparison, and the owned diff.

## Contract Amendment 3 — a stale entry in the honest-deviation list

- Raised by: TASK-08.
- Contract revision: 2

AGENTS.md's "其他与主设计的偏差（诚实清单）" says "ScriptVersion、Script、Info 命令仍负责读取
基础设施内容源". As of TASK-08 those three command handlers read the channel rule service instead.
The entry must be updated rather than left standing, since the whole point of that list is that it
is true.

## Execution Result — round 1

### What changed in `AGENTS.md`

Four edits, all in the direction the Spec's RQ-009 asks for: the record now describes a stack where
it described alternatives.

1. **The flow diagram.** Its last domain arrow read
   `领域服务（Application）→ 仓储与存储（Persistence）/内容源（Infrastructure）` — a domain service
   reaching content directly, which is the drift itself drawn as the design. It now reads
   `领域服务 → 仓储与存储` and, on its own line, `→ 规则服务（Application）→ 内容源（Infrastructure）`.

2. **A new subsection, "三层是上下关系，不是三选一".** It draws the four layers, says each calls
   only the one below it, states that a rule input enters the server only through the rule service
   that owns its subject, and names the three kinds of rule input. It closes the line the design doc
   opened: §6 forbids a rule service from querying world state; this forbids everything above it
   from reaching past a rule service for content.

3. **Three rows of the taxonomy table.** `*RuleService` now says it is the only reading point for
   content sources and rule configuration, and that business services call it rather than handlers.
   `*Rules` now says it is called only from a rule service and is the layer *below* one rather than
   an alternative to it — the exact reading that made the drift look conforming. `*ContentSource`
   and its siblings now say they are injected only into rule services.

4. **The honest-deviation list.** Its entry "ScriptVersion、Script、Info 命令仍负责读取基础设施内容源"
   became untrue in TASK-08 and now records what those three commands do instead.

### What was deliberately not changed

- The `IServerConfigService` exclusion note. The new subsection *refers* to it and does not restate
  or revise it, per the user's standing instruction.
- The four sanctioned handler capabilities in "handler 业务语义规则".

### Checks

| Check | Method | Result |
|---|---|---|
| CK-020 | read the revised record against the completed code | Every statement matches; see below |
| CK-021 | `git grep IServerConfigService 8af7990c` against the working tree | Identical, line for line: `Program.cs:39`, `ServerConfigService.cs:7`, `InfrastructureModule.cs:25`, `GameLoginHandler.cs:19`, `OnlineUserConnectionObserver.cs:22`. The exclusion note's own text is untouched — the `AGENTS.md` diff contains no removed line mentioning it |

CK-020 was verified by sweeping the claims rather than by reading them agreeably:

- "内容源只被规则服务注入": every `*DefinitionSource` / `*ContentSource` / `*Catalog` / `*Source`
  constructor parameter in `Server/src` outside `Infrastructure` sits in a `*RuleService`
  implementation. The one entry that does not look like it — `PlayingCharacter.cs:110` — is
  `ItemUseJudgementService`, a rule service that shares a file with its neighbour.
- "静态规则类只由规则服务调用": zero call sites outside rule services across the whole of
  `Server/src`. The single grep hit is a comment in `ApplicationModule`.

### A statement of my own that had to be corrected before it could pass

The first draft said flatly "handler 也不自己问规则服务". That contradicts the same document two
sections later, where endpoint resolution through `IChannelEntryPolicy` is one of the four
capabilities an ordinary handler may exercise — and it contradicts the code, which has three such
handlers. Caught by reading the revision against the sanctioned-capability list rather than against
the new rule alone. The sentence now names the exception and points at the section that owns it.

An over-absolute convention is worse than none: a reader who found the three handlers would conclude
the record was wrong about everything, which is precisely the failure this Task exists to remove.

## Executor Self-check — round 1

- Acceptance "every statement in the revised record matches the code": met, by the two sweeps above.
- Acceptance "no statement permits a business service or handler to read a rule input": met — the
  new subsection forbids it explicitly and three table rows repeat it from the other side.
- Acceptance "the deployment-configuration exclusion and the four sanctioned handler capabilities
  are unchanged": met, and the diff shows no removed line touching either.
- Acceptance "host deployment configuration consumers are unchanged from the baseline": met, CK-021.
- Two classifications that are not in the Spec but would otherwise leave the record incomplete are
  named in it: `ExperienceDiagnostics` as a measurement switch and `SelectCharacterDeliveryOptions`
  as protocol delivery behaviour. Both point at code that carries the reason.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The flow diagram was the most valuable of the four edits** and was not in the Task's stated
   inputs, which named the taxonomy table and the handler rules. A reader following the diagram
   would have built the drift again regardless of what the table said, because the diagram is what
   gets read first.
2. **The over-absolute sentence is the finding worth keeping.** It would have passed a reading of
   the new rule in isolation and failed against the document as a whole.
3. **The honest-deviation entry was found by TASK-08, not by this Task's checklist.** Worth noting
   that a record's own list of known deviations goes stale the moment one is fixed, and nothing
   points at it from the code.
4. **Scope check.** `AGENTS.md` is the only file changed.
