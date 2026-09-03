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
  workspace-ready：规范根 .worktrees/gold-the-player-carries（git rev-parse --show-toplevel
  实测），分支 gold-the-player-carries，HEAD 1946c012，工作区仅两份声明的未跟踪任务产物；源检出
  main 保持 1946c012 且 status 空；启动产物 sha256 双向核验后转移（b3034415… / 35f03f10…）并删源；
  .worktrees/ 早在 .git/info/exclude:7，无准备性改动、无启动提交；基线判据实测通过。
  gaps-remain：基线 1946c012 上九条成功条件全部 not_started、无已接受证据（见下表），九条约束
  全部 aligned（尚未有任何偏离）。root-satisfied 不成立。
  safe-successor：在根授权范围内存在一个有界稳定结果 —— 「两笔钱各自叫对名字、文档与事实一致，
  且行为逐值不变」。它自带 oracle（全量判据计数不变且不改任何断言 + 全仓检索核销），可独立验收，
  也可在不牵连任何兄弟结果的前提下回滚；交付后即使随身金币永不实现，仓库仍严格更好。
  随身金币的实现依赖它确立的名字，故排在其后，本次不建。
  child-created：确定性标识 spec-001 / spec-001-money-named-for-what-it-is（Spec Attempts 表此前
  为空，故次号为 1，且无 active_attempt、无 running 行）；子契约与路由决策均写在
  attempts/spec-001-money-named-for-what-it-is/ 下，经规范化后证明包含于 task_root。
  依赖链接校验：depends_on 为空 YAML 序列，合法（无未决外部前置）。游标一致性校验：
  safe-successor 的目标为 child_modeling/pending，与转移前持久化状态一致。子路由决策已完成
  readiness / marginal value / Single 结构选择三节，Controller Selection 留给 single-spec-workflow。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gold-the-player-carries/.rks-agent/tasks/gold-the-player-carries
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gold-the-player-carries
  branch: gold-the-player-carries
  baseline: 1946c012fc7083835c742699b0bb2ad5a29fda29
  baseline_evidence: >-
    PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
    Server/86JP.Server.slnx --nologo → exit 0；七个测试程序集合计 1993 通过 / 2 跳过 / 0 失败
    （60 + 52 + 488 + 4 + 248 + 215 + 926），与契约声明的基线逐项吻合。那 2 个跳过出自
    Dfo.Server.Infrastructure.Tests 的缺档案用例，是档案已加载的证据而非静默跳过。
---

# Multi-Spec Map

## Multi-Spec Boundary

- 两个独立可验收的结果，各有各的 oracle 与回滚边界。命名与文档的归位由「全量判据计数不变且不改任何断言」加全仓检索核销；随身金币的实现由实机观察核销（打一把赚钱、重登核对）。
- 判定它们够格分开的证据是：**命名归位交付后即使随身金币永不实现，仓库仍处在严格更好的状态**——假前提被拔除、两笔钱可区分。反过来随身金币依赖归位后的名字，耦合是单向的。
- 两段的失败互不牵连：命名归位失败不该阻止随身金币实现，随身金币实现失败也不该回滚已经正确的命名。

## Controller Cursor

- Read `references/controller-transitions.md` before advancing the cursor.
- Record the transition and resolvable evidence before selecting its target phase.
- On interruption, finish a fully proven transition, restart a phase with no mutation, or block when partial mutation cannot be classified.

## Success Condition Progress

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | 玩家赚到的金币显示在背包上，重新登录后仍然是那个数 | not_started | none | 随身金币无写路径，登录读出恒为 0 | aligned |
| SC-2 | 玩家花钱的既有路径从他看得见的那笔钱里扣，余额与显示一致 | not_started | none | 付费翻牌当前从账号金库扣 | aligned |
| SC-3 | 携带上限对随身金币真实生效，超限入账处理确定且一致 | not_started | none | 上限链存在但限着一个不存在的量 | aligned |
| SC-4 | 新建角色带确定的随身金币起始值，而非靠读取缺省得 0 | not_started | none | 建角色不插主背包槽 0 的行 | aligned |
| SC-5 | `purse` 不再在本仓指代任何一笔钱 | not_started | none | 79 处 / 10 文件，其中测试 62 处含类名与用例名 | aligned |
| SC-6 | `Gold` 只出现在指代金币之处 | not_started | none | 魔方碎片、黄金牌牌面两类非货币用法混在同名空间 | aligned |
| SC-7 | 两笔钱可从名字区分且不能互相误用 | not_started | none | 只有金库一笔被实现，且其名字指向随身语义 | aligned |
| SC-8 | 仓库文档不再把随身金币记作已实现 | not_started | none | 三份文档走样，其中一份是整起事故的源头 | aligned |
| SC-9 | 命名与文档的归位没有改变任何行为 | not_started | none | 归位尚未开始 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-money-named-for-what-it-is/TaskContract.md` | 两笔钱各自叫对名字，文档与事实一致，行为逐值不变 | `SC-5, SC-6, SC-7, SC-8, SC-9` | `running` | `pending` | `active` |
