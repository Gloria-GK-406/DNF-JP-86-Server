# 后续事项清单（TASK-16 汇总）

- 日期 2026-10-22，基准提交 `c3115f9a`。
- 范围：TASK-08～TASK-15 的 Remaining、主控方补充与裁定中「交给某单元／立项／后续」且尚未关闭的事项；更早单元交给 T-004 而仍未关闭的（TASK-04 Remaining 第 1～2 条、TASK-06 §7 待办 4f）；TASK-16 执行时新发现的事项。
- 同一事项在多个单元出现时合并为一行，来源全部列出。来源写作「TASK-xx 节／条」；「补充」指该单元的「主控方补充」，「裁定」指「主控方裁定」。
- 状态：未关闭／已在 TASK-16 关闭／信息（只记录，不需要动作）。

## 1. 清单

| 编号 | 事项 | 来源 | 归属 | 状态 |
|---|---|---|---|---|
| F-01 | 13 个内容源各自持有一份 equipment／stackable 的「id → 路径」字典，合计约 500 MB；第 3 轮 t＝60 s 的 RSS 与全程峰值比第 2 轮高约 0.8 GB，立项时一并评估 | TASK-15 第 3 轮 Remaining 1、补充 | 交主控方立项 | 未关闭 |
| F-02 | RQ-091 的达标判定（单条 CMD ≤ 2 s 为接受线，用户修订） | TASK-14 可见差异 1、TASK-15 第 3 轮 Remaining 3 | M-1 | 未关闭 |
| F-03 | 周签到 D 类读档（几乎每条玩法 CMD 都读，单作业最多 74 次读）——M-1 性能关注点；另两项 D 类：超额技能契约、每日补给规则 | TASK-15 第 3 轮 Remaining 3 | M-1 | 未关闭 |
| F-04 | 首次选角里约 100 ms 不是内容加载（推断为 JIT 与 SQLite 冷页），含 D 类路径的首次 JIT | TASK-15 第 3 轮 Remaining 3 与补充 | M-1 | 未关闭 |
| F-05 | C 类按键缓存的单键首次加载与物品目录的 `searchable` 在执行器上发生；合成负载覆盖不到的 CMD 走到 C／D 类时同样 | TASK-15 第 3 轮 Remaining 3 | M-1 | 未关闭 |
| F-06 | 预热 W2 的识别边界：认不出 `if (x is null)`、手写字典、`is {}` 的首次索引，新增这几种写法要靠人工检索与参与者名单把关。（TASK-15 第 2 轮 Remaining 4 的「32 个探针未触达的豁免持有者」已在第 3 轮处理：纳入 31 个、豁免 1 个，TASK-15 第 3 轮 Progress） | TASK-15 第 3 轮 Remaining 4 | 人工把关；M-1 观测豁免项 | 未关闭（32 个豁免一项已在 TASK-15 关闭） |
| F-07 | 长作业：每日任务、Premium、配额刷新在一个作业里遍历全部在线会话；拆分归 T-008 | TASK-11 Remaining 2 | M-1 观测，T-008 拆分 | 未关闭 |
| F-08 | 空闲作业约每秒 30 个（50 ms 比武扫描、250 ms 全灭扫描、两个 500 ms 两塔扫描、每秒的生物／婚礼／Joust、重试排空）；M-1 记录入站排队延迟 | TASK-11 Remaining 4 | M-1 | 未关闭 |
| F-09 | GM 查询与玩法串行，慢 GM 查询占住执行器 | TASK-14 可见差异 1 | M-1 | 未关闭 |
| F-10 | 订阅组不再并发后，慢订阅（成就、任务进度、称号簿）占住执行器 | TASK-12 Remaining 4 | M-1 | 未关闭 |
| F-11 | T-004c 冲突用例 77 条：§12 的 76 条（含 `EffectOperationReplayTests:371`、`IllusionTowerPublicFlowTests:149`、`ReplayStateSnapshotTests` 中 TASK-07 记作 `:206` 的那条），加上 §13 改归的 1 条（F-56） | `task-07-conflicts.md` §12、§13 | T-004c | **TASK-17 处置 70 条**（删除 41、改写 29，`task-07-conflicts.md` §14，第 1 轮评审后修订）；仍通过的 7 条顺延 T-004c2，届时逐条判定保留或交 T-005 |
| F-56 | `CleanupNotificationDeliveryTests.Existing_owner_and_town_notices_finish_before_party_projection` 在执行器外取 `PartyDeliveryStream.Gate` 与 `PartyParticipantGate`，门断言开启时必然失败。主控方裁定从 T-006 改归 T-004c，在开门断言的同一提交里处置（倾向「经执行器驱动、断言不变」或按 K5 规则改写）。其余 T-006 用例没有在执行器外取附录 F 删除栏之门的（`k5gates.py`） | TASK-16 结果审查 B1；主控方处置（第 2 轮）；`task-07-conflicts.md` §13；`t004c-entry.md` §4 | T-004c | **已在 TASK-17 关闭**：门断言下失败（第二段），`DeliverAsync` 包进作业，断言不变（§14.2） |
| F-57 | RQ-061 判据若连读也拦，下列执行器外的读要先处置：轮询读（Networking 27 条、卡牌 `WaitUntilAsync` 1 条）、本单元新增 T2 的 `world.Runs.Find`、T-006 通知测试的 130 条 | TASK-16 结果审查 B1 与非阻断 7；`t004c-entry.md` §4.1 | T-004c | 未关闭 |
| F-58 | 翻转后，从作业泄漏出的流（作业结束后仍在跑的 `Task.Run` 续体）在执行器外调用总线，会成为一个不带任何请求级帧的新根作业，被当作普通命令执行；旧用例 `EffectOperationReplayTests.An_inherited_closed_operation_cannot_downgrade_to_trusted_direct_bus` 守的「继承已关闭帧不能降级成可信直发」因此只能跨流表达，已删除。生产中执行器外的总线调用为 0（`entry-reconciliation.md` §1.3），作业内的未抑制 `Task.Run` 不被 `DetachmentSiteTests` 拦住（`t004c-entry.md` §5） | TASK-17 §14.1（D 类） | **TASK-18**（主控方裁定：总线执行器外分支拒绝带已结束作业令牌的流，并补反例）；T-008 帧改为显式数据时再复核。TASK-17 第 2 轮已先堵住其中带 `DispatchDepth` 的一类（`RunDetached` 内、作业结束后的处理器续体，报 Nested command dispatch，`CommandBusExecutorTests` F8、F9） | 未关闭 |
| F-12 | 判据要覆盖 5 处裸 `new DomainTransaction()`，并区分只读一次性事务与写单元；`CreatureRuntimeDomainService.cs:274` 的反例由判据自己补 | TASK-09 Remaining 3、TASK-08 §2 b7、TASK-11 对账行 | T-004c（判据）、T-008（附录 C） | 未关闭 |
| F-13 | AC-003 第 3 条（持续灌包时待执行入站 CMD ≤ 1）没有单独测 | TASK-09 Remaining 5 | T-004c | 未关闭 |
| F-14 | VC-8 提前的完成核对（直发 2 秒期限已覆盖入站、生命周期、定时、事件、GM） | TASK-06 §7 4f、TASK-08 Remaining、TASK-10 Remaining 2、TASK-11 Remaining 3、TASK-12 Remaining 3、TASK-14 可见差异 2 与 Remaining 5 | T-004c 核对；T-006 之后消失 | 未关闭 |
| F-15 | Host 中 10 个测试类直接调用总线（Host 3.10 233／664），随翻转处理 | TASK-14 Remaining 6、Checks 9 | T-004c | **已在 TASK-17 关闭**：翻转后由 GmTestHost 的执行器执行，未改代码；两段诊断、正式全量与隔离复跑中 Host 183 全过（`logs/task-17-isolated.txt` 列名） |
| F-16 | RQ-061 启动期例外的取舍（TASK-16 结论：判据只挂工作单元入口等时可以不设） | TASK-14 Remaining 6 | T-004c | 未关闭 |
| F-17 | 事件组门与调度任务并发槽在执行器外持有并等待作业：门断言要排除它们，或先删除 | TASK-16 §7 第 1 项（`t004c-entry.md`）；附录 F | T-004c 与 T-005 定顺序 | **门断言一半已在 TASK-17 关闭**（主控方裁定第 2 条）：不挂门断言，改为反向断言；并发槽在 `slots.WaitAsync` 前补 `AssertNotOnExecutor`（反例 `GameplayGateAssertionTests.Taking_a_scheduled_task_slot_on_the_executor_is_a_self_wait`），组门沿用既有反例 `DomainEventDispatchTests.Draining_from_inside_a_job_fails_at_once_as_a_program_error`。删除仍归 T-005（F-19） |
| F-18 | `UserServiceTests` 中不在冲突清单里的方法也直接调用观察者（x1 中 3.3 为 42／54，计划预期为 0） | TASK-16 新发现（`entry-reconciliation.md` §1.4） | T-004c（处置冲突时一并判定） | **已在 TASK-17 关闭**：9 个方法的观察者调用包进作业、断言不变，无新判定冲突；同形的 `SelectCharacterLifecycleTests.RejectedRepeatedLogin…` 一并处理（§14.4） |
| F-19 | 调度任务的 `Task.Run` 外壳与并发槽；组门与准入门；Weakness `deliveryGate`；Joust `ticks` | TASK-11 Remaining 1 与对账行、附录 F | T-005 | 未关闭 |
| F-20 | VC-8 提前范围（入站、生命周期、定时、事件、GM）在发件箱之后消失；W7 改回严格形式（帧先于 FIN） | TASK-10 Remaining 2、3；TASK-11 Remaining 3；TASK-12 Remaining 3；TASK-14 Remaining 5 | T-006 | 未关闭 |
| F-21 | 停机时排空兜底拆除与 `TryRemove` 仲裁要保留；停服之外是否也会丢关闭回调未测 | TASK-10 Remaining 4 | T-006 | 未关闭 |
| F-22 | `AddNotificationDispatching` 不自带执行器；删除重试排空服务（连同 TASK-11 加的那几行） | TASK-11 Remaining 7、对账行 | T-006 | 未关闭 |
| F-23 | 按帧计期限的残余：慢读但每帧都在 2 秒内放手的客户端，一个批次内可累计让执行器等待超过 2 秒 | TASK-08 Remaining | T-006 | 未关闭 |
| F-24 | `OutboundSender` 的准入循环、每连接排空循环、失败关闭三处分离点只登记，接线时复核 | TASK-08 §2 b7、`entry-reconciliation.md` §3 第 6 行 | T-006 | 未关闭 |
| F-25 | Cera 提醒改发件箱 | TASK-14 对账行 | T-006 | 未关闭 |
| F-26 | 生命周期 CMD 一律新根；`lifecycle:close-cleanup`；由 CMD 登记的关闭引起的断开应继承链 | TASK-10 Remaining 1 | T-007 | 未关闭 |
| F-27 | 事件重试的继承链与深度超限落定 | TASK-12 Remaining 7 | T-007 | 未关闭 |
| F-28 | 定时 CMD 是否继承调度它的入站 CMD 的链（卡牌计时器）；定时源一律新根改为继承链 | TASK-11 对账行 | T-007 | 未关闭 |
| F-29 | 深度超限之后只关闭根来源是客户端连接的那个连接（RQ-053(a)） | TASK-04 Remaining 3 | T-007 | 未关闭 |
| F-30 | 双 Error 日志：入站、生命周期、定时作业、事件（失败记录本身失败时）、GM 委托里没被接住的异常，执行器与生产者／中间件各记一条 | TASK-09 Remaining 1、TASK-10 Remaining 6、TASK-11 Remaining 5、TASK-12 Remaining 6、TASK-14 可见差异 6 与 Remaining 4 | T-008 | 未关闭 |
| F-31 | 「执行器已停止」那一行 Information 不带异常，会把消费循环的故障原因压成「已停止」 | TASK-12 Remaining 8 | T-008 | 未关闭 |
| F-32 | 线级测试：作业内 handler 抛异常 → 断线（现有装饰器在进入内层 dispatcher 之前就抛出） | TASK-09 补充 N-1 | T-008 | 未关闭 |
| F-33 | GM 预缓冲的罕见不等价（过滤器运行时请求体上限只读且分块超过 32 KiB） | TASK-14 Remaining 9 | T-008 | 未关闭 |
| F-34 | 「每个消息类型一个协议 CMD」的归并；请求级上下文与 dispatcher 帧改为显式数据；`EffectOperationReplayTests:269` 随帧语义改写或删除 | TASK-09 对账行、`task-07-conflicts.md` §7 | T-008 | 未关闭 |
| F-35 | AGENTS.md 中把 `ClientSpecStatisticHandler` 称作纯遥测，与附录 B 判据不一致 | TASK-09 Remaining 4 | T-009 | 未关闭 |
| F-36 | AGENTS.md「接口后缀标明类别」表补「入队点」（`IGameplayQueue`）；`GameplayJobHost` 的派生类只能是 `GameplayExecutor` 的结构判据 | TASK-04 Remaining 2、6 | T-009 | 未关闭 |
| F-37 | 取证缺口：TASK-10 第 1 轮 Networking 隔离中 S1 失败一次，身份不明 | TASK-10 Remaining 11 | 取证缺口 | 未关闭 |
| F-38 | 取证缺口：TASK-12 Networking 隔离中测试主机崩溃（`ObjectDisposedException … 'SQLitePCL.sqlite3'`，栈只有原生回调帧，栈根不在 AGENTS.md 表内）；再现时用 `--blame` 取用例名 | TASK-12 Remaining 2 | 取证缺口 | 未关闭；TASK-16 的 Networking `--blame` 隔离复跑没有再现（`logs/task-16-isolated.txt`） |
| F-39 | 取证缺口：Host.Tests 进程级停顿（> 30 s）的机制（换页还是线程池相互作用）未定；再现时按「本单元路径 → 换页 → 线程栈」顺序记录 | TASK-14 Remaining 3 与定向复核裁定 | 取证缺口 | 未关闭；TASK-16 正式全量与隔离复跑中没有 > 30 s 的停顿 |
| F-40 | 升级 TouchSocket 时复核：Received 不被内联调用、关闭回调不内联（R-5，4.3.1 实测不内联） | TASK-09 补充 N-4、TASK-10 Remaining 7 | 升级时复核 | 信息 |
| F-41 | 跨平台「写入真异步」：只在 macOS 上本地证明，Linux／Windows 以 PR CI 通过 `DirectSendDeadlineRealSocketTests` 为证据 | TASK-08 Remaining（审查 N-3） | CI 与升级时复核 | 信息 |
| F-42 | 档案中的既有坏项 `map/grandine/43408.map`（`[type] [noamal]`） | TASK-15 第 3 轮 Remaining 5 | 信息 | 信息 |
| F-43 | GM 登录第二段的记录比对没有专门变异；将来增加「修改 GM 口令」的写入方时补用例 | TASK-14 Remaining 10 | 增加写入方时 | 未关闭 |
| F-44 | `:109` 改写后的 `SelectCardResponse` 断言与等待条件之间的窄竞争；出现偶发时改为等帧到达 | TASK-11 补充 N4 | 出现偶发时 | 信息 |
| F-45 | S2 依赖 `CharacterizationHost` 的私有字段（反射）；CI 无 PVF 时 S2 跳过，S3 在 Windows 上跳过 | TASK-10 Remaining 9 | 测试基础设施 | 未关闭 |
| F-46 | 「consumer 循环兜底」残余：异常若出自 `await job.RunAsync()` 内部，正在执行的作业不会被 `Abandon` 结束（今天不可达） | TASK-04 Remaining 5 | 信息 | 信息 |
| F-47 | 分离点两条盲区：分离点不被作业 await、全部在 `SuppressFlow` 下启动 | TASK-04 Remaining 1（N-2） | — | **已在 TASK-16 关闭**（`entry-reconciliation.md` §3；「不被 await」的依据是代码阅读） |
| F-48 | `RunDetached` 用结构判据限定位置（R2-2） | TASK-04 Remaining 2 | — | **已在 TASK-16 关闭**（`DetachmentSiteTests`，V2、V3） |
| F-49 | 协调器排空与传输排空的先后顺序 | TASK-11 补充 N3、TASK-14 裁定第 14 条 | — | **已在 TASK-16 关闭**（协调器容错，T1、T2、V1；C7 末条改写） |
| F-50 | 附录 A 中与代码不符的文字（141 个 handler、行号、33 个后台服务、「或离场时直接入队」、GM 写操作 34 条等） | TASK-09 对账行、TASK-14 Remaining 7、裁定第 13 条 | — | **已在 TASK-16 关闭**（`entry-reconciliation.md` §2.2） |
| F-51 | 附录 B 纯遥测行第一列的举例与清单矛盾 | TASK-09 补充 N-3（原交 T-009） | — | **已在 TASK-16 关闭**（裁定第 4 条） |
| F-52 | 附录 B 未列的执行器外代码（`ReadDueAsync`、`HttpApiStartupService` 与注册表校验、传输排空、GM 预缓冲、生产者的入队侧） | TASK-12 裁定第 3 条、TASK-11 §1、TASK-14 §2.2 与裁定第 12 条 | — | **已在 TASK-16 关闭**（附录 B 补齐，新增「生产者的入队侧」一行） |
| F-53 | TASK-12 Remaining 5（事件作业早于预热时的首次加载） | TASK-12 Remaining 5 | — | 已在 TASK-15 关闭（附录 B 预热行） |
| F-54 | 打包进程用例在满载下撞自身 15 秒预算：TASK-15 第 2 轮一次；TASK-16 两次正式全量各一次，都是 `LauncherHttpHostStartupTests.GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes(launcher: False, game: True)`，`TaskCanceledException` 正好 15 s。隔离 1 ＋ 5 次与「旁边跑 Application.Tests」2 次都在 4～5 s 通过（`logs/task-16-slow-isolated.txt`），只在整个解决方案并行、swap 7.5～8.8 GB 起步时出现 | TASK-15 第 3 轮 Remaining 2；TASK-16 Result | 测试基础设施 | **已在 TASK-16 关闭**（主控方裁定：该用例的就绪等待预算 15 s → 60 s，断言不改；最终正式全量中该例 26 s 通过，失败只剩 2 条 `DimensionDungeonTests`）。同类打包进程用例的其他预算各自独立、未改，见 §2.2 第 5 条 |
| F-55 | 端口争用：用例「先绑 0 取端口再释放」，宿主绑定前被占走 | TASK-10 Remaining 8、TASK-14 Remaining 2 | 测试基础设施（见 §2） | 未关闭 |

## 2. 测试基础设施：重型宿主与打包进程用例（TASK-16 §5，信息与建议，不改预算）

### 2.1 清单（正式全量 `logs/task-16-build-test.txt` 与隔离复跑 `logs/task-16-isolated.txt`；Theory 按实际运行的例数计）

统计脚本 scratchpad `t16/heavy.py`。用例数取隔离复跑（全量日志里多个项目并行写屏，Host 183 条结果中有 1 条与别的行粘连、解析不到，所以全量一栏的例数可能少 1）。预算是源码中的 `CancellationTokenSource`／等待时限，在基准源码上逐个核对。

| 类别 | 用例数（隔离） | 开预热 | 读真实 PVF；CI 无档案时 | 每个用例的固定预算 | 耗时：第一次全量 `-s1`（和／最大） | 耗时：隔离（和／最大） |
|---|---:|---|---|---|---|---|
| 打包 exe：`EndToEndHostHarness`（`LauncherAuthenticationEndToEndTests`、`HostShutdownOrderTests`、`AvatarCompoundTcpTests`） | 3 | 是（打包默认） | 用打包的默认配置；档案能打开时走预热分支，打不开时走无档案分支（代码阅读，CI 上未测）；`HostShutdownOrderTests` 在 Windows 跳过 | 启动 45 s；停机 20 s | 57.0 s／24.0 s | 16.0 s／6.0 s |
| 打包 exe：`LauncherHttpHostStartupTests` | 15 | 是 | 同上 | 60（原 15，F-54 裁定放宽）、30、20、30、20、20 s（另有一处 250 ms 的探测） | 第一样本 84.6 s／15.0 s（1 例撞原 15 s 预算失败，F-54）；放宽后的最终全量中该例 26 s 通过 | 41.5 s／5.0 s |
| 打包 exe：`ChannelHostStartupTests` | 6 | 是（坏档案用例用合成档案） | 同上 | 启动 45 s、请求 15 s；另有 3 min 与 30 s 两处 | 27.9 s／7.0 s | 19.2 s／5.0 s |
| 进程内 `GmTestHost`（含 `CredentialEntryHttpTests` 的 `LauncherHost`） | 149 | 否（`ContentWarmup:Enabled=false`）；S1 一条开 | 读真实 PVF 的用例以 `PvfFact`／`PvfTheory` 标注（8 个文件 11 处），CI 无档案时跳过 | 无宿主级时限（用例内各自的等待） | 1012.8 s／48.0 s（S1） | 566.1 s／19.0 s |
| Networking `CharacterizationHost`（表征 10 条、停机 S2 1 条） | 11（2 条跳过） | 是 | 需要档案；无档案时跳过（S2 同） | 停机 20 s | 79.0 s／27.0 s | 31.0 s／7.0 s |
| 对照：Networking 线级集成（不读档案的宿主） | 51 | 否 | 不需要 | 各用例 10 s 量级（AGENTS.md） | 61.4 s／4.0 s | 24.4 s／1.0 s |

- **测试进程内存**（Host.Tests 隔离，scratchpad `t14/sampler2.sh`，2 秒一次，KiB）：testhost RSS 峰值 **13367696 KiB**，62 个样本。对照：TASK-14 同口径上限 15931440 KiB；TASK-15 第 2 轮 13454768 KiB；TASK-15 第 3 轮 14247072 KiB。只作信息。
- **满载形状**：正式全量（`task-16-build-test.txt`）开始时 swap 已用 8651 MB，结束时 9758 MB（共 10240 MB）；此前 `-s1` 为 7576 → 11682 MB（共 13312 MB），`-s2` 为 8782 → 10843 MB（共 12288 MB）；第一次隔离复跑结束时 9102 MB。F-54 那一例在隔离下 5 秒通过。

### 2.2 建议（本单元不实施）

1. **同类宿主共享**：`GmTestHost` 的 149 个用例（隔离复跑口径，与 §2.1 的表相同）各自起一个进程内宿主（预热关闭，但读真实 PVF 的用例仍各自加载内容）。改用 xunit 集合夹具按「配置相同」共享宿主，可直接降低 Host.Tests 的峰值 RSS 与换页——对应 TASK-14 定向复核中「Host.Tests 进程级停顿、峰值 10.9～15.2 GB、swap 接近满载」（F-39）。
2. **打包进程按集合串行**：`LauncherHttpHostStartupTests`、`ChannelHostStartupTests`、`EndToEndHostHarness` 各自拉起开预热的打包 exe（每个约 1.2 GB，TASK-15）。放进同一个不并行的集合，可避免几个打包进程与 Host.Tests 自身的宿主同时预热，对应 F-54 的满载撞线。
3. **宿主自己绑定 0 端口再告诉用例**：取代「先绑 0 取端口再释放」，消除 F-55 的端口争用（TASK-10 Remaining 8、TASK-14 Remaining 2 已给出同一改法）。
4. **开预热的宿主数量与 13 份重复路径字典**：开预热的宿主每个约 1.2 GB，其中约 500 MB 是 F-01 的重复字典；F-01 立项后，开预热的宿主（S1、表征测试、停机 S2、打包进程）会同比例变小，F-54 与 F-39 的诱因随之减轻。
5. **预算**：F-54 经主控方裁定只放宽了 `LauncherHttpHostStartupTests.GmComponentHasItsOwnListenerAndDoesNotExposeLauncherRoutes` 的就绪等待（`:34`，15 s → 60 s；它是整个用例体共用的一个 `CancellationTokenSource`）。同类打包进程用例没有共用的预算常量或辅助，其余预算逐个核对后未改：同文件 `:77` 30 s、`:111` 20 s、`:146` 30 s、`:232` 20 s、`:281` 20 s、`:430` 250 ms 探测；`ChannelHostStartupTests` 的就绪已有独立的 45 s（`:30`），`:32` 的 15 s 只管就绪之后的协议交换；`EndToEndHostHarness` 启动 45 s。两次满载全量里只有 `:34` 这一处撞线。建议 1～4 仍未实施。
