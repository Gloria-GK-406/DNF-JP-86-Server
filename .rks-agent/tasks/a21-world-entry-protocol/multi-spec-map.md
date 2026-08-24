---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-002
last_transition: child-created
transition_evidence: >-
  基线 9bc75fa 上重估：SC-1 satisfied，SC-2/4/5/6/7 in_progress，SC-3 not_started，
  七条约束全部 aligned → gaps-remain。successor_decision：下一个缺口已由实测定死为一个
  有界成果——客户端已能到达「选择角色」界面，但账号里没有角色可选；而我们发的两样东西
  与 A21 已知良好会话不一致：LOGIN 应答少一个字节且版本字节写 18 而非 20（客户端当前接受，
  但"没崩不等于对"）；花名册的**头部 18 字节已与参考逐字节一致**，逐角色条目却在 level 之后
  写 `00 00` 而 A21 要 `0B 01`，且缺 23 字节的外观条目与 A21 的 36 字节尾部（我们只写 32 字节
  且末 11 字节内容不同）。这两样共同决定「选人界面能否正确渲染账号已有的角色」，
  可一并验收、一并回滚 → safe-successor。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol/.rks-agent/tasks/a21-world-entry-protocol
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol
  branch: a21-world-entry-protocol
  baseline: 9bc75fa
  baseline_evidence: >-
    build 0 errors 0 warnings；全量测试已跑完但调用里带了管道使聚合退出码被掩盖，
    观察到的分项为 Infrastructure 100/2skip、Networking 46、Persistence 237、
    Protocol 203、Host 3 通过 1 失败。Host.Tests 隔离复跑 4/4 通过，确认是 AGENTS.md
    记载的满载假红。完整聚合计数在第一个子成果的验证环节重新取。
---

# Multi-Spec Map

## Multi-Spec Boundary

进入游戏世界的路径本身就是一条串行的验收链，每一段都有自己独立的可观察终点、
独立的证据与独立的回滚边界：

- 帧层不对，客户端收不到任何完整包，后面所有段都无法判定；
- 帧层对了之后，频道段的终点是「客户端列出频道」；
- 频道段过了，登录与初始连接通知的终点是「客户端进到选人界面」；
- 登录段过了，花名册的终点是「选人界面按账号实际角色渲染」；
- 花名册对了，入场序列的终点是「角色站进城镇」。

每一段都能单独判定到达了下一屏，也能单独回退而不牵动其余段。协议文档随各段
同步更新，最后由根验证证明文档与实现一致。用户要求的「渐进重构」即此形状。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | A21 客户端完成频道握手并列出频道 | satisfied | spec-001 期间实机观察：A21 客户端渲染出「选择频道」界面并列出本仓的 `ch11.厄运之城 (Lv.17~26) 顺畅`，随后进入游戏端点。**频道段未作任何改动**——列表首个 u16 与脚本尾随制表符的差异实测不影响客户端 | 无 | aligned |
| SC-2 | 登录成功进入选人界面，花名册按账号实际角色渲染 | in_progress | spec-001 已让客户端发出并接受 LOGIN，其后 GET_USERINFO / SAVE_GAME_OPTION_1 / USERINFO 往来正常 | 花名册 subtype 与逐角色条目布局未验；LOGIN 应答仍是 A12 形状（`body[1]=18`、偏移 4 少一字节），客户端当前接受但未按实测字节判定 | aligned |
| SC-3 | 选中角色后进入游戏世界且不崩溃 | not_started | none | 入场序列必发包集合、物品条目布局、入场状态各 subtype | aligned |
| SC-4 | 每处改动的线级布局以实测字节为依据 | in_progress | spec-001 的每条具名取值均指向归档件 `protocol/patterns/data/traces/a21-world-entry-20260823.log` 的具体小节；两轮评审逐条回到原始字节复核 | 后继各段同样要归档后再引用。**客户端侧 ReadTrace 目前不可用**（见 spec-001 Successor Guidance 第 5 条） | aligned |
| SC-5 | protocol/ 记录与 catalog、帧基线文档、AGENTS.md 已反映 A21 | in_progress | 帧层、NOTI 0x0001、CMD 0x04DD 三处已落地，含被推翻旧结论的原因与 A21 客户端哈希 | 登录应答、花名册与入场序列各记录待更新 | aligned |
| SC-6 | 构建零错误零警告，全量测试通过 | in_progress | 基线 9bc75fa：构建 0 警告 0 错误；全量 `-m:1` 1194 通过 / 2 跳过 / 0 失败 | 需在最终基线上重取 | aligned |
| SC-7 | 服务架构未被改动 | in_progress | spec-001 经独立评审逐项核对：无 .csproj 改动、切片制品边界与既有切片同形、handler 落在允许类别、ProtocolModule 仍是唯一接线点 | 后继各段需同样核对 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-game-entry-frame-and-channelinfo/TaskContract.md` | A21 客户端连上游戏端点后不再断开并发出 CMD 0x0001 LOGIN：入站帧头 13→14、NOTI 0x0001 CHANNELINFO 的 A21 body、接纳 CMD 0x04DD，三处结论同步落进 protocol/ | SC-1, SC-2, SC-4, SC-5, SC-6, SC-7 | `sealed` | `completed`（见 `attempts/spec-001-…/spec-result.md`） | 已接受结果提交 `9bc75fa`，基线由 0d37e9a 推进至此 |
| 2 | `spec-002` | `attempts/spec-002-login-response-and-roster/TaskContract.md` | A21 客户端在选人界面渲染出账号已有的角色：LOGIN 应答对齐实测 54 字节、花名册逐角色条目补 `0B 01` 与 23 字节外观条目、尾部 32→36 字节，两处结论同步落进 protocol/ | SC-2, SC-4, SC-5 | `running` | pending | `active` |
