---
kind: rks-change-review
verdict: approved
reviewer: /root/pickup_change_review
---

# Change Review: 响应式拾取投影

## Verdict

批准。初审发现投影层只验证当前 selected-session、没有把拾取者绑定到 `change.RunId` 的冻结身份；实现补充了 Run 冻结身份、语义接收者和当前连接代次三重约束，并增加 replacement、离场与 foreign Run 回归测试。复审确认阻断项全部关闭，未发现新的阻断缺陷。

## Reviewed Boundaries

- `DungeonPickupChange` 只携带领域角色身份与语义接收者，不携带连接或 Session。
- 投影层以 `RunId` 查找冻结参与者，并要求拾取者仍在 `Recipients`、冻结身份等于请求者、当前 selected-session 等于冻结身份。
- 观察者同时受 Run、语义接收者和当前连接代次过滤。
- 0x0027 继续只承担共享地面物状态；实现没有修改 UDP/P2P，也没有伪造远端角色动作。

## Evidence

- `GetItemHandlerTests`：6/6 通过，含 replacement picker、departed picker、foreign Run。
- `GroundPickup` 定向测试：24/24 通过。
- `DungeonRunDomainServiceTests`：36/36 通过。
- `git diff --check`：通过。
