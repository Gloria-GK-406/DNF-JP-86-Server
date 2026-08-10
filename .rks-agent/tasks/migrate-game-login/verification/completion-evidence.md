---
kind: rks-verification
decision: pass
---

# Completion Verification: Game login migration

## Claim

The staged Game LOGIN mainline is complete: TouchSocket converts the LOGIN frame to a
typed request, the Application layer authenticates an indexed online-user aggregate under
a writer-preferred exclusive Command boundary, SQLite resolves or creates the account,
the exact base ACK is returned, disconnect cleanup is generation-safe, existing behavior
regresses cleanly, and the reverse-engineered protocol limits are documented.

## Condition-to-Evidence Mapping

| Condition | Fresh evidence |
|---|---|
| SC-1 | `dotnet test Server/86JP.Server.slnx --no-restore` passed `GameLoginCodecTests` and the real-TCP `GameLoginIntegrationTests`, including exact response bytes. |
| SC-2 | The same fresh run passed `GameLoginTests`, `OnlineUserServiceTests`, and `SqliteAccountRepositoryTests`, covering create/reuse, account binding, indexed query, and persistence. |
| SC-3 | The same fresh run passed `OnlineUserExecutionGateTests` and the CommandBus failure/cancellation release tests, covering overlap, draining, writer preference, cancellation/grant races, failure release, idle reclamation, and per-user isolation. |
| SC-4 | The same fresh run passed the anonymous connection, generation-safe disconnect/login race, and real TCP disconnect lifecycle scenarios. |
| SC-5 | The complete solution passed: 39 Application, 2 Infrastructure, and 29 Networking tests; 70 total, 0 failed, 0 skipped. |
| SC-6 | `Docs/NetworkingFrameProtocol.md` identifies LOGIN as code-derived, lists staged post-login exclusions, and retains explicit packet-capture TODOs. |

## Additional Checks

- `git diff --check`: exit 0, no whitespace errors.
- `dotnet list Server/86JP.Server.slnx package --vulnerable --include-transitive`:
  exit 0, no vulnerable packages reported for any solution project.
- `rg -n "ICommandBus" Server/src/Dfo.Server.Application/Commands`: only the CommandBus
  contract, implementation, and DI registration are present; Command handlers do not
  perform nested Command dispatch.
- Independent complete-change review: `reviews/change-review.md` is `verdict: approved`
  with all prior concurrency and lifecycle findings resolved.
