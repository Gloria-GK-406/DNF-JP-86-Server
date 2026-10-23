# TASK-33 — T-009d：文档与附录最终对账（只改文档与 Spec 附录）

代码冻结点 `2bdb1afd`（TASK-31／T-009b 的提交）。本单元是 `tasks/TASK-30.md` §0 拆分中的第四个单元，边界提纲见 TASK-30 §0.2 的 TASK-33 一行，依据去向见 §0.3。本单元不改 `Server/`、`Tool/`，不构建、不跑测试；它的产出只有文档、Spec 附录的事实性修订与已授权的 VC-10，以及一份对账记录。

与 TASK-30 §0.2 提纲的差别：
- 提纲第 ⑦ 项（VC-10）已定：TASK-30「主控方裁定（执行前）」第 3 条取 A，本单元写入。
- F-116 的 (c) 与 F-118 是同一条约定（TASK-30 裁定第 4 条），合成一处写（§2.3 第 13 行）。
- 新增三项：F-125 进 AGENTS.md 的偶发清单（TASK-31 裁定第 6 条）；`t009-criteria.md` §10 的 F5（`CharacterContainerDomainService` 登记参与者却按读归类，是有意例外），在附录 F 对账里写明；TASK-31 改过的事实（F-33 的缓冲上限、执行器失败日志、发送端日志级别）要进附录 B 对账与 AGENTS.md（`t009-logs-and-cases.md` §8 交本单元判断，判断见 §2.2 第 8 条）。

范围取自：
- `tasks/TASK-30.md` §0.2 TASK-33 一行的 ①～⑧ 与 §0.3 分配表；TASK-30 主控方裁定第 3、4 条；
- `spec.md` RQ-100、RQ-024、AC-006、AC-009、AC-014、AC-017、AC-022，修订记录 R3-1 中 RQ-024、RQ-100、AC-009、AC-022、附录 H 各行的 A 一半，R3-2、R3-3；附录 A、B、E、F、G、H；
- `plan.md` 修订记录 R3 中 T-009 的「文档」「附录 A、B、E、F 的最终对账」「AC-009 的 A 一半」「RQ-024 的 A 一半」四项；根契约 SC-10；
- `verification/follow-ups.md` 中归 T-009／TASK-33 的 F-35、F-36（AGENTS.md 一半）、F-89、F-92、F-98、F-99、F-105、F-110（A 一半）、F-116（(c) 一半）、F-118、F-122（A 一半）、F-125（AGENTS.md 一半）；
- `t007-closure.md` §6（附录 A 第 3、4、6、8 行与两个新系统 CMD 类型的最终对账）；`t009-criteria.md` §9、§10（F5）；`t009-logs-and-cases.md` §8；`t004-closure.md`～`t007-closure.md` 中交 T-009 的文档项；
- TASK-32 的 Result 与 Remaining 中交给本单元的事项（规划时 TASK-32 仍在执行，见 V2）。

本单元的产出：
- `AGENTS.md` 的执行模型现状图（RQ-100 的 A 一半，根 SC-10）：新增「玩法执行器、发件箱与发送端」一节，改掉十余处过期段落；仍在的 handler 编排写法与过渡机制写成今天的状态与已知缺口（§2）。
- `Docs/Agent/ArchitecturePhilosophy.md` §1、§5 中关于执行与锁的部分（§3）。
- `spec.md`：附录 A、B 的事实性修订（F-99、F-33 等），附录 G 新增 VC-10（F-98），以及修订记录 R4；附录 E、F、H 只在核对出事实错误时改（§4）。
- `verification/entry-reconciliation.md` 开头补落地状态（F-99，§5）。
- 新建对账记录 `verification/t009-docs-and-appendices.md`：附录 A、B、E、F、H（A 的各行）的最终对账，RQ-024 的 A 一半，AC-009 的 ①②④，AC-022 的 A 一半，以及交给 TASK-34 的移交清单（§6～§10）。
- `verification/follow-ups.md`：关闭或更新上列 F 编号，文首补 TASK-33 一行（§9.2）。

输入：
- 冻结点代码 `2bdb1afd`；基线 `a02bce46`（A 的起点，RQ-024 与 AC-009 的对照基准）；
- `logs/task-31-build-test.txt`（冻结点之前最后一次正式全量，AC-009① 的「通过」依据，见待裁定第 6 条）；
- 脚本：scratchpad `t07/diffcheck7.py`、`t21/methods.py`、`t31/closure_refs.py`、`t31/names.py`（26 个改名的新旧对照）；缺失时从 `logs/task-31-*.txt` 末尾附的全文恢复；
- 以往的对账：`entry-reconciliation.md` §2（附录 A／B）、`t005-closure.md` §2～§4（附录 F 删除与保留、附录 H 第 45／58 行）、`t006-closure.md` §2～§4（附录 F 投递三项、附录 E 十七行、附录 G）、`t007-closure.md` §1～§2（调用链）、`t009-criteria.md` §1～§3、§7（RQ-061、普查、附录 F 名单）、`t009-logs-and-cases.md` §5、§6（AC-009③、F-33）。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **`AGENTS.md` 现状图**（§2；RQ-100 的 A 一半、根 SC-10；F-35、F-36 的 AGENTS.md 一半、F-89、F-92、F-105、F-110 的 A 一半、F-116 的 (c)／F-118、F-122 的 A 一半、F-125）：按 §2.3 的逐节改法直接改掉过期段落；新增执行器一节（§2.4）；过渡机制逐项写成「今天的状态与已知缺口」，不把子 Spec B 的目标写成今天的事实（§2.5）。文中不再出现 A 删除的类型名与机制名（AC-022 的 A 一半，§8）。
  2. **`Docs/Agent/ArchitecturePhilosophy.md`**（§3；RQ-100）：§1 的走向图补上执行器与发件箱，§5 中关于门与管线的两条按冻结点改写；文件头补一行改写记录。
  3. **Spec 附录**（§4）：附录 A 第 3、4、6、8 行与两个新系统 CMD 类型（F-99；`t007-closure.md` §6）；附录 B 按冻结点核对出的事实错误（规划时已知两处：F-33 的缓冲上限、`IGameplayQueue` 持有者的检索数）；附录 G 新增 VC-10（F-98；TASK-30 裁定第 3 条）；附录 E、F、H 只改核对出的事实错误。每一处改动在修订记录 R4 里写一行。RQ、AC 与 Spec 正文不改。
  4. **`entry-reconciliation.md` 落地状态**（§5；F-99）：开头补一段，原文不改。
  5. **附录最终对账**（§6；AC-006 的附录 A／B 一半、AC-014、AC-017、AC-009④）：附录 A 每行「来源 → CMD 类型 → 冻结点上的证据」；附录 B 每行「安全理由 → 判据或检索」；附录 E 十七行各自的测试在冻结点上存在；附录 F「删除」栏检索为零、「保留」栏仍在、「改为执行器内访问」名单出处与判据，F5 作为有意例外写明；附录 H 中属于 A 的三行（dispatcher、45 的取锁部分、0）。
  6. **RQ-024 的 A 一半**（§7）：A 期间（`a02bce46` → `2bdb1afd`）有没有合并或拆分提交点；有则逐条写理由。
  7. **AC-009 的 ①②**（§8.1）：具名判定、效果账本、会话隔离、登录顶替、槽位边界五类既有测试的清单，逐条核对断言在冻结点上不变，改写的逐条指到处置记录；`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 的断言行多重集合不变。
  8. **AC-022 的 A 一半**（§8.2）：A 删除的类型名与机制名在现状文档中检索为零，其余命中逐个归类。
  9. **收口**（§9）：新建 `verification/t009-docs-and-appendices.md`；`follow-ups.md` 关闭 F-35、F-36、F-89、F-92、F-98、F-99、F-105、F-118，更新 F-110、F-116、F-122、F-125 的 A 一半，文首补 TASK-33 一行。
  10. **移交 TASK-34**（§10）：AGENTS.md 中写为过渡态的每一项（SC-12 清单要与它一致）；F-116 的 (a)(b)、F-118 是否设判据、F-125 的生产缺口；本单元发现、不在本单元修的事实错误。
- Owned boundary：
  - **`AGENTS.md`**：§2.3 表列出的各节；新增 §2.4 的一节。表外各节（开发与发布约定、仓库布局、角色、物品与时装、记什么不记什么、仓库边界等）不改。
  - **`Docs/Agent/ArchitecturePhilosophy.md`**：文件头的引用块、§1、§5。§2～§4 不改。
  - **`.rks-agent/tasks/serial-gameplay-executor/spec.md`**：附录 A、B、E、F、G、H 中 §4 列出（或执行时核对出、在 R4 中登记）的行；附录 G 新增 VC-10 一行；修订记录新增 R4 一节。frontmatter、Definitions、Requirements、Acceptance Criteria、Success Evidence、附录 C、D 与 R3 各节不改。
  - **`verification/entry-reconciliation.md`**：只在开头加一段落地状态。
  - **新建** `verification/t009-docs-and-appendices.md`；**改** `verification/follow-ups.md`（§9.2 列出的行与文首一行）；`verification/logs/task-33-*.txt`。一次性脚本放 scratchpad `t33/`，全文附在对应日志末尾。
  - **不改**：`Server/`、`Tool/`、`protocol/`；`plan.md`、`TaskContract.md`、`multi-spec-map.md`、子契约；`Docs/` 的其他文件（含 `Docs/Agent/RefactorPreferences.md`）与 `Document/`；以往单元的收口文件（`t00x-closure.md`、`t009-criteria.md`、`t009-logs-and-cases.md`）、处置记录、`measurement/` 与 `tasks/TASK-0x～TASK-32.md`。
- Required checks（全部是检索与比对，不构建、不跑测试）：
  1. **冻结核对**（`logs/task-33-verify.txt` 开头与 `logs/task-33-boundary.txt` 末尾各一次）：`git diff 2bdb1afd -- Server Tool` 为空；`git status --short` 中 `Server/`、`Tool/` 无条目。
  2. **核实**（`logs/task-33-verify.txt`）：§1 的 V1～V20 逐条记命令与结果；与规划结果不一致的，按 §1 末的规则处理。
  3. **A 删除的名字**（`logs/task-33-deleted-names.txt`，§8.2）：名字清单的来源与加法；每个名字在 `Server/src` 冻结点上 0 处；改完后在 `AGENTS.md`、`Docs/Agent/*.md` 中 0 处；在其余文档中的命中逐个归类（按待裁定第 2 条）。先写预期再跑。
  4. **AGENTS.md 引用核对**（`logs/task-33-agents-refs.txt`）：`git diff 2bdb1afd -- AGENTS.md Docs/Agent/ArchitecturePhilosophy.md` 新增行中反引号里的每个标识符、路径与 `文件:行`，在冻结点代码或仓库中都找得到；`文件:行` 逐个打印该行内容供人工核对；找不到的为 0，或逐条说明（例如作业类型名的模式串，核对其前缀字符串在代码中存在）。
  5. **AGENTS.md 自身规矩**（`logs/task-33-retrievals.txt`）：`AGENTS.md` 的新增行中，`.rks-agent`、`本轮`、`这一轮`、`F-[0-9]`、`TASK-[0-9]`、`T-00[0-9]`、`RQ-[0-9]`、`AC-0`、`VC-[0-9]`、`SC-[0-9]`、`spec-00`、`子 Spec` 均为 0 处；`CMD` 一词只用于 Game 出站 `0x01` 类别与作业类型名字符串（§2.2 第 3 条），逐处列出；新增行里的每个数都带口径（逐个列出）。
  6. **过渡态清单一致**（`logs/task-33-retrievals.txt`）：§2.5 表的每一项在 AGENTS.md 中恰有标注为过渡态的一处，且在 `t009-docs-and-appendices.md` 的移交节中列出；反过来，AGENTS.md 中凡写「过渡」的段落都在 §2.5 表里。
  7. **附录对账检索**（`logs/task-33-appendices.txt`，§6）：附录 A 每行的 CMD 类型字符串、附录 B 每行点名的类型与判据、附录 E 与附录 G 每条证据用例、附录 F 三栏、附录 H 三行，在冻结点上的检索命令与结果；证据用例名先经 `t31/names.py` 换成新名再检索。
  8. **RQ-024**（`logs/task-33-commit-points.txt`，§7）：两个提交上的命令注册（命令类型 → 执行方式）逐条比对，事务开启点比对，命令增删比对；差异逐条归类。
  9. **AC-009 ①②**（`logs/task-33-protection-tests.txt`，§8.1）：选取规则、选出的方法清单与加法、逐方法断言行多重集合比对（`a02bce46` 对 `2bdb1afd`）、差异逐条指到处置记录；每个方法在 `logs/task-31-build-test.txt` 中为通过。
  10. **Spec 改动边界**（`logs/task-33-boundary.txt`）：`git diff 2bdb1afd -- .rks-agent/tasks/serial-gameplay-executor/spec.md` 全文；改动的每一处在 R4 中恰有一行，R4 的每一行恰对应一处改动（逐对列出）；改动只在 Owned boundary 列出的附录行、VC-10 与 R4 一节。
  11. **边界**（`logs/task-33-boundary.txt`）：`git diff --stat 2bdb1afd` 与 `git status --short` 只含 Owned boundary 的文件；`AGENTS.md` 的改动块逐个归到 §2.3 表的一行或 §2.4；`ArchitecturePhilosophy.md` 只动文件头、§1、§5；`entry-reconciliation.md` 只在开头新增、原文逐字不变（`git diff` 中没有删除行）。
  12. **收口引用核对**（`logs/task-33-closure-refs.txt`）：`t31/closure_refs.py` 复制为 `t33/closure_refs.py`，范围改为 `t009-docs-and-appendices.md`；口径同 TASK-31 Checks 9（用例名、日志路径、F 编号都解析得到；26 个旧名只允许出现在注明「旧名」的位置）；0 处不符。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出；改动的已有文件保持 LF（规划时 V20 全为 LF），新建文件 LF。
- Dependencies：
  - TASK-31（冻结点 `2bdb1afd`）：日志面、F-33、三条保护用例与 26 个改名都已在冻结点上；
  - TASK-30（`122576d5`）：普查名单、附录 B「请求级帧（过渡）」一行与 E3 豁免；
  - TASK-32（M-3）：**已提交**，且结论为「M-3 通过」，或结论为「停止」时主控方已决定本单元照常进行。本单元改 `follow-ups.md`，TASK-32 也改它，所以必须在 TASK-32 提交之后开始；
  - **执行前提**：工作区干净（除本任务目录下未提交的本文件）；待裁定各条有裁定。
- Result review：required。
  - 本单元的产出全是文档、Spec 附录与对账记录，没有任何自动检查能证明「文档说的就是代码做的」。§Required checks 只能证明引用的名字存在、删掉的名字不在；一段语义写错（例如把过渡态写成目标结构、把发件箱的提交时机写错），只有审查能拦住。
  - `AGENTS.md` 是后续每个 agent 读的第一份文件，也是子 Spec B 的起点。执行模型写错，B 的每个单元都会在错的前提上规划。
  - 附录 G 的 VC-10 是主控方授权的 Spec 验收修订，附录 A、B 的事实性修订改的是 Spec 本身（待裁定第 1 条）；它们直接进 TASK-34 的 A 最终汇报与外部审核。
  - RQ-024、AC-009、AC-022 的对账结论由 TASK-34 的 AC 状态表照抄；这里错了，A 的封存结论跟着错。
- Task-specific authorization：
  - **本单元不构建、不运行测试**：代码已冻结；「通过」的证据取 TASK-31 的正式全量，A 封存状态下的最终全量在 TASK-34（待裁定第 6 条）。
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 冻结核对不为空，或 TASK-32 未提交、`follow-ups.md` 中有 TASK-32 未提交的改动。
    2. 某个要写进文档的事实，按冻结点代码只能如实写成一处缺陷，而这处缺陷与 Spec 的验收相冲突：附录 F「删除」栏在 `Server/src` 中检索不为零；AC-009 选出的保护测试有断言变化、且在处置记录中找不到；RQ-024 发现没有理由的提交点合并或拆分；附录 E 某行在冻结点上找不到证据用例。按停止条件回报，改代码属于返工单元或子 Spec B（TASK-31 §12）。
    3. Spec 需要改 §4 与 R4 以外的内容：RQ、AC 或 Spec 正文的文字；VC-10 以外的新可见变化；附录 E 的承接方式或附录 F 的去留发生实质变化（不只是改名或位置）。
    4. VC-10 的证据按 §4.4 组合之后仍证明不了它写的差异（待裁定第 5 条）。
    5. 子 Spec B 所属的 AGENTS.md 节（handler 业务语义规则、切片边界规则、快速开发步骤、端到端示例、命令处理器注入面）需要改的不只是 A 删除的机制名、A 改变的事实与 F-118 一条约定（待裁定第 3 条）。
    6. 需要改 Owned boundary「不改」中的任何文件。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 1. 基准核实（执行时重跑并落盘 `logs/task-33-verify.txt`）

| # | 问题 | 检索 | 规划时的结果（HEAD `2bdb1afd`） |
|---|---|---|---|
| V1 | 工作区与冻结 | `git log -3 --oneline`；`git status --short`；`git diff 2bdb1afd -- Server Tool` | HEAD `2bdb1afd`；未跟踪的只有 `tasks/TASK-32.md`（TASK-32 执行中）。执行时 HEAD 应为 TASK-32 的提交，diff 为空 |
| V2 | TASK-32 交给本单元的事项 | 读 `tasks/TASK-32.md` 的 Result、Remaining 与主控方处置；`measurement/m3-summary.md` 的结论 | 规划时未完成。TASK-32 计划第 86～87 行：通过则本单元照常开始；停止则由主控方带回用户决定。M-3 的数字不进文档 |
| V3 | A 删除的名字 | §8.2 的脚本 | 规划时手工检索 26 个名字（附录 F「删除」栏与 F-105 点名的类型）：`Server/src` 中都是 0；`AGENTS.md` 命中 13 行；`Docs/` 5 个文件、`Document/` 二十余个文件有命中 |
| V4 | `AGENTS.md` 的过期点 | `grep -n` 下列词：`UserAccessMode`、`IOnlineUserCommand`、`IMultiAccountCommand`、`UserExecutionGate`、`SelectedSessionTransitionGate`、`OnlineUserExecution`、`AccountExecution`、`TouchSocketNetworkMessageSender`、`NotificationRetry`、`PersistOnTeardownAsync`、`租约`、`lease`、`执行门`、`重试上限`、`投递期限`、`46 条`、`30 秒`、`同账号有闸` | 命中行 100～101、123、280～301、335～338、419～425、443、456～459、524～525、535、814～827；另有 F-35 的 780 行。§2.3 表按此列出 |
| V5 | 管线与执行方式 | `ls Server/src/Dfo.Server.Application/Commands/Pipeline`；各文件的 `Order`；`CommandContracts.cs` 的 `CommandExecutionKind` | 五个行为：0 `CommandLoggingBehavior`、45 `SelectedSessionExecutionBehavior`、58 `SelectedSessionSettlementBehavior`、60 `UnitOfWorkBehavior`、100 `CommandExecutionPolicyBehavior`；执行方式 Direct／Transactional／Workflow／ReadOnly。`IAccountCommand` 仍在（81 个文件），它今天的用途执行时读 45 号行为与 `CommandBus` 确认 |
| V6 | `SendAsync` 的语义 | 读 `Commands/CommandBus.cs:25-60` | `GameplayJob.IsOnExecutor` 为真时内联（`:30`）；否则入队一个作业并等待（`:36`）。执行器外调用产生的作业类型名、生产中有没有执行器外的调用方，执行时确认 |
| V7 | 执行器、发件箱、发送端 | 读 `Abstractions/Execution/*.cs`、`Application/Execution/*.cs`、`Application/Execution/Outbox/*.cs`、`Networking/Outbound/*.cs` 的类型声明与 remarks | `GameplayExecutor`、`GameplayExecutorLifetime`、`IGameplayQueue`、`GameplayJob`（`IsOnExecutor`、`RunDetachedAsync`、`EndedAJob`）、`GameplayOutbox`、`OutboxAdapters`、`OutboundSender`；`OutboundLimits.QueueByteLimit` ＝ 1 MiB；`GameplayCallChain.Limit` ＝ 32。发送期限、Required／BestEffort、有序组的成员名执行时读 |
| V8 | 选角工作流 | 读 `Protocol/Game/SelectCharacter/SelectCharacterHandler.cs` | 规划时 `SelectedSessionTransitionGate` 全仓 0 处。执行时确认：阶段命令的派发顺序、补偿命令、关连接如何登记（排在已登记消息之后） |
| V9 | 生命周期与深度超限 | 读 `OnlineUserConnectionObserver.cs`、`Application/Dispatching/NetworkConnectionLifecycleDispatcher.cs`、`DomainEvents/DomainEventDispatcher.cs:120-140` | `lifecycle:depth-exceeded`（`NetworkConnectionLifecycleDispatcher.cs:59`）、`event:<订阅 ID>/depth-exceeded`（`DomainEventDispatcher.cs:130`）。观察者是否仍直接调用在线服务与清理服务（在生命周期作业内），执行时确认 |
| V10 | 通知总线 | 读 `Abstractions/Notifications/INotificationBus.cs`、通知总线实现、`EventService.ProjectAsync` | `NotificationOutcome.Delivered`（`INotificationBus.cs:23`）；重试上限与每收件人期限已删除（`NotificationRetry*` 0 处）。执行时确认：总线今天记哪些日志、`ProjectAsync` 在可写事务内的限制是否仍在、`NotificationAudienceResolver` 的两次解析是否仍成立 |
| V11 | F-35 | 读 `ClientSpecStatisticHandler` 的构造器与注册方式 | 规划时类型在 2 个文件中出现；附录 B 记它注入了日志以外的接口，所以在执行器上 |
| V12 | F-110 的前提 | 读 `NetworkMessageDispatcher.cs`、`NetworkConnectionLifecycleDispatcher.cs` 中作业开始处对 `outboxes.Current` 的访问；读执行器登记来源关闭的条件 | `follow-ups.md` 记的是 `:51`、`:31`（TASK-24 时）。执行时按冻结点行号写进对账记录（AGENTS.md 不写行号） |
| V13 | 偶发清单 | 对 AGENTS.md「判据偶发失败」表中的用例名与栈根各 `grep -rn`；读 `PvfEquipmentDefinitionSource.cs` 抛 `InvalidDataException` 的行 | 规划时未查。F-125 记的是 `:160`；行号变了按冻结点写 |
| V14 | 附录 B 的计数 | 按 `entry-reconciliation.md` 记录的检索式重跑 `IGameplayQueue` 持有者检索；`ContentWarmupTests.Participants` 的条数；`RegisterTelemetryMessageHandler` 的注册数 | 规划时粗检索 `IGameplayQueue` 在 `Server/src` 为 38 行（附录 B 写的是 `c3115f9a` 上 37 行，检索式不同，执行时用原检索式）；预热 71、遥测 2 个 handler 14 个消息类型待确认 |
| V15 | 附录 B 的 GM 预缓冲 | 读 `HttpApi/HttpGameplay.cs` 的 `BufferBodyAsync` | TASK-31 之后：上限特性可写时缓冲上限 32 KiB、只在内存；只读或缺失时不设缓冲上限，超过 32 KiB 会落临时文件（`t009-logs-and-cases.md` §6）。附录 B 那一行仍写「读入内存（上限 32 KiB）」 |
| V16 | 证据用例名 | 对附录 E（`t006-closure.md` §3）、附录 G（`t006-closure.md` §4）、附录 B、附录 H 引用的每个用例名，先经 `t31/names.py` 换名，再在 `Server/tests` 中检索 | 规划时未逐个查。26 个改名中有 `LifecycleExecutorWireTests` 的两条与 `OutboxExplicitDeliveryTests` 的三条，附录 E 的 DR-15 证据引用了前者所在的类 |
| V17 | VC-10 的证据 | 读 `PartyNotificationTests.cs:252-286`；在 `OutboundSenderTests` 与 `Outbox/` 测试中找「有序组中 Required 失败后，组内其后全部条目为 `Skipped`，不论收件人」的用例；在清理通告与队伍测试中找「失败者连接关闭后，其余成员收到当前队伍状态」的用例 | `A_failed_delivery_in_one_formation_stage_skips_every_later_stage`（`:272`）中失败的阶段 1 只有被邀请者一人，只证明「后续阶段全部跳过」，不直接证明「同一阶段里排在失败者之后的收件人也收不到」。另两类用例待找（§4.4） |
| V18 | 文档里的其他执行模型描述 | 读 AGENTS.md 引用的 `Document/host-component-composition.md` 中关于启动顺序、停机与执行器的段落 | 规划时未读。它不在本单元边界内：过期的只记进移交清单（§10），不改 |
| V19 | 脚本 | `ls` scratchpad 的 `t07/diffcheck7.py`、`t21/methods.py`、`t31/closure_refs.py`、`t31/names.py` | 规划时都在 |
| V20 | 行尾 | 对 `AGENTS.md`、`ArchitecturePhilosophy.md`、`spec.md`、`entry-reconciliation.md`、`follow-ups.md` 数 `\r$` | 规划时全为 LF |

V1、V2 与本表不一致时停下回报（停止条件 1）。V3～V18 有出入的，纳入对应各节并在 Result 说明；某处出入落到停止条件 2～5 的，按停止条件处理。

### 2. `AGENTS.md`（Outcome 1）

#### 2.1 依据的规矩

`AGENTS.md` 是现状图，按它自己的「记什么，不记什么」写：
- 过期段落直接改掉，不另起修正案，不写「已于某日改为」；
- 不写过程、不写「本轮」、不写给所有者的交代、不指向 `.rks-agent/`，也不写 F 编号、TASK 编号与 Spec 的 RQ／AC／VC 编号（它们都在 `.rks-agent/` 里）；一个事实重要到要写进来，就把它自己说完整；
- 写数就写口径；
- 「以后要做 X」不写，除非它已经是一条今天成立的约束。过渡态因此写成约束：「这是过渡形态，新代码不要依赖它的 X」，不写「B 会删除它」。

#### 2.2 局部决定

1. **新增一节，不改编号**：执行器一节放在「### 2) 命令执行总线」之前，标题不带编号（「### 玩法执行器、发件箱与发送端」），避免 1)～3) 的编号与锚点变化。
2. **「架构设计」的一句话与走向图**按冻结点改写（§2.3 第 1 行）；走向图里的每个类型名都要经 Required check 4。
3. **用词**：`AGENTS.md` 里 `CMD` 已经专指 Game 出站 `0x01` 类别（与 `0x00` NOTI 对举）。执行器上的执行单位一律称「作业」，作业类型名照代码里的字符串写（`inbound:<ClientMessageType>`、`lifecycle:disconnected` 等）。Spec 里的「CMD」不搬进 `AGENTS.md`。
4. **子 Spec B 所属的节只做最小改动**（待裁定第 3 条）：handler 业务语义规则、切片边界规则、快速开发步骤、端到端示例、命令处理器注入面。只删 A 删除的机制名、改 A 改变的事实（例如出站一步、取门、租约）、改正漂移的 `文件:行`，以及在快速开发里加 F-118 一条约定；规则本身不动。
5. **`ArchitecturePhilosophy.md` 的锚点**：它的引用块链接 `AGENTS.md#架构设计`，该标题不改名。
6. **数字**：「构建与运行」里 `Networking.Tests` 隔离与全量的条数和耗时，按 `logs/task-31-isolated.txt`、`logs/task-31-build-test.txt` 读出，写成「`2bdb1afd` 上，Release，隔离 N 条 x 秒、全量 y 秒」；日志里读不出耗时的，删去数字，只留「10 秒预算」与判法。M-3 的数字不进文档。
7. **篇幅**：新增的执行器一节控制在约 60 行以内；改写各节时，删掉的过期文字不少于新增的文字（不是硬性指标，Result 里报两侧行数）。按 `AGENTS.md`「注释」一节的尺度，不替过程记账。
8. **TASK-31 的日志变化写成约束**：「作业体失败只由执行器记一条 `Error`；生产者与中间件用 `GameplayJob.EndedAJob` 判断，作业体失败时不再记 `Error`」，以及发送端的级别口径。理由：新增生产者的人要据此写 catch，否则 F-30 的双 `Error` 会在新代码里复现。命令层那一条 `Error` 写成过渡态（§2.5）。

#### 2.3 逐节改法

行号是规划时 `2bdb1afd` 上的；「核实」一列是改写前必须在冻结点代码上确认的事实（记进 `task-33-verify.txt` 或 `task-33-agents-refs.txt`），确认不了的不写。

| # | 节（规划时行号） | 过期的内容 | 改成什么 | 核实 | 归属 |
|---|---|---|---|---|---|
| 1 | 「架构设计」一句话与走向图（79～105） | 走向图从 dispatcher 直接到 handler，「每 message 新 DI scope」；出站一步委托 `TouchSocketNetworkMessageSender` | 一句话补上「一切玩法在唯一的执行器上按作业串行执行，出站先进发件箱、作业完成后才发」；走向图在 dispatcher 之后加「入队 `inbound:<ClientMessageType>` 作业 → 执行器 → 作业内新 DI scope」，出站一步改为「`IGameMessageSender`／`INetworkMessageSender` 登记进本作业的发件箱 → 作业完成后 `OutboundSender` 按消息 CLR 类型查编码器注册表 → 每连接发送队列 → 出站」 | V6、V7；dispatcher 是否仍每消息开 scope | A |
| 2 | 「Server 解决方案结构」表 Application、Networking 两行（44～55 一带） | Application 只列命令总线与分发；Networking 只说传输与「显式多收件人投递」 | Application 补「玩法执行器与发件箱」；Networking 补「每连接发送队列与发送端（期限、队列上限、关连接）」。依赖列不变 | V7；项目引用图不变 | A |
| 3 | 「1) 领域服务」中 `OnlineUsers/`（123）与在线身份一段（126、255） | 「在线聚合 + 执行门」 | 去掉「执行门」；「唯一那把锁」若在冻结点上仍是保留的短锁，照写，否则改 | `OnlineStateRegistry` 的锁 | A |
| 4 | **新增**「玩法执行器、发件箱与发送端」 | — | 见 §2.4 | §2.4 各条 | A |
| 5 | 「2) 命令执行总线」（277～345） | 契约里的 `IOnlineUserCommand`／`UserAccessMode`；执行方式只列三种；管线 50／55／57 与「Order 必须落在 55 与 100 之间」；「执行门」一整段（三个实例、`SelectedSessionTransitionGate`）；注册一段的访问方式（「Direct＋Account Shared」）与 `PersistOnTeardownAsync` | 契约按冻结点列（含 `ISelectedSessionCommand`、`IRequestSettlementCommand`、`IMultiSelectedSessionCommand`、`IAccountCommand` 今天的用途）；执行方式四种（含 ReadOnly）；`SendAsync` 两种语义（§2.5 第 1 项）；管线五层 0／45／58／60／100，各写一句职责，`UnitOfWorkBehavior` 的次序约束按代码 remarks 改写；删去「执行门」一段，代之以一句「玩法路径上没有门与租约，串行由执行器保证」（不点名已删的类型）；注册一段去掉访问方式，Workflow 的三条与补偿的那次持久写按冻结点的方法名改写；「命令处理器禁止嵌套 dispatch」保留 | V5、V6；`ProtocolModule` 中 Publish、PvpReady、Compensate 的执行方式；补偿命令落离场位置的方法名 | A（管线与过渡内联）；命令处理器注入面不动（B） |
| 6 | 「3) Socket 层」（346～369） | 传输把消息「丢给 dispatcher」后的流程；「发送器按端点校验」；连接生命周期「直接调在线服务……不经过命令总线」 | dispatcher 一条改为「入队作业并等待它完成，然后才读下一帧」；发送器一条改为「登记时按端点校验」；生命周期一条改为「经 `NetworkConnectionLifecycleDispatcher` 作为 `lifecycle:connected`／`lifecycle:disconnected` 作业运行，观察者在作业内调用在线服务与清理服务」 | V9；端点校验所在的类 | A |
| 7 | 「handler 业务语义规则」（393～434） | `SelectCharacterHandler` 例外一段的取门、lease、「关闭必须在 gate lease 释放后执行」；清理通告投递点的 `文件:行` | 例外一段改为：它在 `inbound:GameSelectCharacter` 作业内顺序派发阶段命令、检查会话仍为 current、在失败时派发补偿命令；关连接登记进发件箱，排在已登记的消息之后。四类允许操作与「不得承载业务语义」不动。`文件:行` 按冻结点改正或去掉 | V8；清理通告投递点 | B 的节，最小改动 |
| 8 | 「端到端示例：GameLogin」（435～452） | 第 2 步「TCP 连上时由观察者创建」；第 3 步「OnlineUser 独占租约 → Direct 放行」；第 4 步「通过 `SendCmdAsync` 发出」 | 第 2 步改为「`lifecycle:connected` 作业」；第 3 步改为「在 `inbound:GameLogin` 作业内」，执行方式按冻结点注册写；第 4 步改为「登记进作业的发件箱，作业完成后由发送端编码出站」。集成测试名照冻结点 | `LoginGameUserCommand` 的注册；`GameLoginIntegrationTests` | B 的节，最小改动 |
| 9 | 「SelectCharacter：接受与状态同步工作流」（453～464） | 「持有 `SelectedSessionTransitionGate`，在同一 lease 内」「lease 释放后再尽力关闭连接」 | 改为作业内顺序派发；关闭登记进发件箱；「发送前先验证必达消息可编码」按冻结点确认后保留或改写；CMD／NOTI 分类一段不动 | V8 | A（F-89） |
| 10 | 「领域事件与即时投影」（465～553） | 「每订阅组独立异步循环……Reliable 在数据库准入门及新事务中……不能在该门内再获取外层账户／会话锁」；「注册时声明重试上限」「每收件人投递期限」三段；「当前消费边界」中的 `NotificationRetryDrainService`；「投递结果既有摘要，也有日志」中的期限与重试日志 | 组循环改为「生产者：读到期的 delivery，把每次消费入队为 `event:<订阅 ID>` 作业并等待」；删去重试与期限三段，代之以「通知没有重试；交付失败由发送端关闭该连接」；写明 `NotificationOutcome.Delivered` 今天的含义（F-122）；日志一段改为「组装失败在登记时记 `Warning`；交付失败由发送端按连接记（级别见执行器一节）」。三入口的规则（`EnqueueAsync`／`DirectAsync`／`ProjectAsync`）只改与执行器冲突的句子 | V10；Reliable 的写保留（`AcquireWriteAsync`）是否仍在 | A（F-105、F-122） |
| 11 | 「判据偶发失败」（753～778） | 只有 SQLite 释放一类 | 补一条 F-125：身份三样（用例名、`InvalidDataException` 且消息含 `[minimum level]`、栈根在 `PvfEquipmentDefinitionSource` 的那一行），单独复跑 5 次全过才归入；并写明这同时是一处生产缺口（PVF 内容缺该字段时奖励结算抛错）。这一条的用例名是身份的一部分，与上一类「用例名不是判别依据」不同，写明理由（成因是特定内容数据，不是并行释放）。原表的用例名与栈根按 V13 核对 | V13 | A（待裁定第 7 条） |
| 12 | 「其他与主设计的偏差」（779～795） | `CLIENT_SPEC_STATISTIC` 被称作纯遥测（F-35） | 改为：它注入了日志以外的接口，作为 `inbound:` 作业在执行器上运行，不经命令总线；纯遥测 handler 的判据是「构造器只有日志」，经遥测注册的 handler 由启动检查强制。另加一条已知缺口：handler 仍在作业内编排多条细粒度命令（§2.5 第 2 项）。「登录失败无法编码」按冻结点核实 | V11 | A |
| 13 | 「快速开发：新增一个协议的固定动作」（829～845） | 缺扇出字段的上限约定 | 加一条：客户端提供、会扇出给其他连接（或累积进其他连接一次作业的出站）的变长字段，解码器或规则服务必须有上限，并在协议记录里写明依据与依据等级；超出发送队列上限的帧会让第三方连接被关闭。不设判据（待裁定第 4 条） | `OutboundLimits.QueueByteLimit` | B 的节，加一条约定（F-118、F-116 (c)） |
| 14 | 「构建与运行」并发假红两段（814～827） | `Networking.Tests` 46 条的数字；「两个用例同时先读后写……同账号有闸，跨账号能真实撞上」 | 第一段按 §2.2 第 6 条换数或去数。第二段改为：生产中写事务只在执行器上开启（执行器外开启即抛），玩法写入串行，生产路径撞不上；直接操作 SQLite、并行运行的测试仍可能撞上，判法与 30 秒的成因照旧保留 | 写工作单元的开启断言；`Default Timeout` 仍无人设置 | A |
| 15 | 「接口后缀标明类别」表（848～896） | 没有入队点 | 加一行：`IGameplayQueue`（入队点）——把一个作业交给执行器并等待它完成；只由生产者调用，在执行器上调用即自我等待（程序错误）；不得注入命令处理器（白名单判据已拒绝）。表的范围说明不变 | `CommandHandlerArchitectureTests` 是否拒绝它 | A（F-36） |

表外有命中、且属于 A 删除机制的，按同样的规则改，在 Result 里补一行。

#### 2.4 新增一节「玩法执行器、发件箱与发送端」的内容

每条写之前按「核实」确认；写法是约束与现状，不是设计说明。不写 F 编号与行号。

1. **一条队列**：进程内唯一的玩法执行器 `GameplayExecutor`，先进先出、单消费者，一次执行一个作业；入队点是 `IGameplayQueue`。作业类型名按来源：`inbound:<ClientMessageType>`、`lifecycle:connected`／`lifecycle:disconnected`／`lifecycle:depth-exceeded`、`timer:<类型名>[/<任务>]`、`event:<订阅 ID>[/depth-exceeded]`、`gm:<METHOD> <路由模板>`、`http:<METHOD> <路由>`、`startup:…`，以及执行器外直接调用总线时的作业（核实 V6 的类型名，生产中若没有调用方就照实写）。
2. **生产者只入队并等待**：dispatcher、生命周期 dispatcher、定时源、事件组循环、GM／HTTP 路由过滤器、启动服务。它们不读写玩法状态、不开写事务、不发包；同一连接至多一个待执行的入站作业（dispatcher 等作业完成才读下一帧）。
3. **玩法路径上不加门、信号量与「锁内复查、重试」循环**；短 `lock(sync)` 与 PVF 内容源的初始化锁保留。
4. **作业内不得等待网络，不得入队后等待**：在执行器上入队并等待即自我等待，以程序错误失败。从作业里分出去的工作在 `ExecutionContext.SuppressFlow` 下启动；传输回调入口经 `GameplayJob.RunDetachedAsync` 清除作业身份。
5. **运行期判据没有开关**：执行器外开启写工作单元、登记发件箱、调用玩法状态的写入口，立即抛出。玩法状态名单的唯一出处是 `GameplayWriteEntryTableTests.Types` 与 `GameplayStateCensusTests.Exemptions`；新增持有进程内可变状态的单例或静态字段，必须进其中之一（普查判据会拒绝漏登记的）。测试在执行器外写状态要经 `tests/Support/TestGameplay`。只有 `GameplayExecutor` 能执行作业（`GameplayJobHostStructureTests`）。
6. **口令哈希在执行器外**：经 `PasswordComputationLimiter`，结果作为作业的输入；在执行器上调用即失败。
7. **调用链**：每个作业带根、父、深度与来源；事件作业与作业登记的关连接继承登记它的那条链，传输层自发的关闭是新根；深度上限 `GameplayCallChain.Limit`（32），超限不入队、记 `Error`、关闭根连接。
8. **失败与日志**：作业失败由执行器记一条 `Error`（带调用链与来源连接）；生产者与中间件用 `GameplayJob.EndedAJob` 识别作业体失败，此时不再记 `Error`。作业以未处理异常结束时，已提交的出站照发，之后关闭来源连接。
9. **发件箱**：作业内的一切出站——`IGameMessageSender`、`INetworkMessageSender`、`ProjectAsync`、关连接——都登记进本作业的发件箱；工作单元内登记的条目在该单元提交时组装，回滚即丢弃；作业结束后按登记顺序交给发送端。组装失败只丢该收件人的条目并记 `Warning`。**已知约定**：失败作业关闭来源连接，依赖作业里已经打开过发件箱；两个 dispatcher 在作业开始即打开它，新的带来源连接的作业要照做（核实 V12）。
10. **发送端** `OutboundSender`：每连接一个发送队列；条目分必达与尽力；跨连接的有序组按登记顺序交付；超过发送期限或队列字节上限（`OutboundLimits.QueueByteLimit`，1 MiB）即关闭该连接；连接已不在时丢弃；没有重试。日志：失败、超时、超限为 `Warning`；连接已不在时的失败为 `Information` 并带固定后缀。执行器从不等待发送。
11. **停机**：先停止接受连接与 HTTP，再依次排空生产者，然后排空并停止执行器（`GameplayExecutorLifetime`）；在线玩家的拆除落库在执行器停止之前完成。

#### 2.5 写成过渡态的各项（交 TASK-34 的 SC-12 清单对照）

`AGENTS.md` 中每项写成「今天如此；这是过渡形态，新代码不要依赖 X」，不写由谁、何时删除。

| # | 过渡态 | 写在哪一节 | 「新代码不要依赖」的内容 |
|---|---|---|---|
| 1 | `ICommandBus.SendAsync` 在执行器上内联执行，执行器外入队并等待 | 命令执行总线 | 不要在执行器外调用总线来完成玩法动作；命令处理器仍不得派发命令 |
| 2 | handler 在 `inbound:` 作业内经总线顺序派发多条细粒度命令，每条命令一个事务 | 命令执行总线；其他偏差 | 不要假设一个入站请求只有一个事务；跨命令的补偿仍由 handler 派发专用补偿命令 |
| 3 | 管线五层（0／45／58／60／100）仍包在每条细粒度命令外 | 命令执行总线 | — |
| 4 | 请求级上下文是作业内的 AsyncLocal 帧：`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus` 的静态分派深度 | 执行器一节或命令执行总线 | 不要在作业之外读写这些帧，也不要新增同类的 AsyncLocal 帧 |
| 5 | 细粒度命令失败时，`CommandLoggingBehavior` 与执行器各记一条 `Error` | 执行器一节第 8 条 | 读日志时同一次失败可能有两条 |
| 6 | 来源连接关闭依赖作业已打开发件箱（§2.4 第 9 条） | 执行器一节 | 新的带来源作业在开始时打开发件箱 |

这张表与 `t009-docs-and-appendices.md` 的移交节逐行一致（Required check 6）。TASK-34 的过渡机制清单若多出或少了某项，按待裁定第 9 条处理。

### 3. `Docs/Agent/ArchitecturePhilosophy.md`（Outcome 2）

- **写法**：`AGENTS.md`「设计哲学与重构判断」写明两份 Agent 文档与本文冲突时「以本文为准，并修正那两份」，所以直接改 §1、§5，不另起修正案；文件头引用块加一行：「§1、§5 中执行与锁的部分于 <日期> 按基准 `2bdb1afd` 改写；其余各节仍是 `d29033a0` 时的记录。」
- **§1**：走向图在 PacketHandler 之上加一层「执行器作业（`inbound:<ClientMessageType>`）」；`commands.SendAsync` 一行注明「在执行器上内联（过渡）」；出站一行改为「`IGameMessageSender`／`ProjectAsync` 登记进作业的发件箱，作业完成后由发送端交付」。「口述没覆盖的第二条出站路」一段保留结论（回包经 `IGameMessageSender` 直接登记，不经事件服务），把「直接发出」改为「直接登记进发件箱」。典型链的类型名按冻结点核对。
- **§5**：「自取会话门的 handler 不止……」一条改为「handler 在作业内编排多条细粒度命令（过渡形态，见 AGENTS.md 命令执行总线）」；「命令总线还有 ReadOnly 执行方式与……三个管线行为，AGENTS.md 未列」一条删去（AGENTS.md 已列），或改为一句现状（`TransactionalExecution` 已不存在）。其余各条（handler 直接改状态、硬编码、`new` 规则服务）不属于执行与锁，不动，也不复核。
- 引用核对同 Required check 4。

### 4. Spec 附录（Outcome 3）

#### 4.1 规则

- 只改「今天的位置」「为什么安全」这类事实栏中与冻结点不符的文字，以及 VC-10。附录 A 的「新的 CMD」一栏只补 A 已经引入的 CMD 类型。承接方式、去留、保护意图不改；需要改的按停止条件 3。
- 每处改动在原文上直接改（附录是 Spec 的现状表），同时在修订记录 R4 写一行：位置、旧 → 新的要点、依据（F 编号或冻结点检索）。
- R4 一节放在 R3 之前（修订记录按新旧倒序；执行时看现有顺序，与之一致）。写法：

  ```
  ### R4（<日期>，冻结点 `2bdb1afd`；TASK-33 附录最终对账）
  - 起因：T-009 的附录最终对账（plan.md R3 T-009「附录 A、B、E、F 的最终对账」）与 TASK-30 主控方裁定第 3 条（VC-10）。
  - 生效：随 TASK-33 的结果评审（待裁定第 1 条）。
  | 位置 | 改动 | 依据 |
  ```

#### 4.2 附录 A

| 行 | 规划时的出入 | 改法 |
|---|---|---|
| 第 3 行（连接生命周期） | 「今天的位置」写 `TouchSocketNetworkConnectionCloser`，TASK-23 已删除（F-99）；「新的 CMD」缺 `lifecycle:depth-exceeded`（TASK-29） | 位置改为冻结点上关连接的实际路径（作业登记 → 发送端关闭 → 传输层断开回调 → `lifecycle:disconnected`）；补 `lifecycle:depth-exceeded` 及其来源（深度超限时关闭根连接的那次） |
| 第 4 行（登录会话） | TASK-23 已改过一次（F-65）；`t007-closure.md` §6 要求在冻结点上复核 | 复核调用链继承的说法（顶替关闭继承 HTTP 登录作业的链，TASK-31 的 HTTP 根用例），不符才改 |
| 第 6 行（卡牌计时器） | `t007-closure.md` §6 点名复核 | 复核两个 `timer:` 类型名与「排空后不启动计时器」，不符才改 |
| 第 8 行（事件订阅） | 「新的 CMD」缺 `event:<订阅 ID>/depth-exceeded`；52 个订阅的计数是 `c3115f9a` 上的 | 补该类型；计数在冻结点上按原检索式复核，变了就改并注明基准 |
| 其余各行 | — | 只做 §6.1 的对账，不符才改 |

#### 4.3 附录 B

| 行 | 规划时的出入 | 改法 |
|---|---|---|
| 不碰玩法状态的 HTTP 路由（GM 预缓冲） | 「把 JSON 请求体读入内存（上限 32 KiB）」；TASK-31 之后上限特性只读或缺失时不设缓冲上限，可能落临时文件（V15） | 补一句这一种情形及其理由（与基准的读体上限逐字等价，TASK-31 主控方裁定第 3 条） |
| 生产者的入队侧 | `IGameplayQueue` 持有者「以基准 `c3115f9a` 的 37 行检索为准」（V14） | 按原检索式在冻结点重跑；数变了，改为冻结点的数与基准，持有者的增减逐个写进对账记录 |
| 启动预热、纯遥测 | 71 个参与者、2 个 handler 14 个消息类型（V14） | 复核，不符才改 |
| 其余各行 | — | 只做 §6.2 的对账，不符才改 |

#### 4.4 附录 G：VC-10

草稿（执行时按 V17 的结果定稿）：

| ID | 变化 | 理由 |
|---|---|---|
| VC-10 | 队伍状态按阶段推进（XO-6）时，某个收件人在某一阶段交付失败：发件箱里排在他之后的条目都不再交付，包括同一阶段里排在他之后的收件人；这些人收不到这一阶段及以后各阶段。失败者的连接被关闭，其余成员随后经清理通告（XO-5）收到当前队伍状态。今天同一阶段里每个收件人都会收到，有人失败才停止推进后续阶段。只出现在交付失败的路径上 | 发送端的有序组没有阶段边界，一条必达条目失败即跳过组内其后的全部条目（RQ-033、RQ-034）；F-98，TASK-30 主控方裁定第 3 条 |

证据（RQ-082 要求每项都有测试证据）：
1. 「后续阶段全部跳过」：`PartyNotificationTests.A_failed_delivery_in_one_formation_stage_skips_every_later_stage`；
2. 「组内其后的条目不论收件人都跳过」：V17 在发送端或发件箱测试中找到的有序组用例；
3. 「其余成员随后收到当前队伍状态」：V17 找到的清理通告或队伍用例。

三条都找到，VC-10 照草稿写。只找到第 1、2 条：第 3 句改为「其余成员随后由断线清理收到当前队伍状态（读码，没有测试直接证明）」，对账记录里记为附条件。第 1、2 条有一条找不到，按待裁定第 5 条与停止条件 4 处理。

#### 4.5 附录 E、F、H

- 附录 E：表里没有测试一栏；对账（每行的测试）写在对账记录里（§6.3）。只在「今天」「新的承接方式」与冻结点矛盾时改，规划时没有已知出入。
- 附录 F：「删除」栏检索为零、「保留」栏仍在（§6.4），规划时没有已知出入。F5 不进附录 F 的文字：名单的识别边界写在判据 remarks 里，附录 F 已经这样指向；F5 写进对账记录。
- 附录 H：A 的三行按 R3-1 已经拆清，规划时不改文字；对账写进记录（§6.5）。

### 5. `entry-reconciliation.md` 的落地状态（Outcome 4）

按 `AGENTS.md`「过期的记载怎么办」：它是带日期的对账，保留原文，在标题下加一段：

- 日期与冻结点 `2bdb1afd`；
- 今天不成立的条目：§2 的 `lifecycle:close-superseded`（TASK-23 删除，顶替关闭改由顶替发生的作业登记）；§2.3、§2.4 与 §3 分离点表中的 `ExecutorSendDeadline`、`TouchSocketNetworkMessageSender`、`TouchSocketNetworkConnectionCloser` 与「`OutboundSender` 未接线」（TASK-23 删除或接线）；V3 检索出的其余已删名字；
- 今天的答案在哪：`t009-docs-and-appendices.md` §6.1、§6.2。

用例名若在 TASK-31 改过名，落地状态里注一句「文中用例名为 TASK-16 时的名字，新旧对照见 `t009-logs-and-cases.md` §7.3」，不逐个改原文。

### 6. 附录最终对账（Outcome 5；写进 `t009-docs-and-appendices.md`）

写法仿 `t007-closure.md`、`t009-criteria.md`：「要求 → 冻结点上的位置 → 证据 → 状态」。证据的检索命令与结果全在 `logs/task-33-appendices.txt`。

#### 6.1 附录 A（AC-006 的附录 A 一半）

每行一条：来源 → 作业类型名（在冻结点代码中检索到该字符串或其前缀的位置）→ 经真实入口驱动的测试（沿用 `entry-reconciliation.md` §2.1 的证据，经换名后在冻结点上检索到）→ 状态。T-007 之后的「继承链」口径写一句（事件作业与登记的关闭各加一跳，入站、定时与传输层关闭是根），出处 `t007-closure.md` §2。

#### 6.2 附录 B（AC-006 的附录 B 一半）

每行一条：安全理由 → 判据或检索（判据名、用例名、检索式）→ 冻结点上的结果 → 状态。「请求级帧（过渡）」一行与 `GameplayStateCensusTests.Exemptions` 的 E3 四项一致（`t009-criteria.md` §9），并与 §2.5 第 4 项一致。AC-006 中「代码中检索不到在执行器外改状态、写库或发包的路径」一半引 `t009-criteria.md` §1、§2 与 TASK-31 的取证（`logs/task-31-violations.txt`：反例类与对照以外 0，`exe:` 0），不重跑。

#### 6.3 附录 E（AC-014）

十七行，每行：证据用例（`t006-closure.md` §3 的编号与名字，经 `t31/names.py` 换名）→ 在冻结点上检索到的文件 → 状态。DR-15 另加 TASK-31 的线级用例（`HandlerFailureConnectionIntegrationTests` 中作业内 handler 抛异常断线的那条，`t009-logs-and-cases.md` §5）。DR-16 维持「满足，附条件」（`t006-closure.md` §3 末）。

#### 6.4 附录 F（AC-017）

- 「删除」栏：每项一个检索式（沿用 `t005-closure.md` §2 与 `t006-closure.md` §2 的 R1～R3 形式），冻结点上 `Server/src` 的结果；全部为零，或与以往一致的「同名无关成员」（如 `.Gate`）逐行说明。
- 「保留」栏：每项在冻结点上仍在的位置。
- 「改为执行器内访问」：名单出处 `GameplayWriteEntryTableTests.Types` 与 `GameplayStateCensusTests.Exemptions`，判据名；挂点与豁免的数（`t009-criteria.md` §2.3：持有者 73 ＝ 挂点 40 ＋ 豁免 33）在冻结点上按源码复核两张表的条目数（不运行判据）。
- **F5，有意例外**：`CharacterContainerDomainService` 的 `Find`／`Get`／`GetContainers` 在事务内会登记参与者，分类表却按读登记（`ReadNote(StagedRead)`）。按 TASK-18 的六种形状，「登记参与者」本应算写；这里的理由是只做暂存，不写就不提交，执行器外走的是加锁直读（TASK-30 第 1 轮评审 F5，评审认为可以接受）。写明它在分类表中的位置（冻结点行号）；交 TASK-34 在 A 最终汇报的「判据的已知边界」中列出。

#### 6.5 附录 H 中属于 A 的三行（AC-009④）

| 行 | A 的一半 | 冻结点上的证据 |
|---|---|---|
| dispatcher | 检查在执行器上、作业开始时执行（AsyncLocal 帧，过渡） | `NetworkMessageDispatcher` 中 `IsBlocked`、`IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter` 都在作业体内的位置；`GameplayDispatchTests.Request_frames_are_built_inside_the_job_and_reach_the_commands_it_runs` |
| 45 `SelectedSessionExecutionBehavior` | 取锁部分删除 | 该文件中没有门、信号量、租约（检索）；`t005-closure.md` §4 的逐项对账仍成立（逐项核对行号） |
| 0 `CommandLoggingBehavior` | 执行器对每个作业记录调用链 | 执行器开始、完成、取消、失败四条日志的模板含根、父、深度（`t007-closure.md` §1 的用例在冻结点上存在）；`CommandLoggingBehavior` 本身仍在（B） |

### 7. RQ-024 的 A 一半（Outcome 6）

问题：A 期间，今天「一命令一事务」的提交点有没有被合并或拆分。做法（脚本 `t33/commit_points.py`，全文附在日志末尾）：

1. **命令注册比对**：在 `a02bce46` 与 `2bdb1afd` 上，从 `ProtocolModule.cs`、`ApplicationModule.cs` 与其余含 `AddCommandHandler<` 的文件中取出（命令类型 → 执行方式）。规划时已知：基准上的注册带访问方式参数（如 `(CommandExecutionKind.Transactional, UserAccessMode.Exclusive, UserAccessMode.Exclusive)`），冻结点上只剩执行方式。脚本只比执行方式。输出：两侧都有且执行方式相同的数、执行方式变了的（逐条）、只在一侧的（逐条）。
2. **事务开启点比对**：两个提交上 `new DomainTransaction(` 与 `DomainTransactionScope` 的开启方法。规划时已知：冻结点多了 `DomainTransactionScope.BeginSnapshot`（`:68`，只读快照、写模式、从不提交，TASK-18 裁定第 3 条），它不是提交点，写明理由；其余 5 处与基准一一对应（行号漂移）。
3. **命令增删**：只在一侧的命令逐条归类——随删锁或重试一起删除（指到 `t005`／`t006` 处置）、新增的（指到引入它的单元）。新增或删除的 Transactional 命令都要说明它是否改变了某个用例的提交点。
4. **作业层的事务**：事件作业（Reliable 的消费与完成标记同事务）、启动作业、拆除时的落库，各自在基准上对应哪个事务，逐条写「一对一」或差异与理由。

判定：没有差异，记「A 期间未合并、未拆分提交点」；有差异且能指到授权的单元裁定，逐条列出；没有理由的差异按停止条件 2。

### 8. AC-009 ①② 与 AC-022（Outcome 7、8）

#### 8.1 保护测试清单（AC-009 ①②）

- **选取规则**（脚本 `t33/protection.py`）：在 `a02bce46` 的 `Server/tests` 中，方法体（`t21/methods.py` 的切法）引用以下任一名字的测试方法，按类别归组：
  - (a) 具名判定：`IsSelected`、`IsInWorld`、`TryGetInWorld`、`StandingOf`、`ReleaseExact`；
  - (b) 效果账本：`EffectOperationLedger`、`EnterWithoutSelectedSession`、`BeginMain`，或类名含 `EffectOperation`、`Replay`；
  - (c) 会话隔离：`SelectedSessionSafety`、`BlockAsync`、`TransactionOutcomeUncertainException`、`IsBlocked`；
  - (d) 登录顶替：`IsSuperseded`、`CaptureAuthority`；
  - (e) 槽位边界：`BusinessSlot`，或类名含 `InventorySlotBoundary`、`SlotMapping`。
  选取规则在规划时没有试跑，数量未知；脚本输出每类的方法数与加法。规则选出的方法若多到无法逐条读（例如超过 300），仍全部做机械比对，只对有差异的逐条读。
- **比对**：每个方法在冻结点上的对应（同名，或经 `t31/names.py` 换名）；断言行多重集合（`diffcheck7.py` 的口径：含 `Assert.` 或 `Skip.` 的行，去空白）`a02bce46` 对 `2bdb1afd`。包装进 `TestGameplay` 只改缩进、不改断言文本，比对不受影响。
- **差异的处置**：方法消失、或断言多重集合变化的，逐条指到 `t005-test-dispositions.md`、`t006-test-dispositions.md`、`task-07-conflicts.md`、`t009-criteria.md` §3 或 `t009-logs-and-cases.md` §1.3 中的那一行；指不到的按停止条件 2。
- **通过**：每个方法在 `logs/task-31-build-test.txt` 中为 Passed（按换名后的名字检索）；Theory 按行计。不在日志里的（例如被跳过）逐条说明。
- **②**：`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement` 单列：方法体在两个提交上的断言行多重集合相同（TASK-25 记的是 8 行），并写出方法体是否逐字相同（该文件 A 期间有大量包装改动，`git diff --stat` 为 751 增 641 删）。
- **③** 已在 TASK-31 关闭（`EffectReplayWireTests`，`t009-logs-and-cases.md` §5），这里只引用。**④** 见 §6.5。

#### 8.2 A 删除的名字（AC-022 的 A 一半）

- **名字清单**（脚本 `t33/deleted_names.py`）：
  1. 在 `a02bce46` 的 `Server/src` 中声明、在 `2bdb1afd` 中不再声明的类型（`class`／`record`／`struct`／`interface`／`enum`／`delegate`，按名字去重）；
  2. 附录 F「删除」栏点名的成员与机制（`AcquireManyExclusiveAsync`、`LeaseSet`、`groupGates`、`PartyDeliveryStream.Gate` 等）；
  3. F-105、F-92 点名的名字（`PersistOnTeardownAsync`、`retryLimit`、`CloseRequests`、`lifecycle:close-superseded`、`UserAccessMode` 等）。
  三类的数与去重后的总数写出加法。第 1 类中只是随文件改名而换了名字的类型，在清单里标出新名。
- **检索范围与判定**（待裁定第 2 条）：
  - 现状文档 `AGENTS.md`、`Docs/Agent/*.md`：改完后每个名字 0 处（`AGENTS.md` 没有「被推翻的做法」一类的记录，所以没有例外）；
  - `Docs/` 其余文件、`Document/`、`protocol/`：逐个命中归类为「带日期的记录（保留原文）」或「现状描述、已过期」；后者不改，列进移交清单（§10）；
  - `Server/src`：0 处（与 §6.4 重叠，这里只确认清单第 1 类）。
- 先写预期（`AGENTS.md`、`Docs/Agent` 为 0；其余文档的命中数按规划时 V3 的量级）再跑。

### 9. 收口

#### 9.1 `verification/t009-docs-and-appendices.md`

- 文件头：日期、冻结点 `2bdb1afd`、用途（TASK-34 的 AC 状态表与 A 最终汇报从这里取输入）、写法（仿 `t007-closure.md`，「要求 → 位置 → 证据 → 状态」，未关闭事项只引 F 编号）。
- §1 RQ-100 与根 SC-10：`AGENTS.md` 与 `ArchitecturePhilosophy.md` 改了哪些节（§2.3 表的行号对到改后行号），A／B 的拆法（R3-1 RQ-100 一行），§2.5 过渡态表的引用。
- §2 Spec 附录的改动：R4 的引用，以及每处改动的授权依据（待裁定第 1 条的裁定）。
- §3 附录 A；§4 附录 B；§5 附录 E；§6 附录 F（含 F5）；§7 附录 H 的 A 三行；§8 附录 G 与 VC-10 的证据；§9 RQ-024；§10 AC-009 ①②④（③引 TASK-31）；§11 AC-022；§12 AC 状态（AC-006、AC-009 的 A 一半、AC-014、AC-017、AC-022 的 A 一半，各写「满足／满足，附条件／未满足」与依据）；§13 移交（§10）。

#### 9.2 `follow-ups.md`

| 编号 | 处置 |
|---|---|
| F-35、F-36（AGENTS.md 一半）、F-89、F-92、F-105 | 关闭：写明 `AGENTS.md` 改了哪一节 |
| F-98 | 关闭：附录 G VC-10 |
| F-99 | 关闭：附录 A 第 3 行、`entry-reconciliation.md` 落地状态 |
| F-118 | 关闭：`AGENTS.md` 快速开发一条约定；判据不设，列入 TASK-34 遗留（待裁定第 4 条） |
| F-110 | 更新：A 一半已记为已知约定（执行器一节）；结构保证仍归子 Spec B |
| F-116 | 更新：(c) 已写进 `AGENTS.md`；(a)(b) 交主控方立项，列入 TASK-34 遗留 |
| F-122 | 更新：A 一半已写明今天的含义；改名仍归子 Spec B |
| F-125 | 更新：已登记进 `AGENTS.md` 偶发清单；生产缺口仍交主控方 |
| 新发现 | 本单元发现、不在本单元修的（例如 V18 的过期文档），新增编号 |

文首补 TASK-33 一行（日期、冻结点、对账记录、关闭与更新的编号、按授权改的 Spec 附录）。改之前先读 TASK-32 提交后的 `follow-ups.md` 全文，不覆盖 TASK-32 的改动。

#### 9.3 检索集（`logs/task-33-retrievals.txt`）

每个检索式先写预期再跑：
- R1 Required check 5 的自身规矩检索；
- R2 Required check 6 的过渡态一致；
- R3 `AGENTS.md`、`Docs/Agent/*.md` 中 A 删除的名字（§8.2）为 0；
- R4 `AGENTS.md` 中「门」「租约」「lease」「信号量」的剩余命中逐处归类（例如「数据库准入门」若在冻结点上仍在则保留，「不加门」的约束句保留）；
- R5 `spec.md` 中附录 A 第 3 行与 `entry-reconciliation.md` 落地状态都不再把已删类型当作今天的位置。

### 10. 移交 TASK-34

写进 `t009-docs-and-appendices.md` 最后一节：
- §2.5 过渡态表（SC-12 清单的输入，二者要一致，待裁定第 9 条）；
- F-116 的 (a)(b)、F-118 是否设判据、F-125 的生产缺口、F5 的判据边界：列入 A 最终汇报的遗留问题或已知边界；
- VC-10 是主控方授权的 Spec 验收修订；附录 A、B 的事实性修订（R4）：列入 A 最终汇报的「Spec 修订」一节；
- AC 状态（§9.1 的 §12）；
- 本单元发现、不在本单元修的事实错误（V18 等）及新增的 F 编号；
- AC-009① 的方法清单：TASK-34 的最终全量中逐条确认通过（待裁定第 6 条）。

### 11. 执行顺序

1. §1 核实（V1～V20），落盘 `task-33-verify.txt`；V1、V2 不符即停。
2. 先跑只读的脚本：§8.2 名字清单、§7 提交点、§8.1 保护测试、§6 附录检索；它们的结果决定后面要写什么。任何一项触发停止条件 2，停下回报。
3. Spec 附录与 R4（§4），VC-10 按 V17 定稿。
4. `entry-reconciliation.md` 落地状态（§5）。
5. `AGENTS.md`（§2），按 §2.3 表自上而下；新增一节（§2.4）。
6. `ArchitecturePhilosophy.md`（§3）。
7. 对账记录（§9.1）与 `follow-ups.md`（§9.2）。
8. Required checks 3～6、10～13（引用、自身规矩、过渡态一致、Spec 边界、文件边界、收口引用、`diff --check`）；最后一次冻结核对。

### 12. 表征测试、金样本与生产行为

本单元不改代码与测试，客户端可见行为不变。VC-10 记录的是 A 已有的行为（TASK-23 引入），不是本单元引入的变化。

## 待裁定

1. **Spec 附录的事实性修订与 VC-10，随本单元的结果评审生效，不另做 Spec 修订审查**（§4；Outcome 3）。
   - R3 是一次改道，改了验收边界，所以要过三份审查；本单元改的是附录里与冻结点不符的位置与计数，外加已经授权的 VC-10。附录 A、F 本身写着「实施时逐项对账」「实施时补入本表」，TASK-23、TASK-30 也都在单元内改过附录（附录 A 第 4 行、附录 B、F 各行），先例是随单元评审。
   - **建议：接受。** 每处改动在 R4 写一行，结果评审逐行核对；frontmatter 不变；TASK-34 的 A 最终汇报把 VC-10 与 R4 列为「主控方授权的 Spec 修订」，交外部审核时说明。
   - 备选：R4 单独走一次 Spec 修订审查（仿 R3）。代价：多一轮审查，而改动不涉及验收边界；VC-10 的授权已由主控方给出。

2. **AC-022 的 A 一半的检索范围**（§8.2）。
   - AC-022 写的是「检索已删除的类型名只出现在『被推翻的做法』一类的记录中」，没说检索哪些文件。规划时 `Docs/` 有 5 个文件、`Document/` 有二十余个文件提到 A 删除的名字，都是带日期的设计、调查或任务记录。
   - **建议**：现状文档（`AGENTS.md`、`Docs/Agent/*.md`）必须为 0；`Docs/` 其余文件、`Document/`、`protocol/` 中带日期的记录按 `AGENTS.md`「过期的记载怎么办」归为「保留原文的记录」，算作 AC-022 所说的那一类，不改、只在日志中逐个列出；其中若有不带日期、描述现状的文档，列进移交清单，不在本单元改。
   - 备选：给 `Docs/` 那 5 个文件各补一段落地状态。代价：超出 RQ-100 点名的两个文件，扩大本单元边界；`Document/` 的同类文件更多，不补则口径不一致。

3. **子 Spec B 所属的 `AGENTS.md` 节只做最小改动**（§2.2 第 4 条；§2.3 第 7、8、13 行）。
   - R3-1 把「handler 业务语义规则、命令处理器注入面、切片边界规则、快速开发步骤与端到端示例」划给 B。但其中两节点名了 A 删除的 `SelectedSessionTransitionGate` 与租约，端到端示例的出站一步写的是 A 删除的发送器；不改，AC-022 的 A 一半就不成立，文档也与 A 封存状态不符（SC-10）。
   - **建议：接受。** 这几节只删 A 删除的机制、改 A 改变的事实与漂移的 `文件:行`，并按 TASK-30 裁定第 4 条在快速开发里加 F-118 一条约定；规则本身不动，留给 B。
   - 备选：这几节一字不动，AC-022 记为「A 一半除 B 所属节外满足」。代价：`AGENTS.md` 在 A 封存时仍描述已删除的门，下一个读它的 agent 会照着错的前提工作。

4. **F-118 不设判据**（§2.3 第 13 行）。
   - TASK-30 提纲写了「建议不另设判据」。代码已冻结，本单元也加不了判据；而「解码器有上限」是否可机械判定（哪些字段会扇出）需要逐协议判断。
   - **建议：A 内只写约定**；是否设判据列为遗留问题，交 TASK-34 写进 A 最终汇报，由主控方决定归子 Spec B 还是另立。
   - 备选：开一个返工单元加判据。代价：打破代码冻结（TASK-31 §12），M-3 之后又要重新验证。

5. **VC-10 的证据可以是组合，「自愈」一句没有直接测试时降为读码**（§4.4）。
   - RQ-082 要求每项可见变化都有测试证据。规划时找到的 `A_failed_delivery_in_one_formation_stage_skips_every_later_stage` 中，失败的阶段只有一人，没有直接演示「同一阶段里排在失败者之后的人也收不到」。代码已冻结，不能补用例。
   - **建议**：以「队伍阶段用例（后续阶段跳过）＋ 发送端有序组用例（必达失败后组内其后条目不论收件人都跳过）」两条组合作为证据，VC-10 记为「满足，附条件：证据为组合」；「其余成员随后收到当前队伍状态」一句有测试就引用，没有就在 VC-10 里写明「读码」。两条组合中有一条找不到，按停止条件 4 回到主控方。
   - 备选：开返工单元补一条直接演示的用例。代价同第 4 条。

6. **AC-009① 的「通过」取 TASK-31 的正式全量，本单元不跑测试**（§8.1）。
   - 冻结点 `2bdb1afd` 与 TASK-31 正式全量所跑的代码只差评审后的一处空格（TASK-31「主控方处置」发现 3）；A 封存状态下的最终全量在 TASK-34（AC-020）。
   - **建议：接受。** 本单元给出方法清单与断言不变的比对；「通过」先引 TASK-31 的日志，TASK-34 在最终全量中按清单逐条确认，确认结果写进 AC 状态表。
   - 备选：本单元过滤运行清单中的方法。代价：要构建；而 TASK-34 无论如何要再跑一次全量，结论以那次为准。

7. **F-125 写进 `AGENTS.md` 偶发清单时，用例名作为身份的一部分**（§2.3 第 11 行）。
   - `AGENTS.md` 现有的偶发表明写「用例名不是判别依据」（SQLite 并行释放一类，同一栈根出现在多个用例上）。TASK-31 裁定第 6 条给 F-125 定的身份三样里含用例名，因为成因是特定 PVF 内容，只在这一条随机奖励路径上出现。登记之后，它在 RQ-083 下成为「AGENTS.md 已登记的偶发项」，TASK-34 的最终全量按它判定。
   - **建议：接受。** 写成单独一条，身份三样与「单独复跑 5 次全过」原样写入，并写明这同时是一处生产缺口（修复交主控方另行立项）。
   - 备选：只写栈根与异常两样。代价：同一栈根若出现在别的用例上（另一条奖励路径），会被当作已登记偶发而放过，而那可能是新的内容缺陷。

8. **（无需裁定，供知悉）`ArchitecturePhilosophy.md` 直接改 §1、§5**，文件头补一行改写记录（§3）。依据是 `AGENTS.md`「两份是归纳而来，与本文冲突时以本文为准，并修正那两份」。

9. **`AGENTS.md` 的过渡态与 TASK-34 的过渡机制清单（SC-12）以哪一份为准**（§2.5；§10）。
   - 两者描述同一组事实。本单元先写 `AGENTS.md`，TASK-34 后写清单；TASK-34 若在检索中多认出一项（例如某个转发层），`AGENTS.md` 就漏了。
   - **建议**：SC-12 清单是这组事实的正式出处；TASK-34 的 Owned boundary 加上「`AGENTS.md` 中写为过渡态的段落，只在清单与之不一致时改」，改动写进 TASK-34 的 Result。本单元的 §2.5 表作为 TASK-34 的起点。
   - 备选：TASK-34 不改 `AGENTS.md`，不一致时回到主控方另开小单元。代价：A 封存前多一次往返。

## 主控方裁定（执行前）

1. **附录修订与 VC-10 随本单元结果评审生效，不另做 Spec 修订审查，接受。** 附录本身就是「实施时对账」；VC-10 由 TASK-30 裁定第 3 条授权。spec-001 封存前的完整结果评审会再次覆盖这些改动。
2. **AC-022 的检索范围，接受。** AGENTS.md 与 `Docs/Agent/*.md` 中必须为 0。`Docs/` 其余文件、`Document/`、`protocol/` 中带日期的记录归为「保留原文的记录」，只在日志里列出，不改。
3. **B 所属的几节只做最小改动，接受。** 只删 A 删除的机制，只改 A 改变的事实和漂移的行号，另加 F-118 一条约定。
4. **F-118 不设判据，只写约定，接受。** 「要不要设判据」由 TASK-34 列为遗留问题。
5. **VC-10 的证据可以用组合，状态记为「满足，附条件」，接受。** 「其余成员随后拿到当前队伍状态」若没有测试，写明这是读码得出的。组合仍证明不了时，停下回报。
6. **AC-009① 的「通过」引用 TASK-31 的正式全量，接受。** 由 TASK-34 在最终全量里按清单逐条确认。
7. **F-125 进偶发清单时，用例名算身份的一部分，接受。** 单独写一条，并注明它同时是生产健壮性缺口。
8. 知悉。ArchitecturePhilosophy.md 直接改 §1、§5。
9. **过渡态以 TASK-34 的 SC-12 清单为正式出处，接受。** TASK-34 的边界加一条：两者不一致时，可以改 AGENTS.md 中写为过渡态的那几段。

开工前提：TASK-32 已提交，`git diff 2bdb1afd -- Server Tool` 为空。

## Result

执行轮次：第 1 轮（2026-10-23 UTC 22:32～23:10，即本地 2026-10-24；基准 `2bdb1afd`，HEAD `a5e3bccb`，未提交）。拥有的改动见 Changed 与 `logs/task-33-boundary.txt`。

- **Outcome**：done。停止条件一条都没有触发：冻结核对开头、结尾都为空；附录 F「删除」栏在 `Server/src` 中只有同名无关的命中；AC-009 选出的保护测试，断言有变化的 12 条与删除的 2 条都指得到处置记录；RQ-024 没有合并或拆分提交点；附录 E 十七行的证据用例都找到；VC-10 按裁定第 5 条用组合证据写成「满足，附条件」；B 所属的节只做了裁定第 3 条允许的改动。
- **Changed**：
  - `AGENTS.md`（＋170／－83，`git diff --stat 2bdb1afd`）：按 §2.3 表逐节改写，新增「玩法执行器、发件箱与发送端」一节（64 行，含空行）。六项过渡态各在一处写明「这是过渡形态」（§2.5 表，`t009-docs-and-appendices.md` §13 T1～T6）。发送端一段按今天的事实写日志级别，并把 F-127（写端已完成的形态仍记 `Warning`）写成已知缺口。逐节对照在 `t009-docs-and-appendices.md` §1。
  - `Docs/Agent/ArchitecturePhilosophy.md`：文件头加一行改写记录；§1 走向图、「第二条出站路」；§5 两条。
  - `spec.md`：附录 A 第 3、5、8 行，附录 B「发送端」「不碰玩法状态的 HTTP 路由」「生产者的入队侧」三行，附录 F「保留」栏短 `lock` 一项，附录 G 新增 VC-10，修订记录新增 R4（9 行，与 8 处改动逐一对应，见 `logs/task-33-boundary.txt`）。
  - `verification/entry-reconciliation.md`：开头加落地状态，原文未改（删除行 0）。
  - 新建 `verification/t009-docs-and-appendices.md`（§1～§13）。
  - `verification/follow-ups.md`：文首补 TASK-33 一行；关闭 F-35、F-36、F-89、F-92、F-98、F-99、F-105、F-118；更新 F-110、F-116、F-122、F-125、F-127；新增 F-128。
  - 日志 `verification/logs/task-33-{verify,deleted-names,commit-points,protection-tests,appendices,agents-refs,retrievals,boundary,closure-refs}.txt`。一次性脚本在 scratchpad `t33/`，全文附在对应日志末尾。
- **Why**：
  - **B 所属的节只改 A 的事实**：端到端示例第 3 步只去掉租约与「Direct 放行」，基准上就已漂移的 `LoginGameUserCommand` 等名字保留原样，记为 F-128 交子 Spec B。A 所属的「命令执行总线」注册一段是本单元重写的，同一段里基准上就已漂移的三处（`LoginGameUser`、`AcknowledgeGameExit`、「`ApplicationModule` 只留 `AddCommandDispatching()`」）按冻结点改正。
  - **`OnlineUsers/` 整条删去**：Application 里已经没有这个目录，删掉「执行门」三个字之后剩下的条目同样不成立。
  - **附录 B「发送端」行只补「为什么安全」一栏**：第一列的行名被 `GameplayStateCensusTests.Exemptions` 当作字符串引用（E2 指名本行），改行名会让判据引用落空。
  - **附录 A 第 5 行、附录 F 短 `lock` 补冻结点的数，不替换基准的数**：两处原文都写明了口径的基准，覆盖掉就丢了这个出处。
  - **VC-10 第 3 句用「各段有测试，整条衔接为读码」**：V17 找到三段各自的测试，但没有一条从失败的阶段一直断言到其余成员收到快照；这个说法介于计划给的两种写法之间，比「读码，没有测试直接证明」更准确。
  - **`UnitOfWorkBehavior` 的次序约束写成「落在 58 与 100 之间」**：按该文件 remarks（45 < 58 < 60 < 100）。
  - **新增文字多于删除**：执行器一节是新内容，旧文里没有对应段落可删；其余各节删掉的过期文字与新增的大致相当。
- **Checks**：
  1. 冻结核对：开头（`logs/task-33-verify.txt` V1）与结尾（`logs/task-33-boundary.txt` 开头）`git diff 2bdb1afd -- Server Tool` 都是 0 行，`git status --short` 中 `Server/`、`Tool/` 无条目。
  2. 核实 V1～V20：`logs/task-33-verify.txt`。与规划不同的有：深度常量名是 `GameplayCallDepth.Limit`；编码在登记时进行；清理通告的投递调用是 7 处；表外过期点 4 处（「并发门」、`SelectCharacterLifecycleServices.cs:120` → `:204`、快速开发里的 AccessMode、`OnlineUsers/` 目录）；V14 为 38 行；V17 的第 3 条只找到分段证据。都已纳入对应各节，没有一处落到停止条件。
  3. A 删除的名字：`logs/task-33-deleted-names.txt`。清单 37 ＋ 25 ＋ 11 ＝ 73，去重后 49。`Server/src` 中只有 `Lease` 有 22 行同名无关的命中；`AGENTS.md`、`Docs/Agent/*.md` 改前 14 处，改后 0 处。其余文档的命中逐个归类，只有 `Docs/ProtocolSliceArchitectureNotes.md` 是过期的现状描述（F-128）。
  4. 引用核对：`logs/task-33-agents-refs.txt`。新增行 180 行、反引号 114 个，4 个 `文件:行` 与 3 个 `:N` 逐行打印，内容相符。剩下 6 项逐条说明：E2E 两个漂移名（F-128）、`dotnet test`、一处带箭头的两个标识符、`PacketHandler` 与 `XxxCommand` 这两个图中原有的口述名或占位。
  5. 自身规矩：`logs/task-33-retrievals.txt` R1。12 个禁用模式都是 0；`CMD` 只有 1 处，是 0x01 类别；新增行里的每个数都列出了口径。
  6. 过渡态一致：R2。含「过渡」的段落或列表项正好 6 个，与 §2.5 表一一对应，也都在 `t009-docs-and-appendices.md` §13 的表里。
  7. 附录对账：`logs/task-33-appendices.txt`。未解决 2 项，都是 TASK-23 随机制一起删除的测试类（`NotificationRetryDrainExecutorTests`、`ExecutorSendDeadlineTests`）。`Types` 42 ＝ 40 ＋ 2，`Exemptions` 33 ＝ 7 ＋ 21 ＋ 4 ＋ 1。
  8. RQ-024：`logs/task-33-commit-points.txt`。注册两侧都是 204 条、执行方式全同；事务开启点 20 → 22，两处差异都有授权单元的裁定。
  9. AC-009 ①②：`logs/task-33-protection-tests.txt`。选出 195 个方法，断言相同 181、不同 12、删除 2，14 条都有处置；193 个方法在 TASK-31 正式全量中通过，共 331 行；②的 8 条断言行不变，方法体只差包装。
  10. Spec 改动边界：`logs/task-33-boundary.txt`。8 处 hunk 与 R4 的 9 行逐对列出，改动只在附录 A、B、F、G 与 R4。
  11. 文件边界：同一日志。改动的文件都在 Owned boundary 内；`AGENTS.md` 的每个 hunk 都归到了 §2.3 表的一行，`ArchitecturePhilosophy.md` 只动了文件头、§1、§5，`entry-reconciliation.md` 删除行 0。
  12. 收口引用：`logs/task-33-closure-refs.txt`，`t33/closure_refs.py` 退出码 0，未解决 0。规则在 TASK-31 版本之上加了一条：标明「删除」的测试名到基准或 A 期间的历史里去找。
  13. `git -c core.whitespace=cr-at-eol diff --check` 无输出；改动的文件与新建文件 CR 0、行尾空白 0。
  - 未构建、未跑测试（按授权）。未 commit，未用任何形式的 `git stash`，未 push。
- **Remaining**：
  - 本单元的 Result review 为 required，由主控方派发独立复核。
  - 交 TASK-34 的事项见 `t009-docs-and-appendices.md` §13：过渡态六项、遗留问题与已知边界（F-116 的 (a)(b)、F-118 要不要设判据、F-125 的生产缺口、F5、F-110、F-122、F-127）、Spec 修订（VC-10 与 R4）、AC 状态、F-128、AC-009① 的方法清单。

### 评审后修改

独立快审结论为 `approved`（`reviews/task-33-result-review.md`）。按主控方转达的处置，提交前就地处理了 7 条低严重度发现，只改文档：

- **L1**：`spec.md` 附录 G VC-10「变化」第一句改为「同一有序组（XO-6 的队伍状态阶段）里排在他之后的条目都不再交付」，与「理由」栏口径一致。R4 补了第 10 行，R4 由 9 行变为 10 行，与改动的对应关系见 `logs/task-33-boundary.txt`。
- **L2**：`t009-docs-and-appendices.md` §13 第 2 条加 F-128 ①：端到端示例第 3 步的旧命令名与注册一段的 `RedeemGameLogin` 并存，归子 Spec B，提示 B 的首个单元先改这一节。
- **L3**：AGENTS.md「命令执行总线」管线一条改为「这五层今天包在每一条细粒度命令外；这个粒度是过渡形态，新代码不要依赖『一条命令一套管线』」。对账文件 §13 的 T3 行同步补上「新代码不要依赖」一栏。本文件 §2.5 规划表第 3 行仍是「—」，规划原文不改。
- **L4**：对账文件 §3 第 5 行的计数说明改为按实际构成写：检索式 `"timer:\w+"` 的 22 处含两塔扫描 2 处，卡牌计时器的 2 处带 `/`，另行检索（见第 6 行）。
- **L5**：对账文件 §4 的「三个回调」改为四个（`NetworkPlugins.cs:25`、`:42`、`:71`、`:159`）。
- **L6**：R4 中附录 A 第 5 行的依据改指 `logs/task-33-verify.txt` 的「t33/hosted.sh」一节（第 379 行起）。
- **L7**：对账文件 §12 AC-022 一行的依据注明检索范围按裁定第 2 条确定。
- **重跑**：全部检索与日志（scratchpad `t33/runall.sh`）已重跑，结果如下：
  - 收口引用核对未解决 0；
  - AGENTS.md 的自身规矩检索仍全为 0；
  - 含「过渡」的段落或列表项仍为 6 个，与 T1～T6 一一对应（`retrievals.py` 中 T3 的匹配串随新措辞更新）；
  - `git -c core.whitespace=cr-at-eol diff --check` 无输出；
  - 冻结核对仍为空。
- 未 commit，未 stash。
