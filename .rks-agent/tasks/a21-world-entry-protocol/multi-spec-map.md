---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: active
phase: child_execution
phase_state: pending
active_attempt: spec-003
last_transition: child-created
transition_evidence: >-
  基线 f2064f8 上重估：SC-1/SC-2 satisfied，SC-4/5/6/7 in_progress，SC-3 not_started，
  约束全部 aligned → gaps-remain。successor_decision：最后一个缺口已由实机取证定死为一个
  有界成果——用服务端自己的 CREATE_CHARACTER 路径建出角色后（此前手工种的角色因缺五张
  入场必需表而被判 incomplete required profile state，是数据问题不是协议问题），客户端
  选人被接受、整批入场通知发出、并回了与参考一致的三条命令，随后发一条 0x02B3 便断开
  ——与 spec-001 里 CHANNELINFO 发错时同一形状。逐条对照已归档为 [K] 节：22 条协议完全一致，
  差异集中在三个 USERINFO 子类型（本仓 134/153/不发，参考 188/519/25）与
  ENTER_GAMEWORLD_COMPLETE（本仓 0、参考 8），另有 9 条缺发与 6 条多发。
  这些共同决定「客户端能否留在游戏世界里」，可一并验收、一并回滚 → safe-successor。
workspace:
  repository_root: D:/project/DNF/DNF-JP-86-Server
  task_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol/.rks-agent/tasks/a21-world-entry-protocol
  code_root: D:/project/DNF/DNF-JP-86-Server/.worktrees/a21-world-entry-protocol
  branch: a21-world-entry-protocol
  baseline: f2064f8
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
| SC-2 | 登录成功进入选人界面，花名册按账号实际角色渲染 | satisfied | spec-002：LOGIN 应答对齐实测 54 字节；花名册 body 与已知良好会话逐字节相同；实机三个角色全部正确渲染（名字、职业、等级皆对） | 无。**注**：实机用的是手工种入的探针角色，建号路径不写外观 blob，因此本仓自产角色外观数恒为 0——该形状亦已实机验证 | aligned |
| SC-3 | 选中角色后进入游戏世界且不崩溃 | not_started | none | 入场序列必发包集合、物品条目布局、入场状态各 subtype | aligned |
| SC-4 | 每处改动的线级布局以实测字节为依据 | in_progress | spec-001 与 spec-002 的每条具名取值均指向归档件的具体小节（`[A]`–`[J]` 十节）；三轮评审逐条回到原始字节复核，其中「外观 23 / 尾部 36」由一次可判别的实机对照确定 | 入场序列各段同样要归档后再引用。**客户端侧 ReadTrace 目前不可用** | aligned |
| SC-5 | protocol/ 记录与 catalog、帧基线文档、AGENTS.md 已反映 A21 | in_progress | 帧层、NOTI 0x0001、CMD 0x04DD、CMD 0x0001 登录应答、NOTI 0x0002 花名册五处已落地，含被推翻旧结论的原因与 A21 客户端哈希 | 入场序列（USERINFO 子类型 0x00 / 0x01 / 0x06 等）各记录待更新 | aligned |
| SC-6 | 构建零错误零警告，全量测试通过 | in_progress | 基线 f2064f8：构建 0 警告 0 错误；全量 `-m:1` 1209 通过 / 2 跳过 / 0 失败 | 需在最终基线上重取 | aligned |
| SC-7 | 服务架构未被改动 | in_progress | spec-001 与 spec-002 均经独立评审逐项核对：无 .csproj 改动、无 ProtocolModule 接线改动、无 handler 行为或事务语义改动；spec-002 跨了 Persistence/Abstractions/Data/Application/Protocol 五层但都在既有形状内 | 入场序列成果需同样核对 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-game-entry-frame-and-channelinfo/TaskContract.md` | A21 客户端连上游戏端点后不再断开并发出 CMD 0x0001 LOGIN：入站帧头 13→14、NOTI 0x0001 CHANNELINFO 的 A21 body、接纳 CMD 0x04DD，三处结论同步落进 protocol/ | SC-1, SC-2, SC-4, SC-5, SC-6, SC-7 | `sealed` | `completed`（见 `attempts/spec-001-…/spec-result.md`） | 已接受结果提交 `9bc75fa`，基线由 0d37e9a 推进至此 |
| 2 | `spec-002` | `attempts/spec-002-login-response-and-roster/TaskContract.md` | A21 客户端在选人界面渲染出账号已有的角色：LOGIN 应答对齐实测 54 字节、花名册逐角色条目补 `0B 01` 与 23 字节外观条目、尾部 32→36 字节，两处结论同步落进 protocol/ | SC-2, SC-4, SC-5, SC-6, SC-7 | `sealed` | `completed`（见 `attempts/spec-002-…/spec-result.md`） | 已接受结果提交 `f2064f8`，基线由 9bc75fa 推进至此 |
| 3 | `spec-003` | `attempts/spec-003-world-entry-sequence/TaskContract.md` | A21 客户端选中角色后留在游戏世界里：USERINFO 入场三子类型（0x00/0x01/0x06）与 ENTER_GAMEWORLD_COMPLETE 的布局，以及入场批次里 9 条缺发与 6 条多发的处置，结论同步落进 protocol/ | SC-3, SC-4, SC-5 | `running` | pending | `active` |
