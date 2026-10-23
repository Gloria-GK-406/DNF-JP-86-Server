# TASK-30 — T-009a：RQ-061 名单补全与 A 的判据收口

基准提交 `a9c79e98`（TASK-29／T-007 已提交）。T-009 按修订记录 R3 收窄为结果 A 的收口，拆成 TASK-30～TASK-34 五个单元（§0）。本文件是第一个单元的完整计划；其余四个单元只写边界提纲（§0.2），各自的计划在临近执行时另写成 `tasks/TASK-31.md`～`TASK-34.md`。

范围取自：
- `plan.md` 修订记录 R3 中 T-009 的交付结果：「RQ-061 运行期判据的最终核对：不设启动期例外（F-16），原行括注按 F-60 改正」「A 的判据加固与反例对账（F-76；RQ-072 的 A 一半）」「A 期间改写的判据逐条对账（RQ-071、AC-018 的 A 一半）」，以及「附录 A、B、E、F 的最终对账」中属于附录 F 名单与附录 B 补名的一半；
- `spec.md` RQ-061、RQ-011、AC-006（`t004-closure.md` §3 记为「未满足的 Spec 验收」，承接者 T-009）、AC-017 的运行期判据一半；修订记录 R3-1 中 RQ-071、RQ-072、AC-018 的 A 一半，R3-3 第 1、2 行；
- `verification/follow-ups.md` 中归 T-009、属于判据的 F-60、F-62、F-75、F-76、F-85、F-97，以及 F-69（续写）；
- `t005-closure.md` §5（F-85）、`t006-closure.md` §2 RQ-061 发件箱一行与 §10（F-97）、`t004-closure.md` §3～§4（F-62、F-75）；
- `t005-test-dispositions.md`、`t006-test-dispositions.md`、`task-07-conflicts.md` 中改写过的判据（AC-018 对账的输入）。

本单元的产出：附录 F 名单以外的进程内玩法状态，经普查后挂上 RQ-061 的写断言，并由一条新的普查判据保证名单完整（关闭 AC-006 的缺口）；`GameplayJobHostStructureTests` 加固（F-76）；登记适配器的位置检查次序（F-97）；A 期间改写与新建的判据逐条对账并补齐反例；附录 F、附录 B 的相应文字；判据收口记录 `verification/t009-criteria.md`。

输入：
- TASK-29 的正式全量 `logs/task-29-build-test.txt`（8262 条）、取证 `logs/task-29-violations.txt`（41 条）、原始输出 scratchpad `t29/formal-raw.txt`；
- 规划时的普查原型 scratchpad `t30plan/census.py`（源码粗扫，67 个文件命中，只作规划依据，不作证据）；
- 脚本：scratchpad `t07/diffcheck7.py`、`t19/analyze.py`、`t21/methods.py`、`t24/elapsed.py`、`t27/mutate.py`、`t27/violations.sh`、`t27/closure_refs.py`；缺失时从 `logs/task-29-*.txt` 末尾附的全文恢复。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **普查与归类**（§2；RQ-061、RQ-011、AC-006；F-62）：按 §2.1 的规则普查 `Dfo.Server.Application`、`Dfo.Server.Protocol` 中持有进程内可变状态的单例，每一个归入「挂点」（加入 `GameplayWriteEntryTableTests.Types`）或「豁免」（指名附录 B 的一行并写理由）。规划时的候选见 §2.3，执行时以普查判据的输出为准。
  2. **挂点**（§3）：挂点类型的每个写成员调用 `GameplayWriteAssertion.Writing("类型.成员")`，读成员不调用；分类表 `GameplayWriteEntryTableTests.Table` 逐成员登记；每个新挂点类型一条运行期反例（执行器外调用一个写成员即抛、读照常）。除了在执行器外调用写成员会抛出之外，生产行为不变。
  3. **普查判据**（§4；RQ-011 式的完整性守护）：新判据从生产模块的服务注册取出单例，按 §2.1 识别可变状态，要求每个持有者要么在 `Types` 里，要么在豁免表里且写明附录 B 的行与理由；名单里多出、已经不持有状态的行也拒绝。配反例。
  4. **`GameplayJobHost` 结构判据加固**（§5；F-76；RQ-072）：反射一支扩到完整的生产程序集闭包（在 `Dfo.Server.Host.Tests` 中运行）；源码一支对 F-76 列出的三种绕过各配一条反例并堵上。
  5. **登记适配器的检查次序**（§6；F-97）：按待裁定第 2 条处理。
  6. **判据对账**（§7；RQ-071、RQ-072、AC-018 的 A 一半）：A 期间改写的既有判据逐条写「旧断言 → 新断言 → 保护意图 → 反例」；A 新建的全部判据逐条写「判据 → 反例 → 证明它会拒绝的证据」。缺反例的就地补。
  7. **RQ-061 的最终核对**（§8；R3-3 第 1 行；F-16、F-60）：判据没有开关、没有启动期例外；启动期写入都作为启动作业运行或不经两类入口。
  8. **Spec 附录文字**（§9；F-62、F-75、F-85）：附录 F「改为执行器内访问」一行写明名单的唯一出处与守它的判据，「保留」一行补 `PasswordComputationLimiter`；附录 B 生产者一行补两个名字，「开始监听前的启动期工作」一行改掉「作为 RQ-061 的启动期例外」。
  9. **收口**（§10）：新建 `verification/t009-criteria.md`；`follow-ups.md` 关闭 F-60、F-62、F-75、F-76、F-85、F-97，续写 F-69，文首补 TASK-30 一行。
  10. **一次正式全量**：带取证汇，反例类与对照以外为 0，`exe:` 为 0；表征测试 10 通过、0 跳过；变异核对（§11）。
- Owned boundary：
  - **生产**：
    - §2.3 表中归为「挂点」的类型所在文件，**只加** `GameplayWriteAssertion.Writing(...)` 调用（每个写成员方法体开头一行，异步方法加在同步部分）；不改方法签名、可见性、逻辑与锁。执行时普查新增的挂点类型照此处理，并在 Result 中逐个列出。
    - `Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs`：只在待裁定第 2 条取 A 时，调换「编码」与「位置检查」的先后，并改类上 remarks 的次序说明。
  - **测试**：
    - `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayWriteEntryTableTests.cs`：扩 `Types` 与 `Table`；
    - 新建 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayStateCensusTests.cs`（普查判据、豁免表与反例）；
    - `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayWriteAssertionTests.cs`：追加每个新挂点类型一条运行期反例，已有用例不改；
    - `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayJobHostStructureTests.cs`：源码一支的识别改写与三条反例，已有用例的断言不改；
    - 新建 `Server/tests/Dfo.Server.Host.Tests/GameplayJobHostClosureTests.cs`（反射一支在完整闭包上运行；`Dfo.Server.Host.Tests.csproj` 只在需要链接源文件时改动）；
    - 待裁定第 2 条取 A 时：`Server/tests/Dfo.Server.Networking.Tests/Integration/OutboundSendFailureTests.cs` 的 4 个用例改为在作业内调用发送器（断言不改），`Server/tests/Dfo.Server.Networking.Tests/Outbound/OutboxRegistrationTests.cs` 追加一条反例；
    - **挂点带来的测试改动**（§3.3）：在执行器外调用新挂点写成员的既有用例，把那一段包进 `tests/Support/TestGameplay.cs` 的 `Run`／`RunAsync`，断言与方法集合不变；涉及的文件在执行时逐个列出；
    - §7 对账发现缺反例时，在该判据所在的测试文件里追加反例。
  - **Spec**：`spec.md` 附录 F 的「保留」「改为执行器内访问」两行，附录 B 的「生产者的入队侧」「开始监听前的启动期工作」两行（§9）；待裁定第 1 条 (b) 取建议时，附录 B 另加「请求级帧（过渡）」一行；其余不改。
  - **文档与证据**：`verification/t009-criteria.md`（新建）、`verification/follow-ups.md`、`verification/logs/task-30-*.txt`；一次性脚本放 scratchpad `t30/`，全文附在对应日志末尾。
  - **不改**：
    - 挂点类型的逻辑；`GameplayWriteAssertion` 与 `GameplayAssertions.cs` 的其余部分；`Server/src` 中 Networking、Persistence、Infrastructure、HttpApi、GmWeb、GameServer、Host 各项目；共享契约；数据库表结构；
    - `plan.md`、`AGENTS.md`、`Docs/`、`protocol/`；`Characterization/` 与金样本；`entry-reconciliation.md` 与以往单元的收口文件；附录 A、E、G、H 与 Spec 正文。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。RED 阶段一次，挂点与测试改动完成后一次，变异全部做完之后一次。
  2. **RED／GREEN**（`logs/task-30-red-green.txt`）：
     - 只加判据与反例、不改生产代码时：普查判据变红，失败信息恰好列出 §2.3 中「挂点」各类型（执行时普查多出的，逐个写明）；每个新运行期反例变红，原因是「没有抛出」；F-76 的三条源码反例变红；闭包判据在基准上为绿（基准没有第二个宿主）。
     - 加上挂点之后、包装测试之前：按项目过滤跑 Application、Protocol、Networking、Host，记录失败用例全集；每一条都要确认失败栈根是测试体在执行器外调用新挂点写成员（§3.3），不是生产路径。
     - 全部改完：上述用例全绿。两段都记命令、退出码与关键输出。
  3. **正式全量**：`DFO_GAMEPLAY_ASSERTIONS=<scratchpad 目录> dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘 `logs/task-30-build-test.txt`。
     - **总数**：基准 8262 ＝ HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 181 ＋ Application 4553 ＋ Host 195。本单元为 8262 ＋ N － D，逐项目写加法，N 逐条列名（预期见 §12）。
     - **结果**：失败只能是 2 条 `DimensionDungeonTests`（`verification/baseline.md`）与 AGENTS.md 已登记的偶发项（三样一起取身份；打包进程用例在满载下的就绪超时，按 F-55／F-103 的形状取身份并隔离复跑）；挂起转储 0。
     - **跳过**：18，不变。
     - **表征测试**：10 通过、0 跳过；`Characterization/` 与金样本目录无改动。
     - **日志信号**：与 TASK-29 Checks 3 同口径、同脚本，逐条对照。
     - 耗时与内存只作信息（RQ-091 用户修订）。F-69：用 `t24/elapsed.py` 与 `t29/formal-raw.txt` 逐条对比，多出 ≥ 9 秒的逐条隔离复核。
  4. **零违规取证**（`logs/task-30-violations.txt`）：命令同 TASK-29 Checks 4。预期先写进日志开头再跑：41 ＋ 新运行期反例数（每条恰报一次 `write-off-executor`）＋ 待裁定第 2 条取 A 时的 1 条。`violations.sh` 只在分类里为新反例加类，改动写进日志。反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。
  5. **普查输出**（`logs/task-30-census.txt`）：普查判据的诊断输出全文（每个单例：实现类型、可变字段、归类、附录 B 行）；与 §2.3 规划表逐行对照，差异逐条说明；挂点数、豁免数与单例总数的加法。
  6. **变异核对**（`logs/task-30-mutations.txt`；程序同 TASK-29 Checks 6：备份、逐字节还原后 `cmp`、前后 `git diff -- Server/src` 与 `-- Server/tests` 的 sha1 相同，全部做完后 `--no-incremental` 重建）：§11 每一项都有指定的用例变红。
  7. **新增测试 ×10**（`logs/task-30-new-x10.txt`）：新增方法隔离连续 10 次全过；`GameplayWriteEntryTableTests`、`GameplayStateCensusTests`、`GameplayJobHostStructureTests`、`GameplayJobHostClosureTests` 全类 ×3。
  8. **检索集**（`logs/task-30-retrievals.txt`，§10.3）：每个检索式先写预期再跑。
  9. **收口引用核对**（`logs/task-30-closure-refs.txt`）：`t27/closure_refs.py` 复制为 `t30/closure_refs.py`，范围改为 `t009-criteria.md`；口径同 TASK-29 Checks 9，0 处不符。
  10. **测试改动核对**（`logs/task-30-diff-check.txt`）：`diffcheck7.py <基准> --lines` 加 `t21/methods.py`：方法集合只多了 N 中列出的，没有少的；§3.3 包装过的文件断言行多重集合不变；断言行有变化的已有文件只有 Owned boundary 中允许改断言的（§5 源码识别、§7 补反例），逐处列出。
  11. **边界**（`logs/task-30-boundary.txt`）：`git diff --stat <基准> -- Server/src` 只有 Owned boundary 的文件；生产 diff 中新增的行只有 `GameplayWriteAssertion.Writing(...)` 调用、必要的 `using`，以及待裁定第 2 条取 A 时 `OutboxAdapters.cs` 的次序与 remarks（脚本逐行核对新增行的形状，其余形状逐条列出）；`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、表征目录、以往收口文件无改动；`spec.md` 只改 §9 列出的各行（四行；待裁定第 1 条 (b) 取建议时五行）。
  12. **隔离复跑**（`logs/task-30-isolated.txt`）：Application、Networking、Host 各一次，都带取证目录，记录各自的反例类与对照记录数。
  13. **`git -c core.whitespace=cr-at-eol diff --check`**：无输出；改动的已有文件行尾与基准一致，新建文件 LF。结束时执行 `dotnet build-server shutdown`；`pgrep -x yes` 为 0。
- Dependencies：
  - TASK-29（`a9c79e98`）：调用链与深度上限、队列层泄漏流判定（F-59）都已在基准上；
  - T-004（TASK-18、TASK-19）：`GameplayWriteAssertion`、分类表判据、事务开启点判据、`GameplayJobHostStructureTests`；
  - T-006（TASK-23）：`OutboxNetworkMessageSender` 的次序与 `OutboundSendFailureTests` 的 4 条（F-97）；
  - **执行前提**：工作区干净（除本任务目录下未提交的本文件）；待裁定第 1、2 条有裁定。
- Result review：required。
  - 分类表本身就是被审的判断：一个写成员若被错标为读，分类判据与运行期反例都照样通过，而它在执行器外被调用时不会抛。新增的挂点有二十个左右的类型、上百个成员行，全量测试暴露不了错标。
  - 豁免同理：一个持有玩法状态的单例若被错误豁免，普查判据会放行它。豁免理由要靠审查逐条对照附录 B。
  - 挂点在执行器外会抛异常，改的是 Application 与 Protocol 两个项目里二十个左右的文件。若生产中有一条没被测试覆盖的执行器外写路径，它会在运行时变成断线或失败作业，测试不一定能发现。
  - 本单元的判据对账与普查结果，是 TASK-33 的附录最终对账、TASK-34 的 A 最终汇报与子 Spec B 沿用的输入。
- Task-specific authorization：
  - **停止条件**，出现即停下回报，不在本单元修：
    1. 普查或 RED 第二段发现**生产路径**在执行器外写新挂点类型（失败栈根不在测试体，或正式全量取证出现反例类以外的记录）。这是一条真实的 RQ-061 违规，修法属于入口迁移，要回到主控方。
    2. 某个候选类型要改结构才能挂点（例如写入发生在执行器外的回调里、或写与读混在一个无法拆开的成员里），不能只加一行断言。
    3. §3.3 的测试改动需要改断言或方法集合，而不只是包装。
    4. 表征测试或金样本需要改动；任何线级测试的断言需要改动。
    5. 需要改 Owned boundary「不改」中的项目、共享契约或表结构；需要改 `spec.md` 中 §9 以外的文字；需要改 `plan.md`、`AGENTS.md`。
    6. 正式全量出现基线与已登记偶发项以外的失败；取证出现 `exe:` 记录。
  - 变异期间允许临时改动 `Server/src` 与测试，用后按三项还原证明处理（备份、`cmp`、`git diff` sha1）。
  - 不 commit，不 `git stash`。

## Detailed Plan

### 0. T-009 的拆分

#### 0.1 为什么拆成五个单元

T-009 的内容可以按「改不改代码」与「失败方式」分成五段，依赖是单向的：

```
TASK-30 判据（改生产与测试）→ TASK-31 日志与保护用例（改生产与测试）→ 代码冻结
  → TASK-32 最终测量（只读代码）→ TASK-33 文档与附录对账（只改文档与 Spec 附录）→ TASK-34 收口与 A 的最终汇报
```

- **TASK-30 单列**：唯一一处会让生产路径在执行器外抛异常的改动，波及面最大（约二十个生产文件，测试改动数量要到 RED 才知道），而它的失败方式（错标、错豁免）测试暴露不了，需要单独审。
- **TASK-31 与 TASK-30 分开**：日志级别、线级用例、改名与删替身都是局部改动，各自的检查能可靠暴露错误；和 TASK-30 放在一起，审查的注意力会被普查表淹没。它必须排在测量之前：F-121 若改发送端的日志级别，M-3 的判定规则要跟着改（§0.2 TASK-32）。
- **TASK-32 单列**：测量要在代码冻结之后才能代表 A 封存状态（TASK-28 主控方裁定第 5 条的理由），运行约一个半小时，判定以判断为主，与 M-2 一样单独审。
- **TASK-33 单列**：文档必须描述最终代码，附录对账也要在最终代码上检索；它只改文档与 Spec 附录，错误只能靠审查发现。
- **TASK-34 最后**：过渡机制清单（根 SC-12）与 A 的最终汇报（根 SC-11）要引用前四个单元的全部结论；最终构建与全量测试也在这里，作为 AC-020 的正式证据。

不拆得更细：TASK-30 的普查、挂点与普查判据是同一个判断的三面，分开会出现「挂了点但没有判据守着」或「判据红着」的中间态。

#### 0.2 后续单元的边界提纲

| 单元 | 交付 | 覆盖 | 依赖 | Result review 预判 |
|---|---|---|---|---|
| TASK-31（T-009b）日志与保护用例 | ① F-30：作业体失败时，生产者与中间件不再记第二条 `Error`（执行器那条保留，带调用链）；② F-31：「执行器已停止」那一行带上停止原因；③ F-121：发送端对「对端先断开」的 `Failed` 降级，真正的发送失败仍为 `Warning`；同时按 `t007-closure.md` §4 第 3 条复核开始／完成日志的级别（建议维持，写明理由）；④ F-32：线级用例「作业内 handler 抛异常 → 断线」（DR-15、RQ-031(c)）；⑤ `t007-closure.md` 评审补记的 HTTP 根继承：经真实 HTTP 登录顶替，被顶替连接的 `lifecycle:disconnected` 父链是 `http:` CMD；⑥ AC-009 的 A 一半第 ③ 条：经真实连接重发一个已提交的效果请求，得到重放、连接不断（现有 `EffectOperationReplayTests` 只经真实 dispatcher，没有经真实连接，也没有断言「不断线」）；⑦ F-111：删除没有用例使用的测试替身；⑧ F-87、F-123：用例改名，计入 RQ-083 的方法集合变化；⑨ F-33：预缓冲在 `IHttpMaxRequestBodySizeFeature` 只读时改用该特性的当前上限，与基准逐字等价，配单元测试（做不到时按 RQ-082 回到主控方）；⑩ 待裁定第 3 条取 B 时，F-98 的发送端阶段语义 | RQ-031(c)、RQ-054、RQ-036 的日志面、AC-009 的 A 一半 ③、RQ-083；F-30、F-31、F-32、F-33、F-87、F-111、F-121、F-123 | TASK-30 | required：改执行器、发送端与中间件的日志面，测量判定依赖它；改名改用例身份 |
| TASK-32（T-009c）最终测量 M-3 | 在 TASK-31 之后的冻结代码上，沿用 M-2 的工具、命令行与 G1 dll（`measurement/m2-summary.md` §9），完整测 (a)～(f)；判定按修订后的 RQ-091（单条 CMD ≤ 2 s 即接受）与 M-2 的 S1～S5；**就绪探测连接的豁免写成规则**（按连接的行为识别：工具连上游戏端口后、未发任何入站帧即关闭的那条连接），不再按行号登记 `ATTRIBUTED`；F-121 改过日志级别时 S5 第 5 项随之改写，并说明真正的发送失败仍被计入；(e) 加跑 G0 控制运行，使 (e) 的对照不只靠一次 G0；与基线、M-1、M-2 四方对照 | RQ-090、RQ-091；AC-005 的测量一半、AC-021、F-02 关闭；F-77～F-82、F-120 更新数值 | TASK-31（代码冻结） | required：判定以判断为主，与 M-2 同 |
| TASK-33（T-009d）文档与附录最终对账 | ① `AGENTS.md` 现状图（RQ-100 的 A 一半）：架构总览与请求走向、命令执行总线（管线实际层数 0／45／58／60／100、`SendAsync` 在执行器上的过渡内联）、SelectCharacter（不再取门）、领域事件与即时投影（发件箱、发送端、无重试）、端到端示例的出站一步、构建与测试中并发假红的段落、`PersistOnTeardownAsync` 等过时名称；handler 仍在作业内编排细粒度命令的事实写成「今天的状态与已知缺口」；F-35、F-36（AGENTS.md 一半）、F-89、F-92、F-105、F-118（约定；建议不另设判据）、F-110 的已知约定、F-122 的今天含义。按 AGENTS.md「记什么，不记什么」直接改掉过期段落，不写过程；② `Docs/Agent/ArchitecturePhilosophy.md` §1、§5 中关于执行与锁的部分；③ 附录 A（F-99：第 3、4、6、8 行与两个系统 CMD 类型）、附录 B、附录 E（每行的测试）、附录 F（「删除」栏检索为零，AC-017）、附录 H 中属于 A 的各行（dispatcher、45 的取锁部分、0）的最终对账；④ RQ-024 的 A 一半（A 期间提交点有没有合并或拆分）；⑤ AC-009 的 A 一半 ①②④（保护测试断言不变的清单、`MonsterDeathHandlerTests.Party_publication_failure_fences_every_recipient_of_the_shared_settlement`）；⑥ `entry-reconciliation.md` 开头补落地状态；⑦ 待裁定第 3 条取 A 时附录 G 加 VC-10；⑧ AC-022 的 A 一半：检索 A 删除的类型名只出现在「被推翻的做法」一类记录里 | RQ-100、AC-022、AC-006（附录 A、B 一半）、AC-009 的 A 一半、AC-014、AC-017、RQ-024 的 A 一半；根 SC-10 | TASK-32（数字不进文档，但对账要在冻结代码上做；若主控方希望先文档后测量，两者可以对调） | required：文档与对账只能靠审查 |
| TASK-34（T-009e）收口与 A 的最终汇报 | ① 最终构建与全量测试（detailed logger 落盘，AC-020）；表征测试在 A 封存状态下断言不变（AC-019）；线级测试文件的改动清单（RQ-081）；② 测试数量加法：基线 7872（`b76504bb`，产品代码同 `a02bce46`）逐单元加到 A 封存，每段写 N、D 与处置记录的出处；③ 根 SC-12 的过渡机制清单 `verification/transition-mechanisms.md`：每项写位置、存在理由、由 B 删除的方式；附检索，证明清单之外没有旧门、旧管线行为、转发层或 `[Obsolete]`；④ 根 SC-11 的 A 最终汇报 `verification/a-final-report.md`：结果、可见变化（VC-1～VC-9，及裁定后的新增）、主控方授权的 Spec 修订与共享契约变更（RQ-053(d)、队列层泄漏流、本单元的附录 F 名单等）、遗留问题（`follow-ups.md` 中仍未关闭的每一项及其归属：子 Spec B、交主控方立项、用户、外部审核、信息）、过渡机制清单的引用、验证结论、rev 2 规划停点三项默示决定的复述（单执行器不分流、短锁暂留、不换数据库也不回内存副本）；⑤ A 的 AC 与根 SC 逐条状态表；⑥ `verification/t009-closure.md`；⑦ `follow-ups.md` 的终态。不含封存前的完整结果评审与最终核验（主控方另行安排） | RQ-080～RQ-083、AC-019、AC-020；根 SC-8、SC-11、SC-12 | TASK-33 | required：汇报与清单是外部审核与子 Spec B 的输入 |

#### 0.3 T-009 依据的去向

| 依据 | 单元 |
|---|---|
| R3：RQ-061 最终核对（不设启动期例外，F-16、F-60） | TASK-30 §8 |
| R3：A 的判据加固与反例对账（F-76；RQ-072 的 A 一半） | TASK-30 §5、§7 |
| R3：A 期间改写的判据对账（RQ-071、AC-018 的 A 一半） | TASK-30 §7 |
| R3：AC-009 的 A 一半 | ③ TASK-31；①②④ TASK-33 |
| R3：RQ-024 的 A 一半 | TASK-33 |
| R3：附录 A、B、E、F 最终对账；附录 H 属于 A 的各行（F-62、F-75、F-85、F-97、F-99） | 附录 F 名单、附录 B 补名与改字、F-85、F-97：TASK-30；其余与最终对账：TASK-33 |
| R3：文档，RQ-100 的 A 一半（F-35、F-36、F-89、F-92、F-105、F-118）；F-110、F-122 的 A 一半 | TASK-33 |
| R3：过渡机制清单（根 SC-12）与给子 Spec B 的移交清单 | TASK-34 |
| R3：最终测量（RQ-090、RQ-091；AC-005 的测量一半、AC-021、F-02） | TASK-32 |
| R3：最终构建与全量测试、测试数量加法（RQ-081～RQ-083） | TASK-34（TASK-30、TASK-31 各跑一次正式全量） |
| R3：F-30、F-31、F-32、F-33、F-87、F-98、F-111、F-121 | TASK-31；F-98 见待裁定第 3 条 |
| F-123；`t007-closure.md` 评审补记的 HTTP 根继承用例 | TASK-31 |
| F-116 | 待裁定第 4 条 |
| F-117 | 不在 A 内（归用户或客户端研究仓库），TASK-34 的遗留问题列出 |
| F-69 | 每个跑正式全量的单元续写（TASK-30、TASK-31、TASK-34） |
| 根 SC-10 | TASK-33；根 SC-11、SC-12：TASK-34 |

### 1. 基准核实（执行时重跑并落盘 `logs/task-30-verify.txt`）

| # | 问题 | 检索 | 规划时在 `a9c79e98` 上的结果 |
|---|---|---|---|
| V1 | 工作区与基准 | `git status --short`；`git log -1` | HEAD `a9c79e98`；工作区只有本任务目录下的本文件 |
| V2 | 测试总数与取证基线 | 读 `logs/task-29-build-test.txt`、`logs/task-29-violations.txt` | 8262（逐项目见 Required check 3），失败 2、跳过 18；取证 41 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 37 |
| V3 | 现有挂点 | `grep -rln "GameplayWriteAssertion" --include='*.cs' Server/src`；读 `GameplayWriteEntryTableTests.Types` | 生产 20 个文件（含 `GameplayAssertions.cs` 本身、`DomainTransactionScope.cs`、`GameplayOutbox.cs`、`CharacterStateAccess.cs`）；`Types` 14 个（附录 F 的 13 个加 `AltarStateStore`）；`Table` 201 行 |
| V4 | 事务开启点 | `grep -rn "new DomainTransaction(" --include='*.cs' Server/src` | 7 处：`DomainTransactionScope.cs:49`、`:68`，`ItemContainers.cs:181`，`CharacterStateAccess.cs:53`、`:96`、`:165`，`CreatureRuntimeDomainService.cs:289`；都由 `GameplayWriteEntryTableTests` 的事务判据钉住 |
| V5 | 生产模块能否在测试里列出服务注册 | 读 `ApplicationModule`、`ProtocolModule` 的 `ConfigureServices`；已有先例 `Persistence.Tests/Accounts/AuthenticationFixture.cs:37`、`Networking.Tests/Integration/MagicSealFrameIntegrationTests.cs:42-44` | 两个模块可以对一个 `ServiceCollection` 调用而不解析；执行时确认不需要真实配置，或给最小配置 |
| V6 | 候选类型的写者是否都在作业上 | 对 §2.3「挂点」每一行，读它全部写成员的调用方；重点查执行器外的回调（发送端观察者、传输回调、计时器、托管服务的 `StartAsync`／`StopAsync`、`Dispose`） | 规划时：`IOutboundDeliveryObserver` 在 Application 与 Protocol 中没有实现；配额刷新与投影的「本期已送」在发件箱刷出时（作业结束钩子内，`t007-closure.md` V7）记录。执行时逐个确认；发现执行器外的生产写者按停止条件 1 |
| V7 | 句柄类型 | 对每个挂点类型，查它返回的、自身带可变状态的句柄类型（例如 `PartyProjectionStreamRegistry.GetOrAdd` 返回的 `PartyDeliveryStream`） | 规划时只确认了 `PartyDeliveryStream` 一例；执行时逐个查，句柄类型按 §2.1 第 4 条处理 |
| V8 | `MemoryItemContainer` 与塔内六格 | 读 `TowerInventoryDomainService`、`CharacterContainerDomainService`、`ItemContainers.cs` | `TowerInventoryDomainService` 自身没有字段；绑定在 `CharacterContainerDomainService.byCharacter`，容器内的物品写经物品 store 会话、要求事务（V4 的开启点）。执行时确认 |
| V9 | F-76 的三种绕过 | 读 `GameplayJobHostStructureTests.WithoutComments`（`:274-275`）与 `HostIdentifier`（`:29`）；`grep -rn "<Using" Server/src/*/*.csproj Server/Directory.Build.props` | 去注释用两条正则，不认字符串字面量；标识符用 `\bGameplayJobHost\b` 匹配，不解码 Unicode 转义；csproj 当前没有 `<Using>` 项 |
| V10 | 测试项目能看到的生产程序集 | 读 `Dfo.Server.Host.Tests.csproj`、`Dfo.Server.Host.csproj` 的项目引用；`ls Server/src/Dfo.Server.Utilities` | Host.Tests 引用 Host，Host 引用 Abstractions、Application、Infrastructure、HttpApi、Persistence、GameServer、GmWeb；`Dfo.Server.Utilities` 没有 `.cs` 文件 |
| V11 | F-97 的现状 | 读 `OutboxAdapters.cs:8-80`、`OutboundSendFailureTests.cs:31-90` | 顺序是「找连接 → 编码 → 位置检查 → 登记」；4 个用例在执行器外调用发送器，断言编码错误 |
| V12 | GmWeb、HttpApi、GameServer、Host 中的可变单例 | 规划原型 `t30plan/census.py` 换项目跑 | 3 个：`GmIconPackStore`、`GmLoginLimiter`、`HttpApiReadiness`，都是附录 B 已列的项 |
| V13 | 脚本 | `ls` scratchpad 下 Inputs 列出的脚本 | 都在 |

V1～V13 与本表不一致时：V1 不干净，停下回报；V2 的总数不同，停下回报；V3、V4 有出入，纳入 §2 的普查；V5 若模块注册需要真实环境才能执行，改用 §4.1 的退路；V6 发现执行器外的生产写者，按停止条件 1；V12 多出项，按 §4.1 第 4 条记录。

### 2. 普查（Outcome 1）

#### 2.1 规则

**普查对象**：`ApplicationModule` 与 `ProtocolModule` 注册的单例（`ServiceLifetime.Singleton`），取实现类型；工厂注册与实例注册按服务类型登记，执行时为每个工厂注册写明它构造的实现类型。

**可变状态**（在实现类型及其同程序集基类上，用反射识别）：
1. 非 `readonly` 的实例字段，含自动属性的后备字段；
2. `readonly` 实例字段，其声明类型是可变集合：`System.Collections.Generic` 中的 `Dictionary<,>`、`HashSet<>`、`List<>`、`Queue<>`、`Stack<>`、`SortedSet<>`、`SortedDictionary<,>`、`LinkedList<>`、`PriorityQueue<,>`，`System.Collections.Concurrent` 中的全部集合，`ConditionalWeakTable<,>`，以及数组；
3. `readonly` 实例字段，其声明类型是同两个程序集中的类，而该类本身满足 1～3（例如 `RunItemDomainStore.State` 这一形状），按递归识别；
4. **句柄**：挂点类型的公开或内部成员返回的、同两个程序集中满足 1～3 的类型，也算持有者（它的写成员同样要挂点）；
5. 静态字段：非 `readonly` 的静态字段，或 `readonly` 且类型满足第 2 条的静态字段，所在类型不论是否单例都算持有者。

**不算可变状态**：`readonly` 的接口类型字段（注入的服务、`IReadOnlyDictionary` 等）、`Frozen*`、`Immutable*`、委托、`ILogger`、`TimeProvider`、`Lazy<>`（首次索引另由 `ContentWarmupTests.W2` 守着）、只读的基元与值。

**识别边界**（写进判据 remarks 与 `t009-criteria.md`）：认不出藏在 `object` 或接口类型字段后面的状态；认不出非单例对象在单例之外长期存活的状态（例如被某个单例捕获在闭包里的局部集合）。后一种由 V6 的读码兜底。

**归类**：
- **挂点**：状态在作业之间存活、由作业写入、决定玩法或出站内容。加入 `Types`，写成员挂断言（§3）。
- **豁免**，按类别，每类指名附录 B 的一行：
  - E1 执行机制与调用链元数据（执行器、入队点、定时工作、发件箱、关闭原因、事件链存储、事件注册表与组循环通道、命令／消息／通知注册表、调度服务的到期队列）：附录 B「生产者的入队侧」与「开始监听前的启动期工作」；理由是不读写玩法状态（TASK-29 §3 第 9 条已为事件链与关闭原因写过同样的理由）。
  - E2 内容与规则的不可变缓存（各 `*RuleService`、内容持有者的首次索引）：附录 B「PVF 内容源与缓存、规则服务的不可变规则」。
  - E3 请求级帧（`NetworkCommandAuthorization` 一类按流的 AsyncLocal 帧）：附录 B 没有这一行。**按待裁定第 1 条处理**。
  - E4 单次调用内的值（例如 `InventoryEffectPreflightDomainService+Standing`，它不是单例，只在规划原型里出现）：不进普查对象，判据本身就看不到它。
- 普查判据不接受「其他」类豁免。

#### 2.2 普查范围之外的项目

`Persistence`、`Infrastructure`、`Networking`、`GmWeb`、`HttpApi`、`GameServer`、`Host` 不进普查判据，理由各自写进 `t009-criteria.md`：
- `Persistence`：状态在 SQLite 里；内存中的易失 store（如 `RunItemDomainStore`）只经 `SessionIn(transaction)` 写，事务只在 V4 的 7 处开启，开启点由事务判据钉住并要求先有写断言。
- `Infrastructure`：内容，附录 B 的 PVF 一行。
- `Networking`：传输注册表与发送端，附录 B 已列。
- `GmWeb`、`HttpApi`、`GameServer`、`Host`：V12 的 3 个持有者都在附录 B；本单元在检索集里记一次（R5），不进判据。理由：普查判据放在 `Dfo.Server.Application.Tests`，要直接读 `GameplayWriteEntryTableTests.Types`，而这个测试项目不引用 GmWeb 等项目。

#### 2.3 规划时的候选（读码，执行时以普查判据的输出为准）

已在 `Types` 的 14 个不重列。

| 候选类型 | 位置 | 可变状态（规划时读码） | 规划时归类 | 依据 |
|---|---|---|---|---|
| `ClientSettingsDomainService` | `Application/Accounts/ClientSettingsDomainService.cs:22` | `profiles`（按连接的 profile 归属） | 挂点 | 决定 C6 上传写进哪个 profile |
| `WeaknessDomainService` | `Application/Characters/WeaknessDomainService.cs:17-19` | `entries`、`pending` | 挂点 | 虚弱状态与待施加的惩罚 |
| `CharacterContainerDomainService` | `Application/Inventory/CharacterContainerDomainService.cs:12` | `byCharacter`（容器绑定，含类型 29 与塔内六格） | 挂点 | 死亡塔换格（`TowerInventoryDomainService` 经它） |
| `HousingState` | `Application/Marriage/HousingState.cs:15-16` | `editors`、`returns` | 挂点 | 小屋编辑者与返回位置 |
| `ExpertStoreDomainService` | `Application/Sessions/ExpertStoreDomainService.cs:11` | `_open` | 挂点 | 摆摊 |
| `PvpLobbyDomainService` | `Application/Sessions/PvpLobbyDomainService.cs:11-12` | `_ready`、`_rooms` | 挂点 | PvP 大厅 |
| `DungeonEntryIntentDomainService` | `Application/World/DungeonEntryIntentDomainService.cs:10` | `_intents` | 挂点 | 进本意向 |
| `JoustMaintenanceService` | `Application/Events/JoustMaintenanceService.cs:13-14` | `observed`、`openedRounds` | 挂点 | 马战阶段去重与公告；写在它自己的定时作业里 |
| `SelectedSessionSafety` | `Application/Commands/SelectedSessionSafety.cs:23` | `connections`（按连接的封锁） | 挂点 | 「保留」项（事务结果不确定时的会话隔离）；写在 45／58 号行为里，都在作业上 |
| `EffectOperationLedger` | `Application/Effects/EffectOperationLedger.cs:17` | `ledgers`（按连接的效果账本） | 挂点 | 「保留」项（一请求一主结算）；写在 dispatcher 作业里 |
| `DungeonRunDeliveryRegistry` | `Protocol/Game/DungeonEntry/DungeonRunDeliveryRegistry.cs:21-22` | `entries`、`killResults` | 挂点 | 副本投递的收件人与击杀结果 |
| 7 个配额刷新服务（`Altar`、`Dimension`、`GoldSilverHorn`、`ReviveCoin`、`Heras`、`Tournament` 与 `Premium` 的 `*DailyRefreshService`） | `Protocol/Game/**/` | `delivered`（本期已送） | 挂点 | DR-6「刷出时记录」 |
| `FatigueProjection`、`ItemUsageProjection`、`UsageQuotaProjection` | `Protocol/Game/Fatigue/`、`StateSynchronization/` | `delivered` | 挂点 | 同上 |
| `PartyProjectionStreamRegistry` 与句柄 `PartyDeliveryStream` | `Protocol/Game/Party/PartyNotificationSlice.cs:457-465` | `streams`；句柄内的 revision 水位线 | 挂点 | DR-5、DR-17 |
| `PartyClientProjectionService` | `Protocol/Game/Party/PartyClientProjectionService.cs:13` | `next` | 挂点 | 协议侧编号分配 |
| `NetworkCommandAuthorization` | `Application/Commands/NetworkCommandAuthorization.cs:36` | 按流的帧 | 待裁定第 1 条 | 过渡机制（根 SC-12），由子 Spec B 改为显式数据 |
| 执行与事件基础设施：`GameplayExecutor`、`GameplayQueue`、`GameplayTimerWork`、`GameplayOutboxes`、`GameplayCloseCauses`、`DomainEventCallChains`、`DomainEventDispatcher`、`DomainEventRegistry`、`ScheduledTaskHostedService`、三个处理器注册表 | `Application/Execution/`、`DomainEvents/`、`Scheduling/`、`Commands/`、`Dispatching/`、`Notifications/` | 计数、通道、队列、元数据 | E1 | 不读写玩法状态 |
| 规则与内容缓存：`CharacterSkillRuleService`、`CharacterStatRuleService`、`ExperienceRuleService`、`JoustRuleService`、`MonsterDeclaredDropRuleService`、`WorldDropRuleService`、`CeraShopRuleService`、`PremiumServiceRuleService`、`QuestHelperPuppetRuleService`、`EpicBookDomainService` 的目录 | `Application/**/` | 首次索引、`Frozen*` 表 | E2 | 不可变内容 |

规划时的计数（执行时以普查输出为准）：挂点 10（Application）＋ 1 ＋ 7 ＋ 3 ＋ 2 ＋ 1（Protocol）＝ 24 个类型，其中 `PartyDeliveryStream` 是句柄。执行时与这个数相差较大时不停，在 Result 中逐条说明。

规划原型 `t30plan/census.py` 是正则粗扫，认不出元组类型的字段：`PremiumDailyRefreshService.delivered`（`:44`，值是四元组）就是读码补进上表的。所以本表只是规划依据，普查以 §4 的反射判据为准。

### 3. 挂点（Outcome 2）

**3.1 写法**：沿用 TASK-18 的约定。每个写成员在方法体开头调用 `GameplayWriteAssertion.Writing(nameof(类型) + "." + nameof(成员))`（编译成一条 `ldstr`，分类判据从 IL 读它；基准上生产中共 143 处这样的调用）；同名重载各自调用、名字相同；异步写成员放在状态机之前的同步部分，`Asynchronous_writes_are_seen_through_their_state_machine` 的识别方式照旧。只读成员不调用。一个成员既读又写，按写。

**3.2 分类表**：`Types` 加入挂点类型；`Table` 逐成员登记（公开、内部、显式接口成员；嵌套私有类型的成员不列）。每个写成员的判定理由按 TASK-18 的六种形状读方法体（字段或集合变化、聚合变化、登记参与者、开启写工作单元、store 写、顺带清理的读），有疑义的写进 `ReadNote` 的理由。

**3.3 测试改动**：挂点之后，在执行器外调用新挂点写成员的既有用例会抛 `GameplayOffExecutorException`。处理：
- 先按 Required check 2 第二段取全集，逐条确认栈根在测试体（停止条件 1）；
- 把测试体中调用写成员的那一段包进 `TestGameplay.Run`／`RunAsync`（与 T-004 的测试迁移相同），断言、等待条件与方法集合不变；
- 包装的位置按「最小包住写调用」的原则；若一个用例的断言夹在两次写之间，分两段包，不移动断言；
- 每个改动的文件在 `t009-criteria.md` §8 逐个列出：文件、用例数、包装段数、断言行多重集合不变（Required check 10）。

**3.4 运行期反例**：`GameplayWriteAssertionTests` 为每个新挂点类型加一条 `<类型>_refuses_a_write_off_the_executor_and_reads_anywhere`，形状同已有 14 条：执行器外调用一个写成员，抛 `GameplayOffExecutorException`，取证源恰报一条 `write-off-executor`，状态未变；同一实例的一个读成员照常返回；在作业里同一写成员照常执行。

### 4. 普查判据（Outcome 3）

**4.1 判据**（`GameplayStateCensusTests.cs`）：
1. `Every_singleton_holding_mutable_state_is_a_write_entry_or_a_listed_exemption`：对新 `ServiceCollection` 调用 `ApplicationModule`、`ProtocolModule` 的 `ConfigureServices`（V5），取单例描述符，按 §2.1 识别持有者；断言每个持有者在 `GameplayWriteEntryTableTests.Types` 中，或在本文件的豁免表中，且豁免带类别、附录 B 行名与非空理由；`Types` 或豁免表中已经不持有状态的行、指向不存在的类型的行，也报违规。诊断输出逐行写出每个单例的实现类型、可变字段与归类（Required check 5）。
2. `The_census_refuses_an_unlisted_holder_an_unreasoned_exemption_and_a_stale_row`：Theory，三行。喂给判据的核心函数一组测试内的假类型：未列出的带 `Dictionary` 字段的单例；带空理由的豁免；列出但已不持有状态的行。各自恰报一条违规。
3. 判据的核心函数（识别与比对）与 §2.1 一一对应，供 1、2 两条共用，不另写第二份。
- **退路**（V5 不成立时）：改为反射两个程序集的全部非嵌套类，加上「未在任何模块注册的类型不算单例」需要的注册信息无法取得时，改用「全部顶层类 ＋ 豁免类别 E4（每次调用一个实例）」，E4 要逐个写理由；这条退路在 Result 中登记。
4. V12 的 3 个持有者不进判据，在检索 R5 里记录，并写进 `t009-criteria.md` §2。

**4.2 豁免表的位置**：豁免表写在 `GameplayStateCensusTests` 里，挂点名单仍只在 `GameplayWriteEntryTableTests.Types`；两者合起来是附录 F「改为执行器内访问」一行的唯一出处（§9）。

### 5. `GameplayJobHost` 结构判据加固（Outcome 4；F-76）

**5.1 反射一支扩到完整闭包**：新建 `Dfo.Server.Host.Tests/GameplayJobHostClosureTests.cs`，一条 `Only_the_sealed_gameplay_executor_derives_from_the_job_host_in_the_full_production_closure`：从 `Dfo.Server.Host` 程序集出发，沿引用收齐全部 `Dfo.Server.*` 与 `Dfo.Pvf` 生产程序集；先断言闭包包含 `Dfo.Server.GameServer`、`Dfo.Server.GmWeb`、`Dfo.Server.HttpApi`、`Dfo.Server.Host` 四个（源码一支今天只覆盖它们），再断言其中 `GameplayJobHost` 的派生类只有密封的 `GameplayExecutor`。`Dfo.Server.Utilities` 没有源文件（V10），在 remarks 写明，由源码一支覆盖。实现上复用 `GameplayJobHostStructureTests` 的闭包收集逻辑：两个测试项目都链接同一个 `tests/Support` 下的辅助文件，或在 Host.Tests 内写一份最小实现；选哪种按「每种事实只有一个归处」取前者，链接要改 csproj 时在 Result 登记。

**5.2 源码一支**（`GameplayJobHostStructureTests.cs`）：
- **Unicode 转义**：匹配前把源码中的 `\uXXXX`、`\UXXXXXXXX` 解码；
- **字符串里的 `//`**：`WithoutComments` 改为一个最小词法扫描，跳过普通字符串、逐字字符串（`@"…"`，含 `""`）、字符字面量与插值字符串的字面部分，只在代码区识别注释。原始字符串字面量（`"""`）按整段跳过；识别不了的形状写进 remarks 的识别边界；
- **csproj 别名**：另扫 `Server/src/**/*.csproj`、`Server/Directory.Build.props`、`Server/Directory.Build.targets`（存在时），`<Using>` 项的 `Include` 或 `Alias` 命中 `GameplayJobHost` 即违规；
- 反例：一条 Theory `The_source_criterion_sees_through_an_escape_a_string_and_a_project_alias`，三行，各喂一份构造的源码或项目文本，各自恰报一条违规。
- 已有 9 个用例的断言不改。

**5.3 二者的关系**：反射一支成为主判据；源码一支保留，作为闭包之外（`Utilities` 与将来新增、尚未被 Host 引用的项目）的兜底。写进两个文件的 remarks。

### 6. 登记适配器的检查次序（Outcome 5；F-97）

按待裁定第 2 条：
- **取 A（建议）**：`OutboxNetworkMessageSender` 的三个发送入口改为「找连接 → 位置检查 → 编码 → 登记」；类上 remarks 的次序说明随之改写。`OutboundSendFailureTests` 的 4 个用例改为在作业内调用发送器（`TestGameplay.RunAsync`），断言不变。`OutboxRegistrationTests` 追加 `An_unencodable_message_off_the_executor_is_refused_by_position_first`：执行器外发一条不可编码的消息，抛 `GameplayOffExecutorException`、取证报一条 `write-off-executor`。
- **取 B**：不改代码；`t009-criteria.md` 写明今天的次序、缺口（只有执行器外 ＋ 不可编码同时成立时不写取证；此时调用照样被拒，没有登记）与生产中执行器外调用为 0 的证据（`logs/task-23-verify.txt` V1），F-97 记为「接受现状」关闭。

### 7. 判据对账（Outcome 6；RQ-071、RQ-072、AC-018 的 A 一半）

**7.1 收集**：
- 「A 期间改写的既有判据」：从 `task-07-conflicts.md`、`t005-test-dispositions.md`、`t006-test-dispositions.md` 中处置为改写的判据类用例，加 `spec.md` R3-1 RQ-071 一行点名的 `CommandBusTests` 管线与访问方式用例、`CommandRegistrationTests`、`NotificationHandlerArchitectureTests`，以及 `CommandHandlerArchitectureTests.ATypeOutsideTheVocabularyIsRefused` 在 TASK-26（`8c531c9b`）补的两行；再用 `git diff a02bce46 <HEAD> -- Server/tests` 中文件名含 `Architecture`、`Registration`、`Boundary`、`Source`、`Structure` 的文件核对有没有遗漏。属于子 Spec B 的（R3-1 RQ-071 一行的 B 一半）只列名、不对账。
- 「A 新建的判据」：`git diff --diff-filter=A --name-only a02bce46 <HEAD> -- Server/tests` 中的判据类文件（反射、源码扫描、注册检查、启动检查），加上 R3-1 RQ-072 一行点名的 `GameplayWriteAssertionTests`、`LockRemovalSourceTests`、`OutboxRegistrationTests` N3、`GameplayJobHostStructureTests`，以及本单元新增的普查判据与闭包判据。规划时已知的还有：`DetachmentSiteTests`、`ContentWarmupTests` W2、`GmGameplayRouteTests` 的路由豁免判据、`PasswordComputationLimiterTests`、传输写入点判据（`logs/task-23-lock-scan.txt`）、`GameplayCallDepthTests`、`MessageHandlerRegistry` 对遥测 handler 的启动检查、`GameplayWriteEntryTableTests` 的事务判据。执行时以检索结果为准。

**7.2 对账表**（写进 `t009-criteria.md` §3、§4）：
- 改写的：用例 → 旧断言 → 新断言 → 保护意图 → 反例（用例名）→ 出处（处置记录的节）；
- 新建的：判据 → 它拒绝什么 → 反例 → 证明区分力的证据（RED、变异的日志位置）。

**7.3 补反例**：对账中「反例」一栏为空的，在该判据的测试文件里补一条，形状仿同文件已有的反例；补的每一条都进 N，并在 §11 配一项变异或 RED 证据。规划时预计为 0～3 条。

### 8. RQ-061 的最终核对（Outcome 7）

- 判据没有开关：`GameplayWriteAssertion.Writing` 只判 `GameplayJob.Active`；`GameplayAssertionDiagnostics.IsEnabled` 只决定写不写取证，不决定抛不抛（检索 R2）。
- 没有启动期例外（F-16）：启动期的写入作为启动作业运行（`startup:` 三类，附录 B 第 284 行）；仍在执行器外的建表与迁移不经两类入口（V4 的开启点都不在 Persistence 构造路径上）。
- RQ-061 的三类入口都在判据内：写工作单元（事务判据）、发件箱登记（`GameplayOutbox.Register`，N3）、附录 F 名单的写入口（分类判据 ＋ 本单元的普查判据）。
- F-60：`plan.md` 修订记录 R3 的 T-009 一段已写明「不设启动期例外……原行括注按 F-60 改正」，本单元不改 `plan.md`，在 `follow-ups.md` 中按此关闭 F-60 并引用该段。

### 9. Spec 附录文字（Outcome 8）

只改下面各处（第 5 条视裁定而定），其余不动：
1. 附录 F「保留」一行：补「`PasswordComputationLimiter` 的信号量（RQ-007 的口令哈希并发上限，在执行器外；`LockRemovalSourceTests` 的信号量名单已列）」（F-85）。
2. 附录 F「改为执行器内访问」一行：末尾补一段「**名单（TASK-30，基准 `a9c79e98`）**」：写明名单的唯一出处是 `GameplayWriteEntryTableTests.Types`（挂点）与 `GameplayStateCensusTests` 的豁免表，守它的判据是普查判据；新增持有进程内可变状态的单例，必须同时改其中之一；识别边界一句。不逐个重抄类型名（与附录 B 预热一行的写法相同）。
3. 附录 B「生产者的入队侧」一行：补 `LoginSessionMaintenanceService` 的关闭触发出队循环与卡牌协调器的 `Dispose`（F-62 评审补记）。
4. 附录 B「开始监听前的启动期工作」一行：前半句「作为 RQ-061 的启动期例外」改为与同格末句一致的现状（F-75）。
5. 只在待裁定第 1 条 (b) 取建议时：附录 B 加「请求级帧（过渡）」一行，写明 `NetworkCommandAuthorization` 一类按流的 AsyncLocal 帧为什么不挂点、由子 Spec B 改为显式数据（根 SC-12）。

这四处都是 Spec 自身授权的补记（附录 F「实施时如果发现本表以外的并发原语，补入本表」；RQ-011 要求执行器外的部分同步更新附录 B 与判据）。附录 F 名单的扩大使 RQ-061 的覆盖范围变大，但没有改 RQ-061 的文字，交外部审核时说明（写进 `t009-criteria.md` §7）。

### 10. 收口

**10.1 `verification/t009-criteria.md`**（新建，开头写日期与基准；写法仿 `t007-closure.md`，只写「要求 → 实现位置 → 证据 → 状态」，未关闭事项只引 F 编号）：
- §1 RQ-061：三类入口、无开关、无启动期例外（§8）；
- §2 普查：规则（§2.1）、范围之外的项目（§2.2）、普查输出的汇总（挂点、豁免按类别计数与加法）、识别边界；AC-006 中本单元关闭的一半（「附录 F 名单以外的进程内玩法状态不在判据内」），附录 A、B 对账的一半留给 TASK-33；
- §3 A 期间改写的判据对账；§4 A 新建的判据对账（§7）；
- §5 F-76；§6 F-97；§7 Spec 附录的四处改动及其授权依据；
- §8 测试处置：N 的逐条名单与加法；§3.3 包装过的文件清单；
- §9 移交：给 TASK-33（附录 A、B、E、F、H 的最终对账）与 TASK-34（汇报里要提的共享语义变化：名单扩大）。

**10.2 `follow-ups.md`**：关闭 F-60、F-62、F-75、F-76、F-85、F-97（按待裁定第 2 条的结果写「已修」或「接受现状」）；F-36 不动（AGENTS.md 一半归 TASK-33）；续写 F-69；文首补 TASK-30 一行；新事项从 F-124 起编号。

**10.3 检索集**（Required check 8，先写预期再跑）：

| # | 检索 | 预期 |
|---|---|---|
| R1 | `grep -rn "GameplayWriteAssertion.Writing(" --include='*.cs' Server/src`，按文件计数 | 每个挂点类型的调用数等于它在 `Table` 中的写成员数（同名重载按 TASK-18 的约定计）；事务开启点不变 |
| R2 | `grep -n "Environment\|IsEnabled\|static bool" Server/src/Dfo.Server.Abstractions/Execution/GameplayAssertions.cs` | 只有取证源的 `IsEnabled`，没有开关 |
| R3 | `grep -n "启动期例外" .rks-agent/tasks/serial-gameplay-executor/spec.md` | 只剩 RQ-061 正文与修订记录中的引用，附录 B 两行不再出现「作为 RQ-061 的启动期例外」 |
| R4 | `grep -n "PasswordComputationLimiter\|GameplayStateCensusTests\|LoginSessionMaintenanceService" .rks-agent/tasks/serial-gameplay-executor/spec.md` | 附录 F、附录 B 各一处新命中，逐行列出 |
| R5 | `python3 -I t30plan/census.py` 对 GmWeb、HttpApi、GameServer、Host 跑一次 | 与 V12 相同的 3 个，都在附录 B |
| R6 | `grep -rn "<Using" Server/src --include='*.csproj'`；`grep -n "<Using" Server/Directory.Build.props` | 0 |
| R7 | 待裁定第 2 条取 A 时：读 `OutboxAdapters.cs` 三个入口的语句次序 | 位置检查都在编码之前 |

### 11. 变异（Required check 6）

| 变异 | 改动 | 应当变红 |
|---|---|---|
| MU1 | 删掉一个新挂点类型某个写成员的 `Writing` 调用（每个项目各取一例：Application 的 `PvpLobbyDomainService`、Protocol 的一个配额刷新服务） | `GameplayWriteEntryTableTests.Every_member_of_the_listed_types_is_classified_and_exactly_the_writes_assert`；对应的运行期反例 |
| MU2 | 从 `Types` 删掉一个新挂点类型（保留它的 `Table` 行） | 普查判据（未列出的持有者）；分类判据（多出的行） |
| MU3 | 普查识别去掉第 2 条（`readonly` 可变集合） | 普查判据（大批 `Types` 行变成「不持有状态」）；普查反例的「未列出的持有者」一行 |
| MU4 | 普查识别去掉第 5 条（静态字段） | 普查反例中静态字段那一种构造（§4.1 第 2 条的三行之外，若执行时为静态字段加了一行，就以它为准；否则本项改为证明 V 的检索在生产中没有静态可变字段，写进日志） |
| MU5 | 源码一支去掉 Unicode 解码 | 源码反例的转义一行 |
| MU6 | 源码一支恢复两条正则去注释 | 源码反例的字符串一行 |
| MU7 | 源码一支去掉 csproj 扫描 | 源码反例的别名一行 |
| MU8 | 闭包判据的程序集过滤排除 `Dfo.Server.GmWeb` | 闭包判据（四个程序集的包含断言） |
| MU9 | 待裁定第 2 条取 A 时，恢复「先编码」 | `OutboxRegistrationTests` 新反例 |
| MU10 | §7.3 每条补的反例，删掉它所证明的那一行判据逻辑 | 该反例 |

### 12. 数量与取证预期

- **N** ＝ 运行期反例 h（每个新挂点类型一条，规划时 h ＝ 24，句柄类型若与所有者共用一条则减一）＋ 普查 1 ＋ 普查反例 3（Theory 按行计）＋ 闭包 1 ＋ 源码反例 3 ＋ 待裁定第 2 条取 A 时 1 ＋ §7.3 补的 k 条。规划时取 h ＝ 24、k ＝ 0：24 ＋ 1 ＋ 3 ＋ 1 ＋ 3 ＋ 1 ＝ 33。项目分布：Application 24 ＋ 1 ＋ 3 ＋ 3 ＝ 31，Host 1，Networking 1。
- **D**：0。
- **取证**：41 ＋ h ＋（取 A 时 1）。规划时 41 ＋ 24 ＋ 1 ＝ 66。`OutboundSendFailureTests` 的 4 条改在作业内之后不报记录。`exe:` 0。

### 13. 执行顺序

1. §1 核实（V1～V13），落盘 `task-30-verify.txt`；V1 不干净即停。
2. 写普查判据与反例、运行期反例、F-76 的三条源码反例与闭包判据，不改生产代码；构建并跑：RED 第一段（Required check 2）。普查判据的诊断输出即普查的第一份结果，与 §2.3 对照后定下挂点与豁免名单。
3. 按名单给生产代码挂点，扩 `Types` 与 `Table`；构建，按项目过滤跑 Application、Protocol、Networking、Host：RED 第二段，取测试改动的全集并逐条确认栈根（停止条件 1）。
4. §3.3 包装测试；改源码一支的识别；按待裁定第 2 条处理 F-97。GREEN：过滤运行全绿。
5. §7 对账与补反例。
6. 正式全量与取证（Required checks 1、3、4、5），隔离复跑（check 12），×10／×3（check 7）。
7. §11 变异，重建。
8. §9 的 Spec 附录文字；§10 的收口记录、`follow-ups.md` 与检索集。
9. 收口引用核对、diff 核对、边界、`cr-at-eol` 口径 `diff --check`（checks 9～11、13）。

### 14. 表征测试、金样本与生产行为

- 合法玩法下客户端可见的行为不变：挂点只在执行器外调用写成员时抛出，生产中这样的调用应为 0（正式全量的取证与停止条件 1 证明）。
- 表征测试 10 条与全部金样本不变；需要改的，按停止条件 4 处理。

## 待裁定

1. **AC-006 缺口的关法：名单扩大到普查出的全部进程内可变状态，并由普查判据守着**（§2、§4、§9；F-62；AC-006）。
   - 今天 RQ-061 的写入口只挂在附录 F 点名的 13 个类加 `AltarStateStore` 上；名单之外的进程内玩法状态（规划时读出约 24 个类型，包括 PvP 大厅、摆摊、虚弱、配额刷新的「本期已送」、队伍投影水位线、效果账本、会话封锁）不在判据内，所以 AC-006「代码中检索不到在执行器外改状态的路径」今天不成立。
   - **建议 A**：按 §2.1 的规则普查，玩法状态一律挂点；执行机制、不可变内容两类按附录 B 已有的行豁免；新增普查判据守着名单的完整性。附录 F「改为执行器内访问」一行补一句名单的唯一出处（§9 第 2 条）。这扩大了 RQ-061 的实际覆盖范围，但不改 RQ-061 的文字，属于 Spec 自身「实施时补入」的授权。
   - 请同时确认两个归类：
     - (a) `EffectOperationLedger`、`SelectedSessionSafety` 是「保留」项，子 Spec B 要把它们迁进协议 CMD。建议现在就挂点：它们今天就在作业上写，挂点只增加保护；B 迁移时随之迁移断言。
     - (b) `NetworkCommandAuthorization` 的按流帧（E3）。附录 B 没有对应的行。建议豁免，理由是「请求级帧是过渡机制（根 SC-12），由子 Spec B 改为显式数据」，并在附录 B 补一行「请求级帧（过渡）」；这会使 §9 的附录 B 改动从两处变为三处。备选：挂点，帧的进入与退出都要求在作业上——它今天确实只在作业内设置（TASK-09），但帧的退出发生在 `finally` 里，作业被取消时的时序要另查，风险高于收益。
   - 备选 B：只把规划时点名的几个（例如 `TowerInventoryDomainService` 所在的容器绑定）补进名单，不设普查判据，AC-006 以「人工检索 ＋ 名单」关闭。代价：没人维护的名单等于没有规则（AGENTS.md「命令处理器只够得到领域形状」一节的教训），下一次新增的玩法状态又会落在判据之外。

2. **F-97：登记适配器是否改成「先判位置、后编码」**（§6）。
   - 这推翻 TASK-23 主控方裁定（Revision 2）第 1 条：当时为了让 `OutboundSendFailureTests` 的 4 个用例不改，取了「先编码」。
   - **建议 A：改。** 4 个用例改为在作业内调用发送器，断言不变；执行器外的调用一律先按位置拒绝、写取证，取证不再有窄缺口。新增 1 条反例。
   - 备选 B：不改，F-97 记为「接受现状」：执行器外 ＋ 不可编码同时成立时，调用照样被拒、没有登记，只是不写取证；生产中执行器外调用为 0。

3. **F-98：XO-6 的阶段近似怎么落地**（TASK-31 或 TASK-33；附录 G、RQ-082）。
   - 今天（基准）同一阶段里每个收件人都会尝试，有人失败才停止后续阶段；A 的发送端有序组没有阶段边界，失败者之后、同一阶段的收件人收不到这一阶段（TASK-23 §7）。这是一处客户端可见的差异，附录 G 没有列，与 RQ-082「可见行为变化只允许附录 G 所列各项」不一致。影响只在交付失败时，且失败者的连接会被关闭，其余成员随后由清理通告（XO-5）拿到当前队伍状态。
   - **建议 A**：附录 G 加 VC-10，写明这条差异与上面的自愈路径，证据是 TASK-23 的 N8 XO-6 变体（`logs/task-23-*`）；由 TASK-33 写入。属于改 Spec 验收（附录 G）。
   - 备选 B：给发送端的有序组加「阶段」语义（阶段内 BestEffort，跨阶段检查 Required 失败），逐字保持今天的行为；改 T-003 的发送端组件与它的用例，由 TASK-31 做。
   - 备选 C：不改 Spec，只在 A 的最终汇报里列为已知近似。不建议：与 RQ-082 字面冲突。

4. **F-116：发送端或登记点的通用防线**（TASK-34 的遗留问题）。
   - 三种选项中 (a) 只丢超限的 BestEffort 条目、(b) 登记点对发往非来源连接的大帧只丢该收件人，都改已定契约（RQ-036、RQ-031(d)）；(c) 约定由 TASK-33 写进 AGENTS.md（F-118）。
   - **建议**：A 内只做 (c)；(a)(b) 交主控方立项，在 A 的最终汇报里列为遗留问题。

5. **SC-11、SC-12 的产出位置**（TASK-34）。
   - **建议**：过渡机制清单写在 `verification/transition-mechanisms.md`，A 的最终汇报写在 `verification/a-final-report.md`；封存时主控方新建的 `attempts/spec-001-executor-outbox-lock-removal/spec-result.md` 引用这两份。理由：子 Spec B 以过渡机制清单为起点（`multi-spec-map.md`「Overall Direction」），放在 A 的 `verification/` 下，B 只读引用、不改，符合子路由决定的路径约定。
   - 备选：汇报直接写进 `spec-result.md`。代价：汇报要在封存之前给出，而 `spec-result.md` 是封存时由主控方建的。
   - 汇报写成后是否发布为页面，由主控方决定。

6. **M-3 的运行矩阵**（TASK-32）。
   - **建议**：与 M-2 相同的 16 次运行，另加 2 次 G0 控制运行，使 (e) 与基线的对照不只靠一次（`m2-summary.md` §9 第 1 条）；约一个半小时。性能只按「单条 CMD ≤ 2 s」判定，原目标值只作对照；S5 的正确性各项全部保留。
   - 备选：缩减为每类一次（约 50 分钟）。代价：S1、S2 的干净样本只有一份，受污染时要补跑，总时间不一定更短。

7. **T-009 拆成五个单元**（§0）。请确认拆法与顺序；TASK-32 与 TASK-33 可以对调（§0.2 TASK-33 一行）。

## 主控方裁定（执行前）

1. **AC-006：按规则普查，玩法状态一律挂点，执行机制和不可变内容按附录 B 豁免，另加普查判据守住名单完整。接受。** 附录 F 补一句，写明名单的唯一出处。RQ-061 的文字不改，实际覆盖面的扩大在 Result 中登记。
   - (a) `EffectOperationLedger` 与 `SelectedSessionSafety` 本单元挂点。
   - (b) `NetworkCommandAuthorization` 的按流帧豁免；附录 B 补一行「请求级帧（过渡）」，同时写进 SC-12 的过渡机制清单，交 B（请求级上下文改为显式数据，归 T-008）。
2. **F-97 改为「先判位置、后编码」，接受。** 这推翻了 TASK-23 裁定第 1 条里与次序相关的部分（编码本身仍在登记点完成）。`OutboundSendFailureTests` 的 4 条改为在作业内调用，断言不变；另补 1 条反例。在 Result 中登记推翻了哪条裁定、为什么推翻。
3. **F-98（XO-6 阶段近似）：在附录 G 加 VC-10，由 TASK-33 写入，接受。** 这是主控方授权的 Spec 验收修订，写进 A 的最终汇报，交外部审核时说明。不给发送端加阶段语义。
4. **F-116：A 内只把约定写进 AGENTS.md，接受。** 改契约的两种做法列为遗留问题，交主控方另行立项。
5. **SC-11 与 SC-12 的产出放在 `verification/a-final-report.md` 与 `verification/transition-mechanisms.md`，接受。** 封存时由 `spec-result.md` 引用这两份文件。
6. **M-3 的运行矩阵与 M-2 相同（16 次），另加 2 次 G0 控制运行，接受。** 判定按「单条 ≤ 2 s」与 M-2 裁定后的停止线，正确性项全部保留。就绪探测连接的豁免写成规则。
7. **拆分与顺序（TASK-30 → 31 → 代码冻结 → 32 → 33 → 34），接受。**

## Result

执行轮次：第 1 轮（基准 `a9c79e98`；改动未提交）。

- **Outcome:** done（Result review 为 required，提交前待主控方安排独立复核）。
- **Changed:**
  - 普查判据：新建 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayStateCensusTests.cs`（主判据、4 行反例 Theory、豁免表 33 项、不持有状态的写入口表 1 项）。普查 73 个持有者 ＝ 挂点 40 ＋ 豁免 33（E1 7 ＋ E2 21 ＋ E3 4 ＋ R061 1）；单例 53 ＋ 静态 17 ＋ 组件 2 ＋ 句柄 1 ＝ 73（`verification/logs/task-30-census.txt`）。
  - 挂点：`Server/src` 改 27 个文件（Application 11 ＋ Protocol 16；其中 26 个只加断言，1 个是 F-97 的 `OutboxAdapters.cs`），新增 `GameplayWriteAssertion.Writing` 68 处（分类表写成员 53 ＋ 私有成员 15），生产共 143 ＋ 68 ＝ 211 处。新挂点 27 个：Application 10（`ClientSettingsDomainService`、`WeaknessDomainService`、`CharacterContainerDomainService`、`HousingState`、`ExpertStoreDomainService`、`PvpLobbyDomainService`、`DungeonEntryIntentDomainService`、`JoustMaintenanceService`、`SelectedSessionSafety`、`EffectOperationLedger`）；Protocol 17（`DungeonRunDeliveryRegistry`、10 个 `*DailyRefreshService`、`PartyProjectionStreamRegistry`、`PartyDeliveryStream`、`PartyClientIdRegistry`、两个塔的 `*DepartureProjectionQueue`、`JoustBetHandler`）。`GameplayWriteEntryTableTests`：`Types` 14 → 41，`Table` 201 → 290 行（写 190 ＋ 读 86 ＋ 豁免 14）。
  - 运行期反例：`GameplayWriteAssertionTests` 新增 27 个用例（17 个 Fact ＋ 每日刷新 Theory 10 行），每条：执行器外调用写成员即抛、恰报一条取证、读照常、作业内照常。
  - F-76：反射一支的闭包收集与宿主判定抽到 `Server/tests/Dfo.Server.Application.Tests/Execution/GameplayJobHostClosure.cs`，新建 `Server/tests/Dfo.Server.Host.Tests/GameplayJobHostClosureTests.cs` 在完整生产闭包上运行（`Dfo.Server.Host.Tests.csproj` 加一行链接）；`GameplayJobHostStructureTests.cs` 的源码一支改为词法去注释、解码 Unicode 转义、扫描项目文件 `<Using>`，加 3 行反例 Theory，已有 9 个用例断言不变。
  - F-97：`Server/src/Dfo.Server.Application/Execution/Outbox/OutboxAdapters.cs` 三个入口改为先判位置后编码，类 remarks 随之改写；`OutboundSendFailureTests.cs` 4 条改在作业内调用（夹具加 `Services`），断言不变；`OutboxRegistrationTests.cs` 加 1 条反例。
  - §7.3 补反例 2 条：`PasswordComputationLimiterTests.A_command_handler_that_takes_the_limiter_is_refused`、`TransportWriteSourceTests.A_sequence_send_and_a_udp_send_in_any_other_file_are_named`。
  - §3.3 包装：45 个测试文件、91 处（`verification/logs/task-30-red-green.txt`、`logs/task-30-boundary.txt`）。
  - Spec：`spec.md` 附录 F「保留」「改为执行器内访问」两行、附录 B「生产者的入队侧」「开始监听前的启动期工作」两行，并新增「请求级帧（过渡）」一行（裁定 1(b)），共 5 处。
  - 文档与证据：新建 `verification/t009-criteria.md`；`verification/follow-ups.md` 关闭 F-60、F-62、F-75、F-76、F-85、F-97，续写 F-69，新增 F-124，文首补 TASK-30 一行；`verification/logs/task-30-*.txt` 12 份（脚本全文附在各自末尾）。
- **Why:**
  - 普查规则在规划（§2.1）上细化了 5 处，都写在判据 remarks 与 `t009-criteria.md` §2.1：主构造器捕获参数按类型判定（否则每个主构造器类型都成了持有者）；规则 2 加 `AsyncLocal<>`、`ThreadLocal<>`（否则裁定 1(b) 的请求级帧根本认不出、豁免无从落地）；接口元素数组不算状态；引用另一个已注册单例的字段不算自己的状态、已单独归类的组件不再传染持有者；多一个归类 R061。
  - `DomainTransactionScope` 归 R061 而不进 `Types`：放进 `Types` 会让它交出的租约、事务与发件箱（每个作业一份）按规则 4 被当成句柄、要求挂点；写工作单元本身已由事务开启点判据守着。
  - 每日刷新、`JoustMaintenanceService`、`JoustBetHandler` 与 `WeaknessDomainService` 句柄的写在私有或 protected 成员里，断言加在那里，公开入口（只入队）按附录 B 豁免；分类判据看不到私有成员，这部分由运行期反例证明（变异 MU1c 只让反例变红，已记为边界）。
  - `PartyDeliveryStream` 是唯一改了成员形状的挂点：两个自动属性 setter 改为带 `field` 的访问器，两个交出活集合的 getter 按「既读又写按写」归写并断言；签名、可见性与逻辑不变。
  - 包装形状只有两种（单句包装、多句含断言整体进作业块），断言文本一字不改，因此断言行多重集合不变；写成员出现在断言表达式里的（例如 `Assert.True(lobby.Withdraw(1))`）整句放进作业块，不改断言。
  - F-97 的新反例在三个入口各调用一次，取证 3 条（规划写 1 条）：只测一个入口，另两个入口的次序回退不会被发现。
- **Checks:**
  1. 构建 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：RED 第一段、正式全量的第 1 部分、变异全部做完后的重建，都是 0 警告 0 错误。
  2. RED／GREEN（`logs/task-30-red-green.txt`）：第一段（只有判据与反例）Application 31 条红 ＝ 普查 1 ＋ 源码反例 3 ＋ 运行期反例 27；普查的违规恰好列出 26 个类型（27 个挂点去掉只有把注册表列进 `Types` 后才按规则 4 出现的句柄 `PartyDeliveryStream`）；运行期反例的红因 26 条是「没有抛出」、`EffectOperationLedger` 1 条是「先抛出了它自己的校验异常」；F-97 反例红（编码异常先于位置检查）；闭包判据在基准上为绿；普查反例 4 行在基准上为绿（它们测判据本身）。第二段（挂点后、包装前）：Application 564 ＝ 测试体 559 ＋ 源码反例 3（识别改写之前）＋ 基线 2，Protocol 50，Networking 2 ＝ 测试体 1 ＋ F-97 反例 1，Host 0；测试体 559 ＋ 50 ＋ 1 ＝ 610 个失败来自 85 个调用点，栈底全在测试代码或测试夹具，没有生产路径（停止条件 1 不成立）。三轮包装后（85 ＋ 5 ＋ 1 ＝ 91 处）GREEN：Application 4588 中只剩基线 2 条失败，Protocol 1752、Networking 182、Host 196 全过。
  3. 正式全量（`logs/task-30-build-test.txt`）：HttpApi 54 ＋ Pvf 80 ＋ Protocol 1752 ＋ Persistence 477 ＋ Infrastructure 970 ＋ Networking 183 ＋ Application 4589 ＋ Host 196 ＝ 8301 ＝ 8262 ＋ 39 － 0（逐步：54 ＋ 80 ＝ 134；＋ 1752 ＝ 1886；＋ 477 ＝ 2363；＋ 970 ＝ 3333；＋ 183 ＝ 3516；＋ 4589 ＝ 8105；＋ 196 ＝ 8301）。N ＝ Application 36 ＋ Networking 2 ＋ Host 1 ＝ 39（逐条在 `t009-criteria.md` §8）。通过 54 ＋ 80 ＋ 1752 ＋ 477 ＋ 968 ＋ 183 ＋ 4571 ＋ 196 ＝ 8281，失败 2（基线的 2 条 `DimensionDungeonTests`），跳过 2 ＋ 16 ＝ 18；8281 ＋ 2 ＋ 18 ＝ 8301。挂起转储 0。表征测试 10 通过、0 跳过，`Characterization/` 无改动。日志信号与 TASK-29 逐条相同（必须为 0 的七项中 `GameplaySelfWait` 1、其余 0，「must happen inside a gameplay job」0；bus 8、inbound 8、gm 2），唯一差别是 `the drain tears it down` 5（TASK-29 2），随负载变化，排空路径未改。耗时 187 s，开跑时 swap 已用 10739 MB。F-69：比 TASK-29 多出 ≥ 9 秒的 11 条，各单独复跑两次都是 1～4 秒，满载拉长。
  4. 零违规取证（`logs/task-30-violations.txt`）：预期先写在日志开头（41 ＋ 27 ＋ 3 ＝ 71）。71 条 ＝ bus-leaked-flow 2 ＋ network-wait 1 ＋ queue-leaked-flow 1 ＋ write-off-executor 67（2 ＋ 1 ＋ 1 ＋ 67 ＝ 71）；反例 64 ＋ 对照 7 ＝ 71；反例类与对照以外 0；没有测试帧 0；`exe:` 0；退出码 0。`violations.sh` 只改了两个预期数（24 → 51、2 → 5），没有新类。
  5. 普查输出（`logs/task-30-census.txt`）：全文、加法与规划表的逐行对照。规划的 24 个挂点都在，多出 3 个；规划写的 3 个「投影」是文件名、持有者是文件里的刷新服务；`PartyClientProjectionService` 的 `next` 实际在 `PartyClientIdRegistry`。
  6. 变异（`logs/task-30-mutations.txt`）：24 项（MU1a、MU1b、MU1c、MU2～MU9、MU10a、MU10b ＝ 13；MR1～MR11 ＝ 11；13 ＋ 11 ＝ 24）都让指定用例变红；每项 `cmp` 还原、两棵树的 `git diff` sha1 前后相同；重建 0／0。与计划不同：MU2 只让普查判据变红（分类判据只对列出的类型报过期行，名单由普查守着）；MU1c 只让运行期反例变红（私有成员不在分类表里）。
  7. 新增 ×10（`logs/task-30-new-x10.txt`）：39 个用例 ×10 全过；`GameplayWriteEntryTableTests` 9、`GameplayStateCensusTests` 5、`GameplayJobHostStructureTests` 13、`GameplayJobHostClosureTests` 1 各 ×3 全过。
  8. 检索集（`logs/task-30-retrievals.txt`）：R1 211 ＝ 143 ＋ 68，事务开启点 7，分类表 290 行 0 违规；R2 只有 `IsEnabled`；R3 附录 B 两行不再出现「作为 RQ-061 的启动期例外」；R4 附录 F、附录 B 各有新命中，逐行列出；R5 同 V12 的 3 个；R6 0 与 0；R7 三个入口位置检查都在编码之前；另加 R8：豁免表指名的 5 个附录 B 行都存在。
  9. 收口引用核对（`logs/task-30-closure-refs.txt`）：用例名 82、日志 23、F 编号 9（关闭 6 ＋ 移交 1 ＋ 引用 2：6 ＋ 1 ＋ 2 ＝ 9），0 处不符；脚本多了一条规则：今天不存在、但在 `a02bce46` 存在的用例名算「A 期间删除」（3 个，都是对账表里写明删除的）。
  10. 测试改动核对（`logs/task-30-diff-check.txt`）：方法删除 0、新增 25 个方法 ＝ 39 个用例，正是 N；45 个包装文件的断言行多重集合不变、没有白名单外残余；断言行多重集合有变化的 5 个文件都只是新用例带来的新增；白名单外残余 63 处 ＝ 43 ＋ 12 ＋ 3 ＋ 2 ＋ 1 ＋ 1 ＋ 1，都在允许改动判据的文件里。
  11. 边界（`logs/task-30-boundary.txt`）：`Server/src` 27 个文件，只在 Application 与 Protocol；新增行 159 行属白名单形状，其余 67 行逐条列出（表达式体拆成块体的签名与语句、HousingState 两处单行块、F-97 的次序与 remarks、`PartyDeliveryStream` 的访问器头与一行注释）；`plan.md`、`AGENTS.md`、`Docs/`、`protocol/`、表征目录、以往收口文件无改动；`spec.md` 只有 §9 的 5 处。
  12. 隔离复跑（`logs/task-30-isolated.txt`）：Application 单独 4589 ＝ 4571 ＋ 2 ＋ 16，取证 59（反例 58 ＋ 对照 1）；Networking 单独 183 全过，取证 7（反例 6 ＋ 对照 1）；Host 单独 196 全过，取证 1（对照）；类外都是 0。包装第一轮中 Networking 的 2 条 `Connection refused`（F-55／F-103 形状）在 GREEN、正式全量与隔离运行中都通过。
  13. `git -c core.whitespace=cr-at-eol diff --check` 无输出；`HousingState.cs` 保持 CRLF，其余改动文件保持 LF，新文件 LF。`dotnet build-server shutdown` 已执行；`pgrep -x yes` 为 0（本单元没有用负载）。
- **Remaining:**
  - Result review 为 required：提交前需要主控方安排独立复核。重点：分类表 89 个新行的读写判定；豁免 33 项与附录 B 行的对应（尤其 E2 的 15 张常量表与 R061 这一新归类）；普查规则的 5 处细化；私有成员上的断言只有运行期反例守着；`PartyDeliveryStream` 改了访问器形状；§3.3 的 91 处包装。
  - 与计划或裁定不同之处（都已写进 `t009-criteria.md` 与对应日志）：挂点 27 个（规划 24）；N ＝ 39（规划 33）；取证 71（规划 66）；普查反例 4 行（规划 3）；多了 R061 归类与「不持有状态的写入口」表；闭包辅助文件放在 `Dfo.Server.Application.Tests/Execution/`（不在 `tests/Support`，以免改 Application.Tests 的 csproj），Host.Tests 链接它；对账中缺反例的条目以变异 MR1～MR11 证明，合成反例只补 2 条。
  - 新事项 F-124 交主控方；附录 A、B、E、F、H 的最终对账与过渡机制清单中的请求级帧 4 项交 TASK-33、TASK-34（`t009-criteria.md` §9）。

## 主控方处置（第 1 轮评审后）

第 1 轮快审 verdict `changes_required`，原因是 F1。按 `task-rework` 处理：

1. **F1：采用评审给出的「更彻底」修法。** 把 `WeaknessDomainService+Weakness` 列进 `Types` 和 `StatelessEntries`，做法同 `AltarDungeonEngine`，由分类表逐成员登记。`PauseRecovery`、`ResumeRecovery` 各自调用 `Writing`，私有的 `SetRecovery` 是否保留断言由执行者按分类判据的口径决定。运行期反例补上 `SetWeakness()` 与 `PauseRecovery()` 两次执行器外调用。取证数与 `violations.sh` 的预期同步更新。`t009-criteria.md` §2、Result、普查日志 V7 段按实情改写。理由：首选修法只能守住今天的成员，以后新增写成员仍然没有判据；分类表能覆盖这一类缺口。
2. **F2：在普查判据中补一条规则，并配一行反例。** 主构造器捕获参数生成的 `<x>P` 字段，只要在构造器之外有 `stfld`，就算可写。`GameplayOutbox+OrderedScope.set_Delivery` 不是单例，不受影响，在 remarks 中写明。
3. **F4 的小问题：`SelectCharacterProductionStateTests`、`SelectCharacterEntryLifecycleServiceTests` 改为传 services 的 `RunAsync`。** 断言不变，与 `TestGameplay` 的约定保持一致。
4. **F3、F5、F6：只作记录。** F5 写进 `t009-criteria.md`，交 TASK-33 对账时知悉。F6 是基准就有的行为，记一句即可。

改完后重跑：
- Release `--no-incremental` 构建；
- Application.Tests 中普查、分类判据与 Weakness 相关测试的过滤运行，新反例要先红后绿；
- 补两项变异（去掉 `SetWeakness`／`PauseRecovery` 的断言，用例变红）；
- 受影响项目的取证汇；
- `cr-at-eol` 口径的 `diff --check`。

正式全量是否重跑，由执行者按改动面判断：生产改动只有 Weakness 一个文件时，Application 与 Protocol 两个项目重跑即可。

## 第 1 轮评审后修改

执行轮次：第 2 轮（按「主控方处置（第 1 轮评审后）」第 1～4 条；改动未提交）。

- **Changed:**
  - F1：`Server/src/Dfo.Server.Application/Characters/WeaknessDomainService.cs`（本轮唯一的生产改动）——`PauseRecovery`、`ResumeRecovery` 各自断言，私有 `SetRecovery` 不再断言（它只经这两个成员进入，分类表看不到私有成员）。`GameplayWriteEntryTableTests.cs`：`Types` 加 `WeaknessDomainService+Weakness`（41 → 42），`Table` 加 6 行（写 4、读 2），`For` 的读注改写。`GameplayStateCensusTests.cs`：「不持有状态的写入口」表加这个句柄（1 → 2）。`GameplayWriteAssertionTests.cs`：句柄反例改为 async，执行器外依次调用 `Recover`、`SetWeakness`、`PauseRecovery`，断言恰报这三条；类 remarks 的计数改为 24 ＋ 29 ＝ 53。
  - F2：`GameplayStateCensusTests.cs` 规则 (1) 补「构造器之外有 `stfld` 写它的捕获参数按可写计」（`IsReassigned` 扫类型及其嵌套类型的方法），remarks 写明 `GameplayOutbox+OrderedScope.set_Delivery` 不是单例；反例 Theory 加 reassigned parameter 一行（4 → 5 行）。生产中没有新认出的持有者。
  - F4：未改。`SelectCharacterEntryLifecycleServiceTests`、`SelectCharacterProductionStateTests` 的被测服务都是手工 new 出来的，用例里没有 `IServiceProvider`，同文件原有的 `TestGameplay` 调用也都不传 services；要「传 services」只能为这两处新建一个空容器，而空容器里没有 `GameplayExecutor`，`TestGameplay` 照样新建执行器，行为相同、只多一段夹具。请主控方确认是否仍要改。
  - F3、F5、F6：执行者没有拿到评审正文（仓库里没有 `reviews/task-30-result-review.md`，指令里也没有转述），无法按内容记录；`t009-criteria.md` 新增的 §10 留了这三条的位置，请主控方补记。
  - 文档：`t009-criteria.md` §2.1、§2.4、§4（N10、N33）、§8 与新增 §10；`logs/task-30-census.txt`（全文换成重跑结果，加法、V7 段改写）；`logs/task-30-verify.txt` V7 补一段；新建 `logs/task-30-rework.txt`；`logs/task-30-diff-check.txt`、`logs/task-30-boundary.txt`、`logs/task-30-closure-refs.txt` 重跑；`follow-ups.md` 新增 F-125，文首 TASK-30 一行同步。scratchpad `t30/violations.sh` 的预期改为 `GameplayWriteAssertionTests=53`。
- **Checks:**
  1. RED（测试、`Types`、`Table` 与 F2 规则已写，生产未改）：过滤运行 44 ＝ 42 通过 ＋ 2 失败，即句柄反例（`PauseRecovery` 报成 `Weakness.SetRecovery`）与分类判据（`PauseRecovery`、`ResumeRecovery` 写成员不断言）。新普查反例行一写就绿，它的 RED 由变异 MU12 给出。GREEN：普查、分类、句柄反例、`WeaknessTests`、`WeaknessGoldRecoveryTests` 共 73 个全过；普查持有者仍为 73 ＝ 挂点 40 ＋ 豁免 33，`Types` 42 ＝ 40 ＋ 不持有状态 2，0 违规。分类表 296 行 ＝ 写 194 ＋ 读 88 ＋ 豁免 14；新增 `Writing` 69 处（写成员 57 ＋ 列不到的 12），生产共 143 ＋ 69 ＝ 212 处。
  2. 变异 3 项（`logs/task-30-rework.txt`）：MU11a 去掉 `SetWeakness` 的断言、MU11b 去掉 `PauseRecovery` 的断言，都让分类判据与句柄反例变红；MU12 关掉 F2 规则，reassigned parameter 一行变红。每项 `cmp` 还原，sha1 前后相同；随后 `dotnet build Server/86JP.Server.slnx -c Release --no-incremental` 为 0 警告 0 错误。
  3. 取证：生产只改了 Weakness 一个文件，按处置只重跑 Application 与 Protocol。Application 单独运行 4590 ＝ 4571 通过 ＋ 3 失败 ＋ 16 跳过；取证 61 ＝ 反例 60（53 ＋ 1 ＋ 1 ＋ 3 ＋ 1 ＋ 1）＋ 对照 1，类外 0，`exe:` 0。Protocol 1752 全过，取证 1（对照）。
     - 3 个失败 ＝ 基线 2 条 `DimensionDungeonTests` ＋ 1 条新见的 `IllusionTowerPublicFlowTests.Public_handlers_complete_real_pvf_tower_return_to_town_and_reenter`。
     - 这一条的原因：随机奖励选中了 PVF 缺 `[minimum level]` 的装备，在 `PvfEquipmentDefinitionSource.cs:160` 抛错；栈中没有本单元的代码。单独复跑 5 次全过，记为 F-125，按停止条件 6 一并回报。
     - 正式全量（`logs/task-30-build-test.txt`）其余部分沿用。按本轮改动折算：总数 8301 ＋ 1 ＝ 8302（Application 4590），取证 71 ＋ 2 ＝ 73。
  4. 测试改动核对：删除方法 0，新增方法 25 ＝ 用例 40；包装文件的断言行多重集合不变；白名单外残余 63 处 ＝ 43 ＋ 13 ＋ 2 ＋ 2 ＋ 1 ＋ 1 ＋ 1，都在允许改动判据的文件里。
  5. 边界：`Server/src` 仍为 27 个文件；新增行中 164 行属白名单形状，其余 71 行逐条列出（本轮多出 4 行：两个签名与两处 `SetRecovery` 调用）。`spec.md`、`plan.md`、`AGENTS.md` 本轮未动。
  6. 收口引用核对：用例名 86、日志 24、F 编号 10，0 处不符。
  7. `git -c core.whitespace=cr-at-eol diff --check` 无输出；`WeaknessDomainService.cs` 保持 LF，`HousingState.cs` 保持 CRLF。`dotnet build-server shutdown` 已执行。
- **Remaining:** F4 是否仍要改、F3／F5／F6 的内容，交主控方；F-125 交主控方。

## 主控方处置（第 2 轮复核后，提交前）

第 2 轮定点复核 verdict 为 `approved`（`reviews/task-30-result-review.md` Round 2）。提交前处理两点：
- 复跑次数的说法统一为 5 次。
- F-125 补记评审意见：这是一处生产健壮性缺口，内容缺字段时奖励结算会直接抛错，不只是测试偶发。它的归属不在 A 的执行器范围内，交主控方另行立项。
