---
kind: rks-verification
decision: pass
---

# Completion verification

## Claim

The successor server now emits the code-reversed Game initial connection notice as the
first server frame on every valid Game connection, preserves the existing LOGIN flow, and
does so through a typed Application-to-Networking boundary with explicit failure cleanup.

## Condition-to-evidence mapping

| Conditions | Fresh evidence |
|---|---|
| RQ-001, RQ-004 / AC-001 | `GameLoginIntegrationTests` connects a real TCP client, observes the registered anonymous connection, reads an exact `00/0001` frame before writing any client bytes, then sends LOGIN and receives the existing LOGIN response. `UserServiceTests` proves registration precedes send and failed send removes the exact identity. |
| RQ-002, RQ-003 / AC-002, AC-003 | The integration byte vector independently writes every declared field, header, DString and constant. Application behavior verifies channel `100` is preserved from its exact listener rather than replaced by channel `11`. |
| RQ-005 / AC-004 | `ConnectedChannel_DoesNotRegisterOrSendGameNotice` proves a Channel endpoint produces neither Game notice nor Game online identity. |
| RQ-006 / AC-005 | Application behavior rejects channel ID `0`, removes the registered identity and sends nothing. Codec behavior rejects channel ID `256`, malformed address text and IPv6. |
| RQ-007 / AC-006 | Changed-layer scan found no frame encoder, request-info, binary writer, stream writer, or Networking implementation dependency added to Application or Data. Data carries `GameInitialConnectionNotice`; Networking alone encodes the frame. |
| RQ-008 / AC-007 | Fresh full solution test passed 496 tests: PVF 60, Application 250, Networking 67, Infrastructure 119; two intentional missing-archive negative tests were skipped. Host build completed with 0 warnings and 0 errors. |
| RQ-009 / AC-008 | `Docs/NetworkingFrameProtocol.md` now records server-first ordering, all 16 body fields, exact listener mapping, explicit failures, and the code-reversed/not-capture-confirmed confidence boundary. |

## Commands and results

- `PVF_ARCHIVE_PATH=/Users/puxianguo/Downloads/Script.pvf dotnet test Server/86JP.Server.slnx --no-restore` — exit 0; 496 passed, 2 intentional skips, 0 failed.
- `dotnet build Server/src/Dfo.Server.Host/Dfo.Server.Host.csproj --no-restore` — exit 0; 0 warnings, 0 errors.
- Changed-layer dependency and wire-construction scans — exit 0; no prohibited matches.
- `git diff --check` — exit 0.
- Independent complete-change review: `reviews/change-review.md` — `approved`, no blocking findings.

The remaining real-client activity is functional confirmation of code-reversed constants,
not an unimplemented server behavior: maintained documentation keeps that capture boundary
explicit.
