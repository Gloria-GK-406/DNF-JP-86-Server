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

# Spec Result: 固定 A21 原生公共队伍目录运行时闭环

## Accepted outcome

两个固定 A21 客户端在同一 `VisibleTownArea` 中，通过现有 `0x0002 -> 0x0009` 投影让非成员 observer 的
原生 ID `0x11` 主目录显示 A/B 两队。A 的预设标题更新刷新同一行，解散后的 clear 使该行在原生刷新后
消失且只剩 B。

补证会话把同一 `partyId=3` 的 CNUser/CParty 状态固定为：add 时 CParty `0x563BB820`、
`titleIndex=0`；update 后同地址 `titleIndex=1`；delete 后关联归零且该对象消失。final-vptr 未命中被如实
保留，由 Spec 明示允许的等价 CNUser/CParty 状态和原生 UI 完成客户端层证据。

## Review and verification

- Independent review Round 2：`approved`；F-001/F-002 均 resolved，无新 blocker。
- Fresh completion verification：Release build `0 warning / 0 error`；helper `14/14`；全量
  `3127 Passed + 2 NotExecuted + 0 Failed`；六份样本逐字节一致；运行时 exact identity 全部清理；
  相对接受基线无 `Server/src` 变更或新增目录 `0x023C`。
- `0x000C` 的状态精确限定为 12B direct 创建/更新与 `16+n` inline-title 创建；inline-title update/拒绝
  仍待实帧，不在本结果中冒充验证。
