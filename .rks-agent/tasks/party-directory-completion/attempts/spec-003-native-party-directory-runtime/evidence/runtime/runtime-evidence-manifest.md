# Runtime evidence manifest

## Identity

- Runtime root: `C:\Users\Gloria\AppData\Local\Temp\dnf-party-directory-2a293054a08e431ebcc4380a99a57431`
- Repository HEAD at start: `365baed09ee5a19c08550a4f5dbdaecdafca7c8f`
- DNF.exe SHA-256: `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`
- Host SHA-256: `14582C0D5EDCBD48A91FA513152890E20E4629607A7B547B189AF7467C8D365B`
- PVF SHA-256: `19F5FA85FF73CCE94E299E0902269DE2F0164934128F15B8A7B99249C881BE1A`
- Main database SHA-256 before and after: `E06DFFBA863FB5FFFF89DB62D73584F7F4DB3A34F6C22387E39C07A5CF2F5BA7`
- Channel traffic persisted: `false`

## External raw evidence

| File | Bytes | SHA-256 |
|---|---:|---|
| `runtime-manifest.json` | 2210 | `EE7AA4D304D92F97E5A02C968E1CD42FD7B5A9B3F7BE72094F9927699B364C89` |
| `directory-targeted-frames.jsonl` | 27482 | `82A15A331324FD1EAC506EE0311C1CD1B12046529278516122DE03DB96ED471F` |
| `host.stdout.log` | 24087 | `9E5EB48AE5028C8E8C6F4C9AED51FEDD2593B069EE769162364047AF40F0AB95` |
| `directory-state-enhanced.jsonl` | 1198 | `4FBFD924369339BAF8B5D17A1E5ACFEA7CA01AA4999B52FDB7F4A97F3421CF8B` |
| `vptr-window-open.json` | 11287456 | `EBDBF9761F2DBD084CA832DAF247A87D137767EB1C6E0060A8EC73F52709C1F6` |
| `vptr-window-closed.json` | 11307524 | `B09C87C02F659035EA265A774E738905F88B2EF2BDBF3558BA703935BECFCBDD` |
| `vptr-open-closed-diff.json` | 135277 | `E20F68CED1504FC1DF43FA7CB070D4794AB0C9E253F8FB45755495E4B0B31165` |
| `stop-result.json` | 357 | `5636C9C35F5E441ACD3F75C4A4BCEEC5278BCD9B4E86535C3CF4C45915B6722A` |

Raw Host/frame logs stay outside the repository because they are execution evidence, not protocol fixtures. Repository samples contain only
the exact allowlisted bodies cited by protocol pages.

## Repository evidence

- `exact-session-timeline.md`: sanitized wire/Host order.
- `ui-observation.md`: user action sequence, screenshot digest and evidence boundary.
- `client-party-association-after-delete.json`: post-delete pro2 CNUser/CParty state.
- `client-party-association-ui-screenshot.json`: screenshot-time pro2 CNUser/CParty state.
- `party_association_snapshot.py`: reproducible read-only typed-object snapshot.
- `directory_state_probe.py`: ID `0x11` vector attempt plus typed CNUser/CParty readers.
- `directory_capture_proxy.py`: Channel-nonpersisting allowlist proxy.
- `test_*.py`, `test_runtime_receipt_identity.ps1`: helper regression checks.

## Independent-review remediation runtime

- Runtime root: `C:\Users\Gloria\AppData\Local\Temp\dnf-party-directory-reviewfix-20260913-1930`
- Exact clients: pro1 PID `39576`, pro2 PID `62412`; both normally exited before runtime stop.
- Receipt-owned processes: probe `40440`, proxy `48740`, Host `41996`; all three stopped only after PID/path/UTC identity validation.
- All task ports `7001/10010/10011/17001/18080/20010/20011` were released after stop.
- Main database SHA-256 after the run remained `E06DFFBA863FB5FFFF89DB62D73584F7F4DB3A34F6C22387E39C07A5CF2F5BA7`.

| File | Bytes | SHA-256 |
|---|---:|---|
| `runtime-manifest.json` | 2192 | `4CD4092B81CE4AA65390E525A1CEEB19C26DEB64DC3131FDB095D820CDB75258` |
| `directory-targeted-frames.jsonl` | 30774 | `4B641862D68444CCE475DCD035C4060C3534B7017DB1BDE117B0BEE5B0ACDAF6` |
| `host.stdout.log` | 30687 | `867E651D798CCC3BA6B545A512042B4E825E8EE9C278EE99B96BE40FF018961B` |
| `directory-state.jsonl` | 2134 | `A77A8F0617653859576501AF67A9050EAC19B610281C24774F6FC9D3D638C301` |
| `stop-result.json` | 357 | `BEDB6CA164B346E244934FA69BA4C1D46857A7501776BF67A0343F92EA2890E5` |

Repository snapshots `client-party-association-reviewfix2-add.json`, `-update.json`, `-delete.json` are the three
review-remediation checkpoints. They share exact pro2 PID `62412` and prove target `partyId=3` as
`titleIndex=0 -> titleIndex=1 -> object absent` without write/inject access.

| Repository snapshot | Bytes | SHA-256 |
|---|---:|---|
| `client-party-association-reviewfix2-add.json` | 1030 | `A85564BB1D623D3A00006771C116768F19383804B63DD4EB21DCFC79C11ADF92` |
| `client-party-association-reviewfix2-update.json` | 1030 | `78259BAEDB0B958B1FE0D5D1F45ABD3AB0D6E680F824B75BFBA0FE99016DFAAF` |
| `client-party-association-reviewfix2-delete.json` | 841 | `324553C49641177320E1FBBBE3DAA68390824B5703B8673E66B5A00532866702` |

## UI attachment

- Conversation attachment: `codex-clipboard-89c626f9-a7fb-4fca-8ef3-9da5118c2bfa.png`
- Size: `947 x 706`
- SHA-256: `09A6B0EFB0F571BED2FB944FEAB9838EC8A02E8AB5A02538861028AF8403C1B4`
- The repository stores the digest and observation summary, not a duplicate of the user's temporary image.
