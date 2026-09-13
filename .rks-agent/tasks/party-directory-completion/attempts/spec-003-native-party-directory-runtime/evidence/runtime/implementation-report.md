# Implementation-stage report

## Changed

- Built a task-owned transparent proxy that persists only the party-directory Game allowlist and never Channel traffic.
- Built read-only ID `0x11` and typed CNUser/CParty probes, including reproducible add/update/delete snapshots and CParty `titleIndex` readback.
- Executed the two-client native add/update/delete scenario and recorded exact-session wire, Host, client-object and UI evidence.
- Added protocol fixtures and a durable trace for inline-title `0x000C`, same-area observer `0x0009`, and normal-exit `0x0006`.
- Corrected the runtime stop helper's UTC receipt comparison after its identity gate safely refused a false mismatch.

## Why

The static ID `0x11` final vptr did not appear as a live heap instance even though the screenshot exactly identified the native main directory.
The Spec permits an equivalent client container; the implementation therefore retained the failed vptr evidence and used current CNUser/CParty
association plus the native UI, rather than manufacturing a `this+0x188` success.

## Checks

- Python helper suite: 14 tests passed. The title-index extension had a faithful RED (`PartyObjectState` lacked
  `title_index`) followed by GREEN for the focused test and the complete helper suite.
- PowerShell receipt UTC regression: passed with exact tick equality.
- Six persisted protocol sample bodies match the selected raw frame bodies byte-for-byte and have the expected lengths.
- Exact-session: remote `0x0002` preceded party `0x0009` by 7 ms; add/update/clear and Host acceptance align.
- Screenshot-time readback: only B `partyId=2`, one member/leader user `1`; user `2` association null and party `1` absent.
- Review-remediation readback: target `partyId=3` stayed at CParty `0x563BB820` while `titleIndex` changed `0 -> 1`;
  after clear, the remote CNUser association was null and that CParty was absent while B remained.

## Remaining

Independent re-review and fresh completion verification remain; no product-behavior or runtime-evidence blocker remains.
