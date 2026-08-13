# `<TYPE>` `<NAME>`

## Identity and behavior

| Property | Value |
|---|---|
| Endpoint | `<Game/Channel/...>` |
| Direction | `<Client → Server/Server → Client>` |
| Command | `<hex>` |
| Type | `<hex>` |
| Role | `<Report/Query/Mutation/Session side effect/Notification>` |
| Trigger | `<observable client or server action>` |
| Expected response | `<none or exact response identity/order>` |
| Catalog status | `<Observed/Reversing/LayoutKnown/Implemented/ClientVerified>` |

## Wire layout

Frame structure should link to a shared pattern document. Describe the body here.

| Offset | Size | Encoding | Meaning | Confidence | Evidence |
|---:|---:|---|---|---|---|
|  |  |  |  |  |  |

State exact length rules, optional sections, counts, branches and malformed-input behavior.

## Samples

Link committed, sanitized samples and state the client action that produced each one. Record repeated count or differing environment when relevant.

## Client reversing evidence

| Evidence | Kind | Location | Supports |
|---|---|---|---|
| `E-001` | `<static/dynamic>` | `<DNF SHA-256 + RVA/call stack>` |  |

Include concise pseudocode or instructions sufficient for another researcher to reproduce the conclusion. Distinguish observed instructions from inferred names.

## Server and historical evidence

Record old-server handlers, current implementation paths, persistence behavior and whether absence of a handler was verified across history.

## Implementation mapping

| Layer | Type/path | Responsibility |
|---|---|---|
| Data |  |  |
| Networking |  |  |
| Application |  |  |
| Domain/Infrastructure |  |  |

## Verification

- Parser/codec vectors:
- Boundary cases:
- Response count/order:
- Real-client result:

## Unknowns and next evidence

List only unresolved questions that can change parsing, behavior, domain semantics or client compatibility. State the cheapest evidence that resolves each one.
