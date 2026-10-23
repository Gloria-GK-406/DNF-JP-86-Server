# 改道汇总：T-008 改为独立的子 Spec B

- 日期 2026-10-23，基准提交 `06ca5e0f`（分支 `task/refactor-session-20261021`）。
- 状态：**2026-10-23 已生效**（见文末「生效」一节）。以下为送审时的原文：提案，尚未生效。下面所有改动都等主控方审阅，再按 §6 派独立审查；审查通过之前，原 Single Spec 的路由、Spec 与 Plan 继续有效。
- 决定来源：
  - 主控方依用户 2026-10-22 的授权提出改道，用户原话：“你来做主吧，结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。”
  - 2026-10-23，用户本人定下结构：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”（见文末「主控方裁定」）。
- 第 1 轮审查之后（文末「主控方处置」）：根契约升到 revision 4，子契约 A 升到 revision 2。下表与 §1～§7 中写着 revision 3／1 的地方，是第 1 轮送审时的状态；改动的落点见文末「第 1 轮审查发现的落点」。
- 本提案改动或新增的文件，都在本任务目录下：

| 文件 | 改动 |
|---|---|
| `TaskContract.md` | revision 2 → 3，`state: ready`；结构改为结果 A／B，RI-1～RI-16，SC 带「结果」一栏 |
| `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md` | 新增：结果 A 的子契约（revision 1，`state: ready`） |
| `verification/reroute-t008-spec-002-draft.md` | 新增：结果 B 的子契约草案（不在 `attempts/` 下，理由见文件开头） |
| `routing-decision.md` | 末尾追加「改道记录（提案）」；原决定不动 |
| `spec.md` | 31 处行内标记「〔改道提案 R3：…〕」；末尾追加「修订记录 R3」；原文不删 |
| `plan.md` | 14 处行内标记；末尾追加「修订记录 R3」；原文不删 |
| `verification/follow-ups.md` | 文首加一条说明；18 行「归属」末尾加标记；新增 F-122 |
| `verification/reroute-t008.md` | 本文件 |

- 未改动的：`tasks/TASK-*.md`、`reviews/`、产品代码；行尾全部是 LF。没有创建 `multi-spec-map.md`，也没有写子路由决定，理由见 §5。

## 1. 改道前后的结构

| | 改道前（rev 2） | 改道后（rev 3，提案） |
|---|---|---|
| 路由 | `single-spec-workflow`，一个验收边界 | `multi-spec-workflow`，根契约下两个子 Spec，先 A 后 B |
| 结果 A | — | spec-001：单执行器、提交后发件箱与删锁。即现有 Spec 去掉 T-008 的部分：T-001～T-007、T-009、M-1、M-2 |
| 结果 B | — | spec-002：协议级 CMD，即原 T-008。在 spec-001 封存、根评估之后才建模 |
| Spec／Plan | `spec.md`、`plan.md`（task-units） | 原文件原地沿用，作为 spec-001 的 Spec 与 Plan，验收边界收窄；B 另写自己的 Spec 与 Plan |
| 控制器 | `sdd-governed` ＋ `long-running` | spec-001 沿用；spec-002 激活时由 `single-spec-workflow` 重新选择 |
| Spec 基准 | `a02bce46` | spec-001 仍是 `a02bce46`；根 `workspace.baseline` 同为 `a02bce46` |
| 剩余工作 | T-007、T-008、T-009 | A：T-007 → T-009 → 封存（向用户汇报一次）；B：建模 → Spec → Plan → 归并 → 封存；之后外部审核对整个分支一次进行，通过后一次收尾 |
| 授权 | 根契约 task 一行 | 不变 |

改道的触发，是原路由决定预先登记的第一条改道条件。理由写在 `routing-decision.md`「改道记录」，这里不重抄。

## 2. 根契约 revision 3 的分配

**来源意图（Root Intent Coverage）**

| 归处 | RI |
|---|---|
| 结果 A | RI-1、RI-3、RI-4、RI-5、RI-6、RI-7、RI-13 |
| 结果 B | RI-2（「CMD直接把其他的都吸收了。命令变成唯一实体……以协议为单位」） |
| A 与 B 共同 | RI-8、RI-9、RI-15 |
| 根契约本身（流程、授权、结构） | RI-10、RI-11、RI-12、RI-14、RI-16 |

新增的三条：
- RI-14：用户授权主控方，主控方提出改道；revision 4 补入用户 10-23 的结构决定（同一分支、一次收尾）；
- RI-15：用户的性能口径修订，单条 ≤ 2 秒即接受；
- RI-16：GPT Astra 审核通过后才收尾。revision 4 写明：审核默认在 B 完成后对整个分支一次进行；用户先让 Astra 看 A 也不触发收尾，Astra 对 A 的意见作为新的修正子 Spec 或并进 B，封存后的 A 不重开。

rev 2 的 RI-1～RI-13 一条不少。

**成功条件**

| 结果 | SC |
|---|---|
| A | SC-1、SC-3～SC-12。其中 SC-12 是新增的「过渡机制清单」 |
| B | SC-2、SC-8B、SC-9B、SC-10B、SC-11B，以及新增的 SC-13（保留项在 CMD 内位置等价、工作单元对照、SC-12 清单检索为零） |

rev 2 的 SC-8～SC-11 按「A 封存状态」与「B 封存状态」各留一份。

## 3. 移交清单（Spec 一侧）

逐条拆法见 `spec.md` 修订记录 R3-1，原归 T-009 的部分见 R3-3。摘要如下：

- **整条移交 B**：
  - RQ-020、RQ-021、RQ-022、RQ-025、RQ-070；
  - AC-008、AC-010；
  - 附录 C；
  - 附录 H 的 `EffectOperationLedger` 行与第 58、60、100 行；
  - Scope In「协议 CMD 吸收 handler 的工作」；
  - EV-4 中的附录 C 与 AC-010；
  - `SendAsync` 过渡内联规则的删除（原 T-008 最后一项）。
- **跨 A／B**（A 一半在 T-009 做，B 一半在子 Spec B 做）：
  - RQ-023、RQ-024、RQ-071、RQ-072、RQ-100；
  - AC-009、AC-018、AC-022；
  - 附录 H 的 dispatcher 行、第 45 行、第 0 行。
- **A 与 B 各自满足**（不移交）：AC-019、AC-020、AC-021，测试数量加法，最终测量。
- **留在 A**，原文不变：其余全部需求、验收与附录（R3-1 表尾列出）。

Plan 一侧的变化：
- T-008 标为「已改道，见子 Spec B」；
- T-009 的依赖改为 T-007，范围收窄为结果 A 的收口；
- T-007 不变。它的依赖 T-004、T-006、M-2 都已满足，见 `plan.md` 修订记录 R3。

## 4. follow-ups 的判定

`follow-ups.md` 中「归属」写着 T-008 的条目，加上 `t006-closure.md` §10 里交给 T-008、但没有进清单的一项（新增为 F-122），逐条判定如下。

| 编号 | 事项（简） | 原归属 | 判定 | 理由 |
|---|---|---|---|---|
| F-07 | 每日任务、Premium、配额刷新在一个作业里遍历全部在线会话 | T-008 拆分 | 子 Spec B | 原定在内部 CMD 簇改写时拆分。按用户修订，A 只要求单条 ≤ 2 秒，M-2 测到的同名作业平时检查 max 12.27 ms；日切时的遍历仍是 F-82 的覆盖缺口 |
| F-12 | 附录 C 一半 | T-008 | 子 Spec B | 附录 C 整体移交 B |
| F-30 | 双 Error 日志 | T-008 | 留在 A，归 T-009 | 这是执行器与生产者的日志问题，与协议归并无关；T-007 改写作业日志字段之后，T-009 一并处理。T-007 的行不变 |
| F-31 | 「执行器已停止」不带异常 | T-008 | 留在 A，归 T-009 | 同上，属于执行器本身 |
| F-32 | 线级测试：作业内 handler 抛异常 → 断线 | T-008 | 留在 A，归 T-009 | 这是 DR-15／RQ-031(c) 在 A 封存状态下的线级证据；B 改变 handler 形状后重跑 |
| F-33 | GM 预缓冲的罕见不等价 | T-008 | 留在 A，归 T-009 | 属于 GM／HTTP 入口（T-004 范围），与协议归并无关；T-009 判定接受还是修复，并记入 A 的遗留 |
| F-34 | 「每个消息类型一个协议 CMD」；请求级上下文改为显式数据；`EffectOperationReplayTests:269` | T-008 | 子 Spec B | RQ-020、RQ-025 本体 |
| F-58 | 帧改为显式数据时复核泄漏流 | T-008 复核 | 子 Spec B | 随 RQ-025；总线一支已在 TASK-18 关闭，A 不受影响 |
| F-69 | 每单元的测试耗时对比 | T-005～T-008 | A 的 T-007、T-009 与子 Spec B 各自续写 | 信息性记录，跟着跑全量的单元走 |
| F-86 | 卡牌协调器「预留冲突重试 3 次」 | T-008 | 子 Spec B | 改它就要动卡牌协调器，而 B 要把 `*Coordinator` 并入 CMD；A 中它不是「锁内复查」，T-005 已判定不删 |
| F-87 | 用例名仍提到门、租约（改名一半） | T-008 按簇 | **留在 A，归 T-009**（主控方裁定第 6 条） | 原提案：这些用例在 B 中随功能簇改写；A 的 T-009 只在过渡机制清单旁注明。可选：如果主控方希望外部审核看到的 A 里没有过时用例名，可改为留在 A（见 §7 第 6 条） |
| F-88 | R5 当前性检查没有用例守着 | T-008 | 子 Spec B | 风险出在 B 把这些检查搬进 CMD 的时候，所以要在搬之前补守护用例；A 没有移动它们 |
| F-93 | `DiscardExact`、`DemandCurrent(recipient)` 没有用例守着 | T-008 | 子 Spec B | 与 F-88 同类 |
| F-94 | `GuestList.AccountsAsync` 删除 | T-008 | 不改 | 已在 TASK-22 关闭 |
| F-98 | XO-6 阶段语义的近似 | T-008 判断 | 留在 A，归 T-009 | 这是发送端有序组的语义，属于 A 的组件；T-009 判定要不要加阶段语义，不加就记为 A 的已知近似 |
| F-109 | `RefreshObservedPartiesAsync` 的 `representedSessions` 只做空值校验 | T-008 | 子 Spec B | 改的是 `*Delivery` 接口，随队伍簇并入 CMD |
| F-110 | 失败作业关闭来源连接依赖「作业已打开发件箱」的约定 | T-008 | 结构保证 → 子 Spec B；记为已知约定 → A 的 T-009 | B 会重写协议 CMD 的开始点，在那里把约定变成结构保证；A 在 T-009 的文档里把它记为已知约定 |
| F-111 | 没有用例使用的测试替身 | T-008 随簇删除 | 留在 A，归 T-009 | 这些是 A 删门之后留下的残余（门反例已在 TASK-24 删除），只动测试、不改断言；在 A 中删掉，符合「删净」 |
| F-112 | C2 包装各自持有独立发件箱 | T-008 | 子 Spec B | 随功能簇改写时让包装与被测容器共用发件箱；A 的 RQ-031(b) 证据是 `OutboxWorkUnitTests`，不受影响 |
| F-122（新增） | `NotificationOutcome.Delivered` 的含义变了、名字没改 | T-008（`t006-closure.md` §10） | 子 Spec B 改名；A 的 T-009 在文档里写明今天的含义 | 改名牵涉投递接口，随 B 的投递并入 |

合计（按主控方裁定第 6 条更正后）：子 Spec B 11 项（F-07、F-12、F-34、F-58、F-86、F-88、F-93、F-109、F-110、F-112、F-122）；留在 A 7 项（F-30、F-31、F-32、F-33、F-87、F-98、F-111）；两边各自续写 1 项（F-69）；不改 1 项（F-94）。11 ＋ 7 ＋ 1 ＋ 1 ＝ 20，等于 19 个既有条目加新增的 F-122。F-110 计在 B，它「记为已知约定」的一半由 A 的 T-009 做；F-122 同样计在 B，「写明今天的含义」一半由 A 的 T-009 做。

第 1 轮提交审查时，本段写的是「B 12 项（含 F-87）、A 6 项」。

`follow-ups.md` 第 9 行（TASK-21 的文首说明）里的「交 T-008」是历史记录，不改；按本表读。原归 T-007、T-009 的条目都留在 A，不变。

此外，`plan.md` 共享决定「测试」一条里的「假总线 + 直接 new handler 的用例在第 7 步随所属功能簇改写」移交子 Spec B。它不是 follow-up，所以记在 `plan.md` 修订记录里。

## 5. 与 `multi-spec-workflow` 常规流程的偏离

常规流程是：子 Spec 在根路由之后建模，Spec 基准是「加了它那一行的根映射提交」，产物放在 `attempts/spec-NNN-…/` 下。spec-001 是在执行中收编的，与此有三处不同：

1. **产物位置**：spec-001 的 Spec、Plan、Task、审查与验证记录都留在根任务目录下，不搬进 `attempts/spec-001-…/`。搬的话，已提交的 28 个 TASK 文件与各 closure 记录里的相对引用全部失效。子路由决定用「Artifact path overrides」指向这些文件。
2. **Spec 基准早于根映射提交**：spec-001 的真实基准是 `a02bce46`，而根映射提交要在改道生效时才产生（`06ca5e0f` 之后）。回滚 spec-001 时，要回到 `a02bce46`，不是回到映射提交。
3. **根级提交落在 spec-001 的提交区间里**：改道生效时的根级提交（根契约当前 revision、改道记录、映射、子契约 A 与它的子路由决定），会落在 `a02bce46` 到 spec-001 封存提交之间。`controller-validation.md`「Derived progress consistency」第 4 条会把它报成 `foreign-commit`。

此外，有两件事在这次提案里刻意没做：
- **没有创建 `multi-spec-map.md`，也没有写子路由决定。** 按 `using-rks-agent` 第 8 步，根契约 rev 3 审查通过之前，路由与工作区设置都要失败关闭；子路由决定也要等子契约审查通过（激活顺序第 4、5 步）。
- **子契约 A 已经写在确定路径上。** 激活顺序允许「子契约与路由决定都已写好、只缺映射行」时补完激活，所以早写不会让 phase 推导出错。结果 B 的草案不能这样放：在 spec-001 封存之前，`attempts/` 下第二个没有映射行的子目录会让推导第 7 条与第 4 条冲突。

## 6. 对已提交证据的影响

无。

- `a02bce46..06ca5e0f` 共 31 个提交（规划 `b76504bb`；修订 `c3115f9a`（RQ-091 用户修订）、`5f7dba11`（Plan T-004 行）；单元 TASK-01～TASK-28），提交信息都以 `serial-gameplay-executor:` 开头，在 Plan 上都落在 T-001～T-006、M-1、M-2 之内，没有一个属于 T-008。
- 已提交文件中写着「交 T-008」「归 T-008」的去向，按 §4 与 `spec.md` R3-1 的对照读，文件本身不改。
- 已记为「满足」的验收不因改道而变。已记为「未满足、归 T-008」的只有 AC-009 的附录 H 最终对账（`t005-closure.md` §4、§7；`t006-closure.md` §9 引用），按 R3-1 拆分：属于 A 的行已在 T-005 对账，其余移交子 Spec B。
- spec-001 的验收边界是 rev 2 边界的子集，所以已提交的 TASK 证据对 A 仍然有效，不需要重做。

## 7. 需要的独立审查

本机没有安装 `reviewing-task-contracts`。沿用 r1、r2 的做法：新上下文的独立子代理，按 `reviewing-work` 的交付物标准，加 `modeling-task-contracts` 的 Create or Revise 第 5～10 步与 Readiness 检查。

| 顺序 | 审查对象 | 判据要点 | 通过后 |
|---|---|---|---|
| 1 | 根契约 `TaskContract.md` revision 3 | 来源意图一条不少，每条恰好一次；A／B 的边界可独立验收；`preserved_upstream` 的用法；SC-12、SC-13 不是越权的机制约束；Scope Out 交叉；Requires Confirmation 与授权一致 | `routing-decision.md` 改道记录生效（写审查路径与 `target_revision: 3`）；创建 `multi-spec-map.md` |
| 2 | 子契约 A（`attempts/spec-001-…/TaskContract.md` revision 1） | 只承接 A；RI-2 以 `preserved_upstream` 指向根契约是否合规；SC 摘要与根契约同号行一致；收编偏离（§5）是否可接受 | 写 spec-001 的子路由决定（沿用 `sdd-governed` ＋ `long-running`，加 `Parent controller` 与路径覆盖）；映射加 spec-001 一行 |
| 3 | `spec.md` 修订记录 R3 与 `plan.md` 修订记录 R3（Spec／Plan 修订审查，可以合成一次） | A 的验收边界收窄后仍然完整、一致：移交项没有漏到两边都不管，跨 A／B 的拆法可验收，T-009 新范围覆盖 A 的全部剩余验收；行内标记与修订记录一致 | 去掉「提案」，以修订提交落盘，开始 T-007 |

B 的子契约在 spec-001 封存之后另审，不在这一轮。

## 8. 待主控方裁定

1. **结构：Multi Spec，还是两个根任务。** Multi Spec 下，子 Spec 共用一个任务分支，根任务要在全部子 Spec 完成之后才收尾（`finishing-a-development-branch`）。所以，「A 审核通过就推送、开 PR、合并」与 Multi Spec 不兼容；按 AGENTS.md 的分支收尾流程，合并后还要删分支，B 就没有分支可用了。如果 A 要先收尾，更顺的结构是两个根任务：
   - 本任务收窄为 A，仍走 Single Spec，加修订记录；
   - B 另立根任务 `.rks-agent/tasks/<b-id>/TaskContract.md`；
   - 本契约的 RI-2 改为 `preserved_upstream`，指向 B 的根契约；
   - 这样 §5 的三处偏离都不存在。

   **建议**：先问用户 §9 第 1 条。用户如果要 A 先收尾，就改为两个根任务，改动面就是上面四点，现有草案大部分可以复用；如果 A 审核之后仍按 RI-12 等 B 做完再一起推送，就按本提案走 Multi Spec。
2. **spec-001 的收编方式（§5 第 1、2 条）。** **建议**：产物原地沿用，用路径覆盖指向；Spec 基准记 `a02bce46`，并在映射的 Multi-Spec Boundary 一节写明这是收编，不是新建模。
3. **根级改道提交落在 spec-001 区间里（§5 第 3 条）。** **建议**：
   - 把改道分成两个提交：①根级提交（根契约 rev 3、改道记录生效、映射、子契约 A、子路由决定 A）；②spec-001 的修订提交（`spec.md`、`plan.md`、`follow-ups.md` 去掉「提案」）。
   - 在映射里把①登记为收编时的已知例外，注明提交号，供 validation 第 4 条比对。
   - 不要为了让 validation 通过而改写历史。
4. **子契约 B 草案的位置。** **建议**：保持在 `verification/` 下，spec-001 封存后由控制器按激活顺序落到 `attempts/spec-002-protocol-level-cmd/`。
5. **B 什么时候开始。**（已由文末「主控方裁定」第 5 条及其「主控方处置」更正定下：B 在 A 封存后即开始，外部审核对整个分支在最后一次进行。）如果 A 封存后马上开始 B，而外部审核又对 A 提出要改的地方：A 已封存、不可重开，修正只能作为一个新的子 Spec，或者并进 B，会与 B 的归并交错。**建议**：等 GPT Astra 对 A 的结论出来再开始 B；结论要求修改时，先做一个对账子 Spec，再开始 B。
6. **§4 中可上可下的判定。** 包括 F-07（B，还是后续性能优化）、F-30／F-31（T-009，还是 T-007）、F-86 与 F-110（B，还是 A）、F-87 改名（B，还是 A）、F-111（A，还是 B）。**建议**：按 §4 的判定；F-87 例外，如果希望外部审核看到的 A 里没有提及已删除门的用例名，改为留在 A、归 T-009，代价只是改名，不改断言。
7. **SC-11 的「规划停点汇报」没有落盘记录。** 任务目录内找不到 rev 2 规划停点向用户列明三项默示决定的独立记录。**建议**：接受 rev 3 的写法，在 A 的最终汇报里复述这三项决定，不追补历史记录。
8. **审查的组织（§7）。** **建议**：三轮按顺序、各自出结论文件，放在 `reviews/` 下：`task-contract-review-r3.md`、`attempts/spec-001-…/reviews/` 下的子契约审查（或同放 `reviews/` 并在文件名标 spec-001）、`spec-plan-amendment-r3-review.md`。不重做对抗挑战：A 的边界只是收窄，没有新增需求；可以在第 3 轮的审查要点里要求专门找「两边都不管」的漏项。

## 9. 需要用户本人确认的问题

这些问题不在主控方被授权的范围内：用户的原话或会话约定直接涉及它们，或者要由用户本人去做。

1. **GPT Astra 审核的对象与收尾时机。**（已由用户 2026-10-23 决定：同一分支、一次收尾，见文末。）用户说过“结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。”改道之后，第一次送审只包含结果 A，送审时 B 还没做。要请用户确认两件事：
   - 是否接受先审 A、B 完成后再审 B；
   - A 审核通过之后，是马上收尾（推送、开 PR、合并），还是等 B 完成后一起收尾。马上收尾会改变会话约定「本分支全部完成后一次性推送」（RI-12），也意味着要两个 PR、版本号按 AGENTS.md 递增两次（0.1.x 各一次）。

   **建议**：主控方带着 §8 第 1 条的两种结构去问；用户没有表态之前，按 RI-12 不推送。
2. **改道本身的知会。** 改道在用户授权范围内（“你来做主吧”），不需要用户批准。但它改变了用户会收到的东西：交付分成两次，审核分成两次。**建议**：在下一次向用户汇报时，用一两句话说明，与第 1 条一起问。

## 主控方裁定（2026-10-23）

**用户的决定**：「继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出」。据此，待裁定第 1 条定为 **Multi Spec**。

- 结果 A 与结果 B 是两个独立验收的子 Spec，都在 `task/refactor-session-20261021` 上完成。
- 收尾时只推送一次，A 不单独开 PR。
- 收尾仍要等 GPT Astra 审核通过。

其余待裁定：

2. **spec-001 收编进来，原地沿用。** 用路径覆盖指向根目录下的现有产物，并在映射中写明这是收编、Spec 基准为 `a02bce46`。接受。
3. **改道提交拆成两个。** 一个是根级改道提交，另一个是 spec-001 修订提交。前者在映射中登记为已知的 `foreign-commit` 例外，不改写历史。接受。
4. **B 草案暂留 `verification/`。** A 封存后再落到 `attempts/`。接受。
5. **B 在 A 封存后开始。** 不等 Astra 对 A 的结论，因为两者在同一分支、一次收尾，Astra 审核放在最后。A 封存时向用户汇报一次；如果用户想先让 Astra 看 A，B 可以照常推进。〔本句原有的「Astra 意见的承接方式」一半已作废，见下文「主控方处置」的更正：封存后的 A 不重开，意见作为新的修正子 Spec 或并进 B。〕
6. **follow-ups 按表执行。** 只有 F-87 改为留在 A，归 T-009，这样外部审核看到的 A 不带过时的用例名。
7. **rev 2 规划停点的三项默示决定**，在 A 的最终汇报中复述。接受。
8. **审查分三轮**：根契约 rev 3、子契约 A、Spec 与 Plan 的 R3 修订，各出一个结论文件。第 3 轮专门找「A、B 两边都不管」的漏项。接受。

按裁定第 6 条，§4 合计改为：子 Spec B 11 项（F-07、F-12、F-34、F-58、F-86、F-88、F-93、F-109、F-110、F-112、F-122）；留在 A 7 项（F-30、F-31、F-32、F-33、F-87、F-98、F-111）；两边各自续写 1 项（F-69）；不改 1 项（F-94）。11 ＋ 7 ＋ 1 ＋ 1 ＝ 20。

## 主控方处置（三路审查第 1 轮之后，2026-10-23）

三路审查都是 `changes_required`，问题都能就地修正（`reviews/task-contract-review-r3.md`、`spec-001-contract-review.md`、`spec-plan-r3-review.md`）。

**更正裁定第 5 条**：A 封存后不能重开，所以「Astra 发现的问题在 A 上追加修正提交」作废，改为：
- Astra 审核放在 B 完成之后一次进行，审核对象是整个分支，A 的最终汇报是审核的输入之一。
- 如果用户先让 Astra 看 A，A 也照常封存，B 照常推进。Astra 对 A 的意见作为一个新的修正子 Spec 处理（编号接在 B 之后），或者在不跨边界的前提下并进 B，由主控方届时判定。
- 不论哪种情况，都只在 A、B 及修正子 Spec 全部封存、且 Astra 审核通过之后，才推送、开 PR 和收尾。

修正要求如下：
1. **根契约升到 revision 4**：
   - F-1：在 Context 与 RI-14 中补上用户 2026-10-23 的原话；改正两处过时的理由；RI-16 的理由与本处置一致；Requires Confirmation 第 2 项改为「A、B 都封存且 Astra 审核通过后，才推送、开 PR、合并和收尾，只推一次，A 不单独开 PR；这些动作要等用户按 AGENTS.md 明确要求『分支收尾』后才做」。
   - F-2：在 SC-8B 中写明 B 封存时，A 的 SC-1、SC-3～SC-7 仍然成立，对应判据与测试的断言不放宽。
   - SC-11 的证据注明「以 A 最终汇报中的复述为证据（裁定第 7 条）」。
2. **子契约 A 升到 revision 2**：
   - 补 RI-13 一行（`authorized_change`，Goal；SC-4）；
   - RI-16 的理由按本处置改写；
   - Context 引用 10-23 的原话；
   - SC-11 照引根契约；
   - N-2、N-3 一并改；
   - N-1（排除项）写进子路由决定的 Artifact path overrides。
3. **Spec／Plan／follow-ups**：
   - M-1：`follow-ups.md` 第 17 行的列举改正，Plan T-009 的清单补上 F-87；
   - M-2：F-110 的标记补上 A 的一半，Plan T-009 的文档项补上 F-110、F-122；
   - M-3：B 草案的 Current Situation 补上对 §4、合计更正与转 B 条目的引用，并点明 F-88、F-93 要先补守护用例；
   - L-1～L-4 一并处理（R3-4 写明 SC-11、SC-12 按子契约验收；L-2 写明 TASK-26 已补的发送器反例计入 A 的 AC-018 对账；补全对账表；EV 项与标题加注）；
   - 去掉多出的那个行尾空格。
4. **`routing-decision.md`**：改道理由中「A 的外部审核与收尾要等 B」一句，按用户的决定改写。
5. **B 草案**：RI-16 同步改写。

改完后对三份审查各做一次定点复审。

## spec-001 子路由决定的待落地内容（子契约审查 N-1）

子路由决定 `attempts/spec-001-executor-outbox-lock-removal/routing-decision.md` 要等子契约 A 审查通过后才写，下面是届时要写进它「Execution Configuration」的内容，先记在这里。

- Profile、控制器、门：照抄 `routing-decision.md` 上半部分的 Controller Selection 与 Execution Configuration（`sdd-governed` ＋ `long-running`，Spec 基准 `a02bce46`）。
- `Parent controller`：`../../multi-spec-map.md`。
- **Artifact path overrides**：

| 键 | 路径 | 排除 |
|---|---|---|
| spec | `../../spec.md` | — |
| plan | `../../plan.md` | — |
| tasks | `../../tasks/` | — |
| reviews | `../../reviews/` | 根契约的审查：`task-contract-review.md`、`task-contract-review-r2.md`、`task-contract-review-r3.md`，以及之后的根契约审查 |
| verification | `../../verification/` | `reroute-t008.md`（根级改道汇总）、`reroute-t008-spec-002-draft.md`（B 的草案，A 封存后移出） |
| result | `./spec-result.md`（本子目录内，封存时新建） | — |

- 不属于 spec-001 的根级文件，即使与它的产物同处一个目录，也不在覆盖范围内：
  - `../../TaskContract.md`、`../../routing-decision.md`、`../../multi-spec-map.md`；
  - `../../research/`，这是根级输入，只读引用。
- A 封存之后：
  - B 新产生的跟进事项记在 `attempts/spec-002-…/` 下，不改 A 的 `../../verification/follow-ups.md`；
  - 移交给 B 的条目，B 在自己的记录里引用编号并记录关闭；
  - `../../reviews/` 下 spec-001 的审查文件，B 不改。

## 第 1 轮审查发现的落点（2026-10-23，按「主控方处置」第 1～5 条修改）

### `reviews/task-contract-review-r3.md`（根契约）

| 发现 | 落点 |
|---|---|
| F-1 | `TaskContract.md` revision 4，分五处：<br>① Context「revision 3」一段：依据由两条改为三条，新增第 2 条，即用户 10-23 原话与其含义（Multi Spec、同一分支、一次收尾、A 不单独推送或开 PR、Astra 默认最后审、先看 A 时的承接方式）；<br>② 第 1 条的两处过时理由改正：删去「交外部审核；审核通过前不推送、不开 PR」，「外部审核与分支收尾都要等 T-008」改为「A 的验收与封存要等 T-008」；<br>③ 新增「revision 4」一段；<br>④ RI-14 的来源加入 10-23 原话，落点加 Requires Confirmation 第 2 项；RI-16 的理由按处置改写；RI-12 的理由补一句再次确认；<br>⑤ Requires Confirmation 第 2 项按处置改写；Constraints「先 A 后 B」补「不等外部审核；A 封存之后不重开」 |
| F-2 | SC-8B：写明 A 的 SC-1、SC-3～SC-7 在 B 封存时仍成立、断言不放宽；证据加「逐条对照」 |
| 残余风险：「在 A 上追加修正」 | 根契约、子契约 A、B 草案、`plan.md` 中都写为「新的修正子 Spec，或在不跨边界的前提下并进 B；封存后的 A 不重开」 |
| 残余风险：改道记录的理由 | `routing-decision.md`「理由」一节改写，并补上 10-23 原话 |

### `reviews/spec-001-contract-review.md`（子契约 A）

| 发现 | 落点 |
|---|---|
| F-1 | 子契约 A revision 2：补 RI-13 一行（`authorized_change`，Goal；SC-4）；表后一段改为「RI-10、RI-11、RI-12、RI-14」 |
| F-2 | RI-16 的理由按处置改写；Context 引用 10-23 原话；Authorization 一行补上这句原话；Constraints 第 3 条改为「不推送、不开 PR、不收尾」 |
| F-3 | 修在根契约：根 SC-11 的证据改为「A 的最终汇报；规划停点一项以其中的复述为证据（裁定第 7 条）」；子契约 SC-11 照引，摘要与证据栏同步 |
| N-1 | 本文件上一节「spec-001 子路由决定的待落地内容」；子契约 Current Situation 加一句「不属于本结果的文件由子路由决定的路径覆盖逐项排除」 |
| N-2 | 子契约 Constraints「一次到位、删净」改为：附录 F 删除栏与 A 引入后不再需要的东西在 A 内删净，其余仍在的旧机制必须进 SC-12 清单 |
| N-3 | 子契约 Scope Out 改为「包括」，并补全 B 的测试、测量、文档与迁移保留项 |

### `reviews/spec-plan-r3-review.md`（Spec／Plan／follow-ups）

| 发现 | 落点 |
|---|---|
| M-1 | `follow-ups.md` 文首 R3 说明（第 17 行）：F-87 移入「留在 A、归 T-009」，注明依据是裁定第 6 条；`plan.md` R3 的 T-009 follow-ups 清单补 F-87，并注明改名计入 RQ-083；本文件 §4 合计改为 B 11、A 7 |
| M-2 | `follow-ups.md` F-110 的标记改为「结构保证 → 子 Spec B；A 在 T-009 的文档中记为已知约定」，文首说明同步；`plan.md` R3 的 T-009 文档项补 F-110、F-122 的 A 一半；本文件 §4 F-110 行的判定同步 |
| M-3 | B 草案 Current Situation 补：11 项 follow-ups 的清单与三处依据，F-88、F-93 先补守护用例，F-110 只有结构保证一半归 B |
| L-1 | `spec.md` 新增 R3-4：根 SC-11、SC-12 直接按子契约与根契约同号行验收，写明证据 |
| L-2 | `spec.md` R3-1 RQ-071 行：TASK-26（`8c531c9b`）在 `CommandHandlerArchitectureTests` 补的发送器反例计入 A 的 AC-018 对账，B 在其上重写 |
| L-3 | `plan.md` R3：Verification 段补 AC-016、AC-009 的 A 一半、AC-019；T-009 覆盖补 RQ-080 |
| L-4 | `spec.md` R3-1 表尾补 EV-1～EV-3、EV-5 随 AC 留在 A，EV-6 随 AC-022 拆分；R3「生效时要做的改动」加「标题后加注『（spec-001：结果 A）』」 |
| 行尾空格 | `spec.md` 第 66 行 `### C. 协议 CMD` 与标记之间的空格去掉，去掉标记后与 HEAD 逐字相同 |
| 残余风险：「在 A 上追加修正」 | 同根契约一行 |

### 另外随之同步的地方

- `routing-decision.md` 改道记录：
  - 状态与绑定改为 revision 4；
  - 再改道条件中「外部审核否决把 A 作为独立审核对象」改为「Astra 对 A 的意见既不能作为修正子 Spec 承接，也不能并进 B」；
  - 偏离一节改为「已由裁定第 2、3 条接受」；
  - spec-001 一条指向上一节的路径覆盖待落地内容。
- B 草案：
  - 引用根契约 revision 4 与 10-23 原话；
  - SC-8B 摘要同步 F-2；
  - 表后一段把 RI-13 从「由根契约承担」中移出（它由 A 交付）。
- `spec.md`、`plan.md` R3 的「起因」「生效条件」改为根契约 revision 4、子契约 A revision 2。
- `plan.md` R3「Next Planning Boundary 的修订」与 T-009「为什么单列」：删去「T-009 封存之后送外部审核」，改为 A 封存时向用户汇报一次，B 随后开始，外部审核在最后对整个分支一次进行。
- 本文件文末「主控方裁定」第 5 条中「Astra 发现的问题在 A 上追加修正提交」一句，按处置的更正改为作废标注（原意由「主控方处置」取代）。

## 生效（2026-10-23）

三份审查都已通过：
- 根契约 revision 4：`reviews/task-contract-review-r3.md` Round 2；
- 子契约 A revision 2：`reviews/spec-001-contract-review.md` Round 2；
- Spec／Plan 修订 R3：`reviews/spec-plan-r3-review.md` Round 3。

按 `multi-spec-workflow` 做了以下生效步骤（均未提交）：

1. `routing-decision.md` 的改道记录改为「已生效」，并绑定三份审查及各自的 revision。原 Single Spec 决定作废，只保留为历史。
2. 新建 `multi-spec-map.md`：
   - `workspace` 填入规范化路径，`baseline` 为 `a02bce46` 的完整 SHA；
   - spec-001 一行的 `Result` 与 `Git result` 为空，表示它是活动子 Spec；
   - Multi-Spec Boundary 登记三件事：收编、根级改道提交的 `foreign-commit` 例外、spec-002 待激活；
   - SC 进度全部为 `not_started`，因为还没有已封存的子 Spec。
3. 新建 `attempts/spec-001-executor-outbox-lock-removal/routing-decision.md`：
   - 显式写出子契约审查的路径与 `target_revision: 2`；
   - 沿用 `sdd-governed` ＋ `long-running`（`attention_review: on-drift`；Plan 为 task-units），门的配置照搬原 Execution Configuration；
   - Spec 基准为 `a02bce46`；
   - Artifact path overrides 照上文「spec-001 子路由决定的待落地内容」落地；
   - `Parent controller` 为 `../../multi-spec-map.md`。
4. `spec.md`：
   - frontmatter 的 `contract` 改指子契约 A（revision 2）；
   - 标题加注「（spec-001：结果 A）」；
   - 31 处标记改为「〔改道 R3：」；
   - 修订记录 R3 标为已生效，并加一条「生效」说明。

   `plan.md` 的 13 处标记与 T-008 行，以及 `verification/follow-ups.md` 的 18 处标记、文首说明与 F-122，都同样处理。实质内容都没有改。
5. 子契约 A 的 RI-16 补上「在不跨边界的前提下」，与根契约对齐。这是子契约审查 Round 2 非阻断残余第 2 条，只补措辞，不改含义，所以不递增 revision。

本节之上 §1～§9 与「第 1 轮审查发现的落点」都是送审时的过程稿，按 AGENTS.md「过期的记载怎么办」保留原文。
