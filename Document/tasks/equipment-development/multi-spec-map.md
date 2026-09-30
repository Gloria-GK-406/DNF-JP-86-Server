---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 1
phase: completed
phase_state: completed
active_attempt: null
last_transition: verification-passed
transition_evidence: "2026-09-30 Root Verification below passed for automatic delivery against accepted product HEAD 439b6788: five unique completed child attempts, SC-1..5 satisfied with aligned constraints, no active child; fresh build and relevant full suites, with explicitly excluded known real-PVF creature failures and A21 human acceptance pending."
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap/Document/tasks/equipment-development
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/solo-play-roadmap
  branch: task/solo-play-roadmap
  baseline: 439b6788c529fdb89fbfd4a29fa1527721dcd19b
  baseline_evidence: "2026-09-30: one accepted forging commit 439b6788 over c15e6add; linked task worktree clean immediately after commit; fresh build, focused suites, no-PVF Application, Networking, Protocol and real-PVF Infrastructure passed, with two known creature failures in real-PVF Application full run."
---

# Multi-Spec Map

## Multi-Spec Boundary

强化、附魔、魔法封印、异次元属性及锻造的玩家入口、规则、资产结果和 Human Test List 可以分别实现与接受；共享装备实例及事务基础属于已接受基线或逐项复用，不要求预先为未来项创建 Spec。每次只激活一个子项。

## Overall Direction

各子项按“70 端和上游参考 → A21 客户端与 PVF 取证 → 设计 → 实现 → 自动验证 → 待用户集中实机验收”闭环。先处理强化（用户列出的第一项），再依据已接受结果选择后续子项；各项的设计选择保持开放。共用的装备实例、槽位、事务、金币/材料消耗和客户端投影必须以当前代码证据为准，参考端不自动升级为本服事实。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 强化闭环 | satisfied | `spec-001` accepted commit `e1d4c216`; `attempts/spec-001-reinforcement/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行，不计入本自动接受条件 | aligned |
| SC-2 | 装备附魔闭环 | satisfied | `spec-002` 的 `0x0110` 宝珠路径已在 `8ab85ebd` 接受，独立审查与自动验证见该子项记录 | 用户 A21 GUI 验收待后续进行；`0x025A` 附魔师摊位由用户明确排除 | aligned |
| SC-3 | 魔法封印闭环 | satisfied | `spec-003` accepted commit `4dbd73b4`; `attempts/spec-003-magic-seal/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行；材料路径证据不足而安全拒绝 | aligned |
| SC-4 | 异次元属性闭环 | satisfied | `spec-004` accepted commit `c15e6add`; `attempts/spec-004-outworld-amplify/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行；action2/污染装备自然产出留 D-004/D-005 | aligned |
| SC-5 | 锻造闭环 | satisfied | `spec-005` accepted commit `439b6788`; `attempts/spec-005-forging/spec-result.md` 和验证记录 | 用户集中实机验收仍待执行；卷券 variant1/2 与动态名语义未闭合 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| 1 | spec-001 | attempts/spec-001-reinforcement/TaskContract.md | 装备普通强化 | SC-1 | completed | attempts/spec-001-reinforcement/spec-result.md | e1d4c216d702aa829fa69a0eba3c95c1ea9301db |
| 2 | spec-002 | attempts/spec-002-equipment-enchantment/TaskContract.md | 装备宝珠附魔 | SC-2 | completed | attempts/spec-002-equipment-enchantment/spec-result.md | 8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9 |
| 3 | spec-003 | attempts/spec-003-magic-seal/TaskContract.md | 魔法封印装备 | SC-3 | completed | attempts/spec-003-magic-seal/spec-result.md | 4dbd73b48952c3158004167de6f02be14767df39 |
| 4 | spec-004 | attempts/spec-004-outworld-amplify/TaskContract.md | 异界气息与异次元属性 | SC-4 | completed | attempts/spec-004-outworld-amplify/spec-result.md | c15e6add5590d5f957210763a0b1c118a93982c3 |
| 5 | spec-005 | attempts/spec-005-forging/TaskContract.md | 装备锻造 | SC-5 | completed | attempts/spec-005-forging/spec-result.md | 439b6788c529fdb89fbfd4a29fa1527721dcd19b |

## Root Review

2026-09-30，独立只读完整结果审查，verdict：`approved`，仅适用于“自动验证完成、可交用户集中实机验收”的根任务标准。审查者对根契约、五个子项的结果/审查/验证/Human Test List、接受提交和累计代码/协议做交叉核验：未发现协议注册冲突；各功能在原装备实例上更新对应字段，保留 UID 与无关属性；重放和事务失败有针对性测试；父 Map 的 SC-1..5 状态及范围表述一致，`git diff --check` 通过。未发现未解决的高/中严重度阻断项。审查未运行测试、未编辑或提交。

残余边界：A21 GUI/实帧及重登仍待用户集中验收；魔法封印卷轴路径、异界动态 action2、锻造卷券变体因证据不足安全隔离。此审查不升级任何 `ClientStatic`/上游推断为 `ClientVerified`。

## Root Verification

2026-09-30，`kind: rks-verification`，`decision: pass`，仅对根 TaskContract 的自动交付标准有效。接受产品 HEAD 为 `439b6788c529fdb89fbfd4a29fa1527721dcd19b`：五个唯一子项均 `completed`，各有独立提交、`spec-result.md`、批准审查、自动验证和 Human Test List；SC-1..5 均 `satisfied`、约束均 `aligned`、无运行中子项、`active_attempt: null`。根独立审查已 `approved`。

- 新鲜 `dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet`：0 警告、0 错误。
- Protocol 全量：1123 通过、60 跳过；Networking 全量：71/71；真实 PVF Infrastructure 全量：408 通过、2 跳过。
- 无 PVF Application 全量独立运行：2107 通过、171 跳过、0 失败。先前与其它套件并行运行时 `SelectCharacterLifecycleTests.ObserverQueuedBeforeRollbackClose_CompletesAfterGateRelease` 曾因 2 秒等待失败一次；该单测独立通过，随后全量独立通过。根因未定位，记录为时序敏感风险，不把首次失败隐去。
- 真实 PVF Application 全量：2287 通过、2 失败。两项仍是此前子项验证已记录的 `CreatureGoesBackOnTests` 宠物回装失败；该套件不记为通过，不归因于本次装备改造，也不宣称根因已修复。
- `git diff --check` 清洁；产品接受提交后工作树曾为 clean，最终仅本父控制器文档待封存提交。未启动 A21 客户端、部署、推送或修改用户数据库。

未验证边界：五份 Human Test List 仍待用户集中 A21 实帧/GUI/重登验收；证据不足的卷轴/动态变体保持隔离。自动交付 `pass` 不等同 `ClientVerified` 或玩家体验验收通过。
