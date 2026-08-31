---
kind: rks-task-contract
revision: 2
state: ready
---

# TaskContract: Rule Services Own Every Rule Input

## Context

### Source Request

While reviewing the drop system's structure the user asked which shape the rule layer had taken,
then stated the architecture the project is supposed to have:

> 这里的规则服务不只是吃pvf，有可能还吃一些配置。是抽象的规则服务

> 要全改，设定上就是为了将规则部分抽出来不影响业务逻辑才放到业务服务内。而规则又基本读取pvf配置之类的，
> 所以单出一个规则接口。整体的架构实现就是业务服务被CMD handler调用完成业务功能。规则服务在业务服务内
> 提供规则判断，辅助完成整个业务逻辑。如果现在整个仓库里这个结构就是不存在的。那么确实需要全量重构。

After a whole-repository survey was presented, the user chose the baseline and the workflow:

> 先合并 monster-drop-rates 到 main，再从 main 开

> handoff不用管，结束后我们就直接删除那个文件了。合并后写任务契约和sdd。到执行前停下来

The user also settled one boundary question explicitly: host deployment parameters are not rule
inputs, so `IServerConfigService` stays as it is.

### Current Situation

Baseline `8af7990c` on `main`, clean tree, full suite measured on this machine at 1690 passing,
2 skipped, 0 failing.

`Docs/领域结构重构设计.md` §2 already declares the line this task is about: a rule service's inputs
are *content (PVF) + facts + explicit seed*, while an aggregate's input is a command. §6 constrains
only one direction of that line — a rule service may not query world state, "否则它成为世界的第二个
所有者". The opposite direction was never constrained: nothing stops a domain service or a handler
from reading content itself.

`domain-rule-service-taxonomy` gave the categories their names but deliberately changed no
behavior; its Out scope was "any change to observable behavior". It classified what already
existed — a service whose implementation took only content sources became `I*RuleService` — rather
than making rule services the owners of content. So the present state is not a violation of that
taxonomy; it is a boundary that taxonomy never established.

A survey at this baseline found the declared line unenforced in every direction that matters:

- PVF content sources are injected at roughly 49 points across 31 files, covering 24 source
  interfaces. Seven of those injections sit in the four rule services; the remaining 42 do not.
  Sixteen of the 31 files are Application services and fifteen are Protocol-layer files — four of
  them protocol handlers, which crosses two layers at once.
- Of 4 `*Options` types, 2 sit in rule services. `DropRateOptions` is injected into a domain
  service, and `SelectCharacterDeliveryOptions` into a protocol handler, with its type declared in
  that handler's own file.
- Six static rule classes exist and none is reached through a rule service; one is called directly
  from a protocol handler.
- Four call sites reach a rule service from the Protocol layer, bypassing the domain service.

`EntryPlacementRuleService`, which takes `ITownDefinitionSource` together with
`ChannelEntryOptions`, is the only service in the repository already in the intended shape, and is
evidence that the shape works here rather than a shape yet to be proven.

`AGENTS.md` presents `*RuleService` and `*Rules` as two parallel entries in one classification
table. That reading is consistent with the current code and inconsistent with the architecture the
user states, which makes the document a cause of the drift rather than a record of the intent.

## Goal

Make rule inputs reach this server only through rule services, so that a domain service expresses
business flow and a rule service answers what the rules say, and record that layering where a later
reader will find it — all without changing what the server does.

## Scope

### In

- Give every PVF content source, every game-rule options type, and every rule constant currently
  read outside a rule service an owning rule service, and route its existing consumers through
  that owner.
- Bring the calls that reach a rule service directly from the Protocol layer back through the
  domain service that owns the business flow.
- Make each static rule class reachable through the rule service that owns its subject rather than
  from domain services or handlers.
- Record the resulting layering in the repository's own conventions, replacing the reading in which
  `*RuleService` and `*Rules` are alternatives.

### Out

- Host deployment parameters. `IServerConfigService` and its three consumers stay as they are, and
  the note recording it as a deliberate exception is not revised.
- The handler capabilities `AGENTS.md` already sanctions, including endpoint resolution through
  `IChannelEntryPolicy`.
- Any change to observable behavior, frame bytes, persistence, ordering or timing.
- Establishing archive provenance for rule constants that currently declare none. A constant moves
  to its owning rule service carrying the value and the justification it already has; discovering
  whether the archive declares it is separate work.
- The card-flip reward system, and every other deferred feature. This task changes where rules
  live, not which rules exist.

## Constraints

- Behavior must be preserved exactly, including the values the rules produce. This task moves rule
  inputs and rule evaluation; it decides nothing anew.
- The constraint in `Docs/领域结构重构设计.md` §6 must still hold afterwards. A rule service that
  acquires world-state access in order to receive a relocated rule would trade one broken direction
  of the line for the other.
- Deployment parameters remain outside the rule layer, per the user's decision.

## Success Conditions

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | Rule inputs enter the server only through rule services | An enumeration of every PVF content source, game-rule options type and relocated rule constant shows each one reaching the system through a rule service, with the deployment-parameter exception named and no other exception unaccounted for |
| SC-2 | A static rule class is reached only through the rule service that owns its subject | A search for each static rule class's call sites returns rule services only |
| SC-3 | The Protocol layer reaches rules only through domain services | A search shows no protocol or command handler injecting a content source, a game-rule options type or a rule service, apart from the handler capabilities `AGENTS.md` sanctions |
| SC-4 | The server still does exactly what it did | The full solution suite passes at no less than the baseline count, and the diff of `Server/tests` changes no assertion, expected value or test case — only the identifiers and wiring the move requires |
| SC-5 | Rule services still receive facts rather than querying the world | No rule service takes a repository, registry, directory or store, so the §6 constraint is visible in the constructors themselves |
| SC-6 | A later reader finds the layering recorded | The repository's conventions state the layering as a stack rather than as alternatives, and a reader following them would not reproduce the drift this task removes |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | The user described the intended architecture, judged the survey result — "如果现在整个仓库里这个结构就是不存在的。那么确实需要全量重构" — then directed: "合并后写任务契约和sdd。到执行前停下来". Shown the specification at its approval gate, the user extended it: "改spec，但是依旧是std流程。临时改后继续写plan就好了" | Author this TaskContract and the SDD artifacts that follow it — routing decision, specification and implementation plan — on `main` at `8af7990c`, under the controller already selected. Implementation is withheld by the user's own instruction. |

## Requires Confirmation

- Implementation. The user required a stop before execution. The specification and the plan are
  authorized; the start of any code change needs a separate grant.
- Merging, pushing, branch or worktree cleanup, and deletion of `HANDOFF.md`, which the user
  intends to remove after this work rather than now.

## Assumptions

- The survey figures — 31 content-source read points with 4 in rule services, 4 options types with
  2 in rule services, 6 static rule classes with none behind a rule service, 4 Protocol-layer calls
  into rule services — were measured at `8af7990c`. They are expected to hold, but the counts are
  not themselves acceptance criteria; the enumeration is.
- Test files name the types being moved, so their diffs cannot be empty. SC-4 treats an
  identifier-and-wiring-only test diff as evidence of preserved behavior, which is the same reading
  `domain-rule-service-taxonomy` used.
- Six services already named `*RuleService` hold their rules as hard-coded constants rather than
  reading content. They are already on the correct side of the line, and this task does not require
  giving them content sources they do not need.
- The full suite is the practical behavior oracle available here. Two dungeon frames and the
  breakable-object drop counts have no live confirmation yet, so behavior preservation is
  established against the suite and the archive rather than against a running client.
