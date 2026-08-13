---
kind: rks-review
verdict: approved
---

# Complete-change review

## Reviewed boundary

- Scope: complete current worktree change against baseline `1452557be5292bb5331f27aa1123100d402350a2`.
- Contract/specification: `TaskContract.md` revision 1 and `spec.md`, both `state: ready`.
- Change package: tracked diff plus the untracked codec, test channel catalog, and SDD artifacts reported by `git status --short`.
- Legacy compatibility source: `codex/legacy-main-baseline` implementations of `LoginPacketBuilder.BuildInitialLoginNotice` and `GamePacketEnvelopeBuilder.Build`.

## Verdict rationale

The complete change implements the bounded server-first Game handshake without adding post-LOGIN behavior. `OnlineUserConnectionObserver.ConnectedAsync` is Game-scoped, registers the online identity first, resolves the accepted listener through `IChannelEntryPolicy`, rejects channel identifiers outside `1..255`, sends one typed `GameInitialConnectionNotice`, and removes the exact registered generation on preparation or send failure (`Server/src/Dfo.Server.Application/OnlineUsers/OnlineUserConnectionObserver.cs:21-53`). Channel lifecycle returns before registration or send.

Networking alone converts that typed notice to bytes (`Server/src/Dfo.Server.Networking/Runtime/TouchSocketNetworkMessageSender.cs:36-65`). The new codec reproduces the legacy body field order and constants and delegates the accepted 15-byte envelope to the existing frame encoder (`Server/src/Dfo.Server.Networking/Framing/GameInitialConnectionPayloadCodec.cs:18-69`). Direct comparison with the legacy builder confirms success byte, `ch.<channelId>` DString, zero fields, server/channel bytes, one supplied timestamp, configured address DString, UDP ports `12311/12312`, trailing bytes, and counts `1086/1036`; the legacy envelope also confirms command/type/length plus eight zero header bytes.

The live integration oracle connects to Game and reads the exact first frame before writing any client bytes, then writes LOGIN and requires the next frame to be the pre-existing LOGIN response (`Server/tests/Dfo.Server.Networking.Tests/Integration/GameLoginIntegrationTests.cs:60-81,138-174`). Consequently an omitted notice, a notice after LOGIN, or a duplicate notice fails the scenario. The expected body is built independently of the production notice codec. Application tests independently prove registration-before-send, Channel exclusion, distinct resolved channel `100`, failed-send cleanup, and unresolved-listener cleanup with no message (`Server/tests/Dfo.Server.Application.Tests/OnlineUsers/OnlineUserServiceTests.cs:26-115`). Codec negatives reject an unrepresentable channel and non-IPv4 advertised addresses.

The upstream change is typed-only: Data contains the neutral notice record and enum identity, Application contains coordination but no frame/header/body construction, and Networking owns framing. The protocol document records server-first ordering, the complete compatibility layout, failure behavior, and the code-reversed/not-capture-confirmed confidence boundary (`Docs/NetworkingFrameProtocol.md:137-174`). No unrelated packet-diagnostics change is present in this worktree diff.

## Fresh evidence

- `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~UserServiceTests|FullyQualifiedName~SelectCharacterLifecycleTests'` — passed 31/31.
- `dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore` — passed 67/67.
- `PVF_ARCHIVE_PATH=/Users/puxianguo/Downloads/Script.pvf dotnet test Server/86JP.Server.slnx --no-restore` — passed: Pvf 60/60, Networking 67/67, Application 250/250, Infrastructure 119 passed with 2 intentional missing-archive skips.
- `git diff --check` — clean.
- Changed-layer scan plus complete-diff inspection — no Game frame construction or Networking implementation dependency was introduced in Application or Data.

## Non-blocking observations

- The notice constants remain compatibility values inferred from the accepted legacy code. The maintained capture TODO correctly leaves their business semantics open for real-client confirmation.
- Invalid-address cleanup is covered compositionally by codec rejection plus the observer's any-send-failure exact-generation cleanup; the live transport test covers the successful socket path.
