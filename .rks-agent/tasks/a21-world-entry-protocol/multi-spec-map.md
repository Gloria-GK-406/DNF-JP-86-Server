---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
state: completed
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: >-
  2026-08-27，在分支头 6a494c8d 上收口。spec-003 的评审早已 approved，但验证阶段一直没跑，
  游标停在 verification/pending 且 verification/ 是空目录；其间分支又落了十六个提交，
  其中几个直接改了 spec-003 被判定的那几帧。收口的做法是**在当前头上重取证据**而不是
  复原当时的数字——见 attempts/spec-003-.../verification/completion-evidence.md。
  重取结果：入场批次 66 帧对 66 帧、协议与顺序全同、60 帧逐字节相同，余 6 帧全部算平为角色数据。
  据此 SC-3 satisfied，SC-4/SC-5/SC-6/SC-7 一并在头上重估为 satisfied，约束全部 aligned
  → root-satisfied → 根评审无阻断发现 → 根验证通过。
  收口时发现并处理了两处证据卫生问题：上游 packet_log.txt 于 08-27 被新一局抓包覆盖
  （各页引用的 08-23 那局以 packet_log.reference-20260823.txt 存续，比对脚本改为指名文件），
  以及 a21-entry-batch-diff-20260825 的「本仓」列停留在装备与技能修好之前的数字（已重取并保留旧值与变因）。
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
| SC-3 | 选中角色后进入游戏世界且不崩溃 | satisfied | 实机：玩家用真客户端走完新手副本并正常退出（2026-08-27）；`clienthack-faults.log` 五条全是 first-chance，无致命记录，会话是玩家自己结束的。线级：入场批次 66 帧对 66 帧、60 帧逐字节相同 | 无 | aligned |
| SC-4 | 每处改动的线级布局以实测字节为依据 | satisfied | 三个成果的每条具名取值都指向归档件；spec-003 的判据字节归档为 `a21-reference-entry-bytes-20260825`，重取的比对归档在 spec-003 的 `verification/` 下。**客户端侧 ReadTrace 始终不可用**，因此依据一律是抓包字节或反汇编读序 | 无 | aligned |
| SC-5 | protocol/ 记录与 catalog、帧基线文档、AGENTS.md 已反映 A21 | satisfied | 帧层、CHANNELINFO、CMD 0x04DD、LOGIN 应答、花名册、入场三子类型与 ENTER_GAMEWORLD_COMPLETE 各页均已落地并保留被推翻结论的原因；`catalog.md` 状态已更新；`AGENTS.md` 的帧与协议描述已改 | 无。**注**：分支另加了一项更严的性质——A12 内容全部退场（`check-a12-residue.py`），当前报 49 页 a12-only、37 页 mixed，**未达终态**。那不是 SC-5 的字面要求，是分支自设的后续目标 | aligned |
| SC-6 | 构建零错误零警告，全量测试通过 | satisfied | 分支头 6a494c8d：构建 0 警告 0 错误；`dotnet test 86JP.Server.slnx -m:1` 1304 通过 / 2 跳过 / 0 失败 | 无 | aligned |
| SC-7 | 服务架构未被改动 | satisfied | 三个成果各经独立评审逐项核对；分支末尾的任务日志重构另有两名评审复核分层依赖、命令总线与管线、事务参与者规则均未变 | 无 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | `spec-001` | `attempts/spec-001-game-entry-frame-and-channelinfo/TaskContract.md` | A21 客户端连上游戏端点后不再断开并发出 CMD 0x0001 LOGIN：入站帧头 13→14、NOTI 0x0001 CHANNELINFO 的 A21 body、接纳 CMD 0x04DD，三处结论同步落进 protocol/ | SC-1, SC-2, SC-4, SC-5, SC-6, SC-7 | `sealed` | `completed`（见 `attempts/spec-001-…/spec-result.md`） | 已接受结果提交 `9bc75fa`，基线由 0d37e9a 推进至此 |
| 2 | `spec-002` | `attempts/spec-002-login-response-and-roster/TaskContract.md` | A21 客户端在选人界面渲染出账号已有的角色：LOGIN 应答对齐实测 54 字节、花名册逐角色条目补 `0B 01` 与 23 字节外观条目、尾部 32→36 字节，两处结论同步落进 protocol/ | SC-2, SC-4, SC-5, SC-6, SC-7 | `sealed` | `completed`（见 `attempts/spec-002-…/spec-result.md`） | 已接受结果提交 `f2064f8`，基线由 9bc75fa 推进至此 |
| 3 | `spec-003` | `attempts/spec-003-world-entry-sequence/TaskContract.md` | A21 客户端选中角色后留在游戏世界里：USERINFO 入场三子类型（0x00/0x01/0x06）与 ENTER_GAMEWORLD_COMPLETE 的布局，以及入场批次里 9 条缺发与 6 条多发的处置，结论同步落进 protocol/ | SC-3, SC-4, SC-5 | `sealed` | `completed`（见 `attempts/spec-003-…/spec-result.md`） | 结果随分支后续提交一并在 `a21-world-entry-protocol` 上，未单独成一次接受提交——见下节 |

## Beyond this contract, and left that way

**This branch carries more than this contract asked for, and the contract was not widened to
match.** Recording it here rather than amending Scope is deliberate: the contract describes what
was agreed before the work, and rewriting it afterwards to fit what happened would destroy the one
thing it is for.

The Goal is «A21 客户端能从启动一路走到进入游戏世界（角色站进城镇）», and Scope/Out excludes
«进入游戏世界之后的玩法协议（副本、战斗、交易、公会、PVP、邮件、商城等）». The commit
`7ec88361 A21 world entry: the client reaches the game world` is where the Goal was met. Roughly
sixteen commits after it are outside that boundary:

- the tutorial dungeon, from admission through room rules, drops, levelling and clearing;
- leaving a dungeon and the exit burst, including `CMD 0x00C2` and `CMD 0x00FA`;
- the dungeon selection screen and its refusals;
- `CMD 0x001F ACCEPT_QUEST`;
- the quest log becoming its own domain, which ran as **a separate root task**
  (`.rks-agent/tasks/quest-log-domain/`, sealed `completed`) and therefore appears nowhere in this
  map's attempts.

Six further commits are protocol-library work — the A21 census, the tree rebuild, marking the A12
content for retirement — which serve SC-5 and are inside the contract.

None of this out-of-scope work was reviewed against *this* contract, and none of it is claimed as
evidence for any success condition above. It was reviewed and verified under its own terms where it
had them (the quest log domain), and by direct real-client play where it did not.

## Root Review

Read against the contract on branch head `6a494c8d`.

- **Scope**: every in-scope item is delivered. The excess is recorded above rather than absorbed.
- **Constraints**: 服务架构未改（三次独立评审）；开发阶段擦库不迁移（无回填路径）；协议结论
  以实测字节为依据，被推翻的结论保留了原因；`protocol/` 无凭据、无 LOGIN 样本；`Tool/` 未改；
  C# 与文档无 BOM，`TreatWarningsAsErrors` 仍开启。
- **One constraint's text is out of date, not violated.** It names
  `PVF_ARCHIVE_PATH → D:/project/DNF/DNFClient-debug/Script.pvf`; the服务端 serves A21 and the
  archive it must read is `D:/project/DNF/DNFClient-A21-protocol/Script.pvf`. The change was made
  on the user's explicit instruction («之前的服务端用的pvf是A12，记得更新成A21啊») and the host
  now logs which archive it opened at startup. The constraint's intent — tests read one named
  archive, not whatever is lying around — holds.
- **No unresolved blocking finding.**

## Root Verification

Fresh on `6a494c8d`, not carried forward:

- 构建 0 警告 0 错误。
- `dotnet test Server/86JP.Server.slnx -m:1` → **1304 通过 / 2 跳过 / 0 失败**。
- 入场批次对参照 08-23 那一局：66 帧对 66 帧，协议与顺序全同，60 帧逐字节相同，
  余 6 帧全部算平为角色数据（存档见 spec-003 的 `verification/`）。
- 实机：真客户端走完新手副本并正常退出。

七条成功条件全部 satisfied，七条约束全部 aligned，无 running 行，`active_attempt` 为 null。

## Root Result

`completed`。分支 `a21-world-entry-protocol`，头 `6a494c8d`，较 `main` 领先 27 个提交、落后 0。

遗留给后续任务而非本契约的：A12 内容退场未达终态（49 页 a12-only、37 页 mixed）；
`Docs/ProtocolScaffoldDissonances.md` 二十二条各有各的收口判据；`CMD 0x002D MOVE_MAP` 未做。

### 一处红着的自检，不在本契约的成功条件里，但必须写下来

`protocol/patterns/data/check-controls.py` 在分支头报 **`controls failed: 6 / 14`**，
连带 `check-regeneration-safety.py` 报 `FAILED at 'positive controls'; scaffold not regenerated`。

这是**回归**：`protocol-doc-scaffold` 任务当初验证通过时记的是 `controls failed: 0 / 14`
（见该任务 `verification/completion-evidence.md`）。红的六条是
`0x00C2`、`0x008F`、`0x0117`、`0x0008`、`0x0005`、`0x01BA`——布局提取器给出的判读与手写文档
对不上，多半是本分支按 A21 重建协议树、又改了其中几篇（`0x00C2` 就是退出冲刷那帧）之后
没有回头对齐提取器的期望值。

**影响范围**：这道闸门审的是「提取器读出来的布局与文档说的是否一致」，不是服务端行为；
`check-acceptance.py` 仍然 `failures: 0`，构建与全量测试全绿。但它连带的后果是实的——
`rebuild.py` 现在拒绝跑，也就是说协议库「随时可重建且不产生 diff」这条性质**当前拿不到**。

**不在本契约的 SC-1..SC-7 之内**，所以它不改变根任务的判定；记在这里是因为它是一条
先前被验证为绿、现在红了的闸门，下一个动协议树的人第一件事应该是把它弄回去。
