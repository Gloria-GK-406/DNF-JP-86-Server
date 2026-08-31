# TASK-08 — Tutorial reward and channel content answers come from rule services

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: One rule service owns what a tutorial step grants. One rule service owns channel catalogue and script content, and the existing channel entry policy obtains its catalogue from it so one owner remains.
- Referenced spec requirements: RQ-001, RQ-005, RQ-008
- Referenced plan sections: T-008; boundaries BD-010, BD-011, BD-013, BD-014, BD-015, BD-017
- Owned boundary: BD-010 tutorial-reward rule seam and BD-011 channel-content rule seam; the corresponding portions of BD-013, BD-014, BD-015 and BD-017.
- Permitted files or interfaces: the two new rule services and their interfaces; `ITutorialRewardDefinitionSource`, `IChannelCatalog` and `IChannelScriptSource` consumers; `TaggedIdReportHandler` and the domain service owning the tutorial grant; `ChannelEntryPolicy`; `GetChannelInfoCommand`, `GetChannelScriptCommand`, `GetChannelScriptVersionCommand`; `ApplicationModule.cs`; affected test construction sites.
- Dependencies: TASK-07
- Required dependency outputs: a tree green at TASK-07's accepted state.
- Acceptance conditions: the only consumers of the tutorial, catalogue and script sources in `Server/src` are the two new rule services, the channel entry policy reaching the channel rule service, and the infrastructure implementations; no protocol handler injects any of them; the suite is green at the baseline count.
- Task-specific authorization: the root task grant. Implementation only.
- Required methods: `test-driven-development` in its behaviour-preserving form.
- Required checks: CK-016 `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set; CK-017 non-command enumeration of the tutorial, catalogue and script sources' consumers.
- Required evidence: suite result against 1690/2/0, the CK-017 enumeration, and the owned diff.

## Execution Result — round 1

### What was built

- `ITutorialRewardRuleService` / `TutorialRewardRuleService`, owning `ITutorialRewardDefinitionSource`.
- `IChannelContentRuleService` / `ChannelContentRuleService`, owning `IChannelCatalog` **and**
  `IChannelScriptSource`. Three answers: `ChannelsAsync`, `ScriptVersion`, `ReadScriptAsync`.

The two channel declarations were joined because they are two views of one subject: the catalogue
says which channels exist, the script is the same information in the form the client downloads. A
catalogue and a script that disagreed would send a client to a channel it had not been told about.

### Consumers redirected

| Consumer | Layer | Source it held |
|---|---|---|
| `TaggedIdReportHandler` | protocol handler | `ITutorialRewardDefinitionSource` |
| `ChannelEntryPolicy` | rule service | `IChannelCatalog` — the already-compliant reader, now behind the single owner |
| `GetChannelInfoCommandHandler` | command handler | `IChannelCatalog` |
| `GetChannelScriptCommandHandler` | command handler | `IChannelScriptSource` |
| `GetChannelScriptVersionCommandHandler` | command handler | `IChannelScriptSource` |

The three channel commands are named in `AGENTS.md`'s own honest-deviation list —
"ScriptVersion、Script、Info 命令仍负责读取基础设施内容源". That entry is now stale; TASK-10 owns
updating the record.

### Checks

| Check | Command or method | Result |
|---|---|---|
| build | `dotnet build Server/86JP.Server.slnx --nologo` | 0 errors, 0 warnings |
| CK-016 | `dotnet test Server/86JP.Server.slnx --nologo` with `PVF_ARCHIVE_PATH` set | 1690 passed, 2 skipped, 0 failed |
| CK-017 | `grep -rn` over the tutorial, catalogue and script sources in `Server/src` | Only the two new rule services, plus their declarations and the `InfrastructureModule` implementations |

### The suite caught a real startup break, and it was the predicted one

Seven `Dfo.Server.Networking.Tests` integration tests failed with
`SocketException: connection refused` — the listener never opened. Reproduced in isolation, so not
the load-dependent false red `AGENTS.md` documents.

Cause: four of those compositions assemble the server *without* `InfrastructureModule`, registering
a `StaticChannelCatalog` of their own and nothing for the script. `ChannelEntryPolicy` resolves at
startup for endpoint resolution, it now reaches the channel rule service, and that rule service
needs both declarations. A graph missing one of them fails to build, and a host that cannot build
does not listen — which is why this surfaced as a refused socket rather than as a failed command.

This is exactly the failure TASK-03 recorded and told the next task joining two sources to expect.
Fixed by registering a `NoChannelScript` stub in those four compositions, beside the catalogue they
already register by hand. The stub throws rather than answering, because no test on those paths
downloads a script. Production is unaffected: it always has `InfrastructureModule`.

The alternative — splitting the channel rule service in two so the catalogue stays independent —
was considered and rejected. Unlike TASK-06's withdrawn experiment, the coupling here is *inside*
one subject rather than across two, and the cost falls on deliberately partial test graphs rather
than on the production wiring.

## Executor Self-check — round 1

- Acceptance "the three sources consumed only by the two rule services": met, CK-017.
- Acceptance "no protocol handler injects any of them": met. `TaggedIdReportHandler` now holds
  `ITutorialRewardRuleService`; TASK-09 owns that half.
- Acceptance "suite green at the baseline count": met, 1690/2/0, after the fix above.
- Behaviour preservation: all five redirects are one call for one call.
  `TaggedIdReportHandler`'s log messages, including the declared count in the no-session warning,
  are byte-for-byte what they were — the count still comes from the same list read at the same point.
- Not done: the tutorial flag lookup was **not** moved into `GrantTutorialRewardCommand`. It would
  have been the tidier shape, but the handler reports the declared count in a warning on the path
  where no character is selected and therefore no command can be sent, so moving the lookup would
  have cost that log its number. Left for TASK-09, which owns the handler shape and can settle both
  together.

## Quick Review — round 1

- Verdict: `passed`
- Reviewer context: inline, for the reason recorded in TASK-01's review round.

### Findings

1. **The socket failure is the finding.** It was the first time in this task that the suite caught
   something a build could not, and it caught a whole server that would not start. Worth noting that
   the symptom named neither the missing registration nor the rule service — only isolation and
   reading the compositions did.
2. **The `NoChannelScript` stub throws.** Deliberate: a stub returning empty bytes and a made-up
   version would let a future change start serving an empty script to a client on these paths and
   still pass.
3. **`ChannelEntryPolicy` consulting another rule service** is the same intended shape as
   `WorldContentEntryRuleService` in TASK-03, and it is what leaves one owner of the catalogue.
4. **Scope check.** No file outside the permitted list changed, except the four Networking test
   compositions and their shared stub file, which the break required and which are test wiring.
