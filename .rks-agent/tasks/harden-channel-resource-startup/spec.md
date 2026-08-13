---
kind: rks-spec
contract:
  path: ./TaskContract.md
  revision: 1
state: ready
---

# Specification: Reliable packaged Channel resources at Host startup

## Why and Outcome

The server currently reports ready listeners even when its required channel script cannot be
located, so a real client reaches Channel CONNECT and then fails without a script response.
The Host must obtain configuration and required packaged resources from a stable deployment
root regardless of its launch directory, and must refuse readiness before accepting clients
when that resource contract is invalid.

## Task Perspective

- Core outcome: A normally packaged server started from any working directory can complete the
  entire Channel bootstrap exchange instead of failing on the first channel-script request.
- Necessary support: Configuration and relative resource paths share a stable root; required
  script data is validated before readiness; absolute operator overrides remain supported.
- Peripheral assurance: Effective startup settings are visible, production-shaped TCP/process
  tests protect run and publish layouts, and the runbook describes the verified behavior.

## Scope and Non-Goals

### In Scope

- Host configuration and resource-root behavior for built and published deployments.
- Required channel-script validation and serving lifecycle.
- Effective startup summary for resource/address/listener troubleshooting.
- Default and failure-path Host/TCP evidence, and operator documentation.

### Non-Goals

- Any Channel or Game frame-layout, encryption-key, compression, message-order, account,
  database, PVF-content, or client-Patch change.
- Modification or acceptance of the separate packet-diagnostics implementation.
- A claim that the code-reversed Channel protocol is packet-capture-confirmed.

## Constraints

- The default packaged script has one authoritative location under
  `Resources/Channel/channel_info.etc`; compatibility must not depend on an accidental duplicate.
- Absolute configured script paths remain valid overrides.
- An unavailable or empty required script cannot coexist with listeners advertised as ready.
- Startup information must not disclose encryption keys, account details, or packet payloads.

## Definitions

| Term | Meaning |
|---|---|
| Stable Host root | The deployment directory containing the running Host, its packaged configuration, and packaged resources; it does not vary with the process working directory. |
| Ready listener | A Channel or Game TCP endpoint the Host has successfully started and exposed to clients. |
| Channel bootstrap | The ordered client-visible exchange `0x0B→0x0C→0x09→0x0A→0x11→0x12`. |

## External Behavior and Data Rules

- Relative configuration and packaged-resource references are interpreted from the stable Host
  root; an absolute script path is used as supplied.
- The required channel script is nonempty and readable before the Host may report successful
  startup or expose ready listeners.
- Once startup succeeds, each script request is served from the validated startup snapshot, so
  later working-directory changes cannot affect it.
- The Channel frame identities, encryption, compression, and response order remain unchanged.
- Startup output identifies the effective stable root, resolved script path, advertised Channel
  and Game address, Channel listener, and configured Game listeners.

## Observable Requirements

| ID | Requirement |
|---|---|
| RQ-001 | Built and published Hosts load their packaged configuration and channel script identically when launched from their deployment directory or an unrelated working directory. |
| RQ-002 | The packaged default resolves to the single authoritative `Resources/Channel/channel_info.etc` resource without requiring a path environment override. |
| RQ-003 | Explicit absolute channel-script configuration remains supported and is not re-rooted. |
| RQ-004 | Missing, empty, or unreadable required script input prevents successful startup before any listener is advertised ready, with an actionable error identifying the effective path. |
| RQ-005 | A successfully started default Host completes the unchanged Channel bootstrap and returns a nonempty script response. |
| RQ-006 | Script responses use the data validated at startup rather than reopening an unvalidated path on demand. |
| RQ-007 | Startup output exposes the effective root, resource path, public address and listener ports without secrets or packet payloads. |
| RQ-008 | Existing Channel, Game, PVF, database and protocol behavior outside resource initialization remains compatible. |
| RQ-009 | Maintained operator documentation describes launch-directory independence, startup validation, effective configuration, and the remaining code-reversed protocol boundary. |

## Acceptance Scenarios

| ID | Covers | Given | When | Then |
|---|---|---|---|---|
| AC-001 | RQ-001, RQ-002 | A built Host output containing its default configuration and packaged resource | The Host is launched with an unrelated current working directory and no script-path override | It loads the packaged default and reaches successful startup. |
| AC-002 | RQ-001, RQ-002 | A published Host output | It is launched with its deployment root different from the current working directory | It selects the same packaged configuration and script as the built Host. |
| AC-003 | RQ-003 | A readable script outside the Host deployment | An absolute override names that script | Startup validates and serves that exact file. |
| AC-004 | RQ-004 | The effective required script is missing, empty, or inaccessible | Host startup is attempted | Startup fails with the effective path before Channel or Game listeners report ready. |
| AC-005 | RQ-005, RQ-006 | The default Host has started successfully | A TCP client performs the Channel bootstrap | Responses arrive in the declared order and the script response is nonempty even though no path override was injected. |
| AC-006 | RQ-006 | A validated Host is running | Its process working directory or underlying script availability changes after startup | Script delivery continues from the validated startup state. |
| AC-007 | RQ-007 | A Host starts with effective network and resource configuration | Startup output is observed | The root, resource, public address and listener ports are visible, while secrets and payloads are absent. |
| AC-008 | RQ-008 | Existing affected and full-solution behaviors | The completed change is verified | Existing suites and Host build pass without an unrelated protocol or domain regression. |
| AC-009 | RQ-009 | An operator follows the maintained runbook | The server is built or published and launched | The documented commands and expectations match the verified stable-root and fail-fast behavior. |

## Success Evidence

| ID | Requirements | Scenarios | Evidence class |
|---|---|---|---|
| EV-001 | RQ-001, RQ-002 | AC-001, AC-002 | Production-shaped built and published Host startup evidence from an unrelated working directory. |
| EV-002 | RQ-003, RQ-004 | AC-003, AC-004 | Focused absolute-override and missing/empty/unreadable startup failure behavior. |
| EV-003 | RQ-005, RQ-006 | AC-005, AC-006 | Live TCP Channel bootstrap evidence using the default packaged script and validated startup snapshot. |
| EV-004 | RQ-007 | AC-007 | Startup output assertion or reviewed structured summary proving useful effective values and no secret content. |
| EV-005 | RQ-008 | AC-008 | Fresh affected suites, full real-PVF solution, Host build, dependency/scope scan, and diff checks. |
| EV-006 | RQ-009 | AC-009 | Reviewed operator-documentation evidence matching the tested deployment behavior. |

## Assumptions

- The existing packaged `channel_info.etc` content is the accepted compatibility input; this
  task changes when and where it is loaded, not its protocol meaning.
