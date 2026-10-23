---
kind: rks-task-contract
revision: 2
state: ready
parent: ../../TaskContract.md
contributes_to: [SC-1, SC-3, SC-4, SC-5, SC-6, SC-7, SC-8, SC-9, SC-10, SC-11, SC-12]
depends_on: []
---

# TaskContract: 结果 A——单执行器、提交后发件箱与删锁

## Context

### Source Request

本契约是根契约 `../../TaskContract.md`（revision 4）的结果 A。来源请求、用户原话、授权与术语以根契约 Context 为准，不在这里重抄。

拆分的依据是两句用户原话：

- 2026-10-22，用户授权主控方：“你来做主吧，结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。”主控方据此提出拆分。
- 2026-10-23，用户本人决定：“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出”。

据此，根契约定为 Multi Spec：A 与 B 两个子 Spec 都在 `task/refactor-session-20261021` 上完成，A、B 都封存之后一次收尾。本契约承接的是 A。

revision 2（2026-10-23）吸收子契约审查 `../../reviews/spec-001-contract-review.md` 的发现：
- F-1：补 RI-13；
- F-2：RI-16 按用户 10-23 的决定改写；
- F-3：SC-11 照引根契约 revision 4；
- N-2、N-3：改 Constraints 与 Scope Out。

N-1（路径覆盖中的排除项）属于子路由决定，待落地内容见 `../../verification/reroute-t008.md`。

### Current Situation

A 不是新开的工作。它就是改道前按 Single Spec 执行的那份 Spec 去掉协议级 CMD 归并（Plan T-008）之后的部分：

- Spec 基准 `a02bce46`；规划检查点 `b76504bb`；TASK-01～TASK-28 已提交，HEAD `06ca5e0f`；
- Plan 的 T-001～T-006 与测量闸 M-1、M-2 已完成；剩下 T-007（调用链贯通与深度上限）与 T-009（收口）；
- Spec、Plan、Task 文件、审查与验证记录都在根任务目录下（`../../spec.md`、`../../plan.md`、`../../tasks/`、`../../reviews/`、`../../verification/`）。按改道提案，它们原地沿用，不搬进本目录。这几个目录中不属于本结果的文件，由子路由决定的路径覆盖逐项排除。

## Goal

执行层面上，服务端的玩法只有一个串行单位：进程内唯一执行器上的一个作业。每个入口对应一个作业，包括一个入站协议消息、一次定时触发、一次事件消费、一次连接生命周期事件、一次 GM／HTTP 写操作。

作业产生的出站消息先进发件箱，相关事务提交之后才刷进每连接的发送队列，执行器不等待网络发送。玩法路径上没有门、信号量与「锁内复查、重试」循环。调用链与深度上限覆盖全部来源。

A 封存时仍在的过渡机制逐项列明，作为结果 B 的输入。除经审查的 Spec 逐条列出的有意变化外，协议行为、落线顺序与持久化结果不变。

## Scope

### In

根契约「In — 结果 A」的全部各项：

- 执行器与唯一入队点；
- 重计算与背压；
- 全部入口改为作业；
- 提交后发件箱与每连接发送队列；
- 依赖投递结果或同步发送异常的行为；
- 调用链与深度上限；
- 删除玩法路径上的锁；
- 保留项在今天的位置原样保留；
- A 的判据、测试、测量与文档；
- 过渡机制清单。

### Out

- 根契约「In — 结果 B」的全部各项，包括：
  - 协议级 CMD：handler 只解包入队，细粒度命令并入、改成应用服务或删除；
  - 请求级上下文改为显式数据，管线非锁职责的迁移；
  - 迁移保留项与工作单元对照；
  - 删净过渡机制；
  - B 的判据：「handler 只解包入队」「协议级 CMD 注入面」「每个入站协议一个协议 CMD」，以及 A 期间未改写、与协议级 CMD 冲突的既有判据；
  - B 的测试：改写「假总线 + 直接 new handler」一类用例，在 B 封存状态上重跑行为不变的证据；
  - B 的测量与文档。
- 根契约 Scope Out 的全部各项。

## Root Intent Coverage

| Intent ID | Source anchor and material intent | Disposition | Contract location or distinct active owner / authority | Rationale and evidence |
|---|---|---|---|---|
| RI-1 | 根 RI-1：原版不需要细粒度锁 | in_scope | Goal；SC-4 | — |
| RI-2 | 根 RI-2：命令变成唯一实体，以协议为单位 | preserved_upstream | 根契约 `../../TaskContract.md` 结果 B（SC-2、SC-13） | 根契约仍在执行，B 由它承担。本契约只交出 SC-12 的过渡机制清单作为 B 的输入，不交付 B 的结果 |
| RI-3 | 根 RI-3：生产者-消费者执行器 | in_scope | Goal；SC-1 | — |
| RI-4 | 根 RI-4：按频道分流 | authorized_change | 分流键预留，不分流（根 Scope Out 第 1 项） | 同根契约 |
| RI-5 | 根 RI-5：Event 组件保留，NOTI 归拢 | in_scope | SC-3、SC-5 | — |
| RI-6 | 根 RI-6：深度计数、超限处理、调用链日志 | in_scope | SC-6 | — |
| RI-7 | 根 RI-7：大部分锁可以扔 | in_scope | SC-4 | — |
| RI-8 | 根 RI-8：短 lock(sync) 先留 | authorized_change | 根 Scope Out 第 2 项 | 同根契约 |
| RI-9 | 根 RI-9：持久化不变 | in_scope | Scope In「保留项」；根 Scope Out 第 3 项 | — |
| RI-13 | 根 RI-13：#182 统一加锁原语、#204 两塔并发声明的死锁陷阱 | authorized_change | Goal；SC-4 | 同根契约：#182 原先的加锁原语建议由执行器方向替代，#204 的陷阱随锁一起消失，本结果的删锁证据（SC-4）就是它的证据；issue 的更新属根契约的收尾 |
| RI-15 | 根 RI-15：单条指令 ≤ 2 秒即接受 | authorized_change | SC-9 | 同根契约 |
| RI-16 | 根 RI-16：GPT Astra 审核通过后才收尾 | in_scope | SC-11；根 Requires Confirmation 第 2 项 | A 的最终汇报是外部审核的输入之一（SC-11）。审核默认在 B 完成后一次进行；用户若先让 Astra 看 A，本结果照常封存，Astra 对 A 的意见由根契约安排为新的修正子 Spec，或在不跨边界的前提下并进 B，本结果封存后不重开。审核结论不是本结果封存或 B 开始的前提；收尾门属根契约 Requires Confirmation 第 2 项，本结果不推送、不开 PR |

根契约的 RI-10、RI-11、RI-12、RI-14 管的是整个任务的流程、授权与结构（governed SDD、规划停点、一次推送、改道决定），由根契约承担，本契约不重复处置。

## Constraints

根契约 Constraints 的各项都适用于本结果。其中三条在本结果里的含义如下：

- 「一次到位、删净」：
  - 在 A 内删净的有两类：Spec 附录 F「删除」栏的各项，以及 A 引入后又不再需要的东西；
  - 其余仍在的旧机制都要进入 SC-12 清单；不在清单上的，就违反 SC-12；
  - 清单上的过渡机制不在本结果里删除。
- 「先 A 后 B」：本结果的验收不以 B 的任何工作为前提，也不以外部审核的结论为前提；封存后不重开。
- 不推送、不开 PR、不收尾：收尾只在 A、B 都封存且外部审核通过之后一次进行，见根 Requires Confirmation 第 2 项。

## Success Conditions

编号沿用根契约，条件全文与证据以根契约同号行为准；下表只写一句话的摘要，便于对照。两处不一致时以根契约为准。

| ID | Observable condition | Required evidence |
|---|---|---|
| SC-1 | 唯一执行器，全部入口作为作业经唯一入队点进入，执行器外部分逐项列明并说明安全 | 根契约 SC-1 |
| SC-3 | 全部出站消息由执行器上的工作产生、提交后发出，顺序、批次、必达、隔离与跨连接顺序成立 | 根契约 SC-3 |
| SC-4 | 玩法路径上的门、信号量、全局事务门及其管线行为与重试循环删除，其余并发原语逐项去留 | 根契约 SC-4 |
| SC-5 | 事件组件保留，Reliable、LiveOnly 与重试经执行器执行，可靠性语义不变 | 根契约 SC-5 |
| SC-6 | 深度上限、调用链日志与超限处理覆盖全部 CMD 来源 | 根契约 SC-6 |
| SC-7 | 依赖投递结果或同步发送异常的每项行为都有承接方式，可见变化逐条列出 | 根契约 SC-7 |
| SC-8 | A 封存状态下构建与全量测试、线级与表征断言、选角端到端证据成立 | 根契约 SC-8 |
| SC-9 | A 封存状态下有实测数字与口径，单条 CMD ≤ 2 秒即接受 | 根契约 SC-9 |
| SC-10 | 文档与 A 封存状态一致，过渡机制写成今天的状态与已知缺口 | 根契约 SC-10 |
| SC-11 | A 的最终汇报列出结果、可见变化、遗留问题、过渡机制清单与验证结论，复述 rev 2 规划停点的三项默示决定，作为外部审核的输入之一 | 根契约 SC-11：A 的最终汇报；规划停点一项以汇报中的复述为证据 |
| SC-12 | 过渡机制逐项列明，清单之外没有残留 | 根契约 SC-12 |

## Authorization

| Capability | Evidence | Authorized scope |
|---|---|---|
| task | 沿用根契约 Authorization 一行（用户原话：“看着没问题了，模型也简化了很多。不过这个改动比较危险，走gov的sdd路线吧。……靠你了，opus”），以及 2026-10-22 的执行中授权“你来做主吧，结束后我会找gpt astra单独做一遍审核。那边过了我们才收尾。”与 2026-10-23 的结构决定“继续吧，在同一个分支干。不过可以拆成你说的另一个spec单出” | 根契约授权范围内的结果 A：实施、验证及其检查点提交。不含合并、推送、PR、远端 issue 修改、部署或清理工作区 |

## Assumptions

- 根契约 Assumptions 的三条适用于本结果。
- 改道前已提交的 TASK-01～TASK-28 都属于本结果；其证据原样有效，不需要重做。依据：`a02bce46..06ca5e0f` 的 31 个提交都是 `serial-gameplay-executor:` 的检查点（规划 1、修订 2、单元 28）；在 Plan 上，它们都落在 T-001～T-006、M-1、M-2 之内。
