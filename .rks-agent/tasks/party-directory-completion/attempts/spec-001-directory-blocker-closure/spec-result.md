---
kind: rks-spec-result
state: passed
contract:
  path: ./TaskContract.md
  revision: 1
spec: ./spec.md
review: ./reviews/change-review.md
verification: ./verification/completion-evidence.md
---

# Spec Result: 固定 A21 公共目录阻塞证据闭合

## Accepted outcome

ID `0x11` 主目录行由客户端现有 `CNUserManager + PartyManager` 派生；服务端只需保证同一 town-visible
观察者先获得相关角色 `0x0002`，再获得 party `0x0009`。`0x023C` 的逐行 manager/renderer 属于 ID
`0x14` 候补窗；ID `0x11` 仅查询其当前 party 报名状态，不以 record 生成主目录行。

生产实现可开始，且不得新增 `0x023C` 目录 codec、目录仓储或 per-channel directory gate。后继结果只扩展
现有 `0x0009` observer 投影及 entry/move/mutation/cleanup 生命周期。原生 UI 仍需实现后的两客户端验证。

## Review and verification

- Independent review Round 3: `approved`; all earlier P1/P2 closed.
- Fresh verification: 7/7 input hashes PASS, baseline identity PASS, stale-assertion search PASS,
  `git diff --check` PASS.

