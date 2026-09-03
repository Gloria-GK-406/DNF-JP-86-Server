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
  child-sealed：spec-001 以 outcome completed 封存于
  attempts/spec-001-money-named-for-what-it-is/spec-result.md，终止阶段 verification。
  child-accepted：接受前 HEAD 仍等于先前基线 1946c012（实测），完整变更集经检查后一次提交，
  新提交 e4f29e1d4d68abd4b45114822731c8e8f9995874；提交后任务工作区 git status 为空、
  分支仅前进这一步（e4f29e1d 的父提交即 1946c012）、源检出 main 仍为 1946c012 且状态为空、
  无关的 client-debug-lab 仍为 33a18570 未触碰。工作区基线随之推进。
  gaps-remain（第二次）：在已接受基线 e4f29e1d 上，SC-5/6/8/9 已 satisfied、SC-7 in_progress、
  SC-1..SC-4 仍 not_started（见下表），九条约束全部 aligned，无违反也无风险项。root-satisfied 不成立。
  safe-successor（第二次）：剩余缺口全部指向同一个有界稳定结果 —— 「玩家身上的金币是真的：
  能被写入、跨会话保留、变更时告知客户端，且既有的赚钱与花钱路径都作用于它」。它一次性覆盖
  SC-1..SC-4 与 SC-7 的剩余一半；自带 oracle（实机：打一把赚钱、记数、重登核对；付费翻牌前后核对差额）；
  可独立验收，也可在不牵连 spec-001 已接受结果的前提下回滚。拆得更小会交付一个玩家看不见的半成品：
  只有写路径而不改三个入账点，等于交付一个永远显示 0 的钱包，SC-1 无从观察。
  child-created（第二次）：确定性标识 spec-002 / spec-002-gold-the-character-keeps（既有最大尝试号
  为 1，故次号为 2，号不复用；spec-001 已封存，无 running 行，active_attempt 为 null）；子契约与
  路由决策写在 attempts/spec-002-gold-the-character-keeps/ 下，经规范化后证明包含于 task_root。
  依赖链接校验：depends_on 为空 YAML 序列，合法 —— spec-001 是已并入基线 e4f29e1d 的已接受历史，
  按规则不得表示为依赖边。游标一致性校验：safe-successor 的目标为 child_modeling/pending，
  与转移前持久化状态一致。子路由决策已完成 readiness / marginal value / Single 结构选择三节，
  Controller Selection 留给 single-spec-workflow。
  child-sealed（spec-002）：以 outcome completed 封存于
  attempts/spec-002-gold-the-character-keeps/spec-result.md，终止阶段 verification。
  child-accepted（spec-002）：接受前 HEAD 仍等于先前基线 e4f29e1d（实测），完整变更集 46 文件
  +2304/−223 一次提交，新提交 50d45f384819219f4b9ff598b6210cad77c6cc7b；提交后任务工作区
  git status 为空、分支仅前进这一步（父提交即 e4f29e1d）、源检出 main 仍为 1946c012 且状态为空、
  无关的 client-debug-lab 仍为 33a18570 未触碰。工作区基线随之推进。
  root-satisfied：九条成功条件在已接受基线 50d45f38 上全部有已接受证据，九条约束全部 aligned。
  review-approved：两个子 Spec 各自经独立的新上下文评审者审过完整变更集并 approved，无未解决的
  阻断性发现；根级复核确认两者合起来覆盖根契约的全部范围，无遗漏、无越界。
  verification-passed：见下方 Root Verification。
workspace:
  repository_root: E:/Project/DNF/DNF-JP-86-Server
  task_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gold-the-player-carries/.rks-agent/tasks/gold-the-player-carries
  code_root: E:/Project/DNF/DNF-JP-86-Server/.worktrees/gold-the-player-carries
  branch: gold-the-player-carries
  baseline: b46dbbd37c373aa27589ea1590e7a01b92765bc2
  baseline_evidence: >-
    PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test
    Server/86JP.Server.slnx --nologo → exit 0；七个测试程序集合计 1993 通过 / 2 跳过 / 0 失败
    （60 + 52 + 488 + 4 + 248 + 215 + 926），与契约声明的基线逐项吻合。那 2 个跳过出自
    Dfo.Server.Infrastructure.Tests 的缺档案用例，是档案已加载的证据而非静默跳过。
    接受 spec-001 后于 e4f29e1d 复测同一命令，计数逐程序集不变。
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
| SC-1 | 玩家赚到的金币显示在背包上，重新登录后仍然是那个数 | satisfied | spec-002 验证证据；实机三方吻合（日志 / 库 / 客户端同为 199513），且拾取后跨重登仍在 | 无 | aligned |
| SC-2 | 玩家花钱的既有路径从他看得见的那笔钱里扣，余额与显示一致 | satisfied | spec-002 验证证据；实机 200000 − 580 + 47 = 199467，与库一致 | 无 | aligned |
| SC-3 | 携带上限对随身金币真实生效，超限入账处理确定且一致 | satisfied | spec-002 验证证据；上限取档案按等级下限与已买升级的较大者，在唯一入口上截断，13 条判据覆盖 | 无 | aligned |
| SC-4 | 新建角色带确定的随身金币起始值，而非靠读取缺省得 0 | satisfied | spec-002 验证证据；character_stats 的行随角色创建而生，「无行」不再是可与持有零混淆的状态 | 无 | aligned |
| SC-5 | `purse` 不再在本仓指代任何一笔钱 | satisfied | spec-001 验证证据；全仓检索 0 命中，检索有效性经反证 | 无 | aligned |
| SC-6 | `Gold` 只出现在指代金币之处 | satisfied | spec-001 验证证据；逐条判定全部命中，六个碎片字段改为 Cube 结尾，牌面改为 GoldCard | 无 | aligned |
| SC-7 | 两笔钱可从名字区分且不能互相误用 | satisfied | spec-001 与 spec-002 验证证据；随身一侧不带角色标识、金库一侧强制传账号标识，两者不同形，误用在编译期被拒 | 无 | aligned |
| SC-8 | 仓库文档不再把随身金币记作已实现 | satisfied | spec-001 验证证据；四处修正逐条独立查证，含事故源头那一栏改为分述 | 无 | aligned |
| SC-9 | 命名与文档的归位没有改变任何行为 | satisfied | spec-001 验证证据；1993 通过 / 2 跳过 / 0 失败，逐程序集与基线一致，断言与期望值未动 | 无 | aligned |

## Spec Attempts

Only the final row may be `running`. Seal terminal rows in place and never reopen them.

| # | Attempt | TaskContract | Outcome | Affects | State | Result | Git result |
|---:|---|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-money-named-for-what-it-is/TaskContract.md` | 两笔钱各自叫对名字，文档与事实一致，行为逐值不变 | `SC-5, SC-6, SC-7, SC-8, SC-9` | `sealed` | `completed` | `accepted e4f29e1d` |
| `2` | `spec-002` | `attempts/spec-002-gold-the-character-keeps/TaskContract.md` | 玩家身上的金币是真的：能写入、跨会话保留、变更时告知客户端，既有收支都作用于它 | `SC-1, SC-2, SC-3, SC-4, SC-7` | `sealed` | `completed` | `accepted 50d45f38` |

## Root Review

两个子 Spec 各自由独立的新上下文评审者审过完整变更集，均以 `approved` 收束，无未解决的阻断性发现。
根级复核只问一个它们各自答不了的问题：**两者合起来是否覆盖根契约的全部范围，且没有互相破坏。**

- 覆盖：SC-5/6/8/9 由 spec-001 满足，SC-1/2/3/4 由 spec-002 满足，SC-7 的两半分别由两者满足。无遗漏。
- 越界：两个 Spec 的 Out 范围（存取款协议、金库存量迁移、拍卖行、邮件金币与卖装备收入、复活币与
  胜点、宿主部署参数）均未被触碰。
- **互相破坏：查到一处，已整改。**见下。

## Root Verification

在已接受基线 `b46dbbd3` 上新鲜取得：

- 全量判据 `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` → exit 0，
  `60 + 488 + 52 + 255 + 6 + 221 + 933 = 2015 通过 / 2 跳过 / 0 失败`。根契约声明的基线是
  1993 / 2 / 0，增量 22 全部来自新判据，无程序集下降。两个跳过仍是缺档案守卫，即档案已加载的证据。
- 全仓检索 `purse` → **0 命中**（`Server/`、`Docs/`、`protocol/`，`.cs` 与 `.md`）。
- 实机（授权方本人操作客户端）：拾取 46 金币后，服务端日志 `character now carries 199513`、
  数据库 `carried_gold = 199513`、客户端显示 **199513**，三方吻合；`account_cargo_state` 全程 0 行。

**根级验证抓到了一处两个子 Spec 的评审都看不到的回归。** SC-5（`purse` 不再指代任何一笔钱）由
spec-001 满足，而 spec-002 在自己新写的注释里又用了两次该词——`ICharacter.cs:59` 与
`SqliteCharacterCarriedGoldTableTests.cs:154`。两次评审各自看的是一个变更对它自己的边界，在那个
边界内这个词只是无害的散文；只有对着根契约「这个词在本仓不指任何一笔钱」才是回归。已于
`b46dbbd3` 整改，判据计数不变。

这是本根任务保留下来的一条判据：**逐个子 Spec 的评审证明不了「后一个没有打破前一个」。**
