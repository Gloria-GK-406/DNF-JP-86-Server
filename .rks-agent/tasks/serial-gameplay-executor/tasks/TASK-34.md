# TASK-34 — T-009e：A 封存状态的最终全量、过渡机制清单与 A 的最终汇报（收口）

基准提交 `51c294fa`（TASK-33／T-009d 已提交）；代码冻结点 `2bdb1afd`（TASK-31，`t009-logs-and-cases.md` §8）。本单元是 `tasks/TASK-30.md` §0 拆分中的第五个、也是 spec-001 的最后一个单元；边界提纲见 TASK-30 §0.2 的 TASK-34 一行，依据去向见 §0.3。本单元不改 `Server/`、`Tool/`；它在冻结代码上做一次最终构建与正式全量，其余产出都是文档：过渡机制清单、A 的最终汇报、T-009 收口记录与 `follow-ups.md` 的终态。封存前的完整结果评审与最终核验不在本单元内（主控方另行安排），本单元的产出是它们的输入。

与 TASK-30 §0.2 提纲的差别：
- 新增 AC-009① 的逐条确认：`logs/task-33-protection-tests.txt` 末尾的 193 个方法在本单元的正式全量中逐个为通过（TASK-33 主控方裁定第 6 条）。
- 新增 `AGENTS.md` 过渡态段落的一致性核对：过渡机制清单是正式出处，两者不一致时本单元可以改 `AGENTS.md` 中写为过渡态的那几段（TASK-33 主控方裁定第 9 条）。
- AC／SC 状态总表覆盖 A 的全部 AC（含跨 A／B 的 A 一半）与子契约的全部 SC，不只提纲点名的几条（§7）。
- `follow-ups.md` 的终态：每一条未关闭的条目都给出 A 之外的去向（§8，待裁定第 5 条）。
- `multi-spec-map.md` 的 SC 进度与 spec-001 一行由主控方在封存步骤更新；本单元只在 `t009-closure.md` 末节准备这些数据（§10.2）。

范围取自：
- `tasks/TASK-30.md` §0.2 TASK-34 一行的 ①～⑦、§0.3 分配表（F-117、F-69、根 SC-11／SC-12）与主控方裁定第 5 条（两份产出的路径）；
- 子契约 `attempts/spec-001-executor-outbox-lock-removal/TaskContract.md`（revision 2）全部 SC：SC-1、SC-3～SC-12；根契约 `TaskContract.md`（revision 4）中标 A 的同号行；
- `spec.md` RQ-080～RQ-083、AC-019、AC-020，修订记录 R3-1（A／B 拆法）、R3-3（测试数量加法「A 从基线加到 A 封存」）、R3-4（SC-11、SC-12 不另设 AC）；`plan.md` R3 中 T-009 的「过渡机制清单与交给子 Spec B 的移交清单」「最终构建与全量测试，测试数量加法」两项；
- 前四个单元交给本单元的事项：`t009-criteria.md` §9（共享语义变化、请求级帧 4 项）；`t009-logs-and-cases.md` §7.4、§8（N 13、D 0、R 26；F-30 命令层一半；F-33 偏离）；`measurement/m3-summary.md` §1（AC-005 测量一半、AC-021）；`t009-docs-and-appendices.md` §12、§13（AC 状态、T1～T6、遗留问题、Spec 修订、F-128、AC-009① 清单）；`reviews/task-31-result-review.md` 发现 2（Channel 入站 F-30 无用例）；`reviews/task-32-result-review.md` L3（F-127）；`reviews/task-33-result-review.md` L2、L3；
- `verification/follow-ups.md` 中仍未关闭的全部条目（规划时 50 条，V16、§8.1）。

本单元的产出：
- 一次最终构建与正式全量（AC-020、RQ-083、SC-8），日志 `logs/task-34-build-test.txt`，原始输出按待裁定第 1 条落盘；
- 新建 `verification/transition-mechanisms.md`：根 SC-12 的过渡机制清单与残留检索（§6）；
- 新建 `verification/a-final-report.md`：根 SC-11 的 A 最终汇报，含 AC／SC 状态总表（§7、§9）；
- 新建 `verification/t009-closure.md`：T-009 收口，含测试数量加法、测试文件改动清单与封存数据（§4、§5、§10）；
- 修改 `verification/follow-ups.md`：终态（§8）与文首 TASK-34 一行；
- 条件修改 `AGENTS.md`：只在过渡态段落与清单不一致时（§6.4）。

输入：
- 冻结点代码 `2bdb1afd`；Spec 基准 `a02bce46`；规划检查点 `b76504bb`（测试数量基线 7872，`baseline.md`）；表征测试引入点 `f01dc226`；
- `logs/task-31-build-test.txt` 与 scratchpad `t31/formal-raw.txt`（冻结点之前最后一次正式全量，本单元的逐项对照基准）；`logs/task-31-violations.txt`（取证预期 73）；
- 脚本（scratchpad，规划时都在）：`t31/formal.sh`、`t31/violations.sh`、`t31/analyze.sh`（调用 `t19/analyze.py`）、`t31/isolated.sh`、`t31/elapsed.py`、`t31/slow.sh`、`t33/protection.py`、`t33/deleted_names.py`、`t33/retrievals.py`、`t33/closure_refs.py`、`t07/diffcheck7.py`、`t21/methods.py`、`t31/names.py`；缺失时从对应日志末尾附的全文恢复；
- 以往的收口与处置：`t004-closure.md`～`t007-closure.md`、`t009-criteria.md`、`t009-logs-and-cases.md`、`t009-docs-and-appendices.md`、`t005-test-dispositions.md`、`t006-test-dispositions.md`、`task-07-conflicts.md`、`measurement/m3-summary.md`、`reroute-t008.md`。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **最终构建与正式全量**（§2；AC-020、RQ-083、SC-8）：`--no-incremental` 构建 0 警告 0 错误；带取证汇的正式全量一次（detailed logger），失败只有基线 2 条 `DimensionDungeonTests` 与 AGENTS.md 已登记的偶发项（判法按待裁定第 2 条）；表征 10 通过 0 跳过；取证反例类与对照以外 0、`exe:` 0；日志信号逐项对照 TASK-31。耗时与内存只作信息（F-69 续写），不是停止条件。
  2. **AC-009① 的逐条确认**（§3）：193 个方法在本次正式全量中都为通过，按行计的数与 TASK-31 相同（规划时 331）。
  3. **测试数量加法**（§4；AC-020、RQ-083、R3-3）：从基线 7872（`b76504bb`）逐单元加到 A 封存，每段写 N、D、R 与出处；终点等于本次正式全量的总数。
  4. **测试文件改动清单与表征**（§5；RQ-080、RQ-081、RQ-082、AC-019、SC-8）：`a02bce46` → `2bdb1afd` 改动的每个测试文件归类，「断言未改」的逐个列出理由，断言有变化的逐条指到处置记录或附录 G；线级测试文件单列；codec 向量、选角金样本与表征金样本未改；表征测试的断言自引入点起不变。
  5. **过渡机制清单** `verification/transition-mechanisms.md`（§6；根 SC-12）：每项写位置、存在理由、由 B 删除的方式（起点建议，待裁定第 4 条）、`AGENTS.md` 中的位置；附残留检索，证明清单之外没有旧门、旧管线行为、转发层或 `[Obsolete]`（口径按待裁定第 3 条）；与 `AGENTS.md` 的过渡态段落逐项一致。
  6. **AC／SC 状态总表**（§7）：A 的每条 AC（含跨 A／B 的 A 一半）与子契约的每条 SC，各写状态、条件与证据位置；附条件项如实写明条件与条件的去向。
  7. **遗留问题与 `follow-ups.md` 终态**（§8）：每条未关闭条目给出去向（子 Spec B／后续性能优化／交主控方另行立项／用户／外部审核／信息），A 内的条目全部关闭或写明移交；文首补 TASK-34 一行。
  8. **A 的最终汇报** `verification/a-final-report.md`（§9；根 SC-11）：结果、rev 2 规划停点三项默示决定的复述、可见行为变化、主控方授权的全部 Spec 修订与解读、共享契约变更、遗留问题及归属、过渡机制清单的引用、验证结论、AC／SC 状态总表。
  9. **T-009 收口** `verification/t009-closure.md`（§10）：Plan T-009 各项逐项对到 TASK-30～TASK-34 的证据；测试数量加法、测试文件改动清单的全文；交主控方的封存数据。
- Owned boundary：
  - **新建**：`verification/transition-mechanisms.md`、`verification/a-final-report.md`、`verification/t009-closure.md`；`verification/logs/task-34-*.txt`（含待裁定第 1 条取 A 时的原始输出 `logs/task-34-build-test-raw.txt`）。一次性脚本放 scratchpad `t34/`，全文附在对应日志末尾。
  - **修改**：`verification/follow-ups.md`（§8 列出的行、文首一行）。
  - **条件修改**：`AGENTS.md` 中写为过渡态的段落（规划时 6 处，`t009-docs-and-appendices.md` §13 T1～T6 所列位置），只在过渡机制清单与之不一致时改（TASK-33 主控方裁定第 9 条）；改动写进 Result，并重跑 TASK-33 的自身规矩检索。其余各节不改。
  - **不改**：`Server/`、`Tool/`、`protocol/`、`Docs/`、`Document/`；`spec.md`、`plan.md`、`TaskContract.md`、子契约与子路由决定、`multi-spec-map.md`；以往单元的收口文件、处置记录、`measurement/`、`baseline.md` 与 `tasks/TASK-01～TASK-33.md`；`attempts/` 下的 `spec-result.md`（封存步骤由主控方写）。
- Required checks：
  1. **冻结核对**（`logs/task-34-verify.txt` 开头、`logs/task-34-boundary.txt` 末尾各一次）：`git diff 2bdb1afd -- Server Tool` 为空；`git status --short` 中 `Server/`、`Tool/` 无条目；正式全量开始前再核一次（记进 `task-34-build-test.txt` 文首）。
  2. **核实**（`logs/task-34-verify.txt`）：§1 的 V1～V16 逐条记命令与结果；与规划不一致的按 §1 末的规则处理。
  3. **构建与正式全量**（`logs/task-34-build-test.txt`，§2）：先写预期再跑；命令、起止时刻、swap、RSS；逐项目总数、通过、失败、跳过与加法；失败逐条按待裁定第 2 条判定；表征 10／0；挂起转储 0；`Characterization/` 与 `Golden/` 未改。
  4. **取证**（`logs/task-34-violations.txt`）：`t31/violations.sh` 复制为 `t34/violations.sh`，证据目录改到 `t34/formal-ev`，预期类与数不变（73 ＝ 2 ＋ 1 ＋ 1 ＋ 69）；反例类与对照以外 0、`exe:` 0、没有测试帧 0、退出码 0。
  5. **日志信号**（`logs/task-34-build-test.txt` 文首一节）：`t31/analyze.sh` 复制改写，逐项对照 TASK-31：「必须为 0」的七项、执行器失败按类型逐条、发送端各行、生产者 Information 以上各行；预期是与 TASK-31 相同（代码相同），差异逐条归因到负载（例如 F-126 依先后的形态）或停下。
  6. **AC-009①**（`logs/task-34-protection-tests.txt`，§3）：`t33/protection.py` 复制为 `t34/protection.py`，只把「通过」的来源改为本次原始输出；193 个方法全部通过，未找到为通过的 0；总行数与 TASK-31 相同，不同则逐条列出。
  7. **测试数量加法**（`logs/task-34-test-counts.txt`，§4）：每段的总数取该单元的正式全量日志并写行号或日志首段，N／D／R 取处置记录；逐段写出加法；终点等于 Check 3 的总数；查不到 N／D 分列的段如实写「只有总数差」。
  8. **测试文件改动**（`logs/task-34-test-files.txt`，§5）：`git diff --name-status a02bce46 2bdb1afd -- Server/tests` 全文；`diffcheck7.py a02bce46` 在冻结点工作树上的输出；每个 M 文件归类，每处断言变化指到处置记录的那一行或附录 G 的 VC 编号；每个 D 文件指到删除它的单元与处置；codec 向量与金样本的 diff；`git diff f01dc226 2bdb1afd -- Server/tests/Dfo.Server.Networking.Tests/Characterization` 全文。指不到的为 0，否则按停止条件。
  9. **过渡机制与残留检索**（`logs/task-34-transition.txt`，§6）：§6.3 的 S1～S8 每条先写预期再跑；每个命中归到清单一项、附录 F「保留」栏一项、A 的常设机制、授权预留或「同名无关」之一；归不进的为 0，否则按停止条件。
  10. **过渡态一致**（同一日志）：清单每项在 `AGENTS.md` 中恰有一处写明「过渡形态」；`AGENTS.md` 中含「过渡」的段落或列表项都在清单里（`t33/retrievals.py` 的 R2 改写）。`AGENTS.md` 有改动时，再跑 TASK-33 Required check 5 的自身规矩检索（R1），12 个禁用模式仍为 0。
  11. **Spec 修订枚举**（`logs/task-34-spec-revisions.txt`，§9.4）：`b76504bb..HEAD` 中改过 `spec.md` 的每个提交，逐 hunk 列出，每个 hunk 归到 §9.4 表的一行（性质与授权出处）；归不进的为 0，否则按停止条件。
  12. **follow-ups 终态**（`logs/task-34-followups.txt`，§8）：清单中每一行的编号、状态与去向；状态为「未关闭」或「信息」的，去向都属于 §8.2 的六类之一；归属一栏里不再出现以 A 的单元（`T-00x`、`TASK-xx`、`M-x`）为唯一去向的未关闭条目。
  13. **收口引用核对**（`logs/task-34-closure-refs.txt`）：`t33/closure_refs.py` 复制为 `t34/closure_refs.py`，范围改为三份新文件；用例名、日志路径、F 编号、`文件:行` 都解析得到，0 处不符；另检查三份文件中被引为证据的每个用例名在本次正式全量中为通过。
  14. **边界与格式**（`logs/task-34-boundary.txt`）：`git diff --stat 51c294fa` 与 `git status --short` 只含 Owned boundary 的文件；`AGENTS.md` 若改，每个 hunk 归到清单的一项；`git -c core.whitespace=cr-at-eol diff --check` 无输出，新建文件 LF；无残留的 testhost／Host 进程，已执行 `dotnet build-server shutdown`。
- Dependencies：
  - TASK-31（冻结点 `2bdb1afd`）：日志面、保护用例、N 13／D 0／R 26 与 F-30、F-33 的移交；
  - TASK-32（`a5e3bccb`）：M-3 通过，AC-005 测量一半、AC-021 的记法；
  - TASK-33（`51c294fa`）：`t009-docs-and-appendices.md` §12、§13，AGENTS.md 的过渡态 T1～T6，`logs/task-33-protection-tests.txt` 的方法清单；
  - TASK-30（`122576d5`）：普查名单、请求级帧 4 项、共享语义变化（`t009-criteria.md` §9）；
  - **执行前提**：工作区除本文件外干净；待裁定各条有裁定；Script.pvf 在工作区根目录（V3）。
- Result review：required。
  - 本单元的两份主要产出是外部审核（GPT Astra）与子 Spec B 的直接输入：过渡机制清单漏一项，B 封存时的「检索为零」（根 SC-13）就少守一项；最终汇报漏一项 Spec 修订或可见变化，外部审核就在不知情的情况下批准。这两类错误没有任何自动检查能发现，Required checks 只能证明引用存在、加法闭合、检索有归类。
  - AC／SC 状态总表决定 spec-001 能否封存；一条「附条件」写成「满足」，封存结论跟着错，而封存后 A 不重开。
  - 正式全量的失败判定（待裁定第 2 条）与测试文件改动的归类是判断，不是机械比对；判宽了会把真实回归当偶发放过。
- Task-specific authorization：
  - **构建与测试**：本单元可以构建，并做**一次**正式全量；另可按 §2.4 做有限的隔离复跑，只为判定某个失败的身份，不替代正式全量。不改代码、不改测试。
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 冻结核对不为空，或工作区有本文件以外的未提交改动。
    2. 构建有警告或错误。
    3. 正式全量出现按待裁定第 2 条不能归入「基线失败」或「已登记偶发」的失败；总数不等于 8315 且差异不能由日志解释；表征测试不是 10 通过 0 跳过；取证反例类与对照以外不为 0 或 `exe:` 不为 0；AC-009① 的方法有一个未通过。
    4. 残留检索（§6.3）找到归不进清单、附录 F「保留」栏、A 的常设机制或授权预留的旧门、旧管线行为、转发层或 `[Obsolete]`——那意味着 A 不满足 SC-12，要改代码，属返工单元。
    5. 测试文件中有断言变化指不到任何处置记录，或线级断言变化对不上附录 G（RQ-082）。
    6. `spec.md` 的某处修订找不到授权出处（§9.4）。
    7. 需要改 Owned boundary「不改」中的任何文件，或需要改 Spec 的验收、附录内容或共享契约。
  - 性能不是停止条件：正式全量的耗时、RSS 与 swap 只记录（F-69），M-3 的结论不重判。
  - 不 commit，不 `git stash`，不 push。

## Detailed Plan

### 1. 基准核实（执行时重跑并落盘 `logs/task-34-verify.txt`）

| # | 问题 | 检索 | 规划时的结果（HEAD `51c294fa`） |
|---|---|---|---|
| V1 | 工作区与冻结 | `git log -3 --oneline`；`git status --short`；`git diff 2bdb1afd -- Server Tool` | HEAD `51c294fa`；未跟踪的只有本文件；diff 为空 |
| V2 | 冻结点之后的提交只动文档 | `git diff --stat 2bdb1afd 51c294fa` | 只有 `.rks-agent/`、`AGENTS.md`、`Docs/Agent/ArchitecturePhilosophy.md`（TASK-32、TASK-33） |
| V3 | 正式全量的前提 | `ls Script.pvf`；`dotnet --version`；`sysctl -n vm.swapusage`；有无残留 testhost／Host 进程 | Script.pvf 在工作区根目录；其余执行时记 |
| V4 | 脚本 | `ls` 输入一节列的脚本 | 规划时都在 |
| V5 | 对照基准的数 | 读 `logs/task-31-build-test.txt` 文首 | 8315 ＝ HttpApi 57 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 188 ＋ Application 4594 ＋ Host 197；通过 8295、失败 2、跳过 18；表征 10／0；取证 73 |
| V6 | AC-009① 清单 | 读 `logs/task-33-protection-tests.txt` 第 1～11 行与「## method list」 | 193 个方法、331 行；`protection.py` 读 `t31/formal-raw.txt` 里的「已通过 <name>」 |
| V7 | 过渡态 T1～T6 在 AGENTS.md 中的位置 | `grep -n '过渡' AGENTS.md`；读 `t009-docs-and-appendices.md` §13 第 1 条 | `过渡` 6 处，与 T1～T6 一一对应（TASK-33 R2） |
| V8 | 管线行为 | `ls Server/src/Dfo.Server.Application/Commands/Pipeline` | 5 个：`CommandLoggingBehavior`、`SelectedSessionExecutionBehavior`、`SelectedSessionSettlementBehavior`、`UnitOfWorkBehavior`、`CommandExecutionPolicyBehavior` |
| V9 | `AsyncLocal` | `grep -rn 'AsyncLocal<' Server/src --include='*.cs'` | 8 处：`GameplayJob.Running`、`DomainTransactionScope.ambient`、`SelectedSessionCommandScope.current`（`SelectedSessionExecutionBehavior.cs:40`）、`CommandBus.DispatchDepth`、`NetworkCommandAuthorization.current`、`EffectOperationContext` 三个 |
| V10 | `[Obsolete]` | `grep -rn 'Obsolete' Server/src Server/tests --include='*.cs'`（排除 `bin`、`obj`） | `Server/src` 0 处；`Server/tests` 执行时查 |
| V11 | A 新增的类型 | 在 `a02bce46` 与 `2bdb1afd` 上 `git grep -h -oE '(class\|record\|struct\|interface\|enum)[[:space:]]+[A-Z][A-Za-z0-9_]*' -- Server/src`，取后者有、前者无的名字 | 规划时粗检索 101 个（含嵌套与私有类型，如 `Slot`、`Run`、`Item`）；执行时用脚本按声明位置去重 |
| V12 | 自称过渡的代码注释 | `grep -rn -i 'transitional\|过渡\|interim' Server/src --include='*.cs'` | 2 处：`NetworkMessageDispatcher.cs:20`（「no frame, let it through」分支，请求级上下文的过渡形态）、`CharacterOptionsDomainService.cs:21`（GH #129 的不透明存储，与执行器无关） |
| V13 | 测试文件改动 | `git diff --name-status a02bce46 2bdb1afd -- Server/tests` | A 112、D 7、M 363；D 为 `MultiAccountExecutionTests`、`OnlineUserExecutionGateTests`、`SelectedSessionTransitionGateTests`、`NotificationRetryDrainServiceTests`、`NotificationRetryTests`、`TouchSocketNetworkMessageSenderExplicitDeliveryTests`、`ExactConnectionCloseTests` |
| V14 | 表征与金样本 | `git diff f01dc226 2bdb1afd -- Server/tests/Dfo.Server.Networking.Tests/Characterization`；`git diff a02bce46 2bdb1afd -- Server/tests/Dfo.Server.Protocol.Tests/Game/SelectCharacter/SelectCharacterAcceptedTranscriptTests.cs` | 表征目录只有 `SelectCharacterCompensationCharacterizationTests.cs` 两行：b1、b2 去掉 `Skip`（TASK-23，`50d7bce4`）；`Golden/` 未改。选角金样本执行时查 |
| V15 | `spec.md` 的修订提交 | `git log --format='%h %s' b76504bb..HEAD -- .rks-agent/tasks/serial-gameplay-executor/spec.md` | 15 个提交：`53c72543`、`1eea890f`、`bfbcf012`、`6331b23a`、`c3115f9a`、`f13e612a`、`0c9c59c8`、`09e4983c`、`50d7bce4`、`1ee1c91e`、`f7bf0aa3`、`bc735cd0`、`a9c79e98`、`122576d5`、`51c294fa`；`b76504bb..HEAD` 合计 ＋199／－65 |
| V16 | follow-ups 的未关闭条目 | 按表格逐行解析状态一栏（不以「已」开头的） | 清单 128 行，已关闭 78；其余 50 行 ＝ 「未关闭」35 ＋ 「信息」11 ＋ 部分关闭 4（F-05、F-10、F-12、F-58，状态以「……一半已在 TASK-xx 关闭」开头）；35 ＋ 11 ＋ 4 ＝ 50，78 ＋ 50 ＝ 128；§8.1 按此列 |

V1、V2 与本表不一致时停下回报（停止条件 1）。V3～V16 有出入的，纳入对应各节并在 Result 说明；落到停止条件的，按停止条件处理。

### 2. 最终构建与正式全量（Outcome 1）

#### 2.1 做法

- 脚本：`t31/formal.sh` 复制为 `t34/formal.sh`，只改输出目录（`t34/formal-raw.txt`、`t34/formal-ev`）。命令不变：
  - `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`
  - `DFO_GAMEPLAY_ASSERTIONS=<t34/formal-ev> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`
- 开始前：无残留 testhost／Host 进程；记 swap；最后一次冻结核对写进日志文首。结束后：`dotnet build-server shutdown`。
- 原始输出按待裁定第 1 条落盘；`logs/task-34-build-test.txt` 的文首照 `logs/task-31-build-test.txt` 的写法：构建结果、起止与耗时、swap 与 RSS、逐项目加法、失败、跳过、表征、挂起转储、日志信号、取证摘要、F-69 的耗时对比。

#### 2.2 预期（运行前写进日志）

代码与 TASK-31 的正式全量相同（只差 TASK-31 评审后的一处空格），所以：
- 总数 8315，逐项目与 V5 相同；通过 8295、失败 2（基线 `DimensionDungeonTests`）、跳过 18；
- 表征 10 通过 0 跳过；
- 取证 73 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 69；反例 66 ＋ 对照 7；
- 日志信号与 TASK-31 相同；发送端各行的条数可能随负载变（F-126：对端重置时是否带后缀取决于先后），逐条归因。

#### 2.3 失败的判定（按待裁定第 2 条）

每个失败先取身份（AGENTS.md「判据偶发失败」），再归类：
1. 基线已有：两条 `DimensionDungeonTests`，消息与 `baseline.md` 相同；
2. AGENTS.md 已登记的偶发：SQLite 并行释放（三样一起看，栈根在表内）；F-125（用例名、`InvalidDataException` 且消息含 `[minimum level]`、栈根在 `PvfEquipmentDefinitionSource` 的那一行，单独复跑 5 次全过）；
3. AGENTS.md「构建与运行」写明的满载假红：`Host.Tests` 或 `Networking.Tests/Integration/` 的用例，报 `TaskCanceledException`／`OperationCanceledException` 而非断言不符，单独跑该项目全过；
4. 其余：停止条件 3。

第 2、3 类在日志里逐条写身份与复跑结果；它们不算 RQ-083 的违反，也不重跑正式全量。

#### 2.4 隔离复跑

只在 §2.3 第 2、3 类需要时做：`t31/isolated.sh` 复制改写，只跑涉事项目（满载假红）或涉事用例（F-125 五次、SQLite 释放一次）。结果写进 `logs/task-34-isolated.txt`；没有需要就不建这个文件，在 Result 说明。

#### 2.5 F-69

`t31/elapsed.py`、`t31/slow.sh` 复制改写，与 TASK-31 逐用例对比耗时，列出多出 ≥ 9 秒的用例（F-69 的口径）。只作信息，写进 F-69 一行的续写；本单元之后由子 Spec B 续写。

### 3. AC-009① 的逐条确认（Outcome 2）

- `t33/protection.py` 复制为 `t34/protection.py`：选取规则、匹配规则与比对不改，只把「通过」的来源换成 `t34/formal-raw.txt`（或待裁定第 1 条的落盘位置）。
- 预期：选出 195，冻结点上 193 个在，断言比对结果与 TASK-33 相同（181 相同、12 不同、2 删除，代码未变）；193 个全部为通过，331 行。
- 输出全文写进 `logs/task-34-protection-tests.txt`；AC 状态总表中 AC-009（A 一半）的条件「①的通过待最终全量确认」据此解除或保留。

### 4. 测试数量加法（Outcome 3）

#### 4.1 口径

- 计数单位：实际运行的例数，Theory 按行计（与以往各单元相同）。
- N：新增例数；D：删除例数；R：改名（方法集合 －R ＋R，例数不变；TASK-31 裁定第 4 条 (b)）。只改断言、不增删的不计入 N、D，归到 §5。
- 每段写「起点 ＋ N － D ＝ 终点」，出处是该单元的正式全量日志与处置记录；评审后另加的例数单列一段（如 TASK-24 ＋2、TASK-26 ＋1、TASK-30 ＋1）。

#### 4.2 已有的段（规划时读到的数，执行时逐段按日志核对）

| 区间 | 加法 | 出处 |
|---|---|---|
| 基线 | 7872 | `baseline.md`（`b76504bb`） |
| TASK-01～TASK-19 | 7872 → … → 8213（TASK-18）→ 8224（TASK-19） | 各单元 Result 与 `logs/task-0x～task-19-build-test.txt`；`t004-closure.md` §5（8213、8224）。**规划时未逐段核对**：TASK-01 的检查表里有中间值（如 7881，「第 2 版代码，未拆分 b2」），取该单元最终那次正式全量；TASK-01～TASK-17 的 N、D 要从各 Result 与处置记录（`task-07-conflicts.md` §14、§15）取。各日志里用 `grep` 粗取到的「＝ 8651」「＝ 8685」一类数不是总数，一律以 `dotnet test` 的逐项目汇总行为准 |
| TASK-21～TASK-22 | 8224 → 8228 → 8192 | `t005-test-dispositions.md`；`t005-closure.md`（8192） |
| T-006（TASK-23～TASK-27） | 8192 → 8194（TASK-22 评审后 ＋2）→ 8191 → 8229 → 8231 → 8232 → 8238 → 8239 → 8247 | `t006-closure.md` §7 与 §12 第 7 条（已有逐段加法） |
| T-007（TASK-29） | 8247 ＋ 15 － 0 ＝ 8262 | `t007-closure.md` §5.1 |
| TASK-30 | 8262 → 8301 → 8302（评审后 ＋1） | `logs/task-30-build-test.txt` 文首；`t009-criteria.md` §8 |
| TASK-31 | 8302 ＋ 13 － 0 ＝ 8315；R 26 | `t009-logs-and-cases.md` §7.4 |
| TASK-32、TASK-33、本单元 | 不改测试，＋0 | 本单元正式全量 8315 |

TASK-03、TASK-13、TASK-20、TASK-28 不跑全量或不改测试数，逐个写明（TASK-13 只跑隔离）。

#### 4.3 写法与闭合

- 脚本 `t34/counts.py` 从各正式全量日志中取逐项目总数（`dotnet test` 的汇总行），与处置记录的 N、D 比对：每段「起点 ＋ N － D」等于日志的终点。
- 某段只能从日志读到总数、处置记录里没有 N、D 分列的，写「只有总数差 Δ」并注明出处缺口，不凑数；这类段在 `t009-closure.md` 中列出。
- 全程合计写成一行：7872 ＋ ΣN － ΣD ＝ 8315，ΣN、ΣD 各自写出逐段相加的式子（AGENTS.md「两条从踩坑里换来的记法」第 1 条）；ΣR 单列。
- 逐项目的终点（8 个项目）与 `baseline.md` 的逐项目起点并列。

### 5. 测试文件改动清单与表征（Outcome 4）

#### 5.1 全部改动的测试文件（RQ-081、RQ-083、SC-8）

- 在冻结点工作树上跑 `python3 t07/diffcheck7.py a02bce46 --json t34/dc.json --lines`（工作树的 `Server/tests` 即冻结点）。
- 每个 M 文件归为：
  - **(a) 断言未改**：测试方法多重集合与断言行多重集合都相同，残余只有白名单形状（包进 `TestGameplay`、删除的准备数据或测试接缝、`using`）。逐个列出文件与理由类别（SC-8「只因准备数据或测试接缝被删除而改动的文件」即此类）；
  - **(b) 方法增删或改名**：计入 §4，逐个指到处置记录；
  - **(c) 断言有变化**：每处变化指到 `t005-test-dispositions.md`、`t006-test-dispositions.md`、`task-07-conflicts.md`、`t009-criteria.md` §3、`t009-logs-and-cases.md` §1.3、§7 中的那一行；以被替换的发送或分发语义为测试对象的，按 SC-8 归到「测试数量与断言变化」并写明替代它的用例。
- 7 个 D 文件逐个指到删除它的单元与处置（V13）。
- 指不到的为 0，否则停止条件 5。

#### 5.2 线级测试文件（RQ-081）

- 范围：`Dfo.Server.Networking.Tests` 整个项目，加上 `Dfo.Server.Host.Tests` 中经真实 TCP 或打包进程的文件（检索 `TcpClient`、`Socket`、`EndToEndHostHarness`、`ProcessStartInfo`）。
- 这部分从 §5.1 的结果里单列成表：文件、改动类别、断言变化与对应的处置或 VC 编号。规划时 Networking.Tests 中 M 11 个（`Integration/` 9 个与 `Runtime/PreparedBatchTests.cs`、`Dfo.Server.Networking.Tests.csproj`）、D 2 个。
- RQ-082：线级断言中凡表示客户端可见变化的，都对到附录 G 的 VC-1～VC-10 之一（证据出处 `t006-closure.md` §4、TASK-27、`t009-docs-and-appendices.md` §8）；对不上即停止条件 5。

#### 5.3 codec 向量与金样本（RQ-081）

- 选角金样本 `SelectCharacterAcceptedTranscriptTests.cs` 与表征 `Golden/` 目录：`a02bce46`（表征为 `f01dc226`）→ `2bdb1afd` 的 diff 为空，且在正式全量中通过。
- codec 向量：按 `research/05-tests.md` 第 33 行的口径（codec／帧／负载／转写测试），用文件名模式 `*Codec*Tests.cs`、`*Payload*Tests.cs`、`*Frame*Tests.cs`、`*Transcript*Tests.cs` 选出，列出数量并与 research 05 的「82 个文件」对照（差异逐个说明）；它们的 diff 为空，或只属 §5.1 (a) 类；调 `ComposeAsync` 的 9 个文件若有改动，按 RQ-081 末句逐条说明。

#### 5.4 表征测试（AC-019、RQ-080）

- 引入点 `f01dc226`，基线运行日志 `logs/full-with-characterization.txt`；冻结点上的 diff 只有 b1、b2 去掉 `Skip`（V14），断言文本不变；本次正式全量 10 通过 0 跳过。
- b1、b2 在基线上是「基线缺陷」而跳过的（TASK-01 的设计：RQ-031(c) 的严格形式，由 T-006 开启），这一点在 AC-019 的状态里按待裁定第 6 条写。
- 附录 G 允许的差异：表征的断言没有因 VC 改动；VC-1 的证据是金样本 a 不变（`t006-closure.md` §4），逐条写进 AC-019 一行。

### 6. 过渡机制清单 `verification/transition-mechanisms.md`（Outcome 5）

#### 6.1 起点

`t009-docs-and-appendices.md` §13 第 1 条的 T1～T6，加上前几个单元点名、要进清单的：

| # | 过渡机制 | 规划时的出处 | AGENTS.md 中的位置 |
|---|---|---|---|
| T1 | `ICommandBus.SendAsync` 在作业里内联执行，执行器外入队并等待（`bus:<命令类型名>`） | Plan 共享决定；R3-2 | 命令执行总线第 2 条 |
| T2 | handler 在入站作业内经总线顺序派发多条细粒度命令，每条 Transactional 命令一个事务；`SelectCharacterHandler` 的多阶段工作流与补偿命令 | R3-1 RQ-024、RQ-100 | 命令执行总线末条 |
| T3 | 管线五层（0／45／58／60／100）包在每一条细粒度命令外 | R3-1 附录 H；TASK-33 评审 L3 | 命令执行总线管线一条 |
| T4 | 请求级上下文是作业内的 AsyncLocal 帧：`NetworkCommandAuthorization`、`SelectedSessionCommandScope`、`EffectOperationContext`、`CommandBus.DispatchDepth`；`NetworkMessageDispatcher` 在作业开始时建帧，「no frame, let it through」分支（V12） | `t009-criteria.md` §9；附录 B「请求级帧（过渡）」；E3 豁免 | 执行器一节请求级上下文一段 |
| T5 | 细粒度命令失败时 `CommandLoggingBehavior` 与执行器各记一条 `Error`（F-30 命令层一半） | TASK-31 主控方裁定第 5 条 | 执行器一节「失败与日志」 |
| T6 | 失败作业关闭来源连接依赖作业已打开发件箱（F-110 结构保证一半） | `follow-ups.md` F-110 | 执行器一节「发件箱」 |

执行时核对：T2 是否应把「`IAccountCommand` 今天的用途」与 45／58 所依赖的命令标记接口（`ISelectedSessionCommand`、`IRequestSettlementCommand`、`IMultiSelectedSessionCommand`）写进同一项或另立一项（它们随细粒度命令与 45／58 行为一起由 B 处置）；`EffectOperationLedger`、`SelectedSessionSafety` 的断言随 B 迁移（`t009-criteria.md` §9）是否属于 T4。拆或并以「B 删除时是不是同一个动作」为准，结果在 Result 说明。

#### 6.2 每项的写法

| 栏 | 内容 |
|---|---|
| 位置 | 冻结点上的 `文件:行`（类型、成员或注册点），可检索的标识符 |
| 今天的行为 | 一两句，与 `AGENTS.md` 对应段落同义 |
| 存在理由 | 为什么 A 封存时仍在：A 的范围到此为止（R3-1 把它划给 B）、或删它要以 B 的某项为前提 |
| 新代码不要依赖 | 照 `AGENTS.md` 的写法 |
| 由 B 删除的方式（起点建议） | 引 `spec.md` R3-1 对应条目、Plan 共享决定原文与附录 H 的去向；写成「建议」，不替 B 的 Spec 做设计（待裁定第 4 条） |
| B 封存时的检索 | 根 SC-13 要求「检索为零」：给出该项可执行的检索式（类型名、成员名、注册行），B 封存时照跑 |

#### 6.3 残留检索（SC-12「清单之外没有……」；口径按待裁定第 3 条）

每条先写预期，结果逐个命中归类（清单项／附录 F「保留」栏／A 的常设机制／授权预留／同名无关）：

| # | 检索 | 范围 | 预期 |
|---|---|---|---|
| S1 | `[Obsolete]`、`Obsolete(` | `Server/src`、`Server/tests` | 0 |
| S2 | A 删除的名字：`t33/deleted_names.py` 的 49 个 | `Server/src` | 只有 `Lease` 的 22 行同名无关（TASK-33） |
| S3 | 并发原语：`SemaphoreSlim`、`lock (`、`lock(`、`Monitor.`、`ReaderWriterLock`、`Mutex`、`SpinLock`、`AsyncLock`、`Gate` 后缀的字段与类型 | `Server/src` | 全部归到附录 F「保留」栏（短 `lock` 184／11／3、PVF 初始化锁、`PasswordComputationLimiter`、SQLite 写保留）或同名无关；`LockRemovalSourceTests` 的名单与信号量名单为判据 |
| S4 | 管线行为：`ICommandPipelineBehavior` 的全部实现与注册 | `Server/src` | 恰为 V8 的 5 个，全部在 T3 |
| S5 | `AsyncLocal<` | `Server/src` | 8 处（V9）：T4 的 4 个类型 6 处；`GameplayJob.Running`（A 的常设机制，作业身份）；`DomainTransactionScope.ambient`（环境事务，A 之前就有、AGENTS.md「领域事务」记载，不是过渡） |
| S6 | 自称过渡的注释：`transitional`、`过渡`、`interim`、`temporary`（只看与执行、投递、命令相关的） | `Server/src` | V12 的 2 处：`NetworkMessageDispatcher.cs:20` 归 T4；`CharacterOptionsDomainService.cs:21` 同名无关（GH #129） |
| S7 | A 新增的类型（V11） | `Server/src` | 逐个归为 A 的常设机制（执行器、发件箱、发送端、调用链、判据接缝、口令哈希限流、预热、HTTP／GM 路由过滤器等）、授权预留（`GameplayShardKey`、`GameplayShardSelector`：分流键预留，根 Scope Out 第 1 项）或清单项；「转发层」按待裁定第 3 条的定义判（例如 `OutboxNetworkMessageSender`、`OutboxConnectionCloser` 是把既有发送接口接到发件箱的登记适配器，是 A 的投递设计本身，不是过渡） |
| S8 | A 之前就有、按 R3-1 应由 B 删除或改写的类型 | `Server/src` | 只在清单里出现：管线五个行为、请求级帧三个类型、`CommandBus` 的内联分支、命令标记接口 |

归不进的命中：停止条件 4。

#### 6.4 与 `AGENTS.md` 的一致

- 清单最终的每一项，在 `AGENTS.md` 中恰有一处写明「这是过渡形态」；`AGENTS.md` 中凡写「过渡」的段落或列表项都在清单里（Required check 10）。
- 清单比 T1～T6 多出或少了某项（例如 §6.1 末的拆分），按 TASK-33 主控方裁定第 9 条改 `AGENTS.md` 的那几段：照 `AGENTS.md`「记什么，不记什么」写成「今天如此；这是过渡形态，新代码不要依赖 X」，不写由谁、何时删除，不写编号。改完重跑自身规矩检索。
- 一致时 `AGENTS.md` 不改。

### 7. AC／SC 状态总表（Outcome 6；写进 `a-final-report.md` §9）

#### 7.1 状态词

「满足」「满足，附条件」「未满足」三种。附条件写明：条件是什么、为什么接受、条件的去向（F 编号或外部审核）。「满足」只在证据都在冻结点上、证据用例在本次正式全量中通过时用。

#### 7.2 AC（A 的全部，含跨 A／B 的 A 一半）

| AC | 状态的出处 | 本单元补的 |
|---|---|---|
| AC-001、AC-002 | 没有收口记录的状态行：`tasks/TASK-04.md` Outcome 与检查表（`GameplayExecutorOrderTests`、自我等待反例）；AC-002「等待网络」一半在 T-006（`t006-closure.md` §6 RQ-008 行、N4） | 写出证据用例在冻结点上的位置与本次通过 |
| AC-003、AC-004、AC-015 | `t004-closure.md` §3；AC-015 另有 `t005-closure.md` §5 | 本次通过 |
| AC-005 | 执行位置一半 `t004-closure.md` §3；测量一半 `measurement/m3-summary.md` §1（「M-3 满足」） | 合成一行 |
| AC-006、AC-014、AC-017、AC-022（A 一半）、RQ-024（A 一半）、RQ-100（A 一半） | `t009-docs-and-appendices.md` §12 | 照抄并注明 AC-022 的检索范围（TASK-33 评审、裁定第 2 条） |
| AC-007、AC-011、AC-012、AC-013 | `t006-closure.md` §5、§6（AC-011、AC-012、AC-013 均「满足，附条件」） | 条件逐条写出与去向（F-108、F-112、F-113 等） |
| AC-009（A 一半） | ①②④ `t009-docs-and-appendices.md` §10、§7；③ `t009-logs-and-cases.md` §5 | ①的「通过」按 §3 解除附条件或保留 |
| AC-016 | `t007-closure.md` §1 | 本次通过 |
| AC-018（A 一半） | `t009-criteria.md` §3、§4 | — |
| AC-019 | 本单元 §5.4 | 待裁定第 6 条 |
| AC-020 | 本单元 §2、§4、§5 | — |
| AC-021（A 一半） | `measurement/m3-summary.md` §1 | — |
| VC-1～VC-10 | `t006-closure.md` §4；TASK-27（VC-9）；`t009-docs-and-appendices.md` §8（VC-10，附条件） | 每条一个证据用例并本次通过 |

AC-008、AC-010 与 AC-009、AC-018、AC-022 的 B 一半不在表内，注明「移交子 Spec B（R3-1）」。

#### 7.3 SC（子契约 SC-1、SC-3～SC-12）

每条一行：子契约摘要 → 根契约同号行的 Required evidence → 对应的 AC 与收口记录 → 状态。规划时的对应：

| SC | 对应 |
|---|---|
| SC-1 | AC-001、AC-003、AC-004、AC-006；附录 A／B 对账（`t009-docs-and-appendices.md` §3、§4）；RQ-061 判据（`t009-criteria.md` §1、§2） |
| SC-3 | AC-011、AC-012、AC-013；`t006-closure.md` |
| SC-4 | AC-017；`t005-closure.md` §2、§3；附录 F 对账（`t009-docs-and-appendices.md` §6）；本单元 S2、S3 |
| SC-5 | AC-015；`t004-closure.md` §3 RQ-040～RQ-043；`t007-closure.md`（重试继承链） |
| SC-6 | AC-016；`t007-closure.md` |
| SC-7 | AC-014；附录 E、G；VC-1～VC-10 |
| SC-8 | AC-019、AC-020；本单元 §2～§5；选角端到端证据 `SelectCharacterSuccessCharacterizationTests`（金样本 a） |
| SC-9 | AC-005 测量一半、AC-021；`measurement/m3-summary.md` |
| SC-10 | RQ-100、AC-022 的 A 一半；`t009-docs-and-appendices.md` §1、§11 |
| SC-11 | `a-final-report.md` 本身（含三项默示决定的复述，R3-4） |
| SC-12 | `transition-mechanisms.md` 与 `logs/task-34-transition.txt` |

### 8. 遗留问题与 `follow-ups.md` 终态（Outcome 7）

#### 8.1 规划时的未关闭条目（V16）与建议去向（待裁定第 5 条）

| 去向 | 条目 |
|---|---|
| 子 Spec B | F-07、F-12（附录 C 一半）、F-34、F-58（复核一半）、F-86、F-88、F-93、F-109、F-110（结构保证，T6）、F-112、F-122（改名）、F-127（首个单元顺带修，TASK-32 评审 L3）、F-128 ①（AGENTS.md 端到端示例，TASK-33 评审 L2）；建议新增归 B：F-118 是否设判据、F-124、Channel 入站 F-30 降级无用例（TASK-31 评审发现 2）、F-30 命令层一半（T5） |
| 后续性能优化（交主控方立项） | F-77～F-82、F-120 |
| 交主控方另行立项 | F-01（重复路径字典）、F-113（人群项）、F-114（无生产写入方字段的长度约束）、F-116 (a)(b)、F-125 生产缺口、F-128 ②③（RQ-100 点名范围外的两份文档）；测试基础设施与取证缺口：F-37、F-38、F-39、F-45、F-55、F-71、F-103 |
| 用户 | F-117（队伍标题的客户端上限，需 Windows 端客户端或内存转储） |
| 外部审核 | F-107（GM Cera 对外字段）、F-108（AC-011 线级第 3 条证据降级）；F-33 只读情形偏离 TASK-14「只用内存」（已接受，交审时说明） |
| 信息（不需要动作） | F-40、F-41、F-42、F-44、F-46、F-70、F-95、F-96、F-100；条件触发的 F-06（预热识别边界，人工把关）、F-43（增加修改 GM 口令的写入方时） |
| 本单元关闭 | F-69 的 A 一半（本单元续写最后一次，之后由 B 续写）；F-05、F-10 未覆盖的一半已转 F-82，状态改为「已关闭（转 F-82）」 |

执行时按 §1 V16 的完整解析核对：有遗漏的补进对应一类；一条归属在多类的拆成两半写。

#### 8.2 改法

- 每条未关闭条目的「归属」一栏末尾加「〔A 封存：→ <去向>〕」，原文保留（仿改道 R3 的标记写法）；状态一栏不改原文，只在本单元关闭的条目前写「**已在 TASK-34 关闭**」。
- 文首补 TASK-34 一行：日期、基准、正式全量的数、关闭与更新的编号、去向的汇总（每类的条目数与加法）。
- 新发现的事项新增编号（从 F-129 起）。

#### 8.3 进最终汇报的遗留问题

`a-final-report.md` 的遗留问题一节按 §8.1 的六类分组，每条一句话加 F 编号；另列「判据与证明的已知边界」：F5（`CharacterContainerDomainService` 三个查找按读登记的有意例外）、F3（普查认不出藏在其他程序集类型后面的状态，`t009-criteria.md` §10）、F-06、`t006-closure.md` §11 的证明边界、VC-10 的组合证据、F-33 的推理证据。

### 9. A 的最终汇报 `verification/a-final-report.md`（Outcome 8）

读者是外部审核与用户。按 `AGENTS.md`「记什么，不记什么」：写结论与今天靠什么撑着，不写过程；每个论断给证据位置；未关闭事项引 F 编号。

#### 9.1 结构

1. **结果**：A 交付了什么（一个执行器、全部入口为作业、提交后发件箱与每连接发送队列、删锁、调用链与深度上限、判据），与今天的边界（过渡机制见清单）。附一张请求走向的小图（与 `AGENTS.md`「架构设计」一致，不另起说法）。
2. **规划停点的三项默示决定**（根 SC-11、R3-4）：单执行器不分流（只预留分流键，RI-4）、短 `lock(sync)` 与 PVF 内容缓存锁暂留（RI-8）、不换数据库也不回内存副本（RI-9）。每项写：Agent 当时的提议与理由（根契约 Root Intent Coverage 与 Scope Out 第 1～3 项）、用户的回应（「看着没问题了」，根契约原文）、A 封存时的事实（分流键 `GameplayShardKey`／`GameplayShardSelector` 只返回同一执行器；短锁计数 184／11／3；持久化仍是每段 SQLite 事务）、对 B 的含义。注明任务目录内没有 rev 2 规划停点汇报的落盘记录，本节就是证据（R3-4，`reroute-t008.md` 主控方裁定第 7 条）。另列 rev 2 阶段 Spec 中的可见变化 VC-1～VC-8（`b76504bb` 上的原文），与今天的文字对照。
3. **可见行为变化**：VC-1～VC-10 逐条：今天的文字、与 rev 2 文字的差异（VC-3 扩展到两塔、VC-7 补「踢出队员」与 `ExactSessions` 不属于本条、VC-9、VC-10 新增）、证据用例、授权出处。
4. **Spec 修订与解读**（§9.4）。
5. **共享契约与语义的变化**：`NotificationOutcome.Delivered` 的含义（F-122）；`IGameMessageSender`／`INetworkMessageSender` 由「发送」变为「登记进发件箱」；`ICommandBus.SendAsync` 的两种语义（T1）；编码在登记点完成、F-97「先判位置后编码」推翻 TASK-23 裁定的次序部分（`t009-criteria.md` §6）；附录 F「改为执行器内访问」扩大为普查名单（`t009-criteria.md` §1 末）；队列层泄漏流拒绝（TASK-29 主控方裁定第 2 条）；RQ-053(d)；GM Cera 对外字段（F-107）；队伍标题上限 256 B（VC-9，F-117）。
6. **验证结论**：本次正式全量的数与判定；取证；表征与金样本；M-3 的结论（`m3-summary.md` §1 引用，不重抄数字表，只写结论句与最关键的三个数）；判据清单的出处（`t009-criteria.md` §4）。
7. **过渡机制清单**：引用 `transition-mechanisms.md`，列项名与一句话，不重抄位置。
8. **遗留问题及归属**（§8.3）。
9. **AC／SC 状态总表**（§7）。

#### 9.2 写法约束

- 数字一律写口径与出处；加法写全（§4.3）。
- 「满足，附条件」的条件写在表内，不只写在脚注（TASK-33 评审：外部审核只看到表）。
- 外部审核需要知道而文件内又没有的上下文（例如「A 封存后不重开，Astra 对 A 的意见按根契约另立修正子 Spec 或并进 B」），在文首用一段写明，出处根契约 RI-16 与 `plan.md` R3「Next Planning Boundary」。

#### 9.3 篇幅

正文以「可被审核逐条核对」为准，不复述收口记录的推导；每节先给结论再给证据位置。

#### 9.4 Spec 修订与解读的枚举

- 修订：`b76504bb..HEAD` 中改过 `spec.md` 的 15 个提交（V15），逐 hunk 列出（Required check 11），归到下表一行。规划时已知的项：

| 修订 | 提交 | 性质 | 授权 |
|---|---|---|---|
| 附录 D、附录 A 事件链统计等实施时补表（TASK-02、TASK-09、TASK-14、TASK-15、TASK-16、TASK-18 的附录行） | `53c72543`～`0c9c59c8` | 附录对账（附录自己写着「实施时补入」） | 各单元主控方裁定（执行时逐个核对） |
| RQ-091 接受线改为单条 CMD ≤ 2 s | `c3115f9a` | 验收阈值修订 | 用户（2026-10-22），RI-15 |
| VC-7 补「踢出队员」与 `ExactSessions` 不属于本条 | `09e4983c` | 可见变化文字 | TASK-21 处置 L1 |
| 附录 A 第 4 行；VC-3 扩展到两塔进入 | `50d7bce4` | 事实性更正；放宽可见变化 | TASK-23 主控方裁定与评审 F1 处置（`t006-closure.md` §10） |
| RQ-031(d) 末句、AC-011 第 4 条 | `1ee1c91e` | 事实性更正 | TASK-24 主控方裁定第 1 条 |
| VC-9 | `f7bf0aa3` | 新增可见变化 | TASK-27 主控方裁定第 3 条 |
| R3（改道、A／B 拆法） | `bc735cd0` | 验收边界收窄 | 用户 2026-10-23 的结构决定；三份审查通过 |
| RQ-053(d) | `a9c79e98` | 新增需求子项 | TASK-29 主控方裁定第 1 条 |
| 附录 F 两行、附录 B 两行与「请求级帧（过渡）」一行 | `122576d5` | 附录对账与名单扩大 | TASK-30 主控方裁定第 1 条 |
| R4：附录 A 第 3、5、8 行，附录 B 三行，附录 F 短 `lock`，VC-10 | `51c294fa` | 附录事实性修订；新增可见变化 | TASK-30 主控方裁定第 3 条；TASK-33 主控方裁定第 1、5 条 |

- 解读（文字未改）：`t006-closure.md` §10 的 RQ-012／RQ-031(c)「此前登记的消息」按连接计、RQ-034「整批编码」在登记点、VC-6 的取值、AC-011 线级第 3 条证据降级、AC-013 人群项的记法；`t005-closure.md` §8 的已接受差异；RQ-061 只拦写、不设启动期例外（F-16、F-57）；定时源按 RQ-050 字面保持新根（F-28）；M-1～M-3 判定口径（S3 按 2 s 口径、规则 P）；AC-019 的 b1、b2（待裁定第 6 条）。每条写出处与授权。
- 找不到授权出处的修订：停止条件 6。

### 10. T-009 收口 `verification/t009-closure.md`（Outcome 9）

#### 10.1 内容

- 文件头：日期、冻结点、基准、用途（封存与完整结果评审的输入）、写法（仿 `t006-closure.md`）。
- §1 Plan T-009（R3 修订后的内容）逐项：要求 → 单元 → 证据 → 状态；覆盖栏（RQ-061；RQ-023、RQ-024、RQ-071、RQ-072、RQ-100 的 A 一半；RQ-080～RQ-083；RQ-090～RQ-091；附录 A、B、E、F、H 属于 A 的各行；根 SC-10～SC-12）逐条对到出处。
- §2 最终全量（§2 的摘要与日志位置）。
- §3 测试数量加法全文（§4）。
- §4 测试文件改动清单（§5，全表；线级单列）。
- §5 表征与金样本（§5.3、§5.4）。
- §6 移交清单：只列 F 编号与去向（§8.1 的表）。
- §7 封存数据（§10.2）。

#### 10.2 交主控方的封存数据（只准备，不改 `multi-spec-map.md`）

- `Success Condition Progress` 中 SC-1、SC-3～SC-12 各行的建议值：Progress（`satisfied`／`in_progress`）、Evidence（指到 `a-final-report.md` 与 `t009-closure.md` 的节）、Remaining gap（附条件项的条件与去向，或 `none`）、Constraint（`aligned`／`at_risk`／`violated`，逐条对根契约 Constraints 的七条）。按待裁定第 7 条。
- spec-001 一行：Result 路径（`attempts/spec-001-executor-outbox-lock-removal/spec-result.md`，由主控方写）、`spec-result.md` 应引用的两份文件（TASK-30 主控方裁定第 5 条）。
- 封存提交前要再核的事项：冻结核对、工作区状态。

### 11. 执行顺序

1. §1 核实，落盘 `task-34-verify.txt`；V1、V2 不符即停。
2. §2 构建与正式全量（约 3 分钟运行加构建）；§2.3 判定失败，需要时 §2.4 隔离复跑；取证与日志信号。任何一项触发停止条件 2、3，停下回报。
3. §3 AC-009① 确认。
4. 只读脚本：§4 计数、§5 测试文件、§6.3 残留检索、§9.4 Spec 修订枚举；结果决定后面写什么。触发停止条件 4～6 即停。
5. `transition-mechanisms.md`（§6）；需要时改 `AGENTS.md`（§6.4）并重跑自身规矩检索。
6. `follow-ups.md` 终态（§8）。
7. `t009-closure.md`（§10）。
8. `a-final-report.md`（§9），最后写，引用前面各份。
9. Required checks 10～14；最后一次冻结核对。

### 12. 表征测试、金样本与生产行为

本单元不改代码与测试，客户端可见行为不变。正式全量是对 A 封存状态的一次测量，不引入任何变化；`AGENTS.md` 的条件修改只涉及过渡态的描述。

## 待裁定

1. **正式全量的原始输出落进仓库**（§2.1；AC-020「detailed logger 落盘」）。
   - 以往各单元把原始输出放在 scratchpad（如 `t31/formal-raw.txt`，约 3.9 MB），仓库里只放带摘要的日志。scratchpad 是临时目录，A 封存之后、外部审核之前可能已被清掉；而这一次是 A 封存状态的正式证据。基线的原始输出 `logs/baseline-build-test.txt`（3.7 MB）就在仓库里，有先例。
   - **建议：落进仓库**，存为 `logs/task-34-build-test-raw.txt`，摘要日志照旧放 `logs/task-34-build-test.txt`；取证 jsonl 只放摘要（体积小，摘要已能复核）。
   - 备选：照以往放 scratchpad。代价：AC-020 的原始证据在封存后可能不可复核。

2. **正式全量失败的判法与「只跑一次」**（§2.3、§2.4）。
   - RQ-083 允许「基线已有失败」与「AGENTS.md 已登记的偶发项」。AGENTS.md 今天登记了两类偶发（SQLite 并行释放三个栈根、F-125），另在「构建与运行」写明一种满载假红（拉真实进程或 socket 的用例在满载下撞 10 秒预算，报取消异常而非断言不符）。后者算不算「已登记的偶发项」，Spec 没有说。
   - **建议**：三类都按 AGENTS.md 写明的身份判定，并做相应的隔离复跑（满载假红单独跑该项目；F-125 单独复跑 5 次；SQLite 释放复跑一次）；判定成立的逐条记录，不算 RQ-083 的违反，也不重跑正式全量。其余失败一律停下。
   - 备选：出现任何非基线失败都重跑一次正式全量。代价：与「正式全量一次」相冲突，而重跑的绿并不比身份判定更能说明问题。

3. **SC-12「清单之外没有旧门、旧管线行为、转发层或 `[Obsolete]`」的检索口径**（§6.3）。
   - 「旧门」「`[Obsolete]`」可以机械检索；「旧管线行为」与「转发层」没有定义，后者尤其可宽可窄：A 新增了把既有发送接口接到发件箱的登记适配器，也保留了 B 才删的内联分支。
   - **建议**：按 S1～S8 执行。「旧管线行为」指 A 之前就有的 `ICommandPipelineBehavior` 实现（今天 5 个，全在 T3）；「转发层」定义为「只为让旧调用方式在新结构下照常工作而存在、B 归并后不再需要的代码」：从 A 新增的类型（S7）与自称过渡的注释（S6）、以及 R3-1 划给 B 的旧类型（S8）三个方向检索，每个命中归类。登记适配器是 A 投递设计的一部分（AGENTS.md「玩法执行器、发件箱与发送端」一节），归为 A 的常设机制。归不进的按停止条件 4。
   - 备选：只做 S1～S4 的机械检索。代价：「转发层」一项无证据，外部审核会问。

4. **清单中「由 B 删除的方式」只写起点建议**（§6.2）。
   - 根 SC-12 要求每项写「由 B 删除的方式」；而 `multi-spec-map.md` 写明「其余设计属于各子 Spec，这里不预设」，B 的 Spec 还没有写。
   - **建议**：这一栏写成「起点建议」，只引 `spec.md` R3-1、Plan 共享决定与附录 H 的去向原文，并给出 B 封存时可直接运行的检索式（根 SC-13 的「检索为零」）；注明 B 的 Spec 可以改删除方式，但不能让清单中的任何一项在 B 封存时仍在。
   - 备选：只写检索式，不写删除方式。代价：不满足根 SC-12 的字面。

5. **`follow-ups.md` 未关闭条目的终态去向**（§8.1；会改变若干条目的归属表述）。
   - 有几条的归属今天仍写着 A 的单元或「由 T-009 判定」：F-118（是否设判据，「由主控方决定归子 Spec B 还是另立」）、F-124（「归子 Spec B 或另立」）、F-116 (a)(b)（「T-009 或交主控方立项」）、Channel 入站 F-30 无用例（TASK-31 评审发现 2，尚无编号）、F-33 的偏离（「列入 TASK-34 的遗留问题」）、F-05／F-10（归属仍写 M-1）、测试基础设施与取证缺口（F-37 等 7 条，归属写「测试基础设施」）。
   - **建议**：按 §8.1 的表定稿——F-118、F-124、Channel 入站 F-30 无用例归子 Spec B（B 要重写入站 handler 结构与「快速开发」一节，判据与用例在那里加最自然）；F-116 (a)(b)、F-125 生产缺口、测试基础设施与取证缺口交主控方另行立项；F-33 偏离与 F-107、F-108 一起列为「外部审核时说明的已接受偏离」；F-05、F-10 未覆盖的一半已由 F-82 承接，标为关闭（转 F-82）；状态写着「未关闭」的 F-06（预热识别边界）、F-43（增加修改 GM 口令的写入方时）是条件触发的人工把关，归「信息」并保留触发条件。Channel 入站 F-30 一条新增编号（F-129）。
   - 备选：F-118、F-124 交主控方另行立项。代价：判据与 B 的结构改动分开做，B 封存后还要再开一次。

6. **AC-019 中 b1、b2 两条表征测试的记法**（§5.4）。
   - AC-019 写「表征测试在基线提交上通过，改造后断言不变并通过」。10 条中的 b1、b2 在基线上是带着「基线缺陷」理由跳过的（TASK-01 设计：它们表达 RQ-031(c) 的严格形式，基线做不到），T-006 去掉 `Skip` 后通过；断言文本自引入点起未改。
   - **建议：记「满足」**，在 AC-019 一行写明这两条的情形，并把它列进 §9.4 的「解读」（AC-019 的「在基线上通过」对这两条读作「在基线上记录为已知缺陷，断言不改」）。
   - 备选：记「满足，附条件」。代价：附条件通常意味着证据不足，而这里证据是完整的，只是字面与事实的形状不同。

7. **封存数据中 SC 的 Progress 建议值**（§10.2）。
   - `multi-spec-workflow` 的 Progress 只有 `not_started`／`in_progress`／`satisfied`；A 的若干 SC 由「满足，附条件」的 AC 支撑（AC-011、AC-012、AC-013、AC-014、VC-10）。
   - **建议**：本单元正式全量与各项检查都通过时，SC-1、SC-3～SC-12 建议为 `satisfied`，附条件写进 Evidence 与 Remaining gap 两栏（条件都已有去向，且都不是「A 还缺一件要做的事」）；Constraint 逐条对根契约七条给出 `aligned`，有疑问的写 `at_risk` 并说明。最终值由主控方在封存步骤决定。
   - 备选：附条件 AC 所支撑的 SC 记 `in_progress`。代价：A 封存后不重开，`in_progress` 会让根映射里出现一个永远不会推进的状态。

## 主控方裁定（执行前）

1. **正式全量的原始输出落进仓库（`logs/task-34-build-test-raw.txt`），接受。** 这是 AC-020 的正式证据，仓库里已有 `baseline-build-test.txt` 作先例。
2. **全量失败的判法，接受。** 以下三类按 AGENTS.md 写明的身份判定，并做对应的隔离复跑，不重跑正式全量：SQLite 并行释放；F-125（三样身份）；满载假红（报取消异常，单独跑该项目全过）。其余失败一律停下回报。
3. **SC-12 残留检索按 S1～S8 执行，接受「转发层」的定义与三个检索方向。** 登记适配器归 A 的常设机制，分流键归授权预留。两者都要在清单里写明归类理由。
4. **「由 B 删除的方式」只写起点建议，接受。** 只引用 R3-1、Plan 共享决定和附录 H 的原文，并附 B 封存时可直接运行的检索式。
5. **follow-ups 终态去向，按建议接受。** F-129 是新编号，承接 Channel 入站 F-30 降级无用例这一条，归子 Spec B。F-33 与 F-107、F-108 同列，作为「外部审核时说明的已接受偏离」。
6. **AC-019 中 b1、b2 记「满足」，并列进「Spec 解读」，接受。**
7. **封存数据里 SC 的 Progress 建议值按建议给出，接受。** 最终值由主控方在封存步骤决定。

## Blocker

执行轮次：第 1 轮（2026-10-24，HEAD `51c294fa`，冻结点 `2bdb1afd`）。`git diff 2bdb1afd -- Server Tool` 在开始、正式全量前与停下时都是 0 行。

**不能继续的是什么**：Outcome 1（最终构建与正式全量）。正式全量出现一条失败，按主控方裁定第 2 条既不能归入「基线失败」，也不能归入「AGENTS.md 已登记偶发」，触发停止条件 3。本单元停在 §11 第 2 步之后。§11 第 3～9 步都没有做：三份文档（`transition-mechanisms.md`、`a-final-report.md`、`t009-closure.md`）没有写，`follow-ups.md` 也没有改成终态。

**受影响的验收与依赖**：
- 直接受影响：AC-020、RQ-083、子契约 SC-8（「全量测试除基线已有失败外通过」）；
- 以本次正式全量为「通过」来源的：AC-009①（§3），以及 Required check 13 中的「本次通过」；
- 后续的 AC／SC 状态总表与封存数据都以此为前提。

**直接证据**：

- 构建：`--no-incremental` 0 警告 0 错误（`logs/task-34-build-test-raw.txt` part 1）。
- 正式全量只跑了一次（2026-10-23T23:43:47Z～23:46:48Z，181 s）。总数 8315，逐项目与 TASK-31 相同。通过 8294、失败 3、跳过 18，8294 ＋ 3 ＋ 18 ＝ 8315（`logs/task-34-build-test.txt` 文首）。
- 失败 1、2：两条 `DimensionDungeonTests`，属基线失败。
- 失败 3：`AchievementTitleBookTests.Real_title_replaces_mirror_in_one_transaction_and_refusal_or_rollback_preserves_old_wear(mode: 0)`（原始输出第 6081～6107 行）。
  - 异常：`ObjectDisposedException: Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'`。
  - 栈根：`AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery`。位置在 `AchievementTitleBookTests.cs:446`，由 Fixture 构造器在 `:437` 调用；外层是 `TestGameplay.RunAsync`（`AchievementTitleBookMirrorTests.cs:121-123`）。
  - 按 AGENTS.md「判据偶发失败」的三样逐一核对：
    1. 异常类型：相符；
    2. 复跑即绿：按 §2.4 复跑了一次。该方法单独运行 7／7 通过，整个 `AchievementTitleBookTests` 类 66／66 通过，取证 0 条（`logs/task-34-isolated.txt`）；
    3. 栈根在清单内：**不在**。清单只有三条：`CharacterDatabaseSchema.KeepWritersAndReadersApart`、`ItemPathFrameTests.Storage.ReadAsync`、`OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync`。AGENTS.md 写明，清单外的新栈根不得直接归入，要另给不归因的明证，确认后才追加进清单。
  - 也不属于另外两类：不是 F-125（用例和异常都不同）；不是满载假红（不在 `Host.Tests`、`Networking.Tests/Integration/`，也不是取消异常）。
- 判断「不归因」可用的事实（只陈述，不作裁定）：
  - `Fixture.Sql`（`:446`）与构造器里的两处 `Sql` 调用（`:436-437`），在 `a02bce46` → `2bdb1afd` 之间都没有改动。
  - A 对这个方法的改动，是把整个方法体包进 `TestGameplay.RunAsync`（TASK-30 §3.3 的包装形状）。所以失败的这次调用，今天是在测试自有执行器的作业里运行的。
  - 这个栈根在本任务以往的日志与 scratchpad 中都没有出现过：检索 `AchievementTitleBookTests.Fixture.Sql`，只命中本次的原始输出。
- 本次正式全量的其余检查已经做完，供处置时参考：
  - 取证：73 ＝ 2 ＋ 1 ＋ 1 ＋ 69，各预期类都相符；反例类与对照以外 0，`exe:` 0，没有测试帧 0（`logs/task-34-violations.txt`）。
  - 表征 10 通过 0 跳过；挂起转储 0。
  - 日志信号：
    - 「必须为 0」的七项与 TASK-31 相同；
    - 执行器失败 bus 8、inbound 9、gm 2，逐类型与 TASK-31 相同；
    - 有两项与 TASK-31 不同，尚未逐条归因。一是「the drain tears it down」3 行；TASK-31 是 1 行，TASK-30 5 行，TASK-29 2 行，以往随负载变化。二是发送端成对行 8 对，TASK-31 是 7 对。这 8 对里，TimedOut 1 对，为 Warning；Failed 7 对，其中 6 对带「already gone」后缀、为 Information，1 对为 Warning。F-126 已说明后缀取决于先后。
  - AC-009①（初步）：193 个方法全部通过。
    - `t34/protection.py` 报 192 个通过、1 个未找到。未找到的是 `ItemPathFrameTests.AMoveOfARealRowAcknowledgesTheSameBytesAsBefore`，它实际通过了：它的「已通过」与日志前缀挤在同一行（原始输出第 3932 行），脚本的行首匹配看不到。
    - 行数：两次运行脚本都报 331，但两次各有一行被挤掉。本次被挤掉的是上面这条；TASK-31 被挤掉的是 `ReplayStateProjectionTests.Tower_replay_uses_current_memory_slots_and_late_replay_preserves_restored_slots(inTower: True)`（`t31/formal-raw.txt:1700`）。两次实际通过的行数都是 331 ＋ 1 ＝ 332，所以 TASK-33 记的「331 行」少算了 1 行（`logs/task-34-protection-tests.txt` 文首）。
- 核实 V1～V16 都与规划一致（`logs/task-34-verify.txt`）。另有一处与 §8.1 不符：F-118 已在 TASK-33 关闭，§8.1 列的「F-118 是否设判据」没有对应的未关闭行。

**需要主控方给出的输入或决定**（选其一，或另定）：

1. 认定这条失败是 SQLite 并行释放的一个新栈根，并授权把栈根 `AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery` 追加进 AGENTS.md「判据偶发失败」的清单。追加清单不在本单元的 Owned boundary 内，本单元对 AGENTS.md 只能改过渡态段落。认定之后，本单元按待裁定第 2 条把它记为已登记偶发，不重跑正式全量，从 §3 继续。
2. 不认定，按停止条件 3 的本意转入返工或诊断。例如判断 `TestGameplay` 包装是否改变了 Fixture 连接的释放时序。这要改测试，不在本单元的授权内。
3. 授权再做一次正式全量。这与「正式全量一次」相冲突；而且待裁定第 2 条的备选里已经说过，重跑得到的绿，并不比身份判定更能说明问题。

不论选哪一项，继续时还要做两件事：对上面两项尚未归因的日志信号逐条归因；确认 AC-009① 的「通过」按 193 个方法、332 行记。

工作区状态：
- 没有 commit、stash 或 push。
- `git status --short` 只有本文件与 6 个 `verification/logs/task-34-*.txt`（`verify`、`build-test`、`build-test-raw`、`isolated`、`violations`、`protection-tests`）；`Server/`、`Tool/` 下没有条目。
- 已执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0，没有残留的 testhost 或 Host 进程。

## 主控方处置（Blocker 之后）

处置为 `continue`，范围与合同不变，另增两项授权。

1. **对新栈根先补不归因的证据，再按结论处理**，不选「直接认定」，也不选「重跑正式全量」。要补两类证据：
   - **读码**：
     - 核对 `TestGameplay.RunAsync` 的包装是否改变了 `AchievementTitleBookTests.Fixture` 中 SQLite 连接的创建、使用和释放归属与时序：连接在哪个线程上创建、在哪个线程上被使用、何时释放；作业执行期间测试线程是否也会访问同一连接。
     - 把结论写进日志。若读码发现包装让同一连接被并发使用，或者在释放后仍被使用，这就是缺陷，不是偶发项，停下回报。
   - **复现尝试**：
     - 该类在 8 个限时 `yes` 负载下连续跑 30 次（trap 清理，结束后 `pgrep -x yes` 为 0）；
     - Application.Tests 整项目跑 3 次。
     - 观察是否再次出现同一栈根。
2. **复现尝试 0 次复现、读码也没有发现交集时**：
   - 认定这是 SQLite 并行释放偶发的新栈根，并**授权在 AGENTS.md「判据偶发失败」一节的栈根表中追加这一行**（栈根、见于的用例名）。不归因的依据写进 `t009-closure.md`。
   - 之后不重跑正式全量，从 §3 接着做。
   - 若出现复现，停下回报。
3. 接着做之前，先完成 Blocker 中列出的两项：
   - 逐条归因两项日志信号：「the drain tears it down」3 行，以及发送端 8 对；其中那 1 对 Warning 的 `Failed` 要按 F-127 或 F-126 的形态核对。
   - AC-009① 按 193 个方法、332 行记，并订正 TASK-33 中「331 行」的说法：在 `t009-closure.md` 中写明，不改已提交的 TASK-33 文件。
4. **F-118 已在 TASK-33 关闭**：§8.1 中「F-118 是否设判据」一条，改为在遗留问题中写成「扇出变长字段的上限约定没有判据守着（AGENTS.md 已写明），是否设判据交子 Spec B」，不再新开编号。

## Result

- **Outcome**：done。执行轮次为第 2 轮（Blocker 之后），HEAD `51c294fa`，冻结点 `2bdb1afd`。
  - 第 1 轮的 Blocker（正式全量中新栈根的 SQLite 释放失败，停止条件 3）已由「主控方处置（Blocker 之后）」第 1～4 条处置为 continue。本轮按处置补齐了不归因的证据：`TestGameplay` 包装与失败路径无交集；有判别力的整项目 3 次运行复现 0 次（单类 30 次在构造上不具判别力）。A 新增的 3 处 `ClearAllPools()` 与推断的触发机制有交集，交 F-130（第 1 轮评审后改写，见文末「第 1 轮评审后修改」）。
  - 之后按处置第 2 条把栈根登记进 AGENTS.md，从 §3 接着做完，没有重跑正式全量。
  - 其余停止条件一条都没有触发：
    - 残留检索归不进的命中为 0；
    - 断言变化指不到处置记录的为 0；
    - Spec 修订找不到授权出处的为 0；
    - 冻结核对在开始、正式全量前与结束时都为空。
- **Changed**（未提交）：
  - 新建 `verification/transition-mechanisms.md`（根 SC-12 清单 T1～T6、相邻机制、残留检索、与 AGENTS.md 的对应）。
  - 新建 `verification/a-final-report.md`（根 SC-11，§1～§9，含 AC／SC 状态总表）。
  - 新建 `verification/t009-closure.md`（Plan T-009 逐项、最终全量、新栈根的不归因依据、测试数量加法、测试文件改动清单、表征与金样本、移交清单、封存数据）。
  - 新建 `verification/logs/task-34-*.txt` 共 13 个：`verify`、`build-test`、`build-test-raw`（原始输出，裁定第 1 条）、`isolated`、`violations`、`protection-tests`、`test-counts`、`test-files`、`transition`、`spec-revisions`、`followups`、`closure-refs`、`boundary`。
  - 修改 `verification/follow-ups.md`：
    - 50 条未关闭的条目都在「归属」末尾加了「〔A 封存：→ 去向〕」，F-118 的归属也补了去向；
    - 关闭 F-05、F-10 与 F-69 的 A 一半，F-69 续写本单元的耗时对比；
    - 新增 F-129、F-130（F-130 为第 1 轮评审后新增）；文首补 TASK-34 一行。
  - 修改 `AGENTS.md`，2 个 hunk、3 处改动：
    1. 过渡态 T3 一条补上四个命令标记接口，依据 TASK-33 主控方裁定第 9 条，因为清单的 T3 包含它们；
    2. 「判据偶发失败」的栈根表标题由「三条」改为「四条」；
    3. 同一张表新增 `AchievementTitleBookTests.Fixture.Sql → SqliteCommand.ExecuteNonQuery` 一行，依据主控方处置第 2 条。
- **Why**：
  - T3 并入标记接口：按 §6.1「B 删除时是不是同一个动作」，45、58 改为「CMD 声明主结算」时，标记接口随之消失；
  - `EffectOperationLedger`、`SelectedSessionSafety` 不进清单：它们由 B 改写入口，本身保留，不能要求「检索为零」（清单 §2）；
  - 测试文件改动按文件粒度指到处置记录：逐行的对应已在各单元的处置记录里，这里只证明每个有变化的文件都有记录点名它；
  - Spec 修订枚举出一处规划表里没有的：`53c72543` 改了 RQ-050、RQ-053(a) 的需求文字。RQ-050 一半的授权是 TASK-02 结果评审 N-1 的主控方处置；RQ-053(a) 一半出自第 2 轮 R2-N3，当时没有单独裁定，由主控方在本单元第 1 轮评审后追认，强度为「提交即接受＋本次追认」。汇报里分开写明（`a-final-report.md` §4.1）。
- **Checks**：
  1. 冻结核对：`git diff 2bdb1afd -- Server Tool` 在开始（`logs/task-34-verify.txt`）、正式全量前（`logs/task-34-build-test-raw.txt` part 0）与结束（`logs/task-34-boundary.txt`）都是 0 行；`Server/`、`Tool/`、`protocol/`、`Docs/`、`Document/` 在 `git status` 中都没有条目。
  2. 核实：V1～V16 都与规划一致（`logs/task-34-verify.txt` 文首）。
  3. 构建与正式全量（`logs/task-34-build-test.txt`）：
     - 构建 0 警告、0 错误；
     - 总数 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 970 ＋ 188 ＋ 4594 ＋ 197 ＝ 8315；
     - 通过 57 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 188 ＋ 4575 ＋ 197 ＝ 8294，失败 3，跳过 2 ＋ 16 ＝ 18；8294 ＋ 3 ＋ 18 ＝ 8315；
     - 失败 3 ＝ 基线 2 ＋ 登记的 SQLite 偶发 1；
     - 表征 10／0，挂起转储 0；
     - 耗时 181 s，RSS 11235803136 B，只作记录。
  4. 取证：73 ＝ 2 ＋ 1 ＋ 1 ＋ 69；反例类与对照以外 0，`exe:` 0，没有测试帧 0，退出码 0（`logs/task-34-violations.txt`）。
  5. 日志信号：与 TASK-31 逐项对照，不同的两项已逐条归因（`logs/task-34-build-test.txt`「Attribution」一节；另做了一次 Networking 串行运行，188／188 通过）。1 对 Warning `Failed` 的异常是 `SocketException (32) Broken pipe`，属 F-126 的形态，不是 F-127 的形态。
  6. AC-009①：193 个方法全部通过，332 行；TASK-33 的 331 行少算 1 行，已在 `t009-closure.md` §3.3 订正（`logs/task-34-protection-tests.txt`）。
  7. 测试数量加法：39 段，每段加法成立；有实测的终点都在对应日志里找到；不成立的段为 0（`logs/task-34-test-counts.txt`）。
     - ΣN 30 项：10 ＋ 59 ＋ 45 ＋ 13 ＋ 14 ＋ 14 ＋ 34 ＋ 41 ＋ 9 ＋ 1 ＋ 19 ＋ 7 ＋ 10 ＋ 59 ＋ 52 ＋ 11 ＋ 15 ＋ 9 ＋ 2 ＋ 40 ＋ 45 ＋ 2 ＋ 2 ＋ 6 ＋ 1 ＋ 8 ＋ 15 ＋ 39 ＋ 1 ＋ 13 ＝ 596；
     - ΣD 8 项：1 ＋ 44 ＋ 1 ＋ 11 ＋ 45 ＋ 43 ＋ 7 ＋ 1 ＝ 153；
     - 7872 ＋ 596 － 153 ＝ 8315；
     - 逐项目增量 7 ＋ 0 ＋ 58 ＋ 2 ＋ 1 ＋ 91 ＋ 252 ＋ 32 ＝ 443 ＝ 8315 － 7872。
  8. 测试文件：
     - 修改 363（356 ＋ 7）、新增 112、删除 7；
     - 356 个 `.cs` ＝ a1 218 ＋ a2 67 ＋ b 41 ＋ c 30；
     - b、c 中指不到处置记录的为 0；7 个删除文件逐个指到处置；修改过的线级文件里没有客户端可见的断言变化；
     - 金样本 diff 0 行，表征的 diff 只有两处去掉 `Skip`（`logs/task-34-test-files.txt`）。
  9. 残留检索 S1～S8：归不进的为 0（`logs/task-34-transition.txt`）。
  10. 过渡态一致：AGENTS.md 中含「过渡」的单元有 6 个，与 T1～T6 一一对应，不对应的为 0；T3 一条写有四个标记接口；本单元新增的 6 行，自身规矩检索的 12 个禁用模式都是 0。
  11. Spec 修订枚举：15 个提交、52 个 hunk（5 ＋ 1 ＋ 2 ＋ 1 ＋ 1 ＋ 5 ＋ 1 ＋ 1 ＋ 2 ＋ 2 ＋ 1 ＋ 18 ＋ 1 ＋ 3 ＋ 8，脚本末行输出），归不进的为 0（`logs/task-34-spec-revisions.txt`）。
  12. follow-ups 终态：
      - 130 行 ＝ 80 ＋ 36 ＋ 11 ＋ 3；
      - 50 条未关闭的都有六类之一的去向：15 ＋ 1 ＋ 7 ＋ 13 ＋ 1 ＋ 2 ＋ 11 ＝ 50；
      - 没有去向的为 0（`logs/task-34-followups.txt`）。
  13. 收口引用：三份新文件中的用例名、日志路径、F 编号与 `文件:行` 都解析得到；被引为证据的方法都在本次正式全量中通过；不符的为 0（`logs/task-34-closure-refs.txt`）。
  14. 边界与格式：
      - 改动只含 Owned boundary 内的文件，以及主控方处置授权的 AGENTS.md 栈根表；
      - `git -c core.whitespace=cr-at-eol diff --check` 无输出；新建文件都是 LF，没有行尾空格；AGENTS.md 与 follow-ups.md 原本就是 LF，改后仍是 LF；
      - 已执行 `dotnet build-server shutdown`，testhost／Host 进程 0，`pgrep -x yes` 为 0（`logs/task-34-boundary.txt`）。
- **Remaining**（不阻断本单元，去向都已写进 `follow-ups.md` 或汇报）：
  - 封存提交与 `multi-spec-map.md` 的更新由主控方做，建议值见 `t009-closure.md` §7。提交时要纳入约 3.9 MB 的 `logs/task-34-build-test-raw.txt`。
  - 按 Result review: required，提交前要做一次快审。
  - 按 research 05 的口径，codec 文件是 82 个、调用 `ComposeAsync` 的是 9 个；本单元按文件名模式只选出 77 个与 7 个。research 05 没有留下名单，差额无法逐个说明（`t009-closure.md` §5）。

### 第 1 轮评审后修改

依据：`reviews/task-34-result-review.md`（changes_required），以及本文件「主控方处置（第 1 轮评审后）」第 1～5 条。只改文档，没有重跑测试，已登记的栈根不撤回。上面 Outcome、Changed、Why、Checks 中受影响的数与说法已同步改成现值。

| 项 | 改动位置 | 改了什么 |
|---|---|---|
| B-1 RQ-050 的残余 | `a-final-report.md` §4.2（新增一行）、§8「判据与证明的已知边界」（新增一条）、§9.2 SC-6；`t009-closure.md` §7.1 SC-6 | 写明 CMD 已登记关闭、发送端还没执行时，传输层先断开，这次断开仍按继承处理，与 RQ-050 末句字面不符（TASK-29 裁定第 5 条；`t007-closure.md` §3 第 5 条、§4 第 2 条）。SC-6 改为「满足，附条件」，Remaining gap 写明条件与去向：随 B 复核，或在主控方立项的发送端精度改进中处理 |
| B-2 T6 检索式 | `transition-mechanisms.md` T6「B 封存时的检索」、§2 | 检索收窄到 `NetworkMessageDispatcher.cs`、`NetworkConnectionLifecycleDispatcher.cs` 两个文件；`OutboxAdapters.cs:70`、`:164` 与 `NotificationBus.cs:79` 三处登记点守卫在 §2 写成相邻的常设机制，不要求为零 |
| B-3 `53c72543` 的授权 | `a-final-report.md` §4.1 表与末段；`logs/task-34-spec-revisions.txt`（文首与 `53c72543` 一节，`t34/specrev.py` 重跑） | 拆成三项：RQ-050 引 N-1 与主控方原话；RQ-053(a) 引 R2-N3（`reviews/task-02-result-review.md:155`、`:162`）与 `tasks/TASK-02.md:45`，写明没有单独裁定、主控方本次追认，强度「提交即接受＋本次追认」；附录 A／D 引 TASK-02 合同 |
| B-4 新栈根的依据 | `a-final-report.md` §6、§8；`t009-closure.md` §2.2、§6；`logs/task-34-isolated.txt`、`logs/task-34-build-test.txt` 的判定段；`follow-ups.md` 新增 F-130 与文首一行；`logs/task-34-followups.txt` | 依据改为「与失败路径无交集、复现 0 次」；写明 A 新增 3 处进程级 `ClearAllPools()` 与推断的触发机制有交集，可能提高偶发概率；写明有判别力的是整项目 3 次，单类 30 次在构造上不具判别力。F-130 归「交主控方另行立项（测试基础设施）」。计数随之改为 130 行 ＝ 80 ＋ 36 ＋ 11 ＋ 3，未关闭 50 ＝ 15 ＋ 1 ＋ 7 ＋ 13 ＋ 1 ＋ 2 ＋ 11 |
| L-1 hunk 数 | `logs/task-34-spec-revisions.txt` 文首、`a-final-report.md` §4.1 首句、本 Result Check 11 | 53 改为 52，并写出加法；`specrev.py` 改为在末行输出总数 |
| L-2 T3 的措辞 | `transition-mechanisms.md` T3「新代码不要依赖」 | 与 `AGENTS.md:363` 对齐：今天新命令要得到 45、58 号行为仍须实现这些接口；B 归并后不再需要，不要假设它们在 B 之后仍然存在 |
| L-3 Constraint 依据 | `t009-closure.md` §7.1 末 | 新增根契约七条约束 × 依据的表 |
| L-4 AGENTS.md 计数 | 本 Result Changed、`t009-closure.md` §1 | 统一为「2 个 hunk、3 处改动」 |

本轮的检查：
- 收口引用核对：`logs/task-34-closure-refs.txt` 重跑，不符 0；
- `git -c core.whitespace=cr-at-eol diff --check`：无输出（`logs/task-34-boundary.txt` 重跑）；
- AGENTS.md 自身规矩检索：本单元新增行的 12 个禁用模式都是 0，含「过渡」的单元 6 个与 T1～T6 一一对应（`logs/task-34-transition.txt` 末段重跑）。本轮没有再改 AGENTS.md。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict 为 `changes_required`（`reviews/task-34-result-review.md`）。按 `task-rework` 处理，只改文档，不重跑全量，已登记的栈根不撤回：

1. **RQ-050 的残余**（TASK-29 裁定第 5 条）：
   - `a-final-report.md` §4.2 解读表与 §8 已知边界各补一条：「CMD 已登记关闭、发送端还没执行时，传输层先断开，这次断开仍按继承处理；与 RQ-050 末句的字面不符；登记在 `t007-closure.md`」。
   - SC-6 改为「满足，附条件」；`t009-closure.md` §7.1 的 SC-6 行同步改正，Remaining gap 写明条件与去向（随 B 复核，或在主控方立项的发送端精度改进中处理）。
2. **T6 的检索式收窄到两个 dispatcher**：`NetworkMessageDispatcher.cs`、`NetworkConnectionLifecycleDispatcher.cs`。另外 3 处登记点守卫（`OutboxAdapters.cs:70`、`:164`，`NotificationBus.cs:79`）在清单 §2 写成相邻的常设机制，不要求为零。
3. **`53c72543` 的授权出处拆成两项写**：
   - RQ-050 一半：引 TASK-02 评审 N-1 与主控方原话。
   - RQ-053(a) 一半：如实写明出自第 2 轮 R2-N3，评审交主控方处置，执行方在提交前写入，任务目录里没有单独裁定。**主控方在此补作追认**：RQ-053(a)「深度守卫关闭开新根」的澄清与 RQ-050 同属一组，主控方提交 TASK-02 时已接受，现补记为显式授权。汇报中按「提交即接受＋本次追认」的强度列出。
   - `logs/task-34-spec-revisions.txt` 同步改正。
4. **新栈根的依据改写**：
   - 汇报 §6 与 closure §2.2 改为「与失败路径无交集、复现 0 次」，并写明 A 在 Application.Tests 新增 3 处进程级 `ClearAllPools()` 调用，与推断的触发机制有交集，可能提高偶发概率。另说明 30 次单类复现在构造上不具判别力，有判别力的是 3 次整项目运行。
   - 新增 **F-130**（测试基础设施，交主控方另行立项）：进程级清池与并行测试的关系；A 新增的 3 处可改为按路径 `ClearPool`，或放进同一个 collection。
   - 文首一行与 §6 的计数随之更新。
5. **非阻断项一并改**：
   - L-1：hunk 数改为 52，涉及日志文首、汇报 §4.1、Result Check 11 三处。
   - L-2：T3 的措辞改为与 `AGENTS.md:363` 一致：今天新命令要得到 45、58 号行为，仍须实现这些接口；B 归并后不再需要它们。
   - L-3：§7.1 的 Constraint 栏逐条写出根契约七条约束的依据。
   - L-4：统一说法为「AGENTS.md 2 个 hunk、3 处改动」。

改完后跑：收口引用核对；`cr-at-eol` 口径 `diff --check`；AGENTS.md 自身规矩检索。在 Result 中补一节「第 1 轮评审后修改」。
