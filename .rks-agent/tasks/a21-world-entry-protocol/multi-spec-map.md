---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-001
last_transition: child-created
transition_evidence: >-
  root_evaluation：七条成功条件全部 not_started，无已接受证据，约束全部 aligned →
  gaps-remain。successor_decision：实机取证已把最靠前的缺口定死为一个有界成果——
  客户端在频道段已经能走通（实测：A21 客户端渲染出「选择频道」并列出我们的 ch11），
  按下游戏开始后连上游戏口，随即因两件事走不下去：入站 Game 帧头我们按 13 字节切而
  客户端发的是 14 字节（首条消息 CMD 0x04DD，被读成 body(1B)=00 且无处理器而丢弃），
  以及 NOTI 0x0001 CHANNELINFO 的 body 我们发 60 字节、A21 需要 68 字节。
  这两件事共同决定「客户端能否走到发出 CMD LOGIN」，可一并验收、一并回滚，
  且不依赖任何尚未存在的成果 → safe-successor。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol/.rks-agent/tasks/a21-world-entry-protocol
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol
  branch: a21-world-entry-protocol
  baseline: 0d37e9a6116d17517d43640fbb44d5d61095dcd1
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
| SC-1 | A21 客户端完成频道握手并列出频道 | not_started | none | Game 帧入站头 13→14；频道列表 0x12 首个 u16；SC_GET_SCRIPT 投影格式 | aligned |
| SC-2 | 登录成功进入选人界面，花名册按账号实际角色渲染 | not_started | none | CMD LOGIN 响应偏移 4 缺字节且版本 18→20；NOTI 0x0001 相差 8 字节；花名册 subtype 与逐角色条目布局 | aligned |
| SC-3 | 选中角色后进入游戏世界且不崩溃 | not_started | none | 入场序列必发包集合、物品条目布局、入场状态各 subtype | aligned |
| SC-4 | 每处改动的线级布局以实测字节为依据 | not_started | none | 各段结论需分别指向抓包帧、ReadTrace 记录或客户端读序 | aligned |
| SC-5 | protocol/ 记录与 catalog、帧基线文档、AGENTS.md 已反映 A21 | not_started | none | 全部待更新；被推翻的旧结论需保留原因 | aligned |
| SC-6 | 构建零错误零警告，全量测试通过 | not_started | none | 待最终基线取证 | aligned |
| SC-7 | 服务架构未被改动 | not_started | none | 待最终比对 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-game-entry-frame-and-channelinfo/TaskContract.md` | A21 客户端连上游戏端点后不再断开并发出 CMD 0x0001 LOGIN：入站帧头 13→14、NOTI 0x0001 CHANNELINFO 的 A21 body、接纳 CMD 0x04DD，三处结论同步落进 protocol/ | SC-1, SC-4, SC-5 | `running` | pending | `active` |
