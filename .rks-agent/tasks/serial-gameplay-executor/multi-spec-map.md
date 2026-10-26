---
kind: rks-multi-spec-map
contract:
  path: ./TaskContract.md
  revision: 4
workspace:
  repository_root: /Users/puxianguo/project/DNF-JP-86-Server
  task_root: /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021/.rks-agent/tasks/serial-gameplay-executor
  code_root: /Users/puxianguo/project/DNF-JP-86-Server/.claude/worktrees/refactor-session-20261021
  branch: task/refactor-session-20261021
  baseline: a02bce469ec7fde2392592292bbdb9450939a02c
  baseline_evidence: "2026-10-23：`pwd -P` 为 code_root；`git rev-parse --git-common-dir` 为 repository_root/.git；`git rev-parse --abbrev-ref HEAD` 为 task/refactor-session-20261021；HEAD 06ca5e0f2ada44c2a8830f127e9ed5f16ce22502；`git merge-base --is-ancestor a02bce46 HEAD` 成立；`a02bce46..06ca5e0f` 共 31 个提交，都以 `serial-gameplay-executor:` 开头（规划 1、修订 2、单元 28）；`git status --short -- Server Tool Docs protocol AGENTS.md` 为空，未提交的改动只在本任务目录下。baseline 是原 Single Spec 的 Spec 基准，即 spec-001 的收编基准（见 Multi-Spec Boundary）"
---

# Multi-Spec Map

## Multi-Spec Boundary

- **为什么分成两个子契约**：根契约 revision 4 有两个可以独立验收的结果。
  - 结果 A：单执行器、提交后发件箱、删锁与调用链。它在过渡机制下已经构成一个可运行、可测试、可回滚的状态。
  - 结果 B：协议级 CMD，按功能簇做结构归并。
  - 依赖是单向的，B 依赖 A，A 不依赖 B。两者的验收、审查与回滚边界不同。
  - 改道理由见 `routing-decision.md`「改道记录」。用户 2026-10-23 的决定是：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”。
- **同一分支，一次收尾**：全部子 Spec 都在 `task/refactor-session-20261021` 上完成。A、B（以及可能出现的修正子 Spec）都封存，且用户告知 GPT Astra 审核通过之后，才推送、开 PR、收尾；只推送一次，A 不单独开 PR（根契约 Requires Confirmation 第 2 项）。
- **spec-001 是收编的子 Spec，不是新建模的**（主控方裁定第 2 条，`verification/reroute-t008.md` 文末）：
  - 它就是改道前的 Single Spec 去掉原 Plan T-008 之后的部分。Spec 基准沿用 `a02bce46`，不取「加了它那一行的根映射提交」。回滚 spec-001 时，回到 `a02bce46`。
  - 产物原地沿用：`spec.md`、`plan.md`、`tasks/`、`reviews/`、`verification/` 都留在根任务目录，由子路由决定的 Artifact path overrides 指向，并逐项列出排除的根级文件。只有子契约、子路由决定与将来的 `spec-result.md` 在 `attempts/spec-001-executor-outbox-lock-removal/` 下。
  - `a02bce46..06ca5e0f` 的 31 个提交都是 spec-001 的检查点（规划 1、修订 2、单元 28）。
- **已知的 `foreign-commit` 例外**（主控方裁定第 3 条）：
  - 改道生效时的**根级改道提交**落在 spec-001 的区间（`a02bce46` 到 spec-001 封存提交）里。这个提交就是首次加入本文件的那个提交，可以用 `git log --diff-filter=A --format=%H -- .rks-agent/tasks/serial-gameplay-executor/multi-spec-map.md` 唯一确定。
  - 它包含根契约 revision 4、根改道记录生效、本映射、spec-001 的子契约与子路由决定、改道汇总与 B 的草案、根契约与子契约的审查记录。
  - `controller-validation.md`「Derived progress consistency」第 4 条在 spec-001 区间内只对这一个提交放行，其余非 spec-001 检查点的提交照常报 `foreign-commit`。
  - 不为通过校验而改写历史。
  - 紧随其后的 spec-001 修订提交（`spec.md`、`plan.md`、`verification/follow-ups.md` 去掉「提案」）是 spec-001 自己的检查点，不是例外。
- **spec-002 建模与激活边界（当前状态见末行）**：
  - 结果 B 在 spec-001 封存、根评估之后，才按「Model Exactly One Child」建模。
  - A封存前草案暂留verification（裁定4）；A接受后已逐字节移为`attempts/spec-002-protocol-level-cmd/source-draft.md`。控制器按缺口写就该目录TaskContract r1，经独立intake approved后激活，未提前创建Spec。
  - spec-002 在 spec-001 封存后即可开始，不等外部审核。
- **修正子 Spec（可能出现）**：用户若先让 GPT Astra 看 A，Astra 对 A 的意见作为编号接在 B 之后的新子 Spec，或者在不跨边界的前提下并进 B，由主控方届时判定。封存后的 spec-001 不重开。

## Overall Direction

- **A → B 的交接物**：根 SC-12 的过渡机制清单（handler 在作业内编排细粒度命令、`ICommandBus.SendAsync` 在执行器上内联执行、请求级帧以 AsyncLocal 在作业内设置、细粒度命令及其 0／45／58／60／100 管线），由 A 在 T-009 交付。B 以它为起点，在 B 封存时检索为零（根 SC-13）。
- **移交给 B 的起点材料**：
  - `spec.md` 修订记录 R3-1 中标「B」与「跨」的条目；
  - 原 Plan T-008 一行（功能簇顺序）；
  - `verification/follow-ups.md` 中标「〔A 封存：→ 子 Spec B〕」的18项：F-07、F-12、F-34、F-58、F-69、F-86、F-88、F-93、F-109、F-110、F-112、F-122、F-124、F-127、F-129、F-130、F-131、F-132；另有F-128①及无编号扇出字段判据事项，范围见`verification/t009-closure.md` §6。
- 其余设计属于各子 Spec，这里不预设。

## Controller Progress

- Derive the root phase from this map, the attempt rows, child artifacts, and Git with `references/controller-transitions.md`.
- Commit this map alone after adding or sealing a row and after appending root review, verification, or block evidence.
- On interruption, finish a fully proven transition, restart work that left no mutation, or block when partial mutation cannot be classified.
- 例外：本映射的首次落盘与改道的其余根级文件同在一个根级改道提交里（见 Multi-Spec Boundary），不单独提交。

## Success Condition Progress

按 `multi-spec-workflow`，只有已封存、已完成子 Spec 的证据才算交付进度。spec-001已在e6f55558封存，A的十一条条件按最终核验和原附条件计入；B六条仍未交付。

| SC | Requirement | Progress | Evidence | Remaining gap | Constraint |
|---|---|---|---|---|---|
| SC-1 | A：唯一执行器，全部入口作为作业经唯一入队点进入 | satisfied | `verification/completion-evidence.md` SC-1；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件（TASK-35 统一口径）：AC-006 依赖 `a-final-report.md` §4.2 的「RQ-061」与「调度器排队中的运行合并」两条解读 | aligned |
| SC-2 | B：每个入站协议一条协议级 CMD，handler 只解包入队 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-001/002/007；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |
| SC-3 | A：出站消息提交后发出，顺序、批次、必达、隔离成立 | satisfied | `verification/completion-evidence.md` SC-3；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件，都已有去向：AC-007、RQ-012「此前登记的消息」按连接计（外部审核时说明）；AC-011 线级第 3 条降到发送端层（F-108）；AC-012 的 XO-2～XO-7 由发送端通用用例承担、XO-6 是近似（VC-10）；AC-013 人群项的适用范围（F-113、F-114）；VC-10 第 3 句的衔接是读码 | aligned |
| SC-4 | A：玩法路径上的门、信号量与重试循环删除 | satisfied | `verification/completion-evidence.md` SC-4；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件（TASK-35 统一口径）：AC-017 依赖 §4.2 的「RQ-061」与「附录 F 改为执行器内访问」两条解读。短 `lock` 与 PVF 内容缓存锁按根 Scope Out 第 2 项保留；A 在 Networking 新增的执行器外短锁已在 `transition-mechanisms.md` §3 列出 | aligned |
| SC-5 | A：事件组件保留，Reliable、LiveOnly 与重试经执行器 | satisfied | `verification/completion-evidence.md` SC-5；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | none | aligned |
| SC-6 | A：深度上限、调用链与超限处理覆盖全部来源 | satisfied | `verification/completion-evidence.md` SC-6；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件：RQ-050 末句的实现精度。CMD 已登记关闭、发送端还没执行时，传输层先断开，这次断开仍按继承处理，与 RQ-050 末句的字面不符（TASK-29 主控方裁定第 5 条；`t007-closure.md` §3 第 5 条、§4 第 2 条）。去向：F-131，子 Spec B（TASK-35 定）；交审重点第 10 项 | aligned |
| SC-7 | A：依赖投递结果的行为都有承接，可见变化列明 | satisfied | `verification/completion-evidence.md` SC-7；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件，已有去向：AC-014 的 DR-16 证据用占位作业代表进入序列；VC-10 的组合证据 | aligned |
| SC-8 | A：A 封存状态下构建、全量测试、线级与表征断言成立 | satisfied | `verification/completion-evidence.md` SC-8；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件（TASK-35 统一口径）：AC-019 依赖 §4.2「AC-019 的 b1、b2」，AC-020 依赖 §4.2「RQ-083 已登记的偶发项」（第四、第五栈根已按主控方处置登记，§2.2、§2.3） | aligned |
| SC-8B | B：B 封存状态下 SC-8 成立，A 的 SC-1、SC-3～SC-7 仍成立 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-002/003/005/006/008/009，保留A附条件；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |
| SC-9 | A：A 封存状态下的实测数字，单条 CMD ≤ 2 秒 | satisfied | `verification/completion-evidence.md` SC-9；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件（TASK-35 统一口径）：判定口径依赖 §4.2「M-1～M-3 的判定口径」。未达原目标值的项是后续性能优化（F-77～F-82、F-120），不阻断 | aligned |
| SC-9B | B：B 封存状态下再测并与 A 对照 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-010；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |
| SC-10 | A：文档与 A 封存状态一致 | satisfied | `verification/completion-evidence.md` SC-10；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 附条件（TASK-35 统一口径）：AC-022 的检索范围按 §4.2；F-128 ②③：RQ-100 点名范围之外的两份文档，交主控方 | aligned |
| SC-10B | B：文档与协议级 CMD 一致 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-007/011；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |
| SC-11 | A：A 的最终汇报 | satisfied | `verification/completion-evidence.md` SC-11；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | 外部审核在 B 完成后进行（根 RI-16），审核结论不是封存的前提 | aligned |
| SC-11B | B：B 的最终汇报 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-011；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |
| SC-12 | A：过渡机制清单，清单之外无残留 | satisfied | `verification/completion-evidence.md` SC-12；`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`（seal `e6f55558`）；`verification/a-final-report.md` §9 | none | aligned |
| SC-13 | B：保留项在 CMD 内位置等价，工作单元对照，SC-12 清单清零 | satisfied | `attempts/spec-002-protocol-level-cmd/verification/completion-evidence.md` AC-003/004/005/007；B seal `ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；完整报告及独立审查见该子目录 | 无未交付项；性能下降、2条既有失败/18skip、TitleQuery/F131/F132及A附条件如实保留；用户外部Astra与分支收尾仍待 | aligned |

## Spec Attempts

Only the final row may have an empty `Git result`; that row is the active child. Seal a row by writing its result path and its seal or rollback commit, and never reopen it.

| # | Attempt | TaskContract | Outcome | Affects | Result | Git result |
|---:|---|---|---|---|---|---|
| `1` | `spec-001` | `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md` | 结果 A：单执行器、提交后发件箱与删锁（收编，Spec 基准 `a02bce46`） | `SC-1, SC-3, SC-4, SC-5, SC-6, SC-7, SC-8, SC-9, SC-10, SC-11, SC-12` | `attempts/spec-001-executor-outbox-lock-removal/spec-result.md` | `2079d98d179ffd423298f18dba358949cd7ae4f1`（改写前 `e6f55558`） |

| `2` | `spec-002` | `attempts/spec-002-protocol-level-cmd/TaskContract.md` | 结果 B：协议级CMD、显式请求上下文、工作单元与过渡清零 | `SC-2, SC-8B, SC-9B, SC-10B, SC-11B, SC-13` | `attempts/spec-002-protocol-level-cmd/spec-result.md` | `ec41fc575f9899bf9ce71d22825fa59243e3b3fa` |

## Root Evaluation：接受A后的后继决定（2026-10-24）

- child-accepted：A seal e6f55558182800b656b14c4b863a52fe672ad921可达，父为TASK35 0ecb9af3；封存恰38个已归属制品路径、无Server/Tool改动、工作区干净。a02bce46..seal41提交＝40个A检查点（规划1、Task35、修订3、seal1）＋唯一登记根改道9d146830。没有其他foreign commit，未重开A。
- gaps-remain：SC2、SC8B、SC9B、SC10B、SC11B、SC13均未交付；A十一条已满足并保留原附条件。七约束（顺序/保护/可见变化/搬迁与语义分离/删净/单写者/先A后B）均aligned；保护的F88/F93先补测试、RQ050精度等条件详见A核验，不隐藏。
- safe-successor：沿根task授权及用户2026-10-24“继续完成spec2剩余工作”建模唯一spec-002-protocol-level-cmd，承担上述六条B条件。A已封存，不等Astra才能开始；仍不推送/PR/收尾。
- 首个B单元按已定顺序承接F130（测试清池）和F127（写端完成判定）；F88/F93在迁移相应保护前补，F132在事务内组装调用前处理。
- A最终挑战CH1（多结束挂钩首错短路）/CH2（Warning提供器抛错）为已接受低风险残余，细节见A最终汇报§11；交后继评估，B触及时可有界修，否则修正子Spec，不凭此扩大B硬要求。A保持封存不可改。

## Child Activation：spec-002（2026-10-24）

- ready子契约r1、同目录task-contract-review approved（精确路径/revision匹配）、Single子路由、父r4、canonical containment及depends_on空列表校验均通过，错误列表[]；激活前没有活动子行。
- 激活后只有末行spec-002的Git result为空，A仍是sealed completed。B复用同工作区/分支，其Spec baseline由本激活map检查点给出、写入B路由Execution Configuration。
- B制品在自己的目录；根原草案删除/子source-draft新增均属根输入迁移，A路径覆盖明确排除此文件，不是修改sealed A。该输入迁移与子契约/审查/路由由B规划检查点提交；此提交仅根map。

## 历史改写与流程调整（2026-10-25）

- 用户要求整理已提交的证据。`main..` 区间已改写：去掉本任务目录下的原始证据（14720 个文件，约 4.0 GB），并给 28 个提交补上任务前缀。代码与全部 Markdown 不变，逐提交核对通过。本节之前的记录写的是改写前的提交号，按 `history-rewrite-2026-10-25.md` 换算；Spec Attempts 表的 spec-001 Git result 已换成改写后的号。
- 本节之后，`Derived progress consistency` 里的提交号都以改写后为准。根改道提交（foreign-commit 例外）改写后是 `f4ade320965d`，仍可以用本文件首次加入的那个提交唯一确定。
- spec-002 自 TASK-33 起按 `process-reset-2026-10-25.md` 执行；它的执行配置调整记在 `attempts/spec-002-protocol-level-cmd/routing-decision.md` 末节。根契约、子契约和各条 SC 都不变。

## Child Acceptance：spec-002（2026-10-27）

- 已读B已提交的completed结果、approved完整审查与pass核验，seal为`ec41fc575f9899bf9ce71d22825fa59243e3b3fa`；产品保持`0c5acb958f673d3caf4352c3914960bed78a2c68`，正式源码与测量冻结相同。完整B和A+B集成的独立结果审查在B的`reviews/spec-002-final-review.md`；根门将在本次子项接受提交之后消费同一reviewer的确切提交确认，不提前冒作Root Review。
- 基准`51bd1e9c`至B seal均属本结果规划/Task/修订/收口；其中`15d52320`是用户授权的原始证据清理与B流程重置，含根历史映射/流程文件，`1fabc832`含已授权B草案迁入。这两项是已记录的关联过程变更，不是另一任务产品；A封存制品保持，详见B核验的历史/源身份检查。
- child-accepted后六条B条件以实际seal记satisfied，A原11条及其附条件保留；17项根条件已有提交证据，故root-satisfied。七约束（有序/保护/可见变化/搬迁与语义分离/删净/单写者/先A后B）均aligned。没有未交付的B工作或对已封存A的重开。
- 保留披露：测试9817=9797通过+2原内容失败+18原skip；16场测量最大CMD356.0821ms，清洁p99爬坡与Debug吞吐下降、默认无耗时样本及污染窗口按报告保留；称号查询ReadOnly拒绝、F131精度、F132禁用Compose、A原残余及自动取消的审查结论均不抹去。
- 下一控制器动作是同一完整内部review周期的已提交根状态确认，然后fresh Root Verification和map-only根封存。本次内部Astra不替代用户之后安排的外部审核；继续不推送/PR/合并/tag/远端issue/部署/分支工作树清理。
