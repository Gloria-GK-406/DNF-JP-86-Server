---
kind: rks-verification
decision: pass
---

# Completion Evidence: Channel Protocol Key Boundary

## Claim

The completed change makes Channel Connect a direct protocol response, moves the shared legacy
Channel key rule into Protocol, removes the key-only command and cross-layer key abstraction, and
preserves all affected Channel bytes without modifying Game behavior.

## Condition-to-Evidence Mapping

| Condition | Fresh evidence |
|---|---|
| SC-1 | `ChannelConnectHandler` has no `ICommandBus` reference. The fixed-time handler test and real TCP exact-frame integration test passed. |
| SC-2 | Connect, ScriptVersion, Script and Info are the four production consumers of the one singleton `ChannelProtocolKeyProvider`. The fixed-time Channel control integration test passed with the existing golden ScriptVersion ciphertext and decrypted Script/Info assertions. |
| SC-3 | A source/test/guidance scan found no `IChannelEncryptionKeyProvider` or `EstablishChannelConnection*` symbol. The command registration and Application registration are absent. |
| SC-4 | Fresh solution build completed with 0 warnings and 0 errors; all 587 discovered tests completed with 522 passed, 65 existing archive-dependent skips and 0 failures; focused tests passed; diff hygiene and boundary checks passed. |

## Commands and Results

1. `dotnet build Server/86JP.Server.slnx --no-restore`
   - Exit 0.
   - 0 warnings, 0 errors.
2. `dotnet test Server/86JP.Server.slnx --no-build --no-restore`
   - Exit 0.
   - Application: 259 passed, 10 skipped.
   - Pvf: 51 passed, 9 skipped.
   - Protocol: 88 passed, 0 skipped.
   - Infrastructure: 75 passed, 46 skipped.
   - Networking: 45 passed, 0 skipped.
   - Host: 4 passed, 0 skipped.
   - Total: 522 passed, 65 existing PVF/archive-dependent skips, 0 failed.
3. `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ChannelConnectIntegrationTests|FullyQualifiedName~ChannelControlMessagesIntegrationTests"`
   - Exit 0; 2 passed, 0 skipped, 0 failed.
4. `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ChannelConnectHandlerTests|FullyQualifiedName~CommandHandlerArchitectureTests"`
   - Exit 0; 2 passed, 0 skipped, 0 failed.
5. Absence and ownership checks over `Server/src`, `Server/tests` and `AGENTS.md`:
   - No obsolete provider or connection-command symbols.
   - No `ICommandBus`/generic command send in `ChannelConnectHandler`.
   - Exactly one provider implementation; four production consumers plus one DI registration.
   - Formula remains `GetLocalNow()` plus invariant `yyyyMMdd'000006'`.
6. Boundary and hygiene checks:
   - No diff under `Server/src/Dfo.Server.Protocol/Game` or `GameLoginOptions.cs`.
   - No diff under Networking framing/runtime.
   - No project/build configuration diff or validation weakening.
   - `git diff --check` exited 0.

## Review Gate

The independent complete-change review is `approved`. Its initial documentation-consistency
finding was corrected and the updated complete diff was independently re-reviewed.

## Workspace Hygiene

The unrelated untracked `Diagnostics/` directory was not read into, edited or staged. The task was
performed directly on the user-selected current branch as explicitly requested; no worktree was
created.
