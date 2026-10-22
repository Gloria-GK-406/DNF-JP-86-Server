# TASK-16 — T-004b7：对账收口（探针终核、附录 A/B 对账、分离点终核、排空顺序、后续事项、T-004c 进入条件）

基准提交 `c3115f9a`（TASK-15 与 RQ-091 用户修订之后）。本单元是 `TASK-08.md`「T-004b 拆分」§2 表中的 b7（原编号 TASK-14），编号按 `TASK-14.md`「主控方裁定（执行前）」第 1 条顺延为 TASK-16。它是 T-004b 的最后一个单元，结论作为 T-004c 的进入条件。

## Task Unit Contract

- Revision: 1
- Outcome and references：
  1. **探针终核**（拆分原则第 6 条；Plan T-004「完成条件」的前置）：五个测试项目全量探针复跑，证明 (A) 只剩 §3.10「测试直接调用总线」与经裁定允许的残留；逐族与 TASK-07 对照；TASK-03 三处门外写终核；三项还原证明（§1）。
  2. **附录 A／B 对账**（AC-006、RQ-010、RQ-011、RQ-013～RQ-015）：附录 A 每行「来源 → CMD 类型 → 证据」；修正附录 A 中与代码不符的文字；附录 B 每行「安全理由 ＋ 判据或测试」，并补齐执行器外实际存在、但附录 B 未列的部分（§2）。
  3. **分离点终核**（TASK-04 Remaining N-2、TASK-06 §7 待办 3e）：全部分离点在 `SuppressFlow`／`RunDetached` 下启动、不被作业 await；补一条结构判据，限定 `RunDetached` 与 `SuppressFlow` 的出现位置（TASK-04 Remaining 第 2 条 R2-2），带反例（§3）。
  4. **排空顺序**（TASK-11 主控方补充「交给 TASK-14（审查 N3）」，按 TASK-14 裁定第 14 条归本单元）：卡牌协调器对排空之后的调度请求容错，不再抛 `ObjectDisposedException`；补测试与变异（§4）。
  5. **重型宿主与打包进程用例清单**（TASK-14 定向复核裁定、TASK-15 Remaining 第 1、2 条）：数量、预算、测试进程内存峰值，作为信息与测试基础设施建议，不改预算（§5）。
  6. **后续事项清单** `verification/follow-ups.md`：汇总 TASK-08～TASK-15 的 Remaining、主控方补充与裁定中尚未关闭的事项，每项附来源（§6）。
  7. **T-004c 进入条件核对** `verification/t004c-entry.md`：门断言与 RQ-061 判据开启前需要的事实，逐项给出现状与证据，或写明缺口（§7）。
- Owned boundary：
  - 生产代码，只限 `Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs`（§4）。
  - 测试：
    - `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/DungeonCardPresentationCoordinatorTests.Timers.cs`：追加 §4 的两条用例；
    - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/DetachmentSiteTests.cs`：§3 的结构判据及其反例。
  - 探针工具：`verification/probe-tools/` 新增 `fam.py`、`residual.py`、`hostnotest.py`（从 scratchpad `t12/`、`t14/` 复制，只改路径；§1.2）。`patch.py` 若因 TASK-14／15 的改动对不上文本，只改匹配模式，不改挂点集合（TASK-08 §5 的规则）。
  - 文档与证据：
    - `spec.md`：附录 A 的事实性文字与对账指针；附录 B 的清单补齐与一处自相矛盾的举例（§2）。不改任何 RQ／AC 的文字；
    - 新增 `verification/entry-reconciliation.md`（探针对照、附录 A／B 对账表、分离点表、TASK-03 三处门外写）；
    - 新增 `verification/follow-ups.md`（§6，含 §5 的重型宿主清单与建议）；
    - 新增 `verification/t004c-entry.md`（§7）；
    - `verification/task-07-conflicts.md` 追加 §12（冲突分布的最终形态）；
    - `verification/logs/task-16-*.txt`。
  - **不改**：协调器以外的任何生产文件（含执行器、队列、作业令牌、`GameplayExecutorLifetime`、`TouchSocketTransportDrain`、模块注册次序）；`CommandBus` 与管线；`AGENTS.md` 与 `Docs/`（归 T-009）；表征测试与金样本；其他单元的冲突用例。
- Required checks：
  1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘到 `logs/task-16-build-test.txt`。
     - 总数：TASK-15 为 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 183 ＝ 8137（HttpApi、Pvf、Protocol、Persistence、Infrastructure、Networking、Application、Host）。本单元 ＝ 8137 ＋ N － 0，逐项目对照并写出加法，预计只有 Protocol 与 Application 增加。
     - 失败只能是那 2 条 `DimensionDungeonTests`；跳过 20 条；表征测试 8 通过、2 跳过；挂起转储 0。
     - 日志信号与 TASK-15 第 3 轮 Checks 2 相同的那几项，另加 §4 新增的那条 Information 计数。非零的逐条说明来源。
     - **耗时只作信息**（RQ-091 用户修订：测试套件的耗时与内存不作达标依据）：与 `logs/task-15-build-test-r3b.txt` 逐条比，多出 ≥ 9 秒的列出并隔离复跑一次，按「排空 10 秒／SQLite 30 秒／负载」三种形状归类（TASK-08 N-2 的间接核对）。出现 > 30 秒的停顿时，按 TASK-14 Remaining 第 3 条的顺序记录可得的证据（本单元路径 → 换页 → 线程栈）；不安装 `dotnet-dump`／`dotnet-counters`（TASK-14 定向复核裁定）。
  3. **隔离复跑**（`logs/task-16-isolated.txt`）：Protocol、Application 各一次；Networking 加 `--blame` 一次（TASK-12 Remaining 第 2 条：再出现测试主机崩溃时要取到用例名）；Host 一次，同时用 scratchpad `t14/sampler2.sh`（2 秒一次、KiB）采样 testhost RSS，数字供 §5。
  4. **新增测试 ×10**：§3、§4 的新用例隔离连续 10 次，全量负载下 1 次，全部通过，落盘到 `logs/task-16-new-x10.txt`。
  5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改。
  6. **变异核对**（`logs/task-16-mutations.txt`；每项用 scratchpad 备份，逐字节还原后 `cmp`，全部做完后 `--no-incremental` 重建）：
     - V1：恢复协调器 `Replace` 中的 `ObjectDisposedException.ThrowIf`（即撤销 §4 的容错），§4 的两条用例变红；
     - V2：在 `Server/src` 某个非白名单文件里临时加一处 `GameplayJob.RunDetached(() => { })`，§3 判据变红；
     - V3：临时加一处 `ExecutionContext.SuppressFlow()`，§3 判据变红。
     - 反例用例（RQ-072 式，喂给扫描器合成源码文本，断言被拒）与 V2、V3 互为补充：反例证明扫描器会拒绝，变异证明它确实扫到了真实源码树。
  7. **测试改动核对**：新增测试逐条列名，写出 N 的加法；`DungeonCardPresentationCoordinatorTests.Timers.cs` 用 scratchpad `t07/diffcheck7.py c3115f9a --lines` 证明原有方法集合与断言行多重集合不变，差异只有新增的两个方法。
  8. **生产改动边界**（`logs/task-16-boundary.txt`）：`git diff --stat -- Server/src` 只含协调器一个文件；`IGameplayQueue`、`SuppressFlow|RunDetached` 的命中与基准逐行相同。
  9. **探针终核**（§1）：原始记录落盘到 `logs/task-16-probe-{Application,Persistence,Protocol,Host,Networking}.txt`，分族汇总落盘到 `logs/task-16-probe-summary.txt`；三项还原证明。
  10. **文档对账**：`entry-reconciliation.md` 中引用的每一个测试名，在基准上用 `grep` 找到定义（脚本输出落盘到 `logs/task-16-evidence-names.txt`）；找不到的不得作为证据。
- Dependencies：TASK-09、TASK-10、TASK-11、TASK-12、TASK-13、TASK-14、TASK-15（均已提交，基准 `c3115f9a` 含全部结果）。
- Result review：required — 本单元的结论直接作为 T-004c 的进入条件：如果探针残留归因错了、附录 A／B 漏了一个执行器外的写者，或进入条件里少了一项事实，T-004c 会在错误的前提下翻转 `SendAsync`、开启门断言与 RQ-061 判据，错误要到翻转后的全量才暴露，而且会被当成翻转本身的问题去查。对账表、Spec 文字与后续事项清单是判断性的文档产出，构建与全量测试检验不出它们错没错。§4 的代码改动很小，V1 能暴露它，但不足以降低整个单元的审查需要。
- Task-specific authorization：
  - 探针复跑（Required check 9）与变异核对（Required check 6）期间，允许临时改动 `Server/src`。用后按三项还原证明处理（快照前后 `cmp` 相等、`snapcheck` 只剩本单元的正式改动、`grep TestGameplay|GameplayProbe Server/src` 为 0）；不提交，不 `git stash`。
  - 允许为统计附录 A 第 1 行的注册数写一个**不提交**的临时用例或脚本（§2.2），运行后删除，并在 Result 中写明删除。
  - 本单元没有分配到冲突用例，不删除、不改写任何既有用例。若有既有用例因 §4 失败，停下回报。
  - 对 `spec.md` 的修改限于附录 A、附录 B 的事实性文字与清单（§2）。RQ、AC、Definitions 与附录 C～H 不改。

## Detailed Plan

### 0. 执行顺序

1. §4 协调器容错与两条用例（先写测试，确认红，再改代码）；§3 结构判据与反例。构建，跑 Protocol 与 Application。
2. 变异核对 V1～V3，逐字节还原，重建。
3. 探针终核（§1）：快照 → `patch.py apply` → 构建 → 五个项目 → 汇总与排除轮 → `patch.py revert` → 快照 → 重建。
4. 正式全量 → 隔离复跑（含 Host RSS 采样）→ 新增测试 ×10。
5. 静态检索与文档：附录 A／B 对账（§2）、分离点表（§3）、TASK-03 三处（§1.4）、重型宿主清单（§5）、后续事项（§6）、T-004c 进入条件（§7）、冲突分布 §12。
6. 测试改动核对、边界 grep、证据名核对 → 写 Result。

第 3 步必须在第 1 步的正式改动之后，这样探针跑的是最终代码。第 5 步依赖第 3、4 步的数字。

### 1. 探针终核

#### 1.1 跑法

- 工具与用法同 TASK-08 Remaining：`PROBE_WORK=<scratchpad 下的 t16/probe>`；`python3 patch.py apply` → `dotnet build -c Release` → `bash runall.sh p1`（默认五个项目）→ 分族汇总 → `python3 patch.py revert`。`emit.py` 前缀 `PROBE_LOG_PREFIX=task-16-probe`，P1b 先经 `p1bsplit.py --write` 再落盘。
- `patch.py apply` 若报某个挂点文件对不上（TASK-14／15 改过 `GameServerStartupService.cs` 等），只改匹配模式，不改挂点集合；Result 中逐个列出改了哪条模式。
- 探针轮的测试失败不作为证据（TASK-07 §7）；只记录。

#### 1.2 分族与工具固化

- 分族沿用 TASK-10 起的 `fam.py`（alist.py 的规则，3.1 拆成 dispatcher/Received 与 Connected/Closed）。它和 `residual.py`（分发器自身发起的残留）、`hostnotest.py`（无测试帧的 (A) 记录）目前只在 scratchpad。复制进 `probe-tools/`：去掉绝对路径，`PROBE_TOOLS` 改为由脚本所在目录推导；规则正文不改。复制后用 TASK-14 p1 的原始 JSON（scratchpad `t14/probe/p1`）重跑一遍，输出须与 `logs/task-14-probe-summary.txt` 的表逐格相同，证明复制没有改变口径。

#### 1.3 判定：(A) 只剩什么

下表是「允许的残留」的完整清单。最终轮中，3.1～3.9 各族出现本表以外的任何 (A) 记录，**停下回报主控方，不在本单元迁移**：那意味着有入口没有迁入，属于范围问题。

| 族 | TASK-07 最终轮（四项目；Persistence 另列） | 最近一次（TASK-14 p1） | 本单元允许的终值 | 允许的依据 |
|---|---|---|---|---|
| 3.1 dispatcher/Received | 5208／16257 | 1／1 | 1／1 | 遥测路径的只读 `IsSuperseded`（TASK-09 裁定第 1 条） |
| 3.1 Connected/Closed | 202／1191 | 0／0 | 0／0 | — |
| 3.2 效果账本 | 17／102 | 1／1 | 1／1 | `EffectOperationReplayTests` `:371`，顺延 T-004c |
| 3.3 连接观察者 | 83／118 | 79／114 | 79／114，排除轮为 0 | `OnlineUserServiceTests`、`SelectCharacterLifecycleTests` 直接构造观察者，7 条冲突顺延 T-004c（TASK-10 裁定第 2 条） |
| 3.4 登录会话 | 1／1 | 0／0 | 0／0 | — |
| 3.5 事件 | 461／1899 | 17／17（改正后 0／0） | 改正后 0／0 | 17 条是 `DrainOnceAsync` 返回后的测试续行（TASK-12 note 1） |
| 3.6 卡牌计时器 | 24／141 | 0／0 | 0／0 | — |
| 3.7 GM/HTTP | 1031／2198 | 0／0 | 0／0 | — |
| 3.8 后台与调度 | 223／4601 | 0／0 | 0／0 | — |
| 3.9 启动与停机 | 190／376 | 0／0 | 0／0 | TASK-15 的预热在执行器外，不经 P1／P2／P3 挂点；若出现记录，逐条看是否来自 `ContentWarmup` |
| 3.10 命令总线 | 4171／18235（Persistence 40／193） | 3518／10203（Persistence 46／180） | 只允许「测试直接调用总线」 | 翻转（T-004c）的对象 |

- TASK-07 一栏取 `TASK-09.md` Checks 8 的「TASK-07 最终轮」列与 `task-07-probe.md` §2；3.5～3.10 的归族口径自 TASK-10 起按 `fam.py` 重建，TASK-07 一栏只作量级对照，不逐条归因（TASK-10 Checks 10 已说明）。
- **3.10 的归因**：五个项目各自列出「没有测试帧」的 3.10 记录（`hostnotest.py` 对每个项目运行），逐条按 TASK-14 的两种形状归类：撞 40 帧上限、可见最外层是总线或管线；完整栈、底部是线程池续体。归因依据是结构论证，不用排除轮单独归因（TASK-14 第 2 轮非阻断 2）：
  - 生产代码中在执行器外调用 `ICommandBus.SendAsync` 的位置为 0。TASK-14 Checks 8 对 GmWeb、HttpApi、Application 做过；本单元补 Protocol、Networking、GameServer、Infrastructure、Persistence 四个项目，方法是列出全部 `SendAsync(` 的调用点，逐个说明它所在的方法只从已迁入的入口（handler、投递、协调器、定时源作业体、订阅处理器）被调用；
  - 不能这样归因的记录，停下回报。
- **排除轮**：
  - x1：Application，排除 T-004c 的全部冲突用例（以 scratchpad `t07/conflicts.py` 为底，按 `task-07-conflicts.md` §7～§12 的去向重新生成过滤器，过滤器文本落盘），断言 3.1～3.9 除 3.1 遥测那一条外全部为 0，3.3 为 0；
  - 其余项目首轮即符合上表时不做排除轮。
- **P1b 各类**：五个项目的 P1b-teardown、P1b-volatile、P1b-creature 的 (A)／(B)／(C) 都必须为 0；P1b-read 的 (B) 照实计数，作为给 T-004c 的信息（`task-07-probe.md` §4.1：判据要区分只读的一次性事务与写单元）。

#### 1.4 TASK-03 三处门外写终核

| 位置 | 终核方法 | 预期 |
|---|---|---|
| `SetUserAreaHandler` 跨区落位置 | 探针：§3.11 第 1 行的 P1b-teardown 与同路径 P1 在执行器外的记录 | 0（TASK-09 已归零，本轮复核） |
| `CharacterStateAccess.cs` 的 `AloneAsync`（拆除时落位置） | 探针 P1b-teardown 全部类别；静态：`AloneAsync` 唯一调用点 `CharacterStateAccess.cs:156` 的全部上游入口，逐个对到附录 A 的行 | 0；上游只有入站 handler、生命周期观察者、传输排空的兜底拆除，都在执行器上 |
| `CreatureRuntimeDomainService.cs:274`（访问器不是 `DomainTransactionScope` 时自建事务） | 探针 P1b-creature；静态：列出生产组合根与全部测试容器注入的 `IDomainTransactionAccessor` 实现 | 0 次命中，分支不可达；它的调用入口 `CreatureClockService` 已在执行器上（TASK-11 E1）。反例归 T-004c 判据，写进 `t004c-entry.md` |

结论写入 `entry-reconciliation.md`，并在 `task-07-probe.md` 不作改动（它是 TASK-07 的记录）。

#### 1.5 还原证明

同 TASK-14 Checks 9：快照前后 sha1 相同、`cmp` 相等；`snapcheck` 中不等的只有本单元的正式改动（协调器 1 个文件；新测试文件不在 `Server/src`）；`patch.py revert` 恢复的文件数与删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 为 0；之后 `--no-incremental` 重建，再跑正式全量。

### 2. 附录 A／B 对账

#### 2.1 对账表（写入 `entry-reconciliation.md`）

附录 A 每行一节，列：来源 → CMD 类型与来源种类 → 经真实入口驱动的测试（类名.方法名，Required check 10 核对存在）→ 探针族终值 → 留给后续单元的部分。各单元已写过对账行，本单元汇总、在基准上复核测试名与数字，不重新设计证据：

| 附录 A 行 | 出处 | CMD 类型（来源种类） | 证据起点 |
|---|---|---|---|
| 1 入站协议 | TASK-09 Checks 9 | `inbound:<ClientMessageType>`（Connection）；14 个遥测消息类型留在外面 | W1、W3（`InboundExecutorWireTests`）、D1（`GameplayDispatchTests`）；遥测 W4、D5、D6、D8；探针 3.1 |
| 2 效果账本重放 | TASK-09 Checks 9 | 随所属入站 CMD | D7（`EffectDispatchOnExecutorTests`）；探针 3.2 |
| 3 连接生命周期 | TASK-10 Checks 11 | `lifecycle:connected`／`lifecycle:disconnected`（Lifecycle），孤儿清理与排空兜底拆除归后者 | W5～W8、S1、S2、LD1、L1、L4；探针 3.1 Connected/Closed、3.3 |
| 4 登录会话 | TASK-10 Checks 11 | `lifecycle:disconnected`、`lifecycle:close-superseded`（Lifecycle）、`timer:LoginSessionMaintenanceService`（Timer） | M1、M2、W9、W7 登录变体；探针 3.4 |
| 5 后台与调度 | TASK-11 Checks 10 | `timer:<类型名>`、`timer:ScheduledTaskHostedService/<任务 ID>`（Timer） | E1（27 行）、E2a～E2d、E3；探针 3.8。E1 只证明经执行器入队，「整段在作业内」靠代码审读、E2 与探针（TASK-11 主控方补充 N1） |
| 6 卡牌计时器 | TASK-11 Checks 10 | `timer:DungeonCardPresentationCoordinator/layout`、`/auto-flip`（Timer） | C1～C7；探针 3.6；本单元 §4 的两条 |
| 7 两塔离场队列 | TASK-11 Checks 10 | `timer:BossTowerReturnSweepService`、`timer:IllusionTowerReturnSweepService`（Timer） | E1 两行、`:149` 的后继用例 |
| 8 事件订阅 | TASK-12 Checks 10 | `event:<订阅 ID>`（Event）、`startup:event/<订阅 ID>`（StartupRecovery）；Direct 留在调用方 CMD | N1～N10、AC-015 子项表；`subcheck.py` 计数；探针 3.5 |
| 9 GM | TASK-14 Checks 10 | `gm:<METHOD> <路由模板>`（Gm） | R1、E1、L2、H1；探针 3.7 |
| 10 启动器 | TASK-14 Checks 10 | `http:POST /api/launcher/v1/register`、`/login`（Http） | E2、H1、R1 |
| 11 启动恢复 | TASK-11、TASK-12、TASK-14 | `startup:JoustMaintenanceService`、`startup:event/<订阅 ID>`、`startup:GameServerStartupService/epic-book`、`/experience-storage`、`startup:GmBootstrapService`（StartupRecovery） | E1 Joust 两行与 V8；N4 与 M4；S1 与 V7、V8；探针 3.9 |

「留给后续」一列从各单元对账行原样汇总（T-005 删锁、T-006 发件箱、T-007 调用链、T-008 归并），并与 §6 的后续事项清单互相引用，不在两处各写一遍正文。

#### 2.2 附录 A 的文字修正（在基准上重新取数，取到什么写什么）

| 行 | 现文 | 取数方法 | 已知的数（待基准复核） |
|---|---|---|---|
| 1 | 「141 个 handler」 | 临时用例（不提交）：按 `ProtocolModule` 组装容器，枚举 `MessageHandlerRegistration` 描述符，数注册条数、不同 handler 类型数、遥测注册数 | TASK-09：173 条注册、141 个 handler 类型；遥测 2 个 handler、14 个消息类型。改写为「N 条消息注册（M 个 handler 类型），其中附录 B 的 14 个遥测消息类型留在执行器外」 |
| 4 | `LoginSessionRuntime.cs:9/22` | 在基准上重新定位维护服务与连接观察者 | 行号按基准更新，补上 `lifecycle:close-superseded` |
| 5 | 「33 个后台服务与 5 个调度任务」 | 在基准上重新检索 `AddHostedService`／`IHostedService`／`BackgroundService` 的注册，按 TASK-11 §1 的归属表复核 | 33 个托管服务中本行 22 个，另 1 个归第 4 行、1 个归第 8 行、4 个归启动期、5 个不碰玩法状态（加法 22 ＋ 1 ＋ 1 ＋ 4 ＋ 5 ＝ 33）；5 个调度任务。若基准上的数与此不同，按基准写并说明差在哪 |
| 7 | 「定时 CMD（或离场时直接入队）」 | — | 改为 TASK-11 采用的「定时 CMD」，并注明「离场时直接入队」不可行的理由（sink 在拆除 CMD 里调用，入队并等待即自我等待） |
| 8 | 「52 个事件订阅（Reliable 17、LiveOnly 22、Direct 13）」 | 在基准上重跑 scratchpad `t12/subcheck.py` | 复核后不变则只加对账指针 |
| 9 | 「GM 写操作 34 条」 | 用 R1 在基准上打出的路由清单（`GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption`） | TASK-14：GM 51 条路由，40 条是 GM CMD、11 条豁免；「34」的口径不足以确定，删去，改为按路由计 |
| 10 | 启动器注册与登录 | 同上 | 3 条路由：2 条 HTTP CMD、1 条豁免 `bootstrap` |
| 11 | 只列 Joust 与遗留 delivery | — | 补上三个启动作业 |

每处修正只改事实与指针，不改「新的 CMD」一列的设计含义（第 7 行是把已裁定的选择写实）。Spec 文末不另写修订说明；修正清单写在 Result。

#### 2.3 附录 B 对账

- 逐行写「安全理由 → 判据或测试」：已有清单的行（PVF 内容源、GM 自身状态、只读 SQLite、口令哈希、纯遥测、启动预热、启动期工作、不碰玩法状态的 HTTP 路由）引用各自的判据（R1、H1～H5、D6、W1／W2／F1、S1）；只有理由、没有判据的行（解包与帧计数、发送端、连接注册、UDP 发现）写明它为什么不需要运行期判据，或由哪条静态检索支撑。
- **补齐执行器外实际存在、附录 B 未列的部分**。做法是列一张「生产中在执行器外运行的代码」清单，逐项对到附录 B 的某一行；对不上的按 TASK-14 裁定第 12 条处理（能并入相近行就并入；确需新类别时按 RQ-011 新增一行并附安全理由）。已知需要处理的：
  - `DomainEventDispatcher.DrainOnceAsync` 在执行器外的只读 `ReadDueAsync`（TASK-12 裁定第 3 条）→ 并入「只读 SQLite 的查询」；
  - `HttpApiStartupService`（只设就绪标志）、3 个 `*RegistryValidationService`、`ChannelScriptStartupService`（TASK-11 §1「不迁」）→ 并入「开始监听前的启动期工作」；
  - `TouchSocketTransportDrain`（停机时请求关闭、分发 `lifecycle:disconnected` 并等待）→ 并入「传输层的连接注册」；
  - GM 过滤器在执行器外预缓冲请求体（TASK-14 §2.2）→ 并入「不碰玩法状态的 HTTP 路由」或新行；
  - **生产者的入队侧**：dispatcher 与生命周期分发器的入队并等待、各定时源循环与合并源、卡牌计时器的延时等待、事件组循环、两个凭据 reader 返回之后的等待——它们只等待和入队，不读写玩法状态。附录 B 没有相近的行，拟按 RQ-011 新增一行（待裁定第 3 条）。
- 纯遥测那一行第一列的举例「客户端规格、会话指标」与本行清单矛盾（TASK-09 主控方补充 N-3，原交 T-009）。本单元在对账时把举例改为清单内的类型（`HiddenCharacterInfoHandler`、`UnansweredReportHandler` 一类），并在 `follow-ups.md` 把该项标为已在 TASK-16 关闭（待裁定第 4 条）。

### 3. 分离点终核

#### 3.1 清单（写入 `entry-reconciliation.md`）

先在基准上检索 `SuppressFlow|RunDetached|Task\.Run|Task\.Factory|ContinueWith|ThreadPool\.|new Thread` 与「`_ = ` 后接一个返回 Task 的调用」，把每个命中归入下表的一行或「不是分离点」；检索命令与命中数落盘。已知的行：

| # | 位置 | 启动方式 | 单元 | 谁等待它 | 证据 |
|---|---|---|---|---|---|
| 1 | `GameplayExecutor.EnsureStarted` 的消费循环 | `SuppressFlow` ＋ `Task.Run` | TASK-02 | `StopAsync`／`DisposeAsync`（执行器外；在本执行器上调用即自我等待） | `GameplayExecutorSelfWaitTests`、`GameplayJobTokenTests` |
| 2 | `ExecutorSendDeadline.StartDetachedClose` | `SuppressFlow` ＋ `Task.Run`，结果丢弃 | TASK-08 | 无 | `ExecutorSendDeadlineTests` f、第 9 条 |
| 3 | `TouchSocketNetworkConnectionCloser.CloseDetachedAsync` | `SuppressFlow` 下调用以 `Task.Yield` 起头的方法，结果丢弃 | TASK-10 | 无 | `DetachedCloseTests` C1、C2 |
| 4 | `DungeonCardPresentationCoordinator.StartDetached` | `SuppressFlow`（已抑制时不嵌套）＋ `Task.Yield`，结果丢弃 | TASK-11 | 只有 `DrainAsync` 经在途计数等待，调用方是 `GameplayExecutorLifetime.StoppedAsync`（执行器外） | C3（V5 证明 `Task.Yield` 必要）、C7 |
| 5 | `NetworkPlugins` 四个回调（Connected、Closed、两个 Received） | `RunDetachedAsync` 流边界，不是分出的工作 | TASK-09、TASK-10 | TouchSocket | `ReceivedStreamBoundaryTests`、`ConnectionCallbackBoundaryTests` L1～L3 |
| 6 | `OutboundSender` 的准入循环、每连接排空循环、失败后的关闭 | `SuppressFlow` ＋ `Task.Run` | TASK-05 | 发送端自身；未接线 | 只登记，T-006 接线时复核 |
| 7 | `ScheduledTaskHostedService` 的 `Task.Run`（`:129`） | 由执行器外的计时循环启动，不从作业里分出 | 既有，T-005 删除 | 宿主 `StopAsync`（执行器外） | 不是作业的分离点，记录理由 |
| 8 | `PvfItemCatalogSource` 的 `searchable`（`:75`） | `await Task.Run(...)`，被调用方当场等待 | 既有 | 调用方本身 | 不是分离点：继承令牌是对的（被等待），体内是纯计算，不入队、不取门 |
| 9 | `PvfArchiveIdentity` 的哈希任务（`:22`） | `Task.Run`，缓存在字段里供后续调用方等待 | 既有 | 后续任何调用方 | 纯文件哈希，不入队、不取门；首个调用方若是作业，令牌随作业结束失效。记录，不改 |
| 10 | `ExecutorSendDeadline.Observe`、`OutboundSender` 的观察续体 | `ContinueWith` 只读异常 | TASK-08、TASK-05 | 无 | 不触达玩法状态 |

#### 3.2 证明方法（逐行写结论）

1. **在 `SuppressFlow`／`RunDetached` 下启动**：静态，读启动点代码；运行期，上表「证据」列的用例（它们都在作业里启动分离工作，并断言分出的工作看不到作业令牌）。第 7～10 行写明它们为什么不属于「从作业分出的工作」。
2. **不被作业 await**：静态，列出每个分离点返回的 `Task` 的全部引用（丢弃、存字段、被谁 `await`），等待方都在执行器外；运行期，正式全量 `GameplaySelfWait` 为 0（自我等待检测只覆盖「入队后等待」，所以「作业 await 一个会入队并等待的分离任务」这种检测不到的死锁，只能由静态这一半排除——这是 TASK-04 N-2 的第一条，结论要明写依据是代码阅读）。
3. **结构判据** `DetachmentSiteTests`（Application.Tests/Execution，源码扫描，仓库根的查找方式沿用 `AssetMutationOwnershipTests`）：
   - `RunDetached`／`RunDetachedAsync` 的调用只允许出现在 `Networking/Runtime/NetworkPlugins.cs`（定义所在的 `GameplayJob.cs` 与 `cref` 注释除外）；
   - `ExecutionContext.SuppressFlow` 只允许出现在上表第 1、2、3、4、6 行的五个文件；
   - 白名单写在判据旁，每项一句理由；加一处必须同时改判据与 `entry-reconciliation.md` 的表；
   - 反例用例：把一段含 `RunDetached(` 或 `SuppressFlow()` 的合成源码文本连同一个非白名单路径交给同一个判定函数，断言被拒；
   - 判据只看出现位置，不证明「不被 await」；后者仍由 3.2 第 2 条承担，判据注释写明这一点。
   - 这条判据是 TASK-04 Remaining 第 2 条交给 T-004 的「`RunDetached` 用结构判据限定」，目前没有任何单元做过（待裁定第 2 条）。

### 4. 排空顺序：协调器容错

#### 4.1 问题与选择

- 今天 `GameplayExecutorLifetime.StoppedAsync` 按注册顺序依次排空。传输排空在 `NetworkingModule` 注册、协调器排空在 `ProtocolModule` 注册，而 `GameServerHostingExtensions` 恰好先装 Networking，所以传输先排空。
- 若协调器先排空，之后才执行的入站或生命周期 CMD 里调用 `ScheduleAfterSettlement`（`DungeonClearSlice.cs:1105`，在结算提交之后）或 `OpenOnRequestAsync`（`ResultScreenSlice.cs:215`、`CardRewardHandlers.cs:43`），会在 `Replace` 的 `ObjectDisposedException.ThrowIf` 处抛出：前者是提交后的 CMD 失败，后者让 handler 抛异常、断开连接。
- 两种修法：
  - **钉顺序**：一条判据断言生产组合中 `IEnumerable<IGameplayProducerDrain>` 里传输排空排在协调器之前。零生产改动，但它钉住的是模块装配次序这个偶然事实；只装 `ProtocolModule` 的测试容器或将来的组合仍可能反过来。
  - **协调器容错**（本单元采用）：停止之后的调度请求不再抛出，只是不启动计时器。语义上等同于「请求在排空前一刻到达、随即被排空取消」——`Replace` 新建的源与 `stopping` 链接，停止之后本来就是已取消的。这样排空顺序与正确性无关，不需要再钉。

#### 4.2 改动（只在 `DungeonCardPresentationCoordinator.cs`）

- `Replace` 改为 `TryReplace`，返回 `CancellationTokenSource?`：在 `gate` 内发现已停止时，释放新建的源并返回 `null`，不改 `pending`、不增加在途计数。判断仍在锁内，避免「检查后才停止」的窗口；`Stop` 照旧不取锁（TASK-10 裁定第 5 条）。锁内判定之后才停止的情形维持现状：源已被取消，计时器在 `RunTimerAsync` 入队前的令牌检查处退出，不入队。
- 两个调用点（`ScheduleAfterSettlement` 与 `OpenLayoutAsync` 末尾启动自动翻牌处）拿到 `null` 时记一条 Information「`{CommandType} was not scheduled: the card presentation coordinator has stopped`」并返回。`OpenLayoutAsync` 中此前的发帧与 `ScheduleClearReturn` 照常执行——它们是当前 CMD 自己的工作，只有计时器不再启动。
- 不改 `DrainAsync`、`Dispose`、`Stop`、`RunTimerAsync`，不改注册顺序，不新增接口成员。

#### 4.3 测试（追加到 `DungeonCardPresentationCoordinatorTests.Timers.cs`，复用其中的 `World` 与 `RecordingQueue`）

- T1 `Settlement_scheduled_after_the_coordinator_drained_is_ignored_without_throwing`：先 `DrainAsync`，再 `ScheduleAfterSettlement`；断言不抛、没有任何 `timer:` 作业入队、再次 `DrainAsync` 立即完成（在途计数仍为 0）、记录到那条 Information。
- T2 `A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip`：已结算的副本局，先 `DrainAsync`，再在一个作业里 `OpenOnRequestAsync`；断言不抛、布局帧照常发出、没有 `auto-flip` 作业入队。
- 先写测试：在改代码前 T1、T2 都以 `ObjectDisposedException` 变红，红的形态落盘到 `logs/task-16-red.txt`。V1 是同一件事的反向核对。
- 既有的 C5、C6、C7 与 `CardTimerShutdownTests` 必须原样通过。

### 5. 重型宿主与打包进程用例清单（信息与建议，不改预算）

- **统计口径**：以正式全量日志中的用例名为准（Theory 按实际运行的例数计），再按源码归类：
  - 拉起打包进程（exe）：`EndToEndHostHarness`（`LauncherAuthenticationEndToEndTests`、`HostShutdownOrderTests`）、`LauncherHttpHostStartupTests`、`ChannelHostStartupTests`；
  - 进程内带真实档案的宿主：`GmTestHost` 中读真实 PVF 的用例（预热关闭）、S1（预热开启）；
  - Networking 的 `CharacterizationHost`（表征测试 10 条、停机 S2，预热开启）；
  - 只拉网络、不读档案的宿主（Networking 的线级集成测试，作为对照一栏）。
- **每类写出**：用例数、是否开预热、是否需要 PVF（CI 无档案时是否跳过）、每个用例的固定预算（源码中的 `CancellationTokenSource`／`WaitAsync` 时限，例如 `ChannelHostStartupTests` 启动 45 秒、请求 15 秒、另有 3 分钟与 30 秒两处；`LauncherHttpHostStartupTests` 15／30／20／30／20／20 秒；`EndToEndHostHarness` 启动 45 秒、停机 20 秒——以基准源码为准逐个核对）、正式全量与隔离运行中的实际耗时。
- **内存**：Required check 3 的 Host.Tests 隔离 RSS 峰值，与 TASK-14（上限 15931440 KiB）、TASK-15 第 3 轮（14247072 KiB）对照，只作信息。
- **建议**（写进 `follow-ups.md`「测试基础设施」一节，本单元不实施）：同一类宿主改用 xunit 集合夹具共享、打包进程按集合串行、宿主自己绑定 0 端口再把实际端口告诉用例（TASK-10 Remaining 第 8 条、TASK-14 Remaining 第 2 条的端口争用）、开预热的宿主数量与 13 份重复路径字典的关系。每条写清它解决哪个已记录的现象。
- **预算不改**，除非正式全量里某个打包进程用例在隔离复跑中也撞线；那时停下回报，不自行放宽。

### 6. 后续事项清单 `verification/follow-ups.md`

- **范围**：TASK-08～TASK-15 的 Remaining、主控方补充、主控方裁定中「交给某单元／立项／后续」且尚未关闭的事项，以及更早单元交给 T-004 而仍未关闭的（TASK-04 Remaining 第 1～2 条、TASK-06 §7 待办 4f「VC-8 提前在 T-004c 完成核对」）。本单元自己关闭的（排空顺序、`RunDetached` 判据、附录 A 文字、附录 B 遥测举例）列为「已在 TASK-16 关闭」。
- **格式**：一张表，列为 编号 ／ 事项（一句话）／ 来源（`TASK-xx` 的哪一节第几条）／ 归属（T-004c、T-005、T-006、T-007、T-008、T-009、M-1、交主控方立项、测试基础设施、取证缺口）／ 状态。同一事项在多个单元出现时合并为一行，来源列全部列出（例如「双 Error 日志」：TASK-09 Remaining 1、TASK-10 Remaining 6、TASK-11 Remaining 5、TASK-12 Remaining 6、TASK-14 可见差异 6）。
- **至少包含**（来源已在各文件核对过，执行时以原文为准）：
  - 交主控方立项：13 个内容源重复持有 equipment／stackable 路径字典约 500 MB，及第 3 轮 t＝60 s 的 RSS 比第 2 轮高约 0.8 GB（TASK-15 第 3 轮 Remaining 1 与主控方补充）；
  - M-1：周签到 D 类读档（单作业最多 74 次读）、超额技能契约、每日补给规则；首次选角剩余约 100 ms（推断 JIT 与 SQLite 冷页）；C 类按键缓存首次加载与 `searchable`；32 个豁免持有者；长作业（每日任务、Premium、配额刷新）；空闲作业约每秒 30 个；GM 查询与玩法串行；订阅组不再并发后的慢订阅；RQ-091 判定（TASK-11 Remaining 2、4，TASK-12 Remaining 4，TASK-14 可见差异 1，TASK-15 Remaining 2～4）；
  - T-004c：`:371`、`:149`、`ReplayStateSnapshotTests:206` 等 76 条冲突用例；裸 `new DomainTransaction()`；AC-003 第 3 条；VC-8 提前的完成核对；Host 中 10 个测试类直接调总线；RQ-061 启动期例外的取舍；
  - T-005：调度任务的 `Task.Run` 与并发槽、组门与准入门、Weakness `deliveryGate`、Joust `ticks`；
  - T-006：VC-8 提前范围（入站、生命周期、定时、事件、GM）；W7 改回严格形式；排空兜底与 `TryRemove` 仲裁要保留；`AddNotificationDispatching` 不自带执行器；重试排空服务删除；按帧计期限的残余；
  - T-007：生命周期 CMD 一律新根、`close-cleanup`、事件重试继承链与深度超限落定、计时器 CMD 是否继承调度它的链；
  - T-008：双 Error 日志；「执行器已停止」日志不带异常；作业内 handler 抛异常 → 断线的线级测试；GM 预缓冲的罕见不等价；
  - T-009：AGENTS.md 中「纯遥测」措辞；
  - 取证缺口：TASK-10 的 S1 隔离失败一次、TASK-12 的 Networking 测试主机崩溃（栈根不在 AGENTS.md 表内）、TASK-14 的 Host.Tests 进程停顿机制；
  - 升级 TouchSocket 时复核：Received 不被内联调用、关闭回调不内联（TASK-09 补充 N-4、TASK-10 Remaining 7）；跨平台「写入真异步」（TASK-08 N-3）；
  - 其他：档案坏项 `map/grandine/43408.map`；W2 的识别边界；GM 登录第二段缺专门变异；`:109` 的窄竞争；S2 依赖 `CharacterizationHost` 私有字段。
- 本单元执行时新发现的事项照同一格式追加。

### 7. T-004c 进入条件核对 `verification/t004c-entry.md`

逐项写「事实 → 现状 → 证据 → 结论（具备／缺口）」。缺口不在本单元补，写清由谁、在 T-004c 的哪一步补。

1. **持门者全部迁入**（Plan 过渡期规则 1）：附录 F「删除」栏的每个原语，列出生产中全部获取点、获取时是否在执行器上。证据：探针 P3 在 3.1～3.9 为 0（§1.3）；P3 没有挂的原语（`EffectOperationLedger` 每连接信号量、`DomainEventDispatcher.groupGates`、Weakness 与 Joust 的信号量、调度任务并发槽）用静态阅读。**已知事实**：`groupGates` 由执行器外的组循环持有并在持有期间等待事件作业，调度任务并发槽同理——门断言若覆盖它们会在执行器外报错，T-004c 要么把它们排除在门断言之外、要么与 T-005 一样先删。
2. **`SendAsync` 翻转的前提**：生产代码中执行器外调用 `ICommandBus.SendAsync` 为 0（§1.3 的静态检索）；3.10 只剩测试直接调用，分项目给出记录数，作为翻转后应当消失的量。
3. **RQ-061 判据的挂点事实**：
   - 工作单元入口：`DomainTransactionScope.Begin`（非只读）与 5 处裸 `new DomainTransaction()`（`CharacterStateAccess.cs` 3 处、`ItemContainers.cs` 1 处、`CreatureRuntimeDomainService.cs:274`）；其中 `ReadAsync` 一类是边界外只读，P1b-read 的 (B) 计数说明判据必须区分只读一次性事务与写单元（`task-07-probe.md` §4.1，TASK-09 Remaining 3）；
   - 附录 F「改为执行器内访问」的类：执行器外仍有的**读**——遥测路径的 `IsBlocked`／`IsSuperseded`（TASK-09 裁定第 1 条）、`TouchSocketTransportDrain` 读传输注册表（不属附录 F）——判据若挂在锁入口而不是写入口，会把这些读拒掉；
   - 启动期：探针 3.9 中工作单元入口在执行器外为 0、P1 带启动帧的记录为 0 时，写明「RQ-061 的启动期例外可以不设」，建表与迁移不经工作单元入口（TASK-14 Remaining 6）；
   - `CreatureRuntimeDomainService.cs:274` 的反例要由 T-004c 自己补（§1.4）。
4. **测试侧**：T-004c 的冲突用例 76 条（按 §12 的最终分布）；`TestGameplay` 辅助与「测试在执行器外开写事务」的分布（P1b-read (B) 所在的类）；Host 中直接调总线的 10 个测试类（TASK-14 Checks 9）；`task-07-probe.md` §5 中 `SelectDungeonHandler` 一条的归属已由 TASK-09 更正。
5. **分离点与结构判据**：§3 的表与 `DetachmentSiteTests` 已就位；`RunDetached` 不会被新代码当作绕过门断言的出口。
6. **停机与排空**：执行器停止前排空全部生产者；协调器容错之后排空顺序与正确性无关（§4）。
7. **已知的过渡态**：VC-8 提前的全部范围（交 T-004c 完成核对）、R-5 现状（TouchSocket 4.3.1 不内联）、中间态 SQLite 忙等风险随 TASK-14 迁入后已无执行器外写者（以 §1.3 的结果确认）。

### 8. 冲突分布 §12

在 `task-07-conflicts.md` 追加 §12「TASK-16（基准 `c3115f9a`）」：本单元没有分配到冲突用例；`ReplayStateSnapshotTests:206` 从「T-004b（早已标顺延）」改记到 T-004c。新分布：T-004b 0；T-004c 76；T-006 53；T-008 1；已删除 1；已改写 3 ＋ 2 ＋ 2 ＝ 7。加法 0 ＋ 76 ＋ 53 ＋ 1 ＋ 1 ＋ 7 ＝ 138。若 §1 的排除轮或全量暴露新的冲突用例，按拆分原则第 5 条判定并追加，不在本单元改写。

### 9. 为什么除一条 Information 日志外行为不变

- 生产改动只有协调器：停止之后的调度请求从「抛 `ObjectDisposedException`」变为「记 Information、不启动计时器」。按今天的模块装配次序，生产停机时这条路径到达不了（传输先排空），所以生产可见行为不变；它改变的是装配次序不同的容器与将来的组合。
- 其余产出是测试、探针工具、Spec 附录的事实性文字与验证文档。

## 待裁定

1. **排空顺序选「协调器容错」**（§4.1），不钉排空顺序。备选是零生产改动的顺序判据。请确认；若改选钉顺序，§4.2 删去，改为在 Application.Tests 加一条「生产组合中传输排空先于协调器」的判据并配反例，V1 相应改为「交换注册次序」。
2. **`RunDetached`／`SuppressFlow` 结构判据放在本单元**（§3.2 第 3 条）。它是 TASK-04 交给 T-004 的事项，至今没有单元做。备选是留给 T-004c 与门断言一起做。建议放在这里，这样 T-004c 开门断言时这个逃生口已经被钉住。
3. **附录 B 新增一行「生产者的入队侧」**（§2.3）。TASK-14 裁定第 12 条允许在确需新类别时按 RQ-011 新增一行；这一类代码只入队和等待，没有相近的行可并。请确认新增，或指定并入哪一行。
4. **附录 B 纯遥测行第一列的举例**由本单元顺带改正（原交 T-009）。请确认；不同意则留给 T-009，`follow-ups.md` 照旧记为未关闭。
5. **探针工具补入 `fam.py`、`residual.py`、`hostnotest.py`**（§1.2），与 TASK-08 固化探针工具的裁定一致。请确认。
6. **停止条件**：最终轮 3.1～3.9 出现 §1.3 表外的 (A) 记录，或 3.10 中有无法按结构论证归因的记录时，停下回报，不在本单元迁移或改测试。请确认这一口径（与 TASK-08 §2 中 b7 的「(A) 清单与附录 A 一一对上」一致）。
7. **重型宿主与打包进程用例只记录、不改预算**（§5）。若主控方希望本单元就放宽某个打包进程用例的预算，需要明确指定用例与新值。
8. **附录 A 的修正范围**（§2.2）：只改事实与指针；第 7 行把「或离场时直接入队」删去，改为已采用的「定时 CMD」并写明理由。这是把已裁定的选择写实，不是需求变更。请确认。

## 主控方裁定（执行前）

待裁定 1～8 全部按本文件的建议执行：
1. **排空顺序**：选「协调器容错」（`TryReplace`）。只改 `DungeonCardPresentationCoordinator.cs`，并加 T1、T2 两条用例和 V1 变异。
2. **结构判据**：`DetachmentSiteTests` 放在本单元，配反例用例。
3. **附录 B**：新增「生产者的入队侧」一行，附上安全理由。依据是 RQ-011 与 TASK-14 裁定第 12 条。
4. **附录 B 纯遥测行**：第一列举例的矛盾由本单元顺带改正，不再交给 T-009。
5. **分析脚本**：`fam.py`、`residual.py`、`hostnotest.py` 固化进 `verification/probe-tools/`，并用 TASK-14 p1 的原始数据重跑，证明结果逐格相同。
6. **停止口径**：3.1～3.9 出现表外 (A) 记录，或者 3.10 中有无法归因的记录，都停下回报，不在本单元做迁移。
7. **重型宿主与打包进程用例**：只记录，不改预算。按用户偏好，耗时与内存只作信息，不作阻断。
8. **附录 A**：只改事实与指针；第 7 行写明已裁定的选择。

## Progress（执行者，2026-10-22：§4 使既有用例 C7 变红，按 Task-specific authorization 停下回报）

### 触发的停止条件

Task-specific authorization 第 3 条：「若有既有用例因 §4 失败，停下回报」。

- 失败的既有用例：C7 `DungeonCardPresentationCoordinatorTests.The_drain_waits_for_a_timer_already_waiting_on_its_cmd_and_stops_the_rest`（`DungeonCardPresentationCoordinatorTests.Timers.cs`，TASK-11 新增）。
- 失败位置：它的**最后一条断言**（基准 `c3115f9a` 第 154～155 行）：
  ```csharp
  Assert.Throws<ObjectDisposedException>(() =>
      TestGameplay.Run(() => world.Coordinator.ScheduleAfterSettlement(world.Session, world.Run.RunId)));
  ```
  这条断言钉住的正是 §4 要撤销的行为（排空之后的调度请求抛 `ObjectDisposedException`）。§4 生效后失败形态为 `Assert.Throws() Failure: No exception was thrown`；同一文件中 C7 前面的断言（排空等待在途计时器、放行后返回、入队序列 `["test:blocker", LayoutCommand]`、没有帧发出）全部仍然成立。
- 计划的矛盾：§4.3 同时要求「既有的 C5、C6、C7 与 `CardTimerShutdownTests` 必须原样通过」与 §4.2 的容错改动；按 C7 第 154 行的断言，两者不能同时满足。计划核对时漏看了这一行（`grep ObjectDisposedException Server/tests` 中与协调器相关的命中只有这一处）。
- 其余协调器与停机用例（C1～C6、`CardTimerShutdownTests` 2 条、原有 12 条协调器用例）在改动后全部通过：卡牌相关过滤 19 条中 18 通过、1 失败（即 C7）。

### 已完成

1. **§4 先写测试**：`DungeonCardPresentationCoordinatorTests.Timers.cs` 追加 T1 `Settlement_scheduled_after_the_coordinator_drained_is_ignored_without_throwing`、T2 `A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip`，以及辅助 `WithLogs`（同一 `World` 上另建一个带日志记录的协调器）、`Inflight`（反射读在途计数）、嵌套类 `CoordinatorLogs`；文件头加 `using Microsoft.Extensions.Logging;`。
2. **红的形态**（`verification/logs/task-16-red.txt` 第一段，基准协调器）：T1、T2 都以 `System.ObjectDisposedException : Cannot access a disposed object.` 失败（T1 在 `ScheduleAfterSettlement`，T2 在 `OpenOnRequestAsync` 发出布局帧之后的 `Replace`）；其余 17 条通过。
3. **§4.2 改动**（`DungeonCardPresentationCoordinator.cs`）：`Replace` → `TryReplace`（锁内发现已停止则释放新源、返回 null，不改 `pending`、不增在途计数）；两个调用点拿到 null 时记 Information「`{CommandType} was not scheduled: the card presentation coordinator has stopped`」并返回；`OpenLayoutAsync` 的发帧与 `ScheduleClearReturn` 照常执行。`DrainAsync`、`Dispose`、`Stop`、`RunTimerAsync`、注册顺序未改。
4. **改动后**（`task-16-red.txt` 第二段）：T1、T2 通过；C7 失败于上述断言；其余全部通过。Protocol.Tests 项目构建 0 警告 0 错误。

§1～§3、§5～§8 均未开始；没有跑探针、变异、全量；没有改 `spec.md`；没有建 `DetachmentSiteTests.cs`。

### 当前 owned diff（未提交，未 stash）

- `Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs`（＋40 −9 量级；基准副本在 scratchpad `t16/base/DungeonCardPresentationCoordinator.cs`）；
- `Server/tests/Dfo.Server.Protocol.Tests/Game/CardReward/DungeonCardPresentationCoordinatorTests.Timers.cs`（只追加，原有行未改）；
- `verification/logs/task-16-red.txt`（两段）。
- 工作区现状：C7 红。若主控方要求先恢复全绿，用 scratchpad 基准副本覆盖协调器即可（T1、T2 随之变红）。

### 需要主控方裁定（选一）

- **(a) 把 C7 最后一条断言作为本单元的冲突用例改写**：旧——排空后 `ScheduleAfterSettlement` 抛 `ObjectDisposedException`；新——排空后调用不抛、不入队任何 `timer:` 作业、`pending` 为 0（与 T1 同形）；保护意图不变：「排空之后协调器不再启动计时器」。需要授权改动该文件的一条断言，并在 `task-07-conflicts.md` §12 记为冲突（分布随之变化）。
- **(b) 改回「钉顺序」**（待裁定第 1 条的备选）：撤销协调器改动与 T1、T2，C7 原样；在 Application.Tests 加「生产组合中传输排空先于协调器」的判据与反例，V1 改为交换注册次序。
- **(c) 其他**。

### 下一步（裁定后）

按裁定处理 §4 → §3 判据 → Protocol／Application 构建与运行 → 变异 V1～V3 → 探针终核 → 正式全量与隔离 → 文档对账（§2、§5～§8）→ Result。

## 主控方裁定（执行中，§4 停止之后）

选 (a)。授权改写 C7（`The_drain_waits_for_a_timer_already_waiting_on_its_cmd_and_stops_the_rest`）的最后一条断言：旧断言「排空后再调度抛 `ObjectDisposedException`」→ 新断言「排空后再调度不抛异常、不入队任何 `timer:` 作业、`pending` 为 0」（与 T1 同形）；保护意图「排空之后协调器不再启动计时器」不变；C7 其余断言原样。在 `task-07-conflicts.md` §12 记为冲突（旧断言 → 新断言 → 理由：抛异常是旧实现副作用，容错实现使排空顺序不依赖模块注册次序），更新冲突分布与加法。§4.3「既有的 C5、C6、C7 … 必须原样通过」中关于 C7 的要求以本裁定为准修正：C7 只改最后一条断言，其余原样通过。随后完成 V1 变异验证并恢复，继续第 2 步起的全部内容。

## Result

### Outcome

- **探针终核**（§1，`verification/entry-reconciliation.md` §1）：五个项目在最终代码上复跑一轮 p1。3.1～3.9 各族的终值与 §1.3 允许表逐格一致：3.1 Received 1／1、3.2 1／1、3.3 79／114、3.5 17／17（改正后 0／0），其余族 0／0。3.10 为 3513／10203（四个项目），Persistence 为 46／180。没有测试帧的 (A) 记录共 379 条，全部落在 TASK-14 的两种形状里（S1 撞 40 帧上限 337 条、S2 线程池续体 42 条，无法归形 0）。依据是结构论证：生产代码中在执行器外调用 `ICommandBus.SendAsync` 的位置为 0。P1b-teardown、P1b-volatile、P1b-creature 在五个项目中都是 0。三项还原证明齐全。**停止口径（裁定第 6 条）没有触发**。
- **与计划不符的一处**：排除轮 x1 中 3.3 为 42／54，计划预期是 0。这 42 条来自不在冲突清单里的 `UserServiceTests` 方法，它们在测试线程上直接调用观察者。生产中观察者唯一的调用方是生命周期分发器（在作业内），所以这些记录仍在允许的 79／114 及其依据之内。已交 T-004c 一并判定（F-18）。
- **TASK-03 三处门外写**：动态和静态核对的结果都是 0（`entry-reconciliation.md` §1.5）；`:274` 的反例归 T-004c。
- **附录 A／B 对账**（§2）：
  - 附录 A 按行修正了与代码不符的文字，按基准取数，并加了对账指针。
  - 附录 B 补齐 5 处执行器外实际存在的代码，按 RQ-011 新增一行「生产者的入队侧」，并改正纯遥测行第一列的举例。
  - 逐行的对账表、证据和执行器外代码的映射写在 `entry-reconciliation.md` §2。
- **分离点终核**（§3）：分离点表共 10 行，逐行写了结论。新增结构判据 `DetachmentSiteTests`，配反例；V2、V3 证明它扫到了真实源码树。
- **排空顺序**（§4）：协调器对排空之后到达的调度请求容错，即 `TryReplace` 返回 null 时记一条 Information，不启动计时器。按裁定改写 C7 末条断言，记为冲突（`task-07-conflicts.md` §12，冲突总数 139）。
- **重型宿主清单、后续事项、T-004c 进入条件**：分别写入 `verification/follow-ups.md`（55 项，§2 是重型宿主清单与建议）和 `verification/t004c-entry.md`（7 项，其中缺口 5 处）。
- **表征测试**：8 通过、2 跳过，金样本未改。
- **F-54 已按主控方裁定关闭**，最终正式全量只剩那 2 条 `DimensionDungeonTests` 失败。经过如下：
  - 前两次正式全量里，`LauncherHttpHostStartupTests.GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes(launcher: False, game: True)` 各失败一次，正好撞上自身 15 s 预算，报 `TaskCanceledException`；
  - 在隔离、旁边跑 Application.Tests 两种负载下共复跑 8 次，都在 4～5 s 通过；
  - 判为满载形状，与本单元无关，依据有三：TASK-15 第 2 轮出现过同一现象；本单元的生产改动只在副本结算之后才走到；该用例的进程从启动到健康检查，不调用协调器的调度入口。
  - 主控方裁定把它的就绪等待放宽到 60 s，断言不改。理由：打包进程保持开启预热，满载下单是预热就可能要几十秒；按用户偏好，性能可接受即可，测试耗时不作阻断。
  - 放宽后：Host 隔离 183 全过（该例 5 s）；最终正式全量中该例 26 s 通过（超过原 15 s 预算）。
  - 前两次全量的日志改名为 `task-16-build-test-s1.txt`、`-s2.txt` 保留。

### Changed

- **生产代码**（`git diff --stat -- Server/src`：1 个文件，＋40 −9）：`Server/src/Dfo.Server.Protocol/Game/CardReward/DungeonCardPresentationCoordinator.cs`。
  - `Replace` 改为 `TryReplace`：在锁内发现已停止时，释放新建的源并返回 null，不改 `pending`，在途计数不加。
  - `ScheduleAfterSettlement` 与 `OpenLayoutAsync` 末尾拿到 null 时，记 Information「`{CommandType} was not scheduled: the card presentation coordinator has stopped`」后返回。
  - `DrainAsync`、`Dispose`、`Stop`、`RunTimerAsync` 与注册顺序都没改。
- **测试**：
  - `DungeonCardPresentationCoordinatorTests.Timers.cs`：
    - 追加 T1 `Settlement_scheduled_after_the_coordinator_drained_is_ignored_without_throwing` 和 T2 `A_layout_opened_after_the_drain_still_answers_but_starts_no_auto_flip`；
    - 辅助 `WithLogs`、`Inflight`，嵌套类 `CoordinatorLogs`，以及 `using Microsoft.Extensions.Logging`；
    - 按裁定改写 C7 末条断言。
  - 新增 `Server/tests/Dfo.Server.Application.Tests/Execution/DetachmentSiteTests.cs`，共 3 个方法：
    - `Detached_work_starts_only_at_the_listed_sites`；
    - `A_detachment_outside_the_listed_sites_is_refused`（Theory，6 例）；
    - `Comments_and_the_listed_sites_are_not_refused`。
  - `Server/tests/Dfo.Server.Host.Tests/LauncherHttpHostStartupTests.cs:34`（F-54 裁定）：`GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes` 整个用例体共用的 `CancellationTokenSource` 从 15 s 改为 60 s，加两行注释，断言不改。
    - 同类打包进程用例没有共用的预算常量或辅助，其余预算逐个核对后都没改：同文件 `:77` 30 s、`:111` 20 s、`:146` 30 s、`:232` 20 s、`:281` 20 s、`:430` 250 ms 探测；`ChannelHostStartupTests` 就绪 45 s（`:30`），`:32` 的 15 s 只管就绪之后的交换；`EndToEndHostHarness` 启动 45 s。
    - 满载下撞线的只有 `:34` 这一处。
- **N ＝ 5 个方法、10 个用例；删除 D ＝ 0**：
  - 方法：Protocol 2，Application 3，2 ＋ 3 ＝ 5。
  - 用例：Protocol 2；Application 1 ＋ 6 ＋ 1 ＝ 8；2 ＋ 8 ＝ 10。
  - C7 的改写不计入 N。
- **探针工具**：`verification/probe-tools/` 新增 `fam.py`、`residual.py`、`hostnotest.py`。
  - `fam.py` 只把 `PROBE_TOOLS` 环境变量改为由脚本目录推导；`residual.py` 原样复制；`hostnotest.py` 加了可选的项目参数，缺省为 Host。
  - 用 TASK-14 p1 的原始 JSON 重跑：缺省调用，以及显式传 Host 的调用，输出都与 scratchpad 原件逐字节相同；fam.py 的表也与 `logs/task-14-probe-summary.txt` 的 TASK-14 p1 一栏逐格相同（`logs/task-16-probe-summary.txt` 开头）。
  - `patch.py` 没有改，全部挂点模式都命中。
- **`spec.md`**：只改附录 A、B 的事实性文字与清单（修正清单见 `entry-reconciliation.md` §2.2、§2.3）。没有改任何 RQ、AC、Definitions，附录 C～H 也没改。
  - 附录 A：
    - 第 1 行：173 条注册、141 个 handler 类型、14 个遥测消息类型、行号 `:62`／`:144`；
    - 第 2 行：方法名；第 3 行：分发器与排空兜底；
    - 第 4 行：基准行号与 `close-superseded`；
    - 第 5 行：22 个服务及 33 的分解（基准注册 34 个，多出 `GameplayExecutorLifetime`）；
    - 第 6 行：方法名与容错；第 7 行：写实已裁定的定时 CMD；第 8 行：复核注记；
    - 第 9 行：51 条路由 ＝ 40 条 CMD ＋ 11 条豁免，删去「34」；第 10 行：3 条路由；第 11 行：补三个启动作业；
    - 表后加一句对账指针。
  - 附录 B：
    - 补齐「传输层」（排空、流边界、TouchSocket 服务）、「只读 SQLite」（`ReadDueAsync`）、「启动期工作」（`HttpApiStartupService` 与 3 个注册表校验服务）、「不碰玩法状态的 HTTP 路由」（GM 预缓冲）；
    - 纯遥测行第一列举例改为清单内的类型，行末注释相应更新；
    - 新增「生产者的入队侧」一行，附安全理由。
- **文档与证据**：
  - 新增 `verification/entry-reconciliation.md`、`verification/follow-ups.md`、`verification/t004c-entry.md`；
  - `verification/task-07-conflicts.md` 追加 §12；
  - 日志 `logs/task-16-{red,mutations,probe-*,probe-summary,build-test,build-test-s1,build-test-s2,isolated,slow-isolated,new-x10,diff-check,boundary,evidence-names,handler-count}.txt`。
- **临时用例**：为统计附录 A 第 1 行，写过 `Application.Tests/Execution/TempHandlerCountTests.cs`（不提交）。运行一次后已删除，副本留在 scratchpad `t16/`，输出在 `logs/task-16-handler-count.txt`。

### Why（本地决定）

- **C7 新断言是「`pending` 不增长」，而不是裁定文字中的「为 0」**。C7 在排空前还为 `later` 局调度了一个计时器，它被排空取消后留在 `pending` 里：`Dispose` 的注释写明会留下，而 `Stop`／`DrainAsync` 不在本单元的改动范围内。所以排空时 `pending` 是 1，「为 0」在不改生产代码的前提下不可能成立。「不增长」在 V1 下同样会变红，因为容错若失效，就会抛出或加入新条目。与 T1 同形的「为 0」由 T1 在干净的协调器上断言。理由已写进 `task-07-conflicts.md` §12。
- **T1、T2 另建一个带日志记录的协调器**（`WithLogs`），共享同一个 `World` 的会话、副本局、总线、发送器、延时与队列。原因是 `Build` 固定注入 `NullLogger`，而主测试文件不在边界内。
- **`DetachmentSiteTests` 另加一条「白名单里的文件若不再出现该原语即红」**，防止豁免过期后静默放宽。注释（含 `cref`）不计入。
- **附录 B 的「生产者的入队侧」写明例外**：事件组门与调度并发槽在持有期间等待作业，所以「等待时不持任何门」只对玩法门成立。
- **三次正式全量**：
  - 第一次多出 1 条打包进程用例失败，于是再跑一次看是否复现；复现了，形状相同。这两份改名为 `-s1`、`-s2` 保留。
  - F-54 裁定放宽预算之后跑第三次，覆盖为正式的 `task-16-build-test.txt`。

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`，0 个警告，0 个错误。三份全量日志开头都是如此。
2. **全量**（正式：`logs/task-16-build-test.txt`，F-54 裁定之后，21:55:25～21:58:59，开始时 swap 已用 8651 MB；此前两次 `-s1` 21:30:58～21:34:07、`-s2` 21:42:14～21:45:43）：
   - **总数**：53 ＋ 80 ＋ 1703 ＋ 476 ＋ 969 ＋ 173 ＋ 4510 ＋ 183 ＝ 8147 ＝ 8137 ＋ 10 － 0。逐项目对照 TASK-15，只有 Protocol 1701 → 1703（＋2）和 Application 4502 → 4510（＋8）变了，2 ＋ 8 ＝ 10；其余项目不变。三份日志相同。
   - **正式全量**：通过 53 ＋ 80 ＋ 1703 ＋ 476 ＋ 967 ＋ 171 ＋ 4492 ＋ 183 ＝ 8125，失败 2，跳过 2 ＋ 2 ＋ 16 ＝ 20；8125 ＋ 2 ＋ 20 ＝ 8147。**失败的就是那 2 条 `DimensionDungeonTests`**。F-54 那一例这次 26 s 通过。
   - **`-s1`、`-s2`**：都是通过 8124、失败 3（多出的是 F-54 那 1 条）、跳过 20。
   - 表征测试 8 通过、2 跳过；挂起转储 0。
   - **日志信号**（三份样本）：
     - 为 0 的：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`GameplayExecutorStoppedException`、startup／timer／event／http 四类 `Gameplay …:… failed`、`Content warmup failed`、`Content warmup skipped:`。
     - 非零的：
       - `Gameplay gm:… failed` 2 条（auth/login、auth/logout），与 `HTTP API request failed` 2 条成对，同 TASK-14；
       - `Gameplay inbound:… failed` 3 条，用例故意注入；
       - `the drain tears it down`：正式 0、`-s1` 0、`-s2` 1，历次在 0～6 之间。
     - §4 新增的「`… was not scheduled: the card presentation coordinator has stopped`」：0。按今天的装配次序（传输先排空），生产停机走不到这条路径；它只在 T1、T2 的捕获日志里出现，那两条用例断言了它。
   - **耗时**（只作信息，`logs/task-16-slow-isolated.txt`）：
     - 对 TASK-15 第 3 轮第二样本，共同用例总耗时：正式 ＋0.8%，`-s1` －7.2%，`-s2` －5.0%。
     - **正式全量**中多出 ≥ 9 秒的有 12 条：
       - `HostShutdownOrderTests.Sigterm…` ＋22，`GmComponentHasItsOwnListener…(False, True)` ＋19，`SealedTreasure…` ＋16，`IllusionTowerPublicFlowTests.Public_handlers_complete…` ＋15（全量中 33 s）；
       - `GmItemIconHttpTests.IconsAreServed…` ＋12，`ClearRewardArchiveTests.One_dungeon_bands…` ＋12，`PvfDropItemPoolSourceTests.The_equipment_pool…` ＋11，`DespairTowerTests` 两条各 ＋11；
       - `GmInitializationHttpTests.Invalid_snapshots…` ＋10，`GmDailyRefillHttpTests.Authentication…` ＋10，`GmOfficialMailHttpTests.ExhaustedWireIdentifiers…` ＋9。
       - 逐条隔离复跑一次，都在 1～5 s 通过，全部归为「负载」：`did not drain within` 为 0，`database is locked` 为 0。
       - 那条 33 s 的用例隔离时 3 s。它是单条用例的耗时，同一时段其他用例照常推进，不是 TASK-14 意义上的进程级停顿。
     - `-s1` 多出 ≥ 9 秒的 3 条：`SealedTreasureTests.Production_factory…` ＋12、`GmOfficialMailHttpTests.InvalidEquipment…` ＋11、`GmComponentHasItsOwnListener…(launcher: True, game: True)` ＋9。
     - `-s2` 4 条：`SealedTreasure…` ＋15、`GmOfficialMailHttpTests.MidBatchStorageFailure…` ＋12、`GmServerInfoHttpTests.ServerInfoReports…` ＋10、`SouthernDaleTests.Every_declared_candidate…` ＋9。
     - 隔离复跑时它们分别是 14、2、5、1、5、12 s，全部归为「负载」：`did not drain within` 为 0，没有 SQLite 30 秒忙等的形状，也没有 10 秒排空的形状。
     - 三份样本都没有进程级的 > 30 s 停顿。
3. **隔离复跑**（`logs/task-16-isolated.txt`）：
   - Protocol 1703 全过。
   - Application 4510：4492 通过，2 失败（就是那 2 条 `DimensionDungeonTests`），16 跳过。
   - Networking（`--blame`）：171 通过，2 跳过，没有测试主机崩溃。
   - Host 183 全过，用时 2.08 min。**testhost RSS 峰值 13367696 KiB**（`t14/sampler2.sh`，62 个样本），只作信息。对照 TASK-14 的 15931440 KiB 与 TASK-15 第 3 轮的 14247072 KiB。
   - F-54 裁定之后，Host 又隔离跑了一次：183 全过，用时 2.09 min，RSS 峰值 12089536 KiB，F-54 那一例 5 s。
4. **新增测试 ×10**（`logs/task-16-new-x10.txt`）：
   - 每轮 Protocol 3 条（T1、T2、改写的 C7）＋ Application 8 条；
   - 连续 10 轮，10 × 11 ＝ 110 次通过，0 失败；
   - 三份全量的负载下，这 11 条也全部通过。
5. **表征测试**：8 通过、2 跳过。`Characterization/` 目录在 `git status` 中没有出现，金样本没有变化，停止条件没有触发。
6. **变异核对**（`logs/task-16-mutations.txt`）：每项先用 scratchpad 备份，做完后逐字节还原；`cmp` 相等，sha1 与改动前相同。全部做完后由正式全量以 `--no-incremental` 重建。
   - V1：在 `TryReplace` 里恢复 `ObjectDisposedException.ThrowIf`。T1、T2 与改写的 C7 变红（19 条中 3 条）。
   - V2：在 `Application/Execution/GameplayQueue.cs` 末尾加一个调用 `GameplayJob.RunDetached(() => { })` 的类。`Detached_work_starts_only_at_the_listed_sites` 变红（8 条中 1 条）。
   - V3：同一位置加 `ExecutionContext.SuppressFlow()`，同一条用例变红。
   - 反例用例证明扫描器会拒绝，V2、V3 证明它扫到了真实源码树。
7. **测试改动核对**（`logs/task-16-diff-check.txt`，`t07/diffcheck7.py c3115f9a --lines`）：
   - `Timers.cs` 的测试方法从 5 个变为 7 个，原有 5 个都在，新增的就是 T1、T2。
   - 断言行从 28 变为 44：28 － 1（C7 的 `Assert.Throws`）＋ 6（T1）＋ 7（T2）＋ 3（C7 新断言）＋ 1（`Inflight` 辅助里的 `Assert.IsType`）＝ 44。
   - 白名单外的片段都在 C7 改写处和新增的 `using` 行。
   - 新文件 `DetachmentSiteTests.cs` 在基准上不存在。
   - `LauncherHttpHostStartupTests.cs`（F-54）：方法 6 ＝ 6，断言行 38 ＝ 38；白名单外的片段只有 2 处，即注释与 `15 → 60`。
8. **生产改动边界**（`logs/task-16-boundary.txt`）：
   - `git diff --stat -- Server/src` 只有协调器一个文件，`Server/src` 下未跟踪文件为 0。
   - `IGameplayQueue` 37 行，与基准逐行相同。
   - `SuppressFlow|RunDetached` 14 行，文本与基准逐行相同；只有协调器那一行的行号从 302 移到 333。
   - 同一日志还收录：总线调用点普查、非 handler 类的调用方、分离点检索、托管服务注册、订阅计数、观察者与事务访问器的检索。
9. **探针终核**：
   - 原始记录在 `logs/task-16-probe-{Application,Persistence,Protocol,Host,Networking}.txt`，分族汇总在 `logs/task-16-probe-summary.txt`。
   - 还原证明：快照前后 sha1 都是 `bf76d2e14c39c051407e5a8cb6b5c0ae3b1044a6`，`cmp` 相等；`snapcheck` 1589／1589，相等 1588，不等的 1 个就是协调器；`patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 为 0；随后以 `--no-incremental` 重建。
   - 探针轮的失败只有那 2 条 `DimensionDungeonTests`。
10. **文档对账**：`logs/task-16-evidence-names.txt` 列出三份文档引用的 53 个测试类、方法与文件名。其中 52 个在基准上 `git grep` 到定义，`DetachmentSiteTests` 是本单元新增、在工作区找到，缺失 0。

### Remaining

1. **F-54 已关闭**（主控方裁定放宽预算）。`follow-ups.md` §2.2 的建议 1～4 仍未实施，记录在那里：共享宿主、打包进程串行、宿主自绑 0 端口、重复路径字典。
2. **x1 中 3.3 不为 0**：计划预期为 0，实为 42／54，详见 Outcome。交 T-004c（F-18）。
3. **T-004c 进入条件中的缺口**（`t004c-entry.md`）：
   - 组门与调度并发槽：门断言要排除它们，或者先删除；
   - `:274` 的反例由判据自己补；
   - 直接调用观察者的非冲突用例；
   - AC-003 第 3 条没有单独测；
   - VC-8 提前的完成核对。
4. **`pending` 留着被取消的源**：协调器排空之后，`pending` 里仍保留被取消的计时器源（`Dispose` 注释所说的既有行为）。本单元没有改；它只影响 C7 新断言的写法，进程即将结束，不影响生产。
5. **未提交**：没有 commit，也没有 stash。

### 主控方裁定（Result 之后）

1. C7 末条断言写作「`pending` 不增长」而不是「为 0」：接受。裁定文字以此为准，意图不变。
2. F-54：授权在本单元内修改测试基础设施，把 `LauncherHttpHostStartupTests` 中打包进程用例的就绪／健康检查等待预算从 15 s 放宽到 60 s；断言不改。已执行，见上面 Result 的 Outcome、Changed 与 Checks 2、3、7。

## Result（第 2 轮：结果审查 changes_required 之后，2026-10-22）

审查：`reviews/task-16-result-review.md`。主控方处置：本轮只改文档、补跑探针排除轮，不改代码，不 commit。本节补充上面的 Result，与它冲突时以本节为准。

### Outcome

- **B1 已修正**：
  1. **`t004c-entry.md` §4.1 新增 (B)／(C) 去向表**：五个项目 P1／P1b-read／P2／P3 的执行器外 (B)／(C) 记录，按最外层测试帧逐组对到冲突清单今天的去向（T-004c／T-006／T-008／不在清单／只有辅助帧）。§1 里「执行器外记录只在 3.3 与 3.10」改为限定 (A) 记录，并指向这张表。
  2. **Protocol 补跑两次排除轮**：
     - x2 排除整份冲突清单（98 项，排除 40 个用例实例）：P3 的 (B)／(C) 为 0，(C) 共 0。
     - x3 只排除 T-004c 的 77 条（76 条加上下面改归的 1 条，排除 31 个用例实例）：P3 的 (B)／(C) 也为 0，P2 (C) 为 0。
     - 所以 p1 中 18 条 (C) 取门记录与 7 条 (C) 访问记录，都来自 T-004c 冲突用例，没有取证缺口。
     - x3 只剩 3 条 (B)，都是读：卡牌测试的 `WaitUntilAsync`、T2 的 `world.Runs.Find`、`ContainerInventoryProjectionTests` 的一次只读事务。
     - 还原证明照旧：x2、x3 的快照前后 sha1 都是 `bf76d2e1…`，`cmp` 相等；`snapcheck` 1589 中只有协调器不等；`revert` 恢复 25 个文件；`grep TestGameplay|GameplayProbe Server/src` 为 0。
  3. **改归 T-004c**：按主控方裁定，`CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection` 从 T-006 改归 T-004c。
     - 记录：`task-07-conflicts.md` §13；分布 0 ＋ 77 ＋ 52 ＋ 1 ＋ 1 ＋ 8 ＝ 139。
     - `t004c-entry.md` §4、`follow-ups.md` F-56 都已写明。
     - 其余 T-006（K5）用例：以 T-006 冲突用例为最外层测试的取门记录（A／B／C 三类都查），五个项目中只有这一条（`k5gates.py`）。**没有其他用例需要改归**。
- **Application 的对照**：x1（第 1 轮已跑，排除 T-004c 的 76 条）中 P1、P3 的 (B) 与全部 (C) 都是 0。剩下的只有读：P1b-read 121 条、P2 中 T-006 130 条、T-008 2 条、通知测试 `RecordingSender` 辅助帧 2 条。
- **非阻断项 1～5、7 已处理**：
  - 1：附录 B「只读 SQLite」行与对账 §2.3、§2.4 补上 `RunGroupAsync` 调用的 `NextDueAsync`。
  - 2：`t004c-entry.md` §5 写明 `DetachmentSiteTests` 只钉住出现位置，拦不住作业内新增的未抑制 `Task.Run`／fire-and-forget，也认不出方法组写法；今后新增的分离点要人工检索。
  - 3：`ReplayStateSnapshotTests` 注明「行号沿用 TASK-07，基准上方法从第 212 行开始」；组门那一行改为「`RunGroupAsync` 调用的 `DrainOnceAsync(string)`」；分离点表第 3 行注明 `SuppressFlow` 在 `:37`，`:18`、`:26` 是丢弃点。
  - 4：`logs/task-16-boundary.txt` 末尾追加补正后的托管服务检索。原模式漏了 `ApplicationModule.cs:382` 的 `JoustMaintenanceService`（逗号后没有空格），补正后为 34 行。
  - 5：`follow-ups.md` §2.1 的 swap 改为正式全量的 8651 → 9758 MB，另列 `-s1`、`-s2` 的数；耗时列标明取自 `-s1`；§2.2 第 1 条改为 149 例。
  - 7：T2 在执行器外的读写进 `t004c-entry.md` §4.1 的「翻转前需关注」与 F-57。
- 非阻断项 6 只是信息，没有动作。

### Changed（本轮）

- 文档：
  - `verification/t004c-entry.md`：§1 两处、§4 新增 §4.1、§5；
  - `verification/task-07-conflicts.md`：§12 行号注记，追加 §13；
  - `verification/follow-ups.md`：F-11 改为 77 条，新增 F-56、F-57，§2 的数字改正；
  - `verification/entry-reconciliation.md`：§1.4 加 (B)／(C) 指针，§2.3、§2.4 补 `NextDueAsync`，§3.2 第 3 行行号；
  - `spec.md` 附录 B「只读 SQLite」行补 `NextDueAsync`，只改事实性清单。
- 证据：
  - 新增 `logs/task-16-probe-round2.txt`：去向表、明细、`k5gates.py`、x2、x3 的原始输出与过滤器；
  - `logs/task-16-boundary.txt` 追加补正检索；
  - `logs/task-16-evidence-names.txt` 重跑：60 个名字，缺失 0。
- 代码与测试：本轮没有改动。

### Checks（本轮）

- **探针排除轮 x2、x3**：只跑 Protocol，见上。每轮前后都做了还原证明；运行时只临时改动 `Server/src`，已还原。
- **测试名核对**：60 个；59 个在基准上找到，`DetachmentSiteTests` 是本单元新增，缺失 0。
- **构建与全量**：本轮没有改代码，沿用上一轮的正式全量 `logs/task-16-build-test.txt`。x2、x3 构建时带着探针挂点，之后 `revert` 恢复，`Server/src` 与快照逐字节相同。

### Remaining（本轮更新）

- T-004c 的冲突用例为 77 条。
- `t004c-entry.md` 中留给 T-004c 的缺口：
  - 组门与调度并发槽：门断言排除它们，或先删除；
  - `:274` 的反例；
  - 判据若连读也拦，要先处置执行器外的读：轮询读、T2、测试替身、T-006 的 130 条；
  - `UserServiceTests` 中直接调用观察者的非冲突方法；
  - AC-003 第 3 条；
  - VC-8 提前的完成核对。
- 未提交：没有 commit，也没有 stash。

### 主控方补充（第二轮复核通过之后，提交之前）

- 审查第 2 轮的非阻断项 1 已处理：主控方在提交前对 Release 做了 `--no-incremental` 重建，结果是 0 警告、0 错误。`Server/src` 中检索 `GameplayProbe|FirstLoadProbe` 为 0，磁盘上的二进制不再带探针。
- T-004b 至此全部完成。T-004c 的进入条件与缺口见 `verification/t004c-entry.md`，后续事项见 `verification/follow-ups.md`。
