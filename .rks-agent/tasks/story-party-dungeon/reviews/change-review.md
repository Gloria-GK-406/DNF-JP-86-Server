---
kind: rks-review
scope: complete-code
baseline: 60ffd784654109665528d2f513bbd181d74cafe3
target: working-tree
verdict: changes_required
---

# Change Review Round 1

独立审查未批准当前结果。阻塞项：

1. MonsterDeath 的逐成员持久化在后续成员失败时提交共享死亡，导致失败成员收益和共享掉落永久丢失；需要 `(RunId, Actor, Participant)` 完成状态与一次性固定掉落，使重试只补未完成成员。
2. `EnterPartyAsync` 在首房间投影完成前发布共享局，投影异常不会被 handler 当前回滚边界覆盖；需要服务自身撤销或延迟发布。
3. SetPlayResult 在全部持久化完成前即把参与者标为 settled，异常后无法重试；需要 claim/commit/release 或等价幂等边界。
4. 队伍结算卡片仍使用默认 `PartyMemberCount=1`。
5. 共享局移动只验证目标存在，未验证与当前房间相邻。
6. MoveMap 在连接 generation 复核前提交共享移动；入场复核后到发送前也存在 replacement 竞态。
7. 断线清理未结束 dungeon participant；ReturnToSelection 未移除 Protocol 投递注册项，最后成员后可能残留共享局或注册项。

非阻塞风险：队员首发协议顺序仍待双端实机确认；多连接网络发送本身不可事务化。

已有证据：审查时 Release build 0 警告/0 错误；完整 TRX 3218 Passed、2 NotExecuted、0 Failed，但最终目标在此后已变化，修复后必须重新验证。

---
kind: rks-review
scope: complete-code
baseline: 60ffd784654109665528d2f513bbd181d74cafe3
target: working-tree
verdict: approved
---

# Change Review Round 2

独立复审批准。Round 1 的七项阻塞均已关闭，最终修复未引入新的阻塞回归：

1. 队伍入场失败或连接代际变化会整体回滚新 RunId，并清理被替换旧局的投递注册。
2. 移动绑定 exact RunId/RoomVersion、相邻房间与 pending death 屏障。
3. 死亡事实只在 durable participant effects 完成后提交，并使用该次提交的不可变快照判定清图。
4. 逐参与者收益可选择性重试，已成功结果在投影/通知完成前保留。
5. 怪物掉落绑定共享局、房间版本和 actor；空掉落也只固定一次。
6. 结算、奖励卡固定及领取均绑定 exact RunId，队伍人数取冻结参与者数。
7. 地面拾取和翻牌采用 reserve/commit/release；拾取持久化成功后，即使拾取者离开共享局仍可提交共享 taken 状态。

复审证据：Release build 0 警告、0 错误；相关 Application 57/57、共享副本领域 29/29、相关 Protocol 164/164；`git diff --check` 通过。复审后的最终串行全套结果另见 completion evidence。

剩余非阻塞风险：A21 双客户端的首发入场、换图、击杀、拾取和结算投递顺序仍需实机确认；在实机闭环前不得标记为 `ClientVerified`。

---
kind: rks-review
scope: live-test-remediation
baseline: 5b7bd9e8d542838c0e25dba5d0a4c7e9b9494950
target: working-tree
verdict: approved
---

# Change Review Round 3

首次双客户端实机在地下城选择区提示与队友连接失败。服务端日志证明组队建立成功且两端均上报 UDP 端点；防火墙规则与本机 UDP 监听正常。根因定位为服务端把客户端报告的虚拟网卡地址直接投影到 `0x000B`。

修复在 Protocol 边界从已认证 Game TCP 连接提取 observed IPv4：只保留客户端报告的 UDP 端口，两个地址均改为 observed address，并固定 `NatType=0`、`MTU=1500`。该行为与上游 `E:\Project\86JP` 提交 `e95cb259` 的已验证 direct path 一致；不移植其中明确标注待真机验证、默认关闭的 UDP relay。

独立审查首轮发现 NatType/MTU 未同步标准化并要求修改；修正后复审批准。最终定向结果：Protocol Party 89/89、Application Parties/OnlineUsers 50/50；完整 Debug TRX 为 3247 Passed、0 Failed、2 NotExecuted；`git diff --check` 通过。

剩余非阻塞风险：当前 wire 仅支持 IPv4，若将来监听器启用原生 IPv6 双栈，需要另行定义 IPv4 映射或拒绝策略。当前监听器为 IPv4，该分支不可达。修复仍需第二次 A21 双客户端实机确认。
