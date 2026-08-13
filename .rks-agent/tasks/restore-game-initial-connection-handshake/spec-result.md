---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: Restore the Game initial connection handshake

## Delivered

- Newly accepted Game connections now receive exactly one server-first `cmd=0x00/type=0x0001` compatibility notice before the client sends LOGIN.
- The listener's exact configured channel, one preparation timestamp, and configured advertised IPv4 address flow through a typed message; Networking alone constructs the 15-byte frame and legacy body.
- Invalid listener/channel/address or send failure aborts the handshake and removes the exact newly registered online identity; Channel endpoints remain unaffected.
- Protocol documentation records the precise code-reversed layout and the remaining real-client capture boundary.

## Success Evidence

| Condition | Evidence |
|---|---|
| Server-first transport and existing LOGIN | Exact live TCP integration reads the notice before writing client bytes, then completes the existing LOGIN exchange. |
| Exact compatibility bytes and channel mapping | Independent byte vector plus distinct-channel and negative behavior tests. |
| Lifecycle and architecture safety | Registration/send/failure tests, Game-only scoping, complete-diff review, and changed-layer dependency scans. |
| Regression safety | Fresh full solution: 496 passed, 2 intentional skips; Host build: 0 warnings/errors; diff check clean. |
| Independent gate | `reviews/change-review.md` has `verdict: approved`. |

## Repository Result

- Changed paths: typed Data message, Application connection observer, Networking codec/sender, affected tests, protocol documentation, and this task's SDD artifacts.
- Baseline or accepted result: baseline `1452557be5292bb5331f27aa1123100d402350a2`; accepted result will be the single local task commit sealing this file.
- Parent rollback required: no.

## Artifacts

- `.rks-agent/tasks/restore-game-initial-connection-handshake/TaskContract.md`
- `.rks-agent/tasks/restore-game-initial-connection-handshake/spec.md`
- `.rks-agent/tasks/restore-game-initial-connection-handshake/reviews/change-review.md`
- `.rks-agent/tasks/restore-game-initial-connection-handshake/verification/completion-evidence.md`
- `Docs/NetworkingFrameProtocol.md`
