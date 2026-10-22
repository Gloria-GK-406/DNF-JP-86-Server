# TASK-14 — T-004b6：GM／HTTP 与启动期工作迁入执行器，口令哈希移到执行器外，启动预热

基准提交 `2d8cec4d`（TASK-13 之后）。边界提纲见 `TASK-08.md` 文末「T-004b 拆分」§2 中原 TASK-13（b6）一行与 §1 的跨单元规则；编号顺延见 `TASK-13.md` 第 7 行。本文件不重抄这些内容，只写本单元的做法。

本单元的输入：
- `TASK-11.md` 主控方裁定第 1 条：Joust 启动静默恢复已在 TASK-11 迁入，不属于本单元。裁定第 7 条及补充：生产者以 `None` 入队。§1 的 33 个服务归属：`GmBootstrapService`、`GameServerStartupService` 归本单元迁入；`MarriageRelationAudit`、`ChannelScriptStartupService` 归本单元列入附录 B。
- `TASK-10.md` §6：停机次序与 `GameplayExecutorLifetime`。
- `TASK-12.md` Result Remaining 第 5 条：事件作业可能早于预热运行，预热清单归本单元。§5.1：执行器外的准入门持有者有 GM／HTTP。
- `TASK-03.md` 主控方处置与 `verification/measurement/baseline-summary.md`：
  - 口令哈希在全局门内串行：`RegisterAccount` 约 666 ms、`AuthenticateAndIssueTicket` 约 638 ms，10 个并发登录合计约 640 ms 的门内哈希；
  - 冷进程首次选角 `PrepareSelectedCharacterState` 503／520 ms，首次进本 `EnterDungeonCommand` 1104 ms；(f) 最长停顿 575.9／1118.5 ms。
- `verification/task-07-probe.md` §3.7、§3.9，以及 TASK-12 Result Checks 9 的 p1 计数。
- `verification/task-07-conflicts.md` 中分给本单元的 2 条。

## Task Unit Contract

- Revision: 1
- Outcome and references:
  - **GM 路由**（附录 A 第 9 行，RQ-015）：GmWeb 的 51 条路由逐条归类，加法见 §1.1。
    - 经总线的 40 条，每次请求是一条 GM CMD，来源 `Gm`。其中 39 条由端点过滤器把**端点委托整体**作为 CMD（§2.2），GM 登录 1 条显式分两段（§3.4）。
    - 不经总线的 11 条留在执行器外，各附附录 B 的理由（§1.1 表）。
    - 一次请求里的多条命令、失败审计与 Cera 提醒都在同一条 CMD 内（RQ-021）。
    - 不在中间件层入队。`GmAccessBoundary` 的身份识别是只读 SQLite，留在外面（附录 B 原文举例「身份识别」）。
  - **启动器路由**（附录 A 第 10 行）：注册、登录各是一条 HTTP CMD，来源 `Http`；`bootstrap` 只读就绪标志，留在外面。
  - **口令哈希移到执行器外**（RQ-007，AC-005 的前半）：
    - 哈希、校验、哑校验只经一个限流器执行。限流器的上限等于处理器数，超出时排队、不拒绝。在执行器上调用限流器，以 `GameplaySelfWaitException` 失败（§3.1）。
    - 哈希结果作为命令的输入：注册带「已准备的口令记录」，登录带「已校验的凭据」，GM 登录带「凭据核对结果」，GM bootstrap 带口令记录（§3.2～§3.5）。
    - 命令侧的领域服务不再注入哈希器，结构判据 H5 守着这一点。
  - **启动期工作作为执行器作业**（plan-review-r2 R-3，附录 B 末行）：`EpicBook` 迁移、经验存储策略安装、GM bootstrap 各是一个 `startup:` 作业，来源 `StartupRecovery`（§4）。
    - RQ-061 的启动期例外是否还需要，留给 T-004c 判定。本单元只做「作为作业运行」。
    - 建表、频道脚本加载、`MarriageRelationAudit` 的只读计数、内容初始化，留在外面，列入附录 B。
  - **启动预热**（RQ-091，附录 B「启动预热」行）：
    - 执行器上会触达的 `Lazy` 与按键缓存的内容源，在 `GameServerStartupService.StartingAsync` 中加载，清单见 §5；
    - 冷进程首次选角、首次进本由首次加载探针（§5.3）与冷启动测量（Required check 11）取证。
  - **冲突用例 2 条**：`GmPlayerSamplingHttpTests:14`、`AuthenticationConcurrencyTests.OverlappingPasswordReads…`，处置见 §7。
  - 覆盖：
    - 附录 A 第 9、10 行；
    - 附录 B 的「GM 自身的内存状态」「只读 SQLite 的查询」「口令哈希等纯计算」「启动预热」「开始监听前的启动期工作」五行，追加清单；
    - RQ-007、RQ-010、RQ-011、RQ-015；RQ-091 中的预热部分；
    - AC-005 中「哈希不在执行器上、并发有上限」的部分。测量记录满足 RQ-091 那一半归 M-1；
    - AC-006 中上述各行的对账。
- Owned boundary:
  - 生产代码：
    - `Dfo.Server.Data/Authentication/AuthenticationModels.cs`：新增凭据记录。`AuthenticationStatus` 加一个内部成员 `CredentialChanged`，见 §3.3。
    - `Dfo.Server.Data/Administration/GmAdministration.cs`：新增 GM 凭据核对记录。
    - `Dfo.Server.Abstractions/Authentication/AuthenticationServices.cs`：`IAuthenticationDomainService` 两个方法改签名；新增 `ILauncherCredentialReader`。
    - `Dfo.Server.Abstractions/Administration/GmAdministrationServices.cs`：`IGmAccessDomainService` 的 `BootstrapAsync`、`LoginAsync` 改签名；新增 `IGmCredentialReader`。
    - `Dfo.Server.Application/Authentication/`：
      - 改：`AuthenticationDomainService.cs`、`AuthenticationCommands.cs`；
      - 新增：`PasswordComputationLimiter.cs`、`LauncherCredentialReader.cs`。
    - `Dfo.Server.Application/Administration/`：
      - 改：`GmAccessDomainService.cs`、`GmAccessCommands.cs`；
      - 新增：`GmCredentialReader.cs`。
    - `Dfo.Server.Application/ApplicationModule.cs`：限流器与两个 reader 的注册。
    - `Dfo.Server.HttpApi/HttpApiApplicationExtensions.cs`：注册、登录两段式；`bootstrap` 加豁免标记。
    - `Dfo.Server.HttpApi/HttpGameplay.cs`（新增）：
      - 入队辅助 `HttpGameplay.RunAsync`；
      - 端点过滤器工厂（含请求体预缓冲）；
      - 两种路由元数据：`OutsideGameplay(理由)`、`EnqueuesItself`。
    - `Dfo.Server.GmWeb/`：
      - `GmWebModule.cs`：给路由组挂过滤器工厂；`health` 与页面路由加豁免标记；
      - `GmAuthenticationEndpoints.cs`：登录两段式；`status`、`csrf`、`session` 加豁免标记；
      - `GmBootstrapService.cs`：外部准备口令，再入队启动作业；
      - `GmItemEndpoints.cs`、`GmServerInfoEndpoints.cs`、`GmOfficialMailEndpoints.cs`：只给 §1.1 的豁免路由加元数据，委托体不改。
    - `Dfo.Server.GameServer/GameServerStartupService.cs`：两个启动作业（§4）与预热清单（§5）。
    - §5.2 表中缺少预热入口的规则服务：在 Abstractions 接口与 Application 实现上**只增加**一个 `int Initialize()`，形状与 `IWorldDropRuleService.Initialize` 相同，不改其他成员。
    - §8 第 (iv) 类确需的缺陷修正，限于上述文件。
  - **不改**：
    - `GameplayExecutor`、`GameplayQueue`、`GameplayJob`、`GameplayTimerWork`、`GameplayExecutorLifetime`；
    - `CommandBus` 与管线、`TransactionalExecutionGate`；
    - `GmAccessBoundary`、`HttpApiExceptionMiddleware` 等中间件；
    - 各 GM 领域服务（`GmAccessDomainService` 除外）、`ProtocolModule`；
    - `MarriageRelationAudit`、`ChannelScriptStartupService`、`HttpApiStartupService`、Persistence 的建表。
  - 测试：
    - 新增：
      - `tests/Support/TestCredentials.cs`：注册、登录辅助，先调 reader、再经总线发命令。链接进 Application、Persistence、Networking、Host 四个测试项目；
      - §6 各条所在的新测试文件。
    - 改：
      - 构造 `RegisterAccountCommand`／`AuthenticateAndIssueTicketCommand`／`LoginGmAdministratorCommand`／`BootstrapGmAdministratorCommand` 的 15 个测试文件（§8.2），只换驱动调用；
      - `HttpApiPipelineTests.cs` 的夹具：注册执行器与 reader 替身；
      - 2 条冲突用例；
      - 按 §8 处置的其他既有测试文件。
  - 文档：
    - `spec.md` 附录 B：按 TASK-09 的先例只在单元格末尾追加清单（§9 第 3 条）；
    - `verification/logs/task-14-*.txt`；
    - `verification/measurement/task-14-coldstart.md`（Required check 11）；
    - `task-07-conflicts.md` 追加 §11：2 条冲突的去向与本单元新判定的冲突。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误。
  2. **全量**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed" --blame-hang-timeout 5m`，落盘到 `logs/task-14-build-test.txt`。
     - **总数**：
       - TASK-12 最终为 50 ＋ 80 ＋ 1701 ＋ 475 ＋ 969 ＋ 173 ＋ 4497 ＋ 166 ＝ 8111，TASK-13 未改变计数；
       - 本单元的总数 ＝ 8111 ＋ N － D，逐项目对照并写出加法。
     - 失败只能是那 2 条 `DimensionDungeonTests`。跳过 20 条。表征测试 8 通过、2 跳过。没有挂起转储。
     - **日志信号计数**：
       - `direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`；
       - `Gameplay gm:`、`Gameplay http:`、`Gameplay startup:` 开头的 `failed`；
       - `GameplayExecutorStoppedException`。
       - 非零的逐条说明来源。GM 失败路径里，执行器的 Error 与端点自己的 Error 会成对出现，属于 TASK-09 待裁定第 4 条的双日志，计数单列。
     - **耗时比对**：
       - 与 `logs/task-12-build-test.txt` 逐条比（TASK-13 没有全量），多出 ≥ 9 秒的逐条判定：负载波动、释放排空、SQLite 忙等，或本单元引入的「HTTP 请求排在执行器作业之后」；
       - Host 项目单列。GM 用例从此与事件作业、定时作业共用执行器。
     - Networking 项目若再次出现 TASK-12 Remaining 第 2 条的测试主机崩溃，按 Required check 3 的 `--blame` 取得用例名，不只记总数。
  3. **隔离复跑**：HttpApi.Tests、Persistence.Tests、Application.Tests、Networking.Tests（加 `--blame`）、Host.Tests 各一次，落盘到 `logs/task-14-isolated.txt`。
     - Host.Tests 包含拉起打包 exe 的真实进程用例（`LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`、`HostShutdownOrderTests` 等），必须全部通过。
     - 超时形态的失败先按 AGENTS.md「满载假红」隔离复跑，再归类。
  4. **新增测试 ×10**：§6 的全部新增用例与 2 条改写后的冲突用例，隔离连续跑 10 次，全量负载下再跑 1 次，全部通过，落盘到 `logs/task-14-new-x10.txt`。
  5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本不改。
     - 表征宿主经启动器 HTTP 注册、取票（`CharacterizationHost.RegisterAndIssueTicketAsync`），会走本单元的两段式。金样本只覆盖 TCP 线上字节，不应变化；
     - 一旦变化，执行者停下回报主控方，不改金样本，也不放宽比对。
  6. **变异核对**：每项都用 scratchpad 备份，逐字节还原后 `cmp`，全部做完之后以 `--no-incremental` 重新构建。
     - V1：路由组不挂过滤器工厂，GM 端点在执行器外运行。E1、R1 变红。
     - V2：过滤器不预缓冲请求体，在 CMD 内读体。B2 变红。
     - V3：注册的 reader 不哈希，原样传口令，由领域服务在 CMD 内哈希。H1 变红。
     - V4：限流器不限并发。H2 变红。
     - V5：去掉限流器的 `AssertNotOnExecutor`。H3 变红。
     - V6：登录第二段不比对口令记录。L1 变红。
     - V7：`EpicBook` 迁移改回在 `StartingAsync` 里直接调用。S1 变红。
     - V8：GM bootstrap 的命令改回在 `StartAsync` 里直接发。S1 变红。
     - V9：从预热清单删掉成就目录一项。W1 变红。
  7. **测试改动核对**：
     - 新增测试逐条列名，写出 N 的加法。
     - §8.2 的 15 个文件只换了驱动调用。用 TASK-07 的差异核对脚本（scratchpad `t07/diffcheck7.py <基准>`）证明方法集合不变；对断言行先按 §8.2 的替换表做规范化，再证明多重集合不变。替换表写进 Result。
     - 改写的冲突用例写成「旧断言 → 新断言 → 保护意图」。
  8. **生产改动的边界与不变量**（`logs/task-14-boundary.txt`）：
     - `git diff --stat -- Server/src` 只含 Owned boundary 所列文件。
     - `grep -rn "IGameplayQueue" Server/src`（排除 bin/obj）：与基准相比，新增的命中只在 `HttpGameplay.cs`、`GmBootstrapService.cs`、`GameServerStartupService.cs`。
     - `grep -rn "IPasswordCredentialRuleService\|PasswordComputationLimiter" Server/src`：只出现在接口定义、实现、两个 reader、限流器与注册处。H5 是它的运行期版本。
     - `grep -rn "SuppressFlow\|RunDetached" Server/src`：与基准逐行相同。
     - **部分迁移期间的不变量**（拆分原则第 3 条，§10）：逐个列出等待新迁入 CMD 的执行器外代码，写明它们等待时不持任何门。
     - 列出生产中仍在执行器外调用 `ICommandBus.SendAsync` 的位置。预期为 0，非零的逐条写明归属。这是给 TASK-15（b7）与 T-004c 的输入。
  9. **探针复跑**（`verification/probe-tools/`，授权见下）：
     - 项目：Application、Persistence、Protocol、Host、Networking 五个。原始记录 `PROBE_LOG_PREFIX=task-14-probe`，落盘到 `logs/task-14-probe-<Project>.txt`。
     - **本族归零**：基线是 TASK-12 p1；Persistence 取 TASK-07 r6。
       - §3.7 GM/HTTP：813／1660 → 0；
       - §3.9 启动与停机（改正后）：161／407 → 0。
     - **§3.10 的 Host 部分**：
       - TASK-12 说明过，Host 中撞到 40 帧上限、中间件帧被截掉的 GM 栈会落进 §3.10；
       - 本单元之后，Host 的 §3.10 记录只允许带测试帧（测试直接调用总线或领域服务，T-004c 的翻转对象）；
       - 既无测试帧、又无入口帧的逐条说明。
     - 残留只允许来自顺延的冲突用例，并用排除复跑证明；排除之后仍有的，按缺陷处理。
     - 其余各族与 TASK-12 p1 并列写出，作为 TASK-15 的输入。
     - 还原证明的三项同 TASK-07 Required check 6；之后先 `--no-incremental` 重新构建，再跑正式全量。
  10. **对账行**（AC-006），按「来源 → CMD 类型 → 证据」写：
      - 附录 A 第 9 行：
        - 40 条 → `gm:<METHOD> <路由模板>` → R1（逐条列出）、E1、L2、探针 §3.7 为 0；
        - 11 条豁免 → 附录 B 的理由，R1 钉住豁免集合；
        - 加法 40 ＋ 11 ＝ 51。
        - 附录 A 原文写「GM 写操作 34 条」，按今天的代码数不出 34 这个口径。Result 写明「按 51 条路由对账；34 的口径不足以确定」。
      - 附录 A 第 10 行：
        - 2 条 → `http:POST /api/launcher/v1/register`、`http:POST /api/launcher/v1/login` → E2、H1；
        - 1 条豁免（`bootstrap`）；
        - 加法 2 ＋ 1 ＝ 3。
      - 启动期：3 个 `startup:` 作业 → S1。附录 B 末行中留在外面的各项，逐项写理由。
      - 预热：§5 清单 → W1、首次加载探针、冷启动测量。
      - AC-005：H1～H5 各自对应哪半句。
      - 标明留给后续的部分：
        - T-004c：RQ-061 启动期例外的取舍、翻转；
        - T-006：Cera 提醒改进发件箱；
        - T-007：GM／HTTP 是新根，没有要继承的链，这一项只核对；
        - M-1：RQ-091 的测量判定。
  11. **冷启动测量**（预热的有效性；授权见下）：
      - 用 `Tool/GameplayLoad` 以构成 B（`--weights 0.5,0.5,0,0,0 --entry-concurrency 1`）跑一次，命令行同 `verification/measurement/README.md`，`--host` 指向本单元的 Release 构建；
      - 记录：
        - 第一次 `PrepareSelectedCharacterState` 与第一次 `EnterDungeonCommand` 的时长，与基线的 503／520 ms、1104 ms 对照；
        - (f) 窗口的最长单次停顿，与基线的 575.9／1118.5 ms 对照；
        - Host 启动到就绪的时长，以及 `GameServerStartupService` 新增预热各项的耗时日志。
      - 写入 `verification/measurement/task-14-coldstart.md`，标明「合成负载、单次运行、只作预热有效性的证据，不作 RQ-091 判定」。
      - 首次选角或首次进本仍高于 250 ms 时不阻塞本单元，但必须给出剩余耗时的来源：首次加载探针中还剩哪些首次加载，或者确认不是内容加载（JIT、SQLite 冷页）。交 M-1。
- Dependencies：
  - TASK-13：基准 `2d8cec4d`；
  - TASK-12：探针基线 p1、全量基线；
  - TASK-11：裁定第 1、7 条，33 个服务的归属；
  - TASK-10：停机次序与 `GameplayExecutorLifetime`；
  - TASK-08：容器执行器注册、直发 2 秒期限、探针工具；
  - TASK-04：`GameplayExecutor`、作业令牌、`AssertNotOnExecutor`；
  - TASK-06、TASK-07：`TestGameplay` 与冲突总清单；
  - TASK-03：测量工具与冷启动基线。
- Result review: required。
  - **共享接口在这里定型**。`IAuthenticationDomainService`、`IGmAccessDomainService` 的签名和四条命令的输入都改了。`HttpGameplay` 的入队辅助、端点过滤器和豁免元数据，会被 T-004c 的翻转、T-006 的发件箱（Cera 提醒）、T-008 的 GM／HTTP 内部 CMD 归并直接沿用。哪里做错，要到这些单元才以「凭据路径重复」「路由漏入执行器」暴露出来。
  - **「哪些路由留外」逐条判断**。漏放一条，全量大多仍是绿的。只有 R1 的豁免集合与探针 §3.7 能看出来，而 Host 的栈常被 40 帧上限截断。
  - **请求体预缓冲的等价性只能部分测试**：分块传输、超限、非 JSON、客户端中途放弃，组合多，B1 只覆盖三种。
  - **登录两段式改变了「谁看到哪个口令记录」**：第二段的复核、重试一次的取舍（§3.3），全量测不出它们在竞态下错了。
  - 两条冲突用例的意图判断，以及预热清单是否完整，需要人来判断。
- Task-specific authorization：
  - 探针复跑（Required check 9）、首次加载探针（§5.3）与变异核对（Required check 6）期间，允许临时改动 `Server/src`。用后按三项还原证明处理；不提交，不 `git stash`。
  - 允许为 Required check 11 构建并运行 `Tool/GameplayLoad`。它拉起 Host 子进程，用临时目录与 SQLite；不改工具源码，不施加 `yes` 负载。
  - 只允许删除或改写两类用例：「因本单元的改动而失败或挂起」的用例，以及冲突总清单分给本单元的 2 条。每条都在 Result 中列出。
  - §8.2 中属于其他单元的冲突用例（`AuthenticationConsumptionTests.ExactCredentialBinds…`、`LoginSessionQueuedCommandTests.ReplacedLogin…`），只因编译需要换驱动调用。断言不变，不算处置，仍归 T-004c。

## Detailed Plan

### 1. 今天的形状与本单元的切法

#### 1.1 GM 路由逐条归类（基准 `2d8cec4d`）

经总线的 40 条（每次请求一条 GM CMD）：

| 文件 | 路由 | 条数 | 方式 |
|---|---|---:|---|
| `GmAuthenticationEndpoints` | `POST auth/login` | 1 | 显式两段（§3.4），标 `EnqueuesItself` |
| 同上 | `POST auth/logout`、`GET audit` | 2 | 过滤器 |
| `GmCeraEndpoints` | `GET accounts`、`GET operations`、`GET operations/{id}`、`POST preview`、`POST grant` | 5 | 过滤器；`grant` 内的 `RemindAsync` 随之进同一条 CMD |
| `GmDailyRefillEndpoints` | 4 条 | 4 | 过滤器 |
| `GmEventsEndpoints` | 7 条 | 7 | 过滤器 |
| `GmInitializationEndpoints` | 5 条 | 5 | 过滤器 |
| `GmOfficialMailEndpoints` | `GET draft`、`GET templates`、`GET recipients`、`GET operations`、`GET operations/{id}`、`POST preview`、`PUT draft`、`POST templates`、`PUT templates/{id}`、`DELETE templates/{id}`、`POST send` | 11 | 过滤器；`send` 的准备、发送、失败审计在同一条 CMD 内 |
| `GmPlayerDirectoryEndpoints` | `GET overview`、`GET accounts`、`GET characters`、`GET accounts/{accountId}`、`GET characters/{characterId}` | 5 | 过滤器 |

加法：1 ＋ 2 ＋ 5 ＋ 4 ＋ 7 ＋ 5 ＋ 11 ＋ 5 ＝ 40，其中过滤器 39 条、显式 1 条。

不经总线的 11 条（标 `OutsideGameplay(理由)`，列入附录 B）：

| 路由 | 做什么 | 附录 B 行 |
|---|---|---|
| `GET /api/gm/v1/health` | 常量 | 不碰玩法状态 |
| `GET /gm/{**path}` | 静态文件 | 不碰玩法状态 |
| `GET auth/status` | `IsInitializedAsync`：只读 SQLite | 只读 SQLite 的查询 |
| `GET auth/csrf` | 防伪令牌 | 不碰玩法状态 |
| `GET auth/session` | 读中间件放进 `HttpContext.Items` 的身份 | 不碰玩法状态 |
| `GET server-info` | 配置与 PVF 摘要 | PVF 内容源 |
| `GET items/icon-pack`、`GET items/{id}/icon` | `GmIconPackStore` | GM 自身的内存状态 |
| `GET items/{id}/tooltip` | `IItemTooltipSource` | PVF 内容源 |
| `GET mail/items`、`GET mail/items/{itemId}` | 身份识别 ＋ 物品目录：只读 | 只读 SQLite 的查询（附录 B 原文举例「GM 邮件物品查询」） |

加法：1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 2 ＋ 1 ＋ 2 ＝ 11。总计 40 ＋ 11 ＝ 51，与 `grep Map(Get|Post|Put|Delete|Methods)` 加上 `Get(`／`Edit(` 辅助的展开数相等（48 行，其中 3 行各含两条路由：48 ＋ 3 ＝ 51）。

**经总线的只读查询也一律进执行器**。附录 B 允许「只读 SQLite 的查询」留外，但这条只用于上表 11 条。理由有三：
- 查询类命令有 14 种注册成 `Transactional`，会开写事务。按 `ApplicationModule` 逐行数：玩家目录 5（`QueryGmAccounts`、`QueryGmCharacters`、`FindGmAccount`、`FindGmCharacter`、`QueryGmOverview`）、`QueryGmCera` 1、`QueryGmMail` 1、`QueryGmAudit` 1、每日补给 3（`Query`、`Search…Items`、`Read…Item`）、初始化 3（同形），5 ＋ 1 ＋ 1 ＋ 1 ＋ 3 ＋ 3 ＝ 14；
- 玩家目录与总览读在线身份和在线运行态（`GmPlayerSamplingHttpTests:41` 正是在位置读取中途注入断连）；其余查询是否读内存玩法状态，没有逐条核对，按本规则不需要核对；
- T-004c 翻转之后，`SendAsync` 在执行器外本来就会入队。
- 「凡经总线即入执行器」是一条不需要逐条判断的规则。

#### 1.2 启动器路由

| 路由 | 今天 | 本单元之后 |
|---|---|---|
| `GET bootstrap` | 读 `HttpApiReadiness` | 留外，标 `OutsideGameplay` |
| `POST register` | 读体 → `RegisterAccountCommand`（命令内校验并哈希，再写库） | 读体 → reader 校验并哈希（限流器）→ `http:POST /api/launcher/v1/register` CMD 写库（§3.2） |
| `POST login` | 读体 → `AuthenticateAndIssueTicketCommand`（命令内查账号、校验口令、可能重哈希、签发会话） | 读体 → reader 查账号（只读）并校验口令（限流器）→ `http:POST /api/launcher/v1/login` CMD 复核记录、写库、签发会话（§3.3） |

#### 1.3 启动期

| 服务 | 今天 | 本单元之后 |
|---|---|---|
| `GameServerStartupService.StartingAsync` | 内容初始化；`EpicBook.InitializeAsync`（自开事务迁移）；经验存储策略 `InstallAsync`（直连 SQLite 改数据、装触发器） | 内容初始化留外；两处写库各一个 `startup:` 作业（§4）；加入预热清单（§5） |
| `GmBootstrapService.StartAsync` | 经总线发 `BootstrapGmAdministratorCommand`，命令内哈希 | 外部准备口令记录，再入队 `startup:GmBootstrapService` 作业 |
| `MarriageRelationAudit.StartAsync` | 只读 SQLite 计数 | 留外，列入附录 B |
| `ChannelScriptStartupService.StartAsync` | 加载频道脚本文件 | 留外，列入附录 B |
| `HttpApiStartupService` | 设置就绪标志 | 不迁（TASK-11 §1 已定） |
| 建表与迁移（Persistence 单例构造时直连 SQLite） | 不经工作单元入口 | 不改，附录 B 原有一项 |

### 2. HTTP 入队的共同形状

#### 2.1 入队辅助

```
// Dfo.Server.HttpApi/HttpGameplay.cs
public static Task<T> RunAsync<T>(HttpContext context, GameplayCallSource source, string commandType,
    Func<CancellationToken, ValueTask<T>> body)
  => queue.RunAsync(new GameplayWork<T>(GameplayShardKey.World, GameplayCallChain.NewRoot(source),
         commandType, body), context.RequestAborted).AsTask();
```

- **以 `RequestAborted` 入队**，与定时生产者以 `None` 入队不同。理由：
  - 请求方在 CMD 开始之前离开时，CMD 不再执行，这时它还什么都没做；
  - 请求方在 CMD 执行中离开时，作业体经同一个令牌抛出取消，执行器按「被调用方取消」记 Debug，不记 Error；
  - 停机时 Kestrel 先停止接受，在途请求的 CMD 在执行器停止前执行（RQ-009，TASK-10 §6 的次序），不需要 `None` 那条保证。
- **与今天的差异**：今天请求在总线门上排队时被放弃，`mail/send`、`cera/grant` 的 catch 会记一条失败审计。从此，在执行器队列里被放弃的请求不留这条审计。没有任何东西被提交，客户端也已离开；这一条登记进 Result 的可见差异，交主控方确认（待裁定第 7 条）。
- 来源 `Gm` 或 `Http`，一律新根（RQ-050：GM、HTTP 是新根，T-007 不需要改这里）。
- **CMD 类型**：`gm:<METHOD> <路由模板原文>`、`http:<METHOD> <路由模板原文>`。路由模板取自端点元数据 `RouteEndpoint.RoutePattern.RawText`，例如 `gm:POST /api/gm/v1/mail/send`。一条路由对应一个 CMD 类型，对账可以直接 grep。

#### 2.2 端点过滤器（GM 的 39 条）

- `GmWebModule.MapEndpoints` 给路由组挂 `AddEndpointFilterFactory(HttpGameplay.FilterFactory(GameplayCallSource.Gm, bodyLimit: 32768))`。工厂在构建期看端点元数据：
  - 有 `OutsideGameplay` 或 `EnqueuesItself` 的，原样返回 `next`，不包；
  - 其余的，把 `next(context)` 包进一条 CMD。
- **默认进执行器，豁免必须写理由**：新加的 GM 路由不写豁免就自动成为 CMD，符合 RQ-010 的安全方向。R1 钉住豁免集合，加豁免必须同时改 R1 与附录 B（RQ-011）。
- **请求体在执行器外读入内存**：CMD 内不能等网络（RQ-003、RQ-008）。今天的委托在 `Read` 里 `await ReadFromJsonAsync`，一个慢慢上传请求体的客户端会把执行器占到 Kestrel 的最低速率超时为止。过滤器在入队之前预缓冲，规则如下：
  1. 不是 JSON（`!HasJsonContentType()`），或 `ContentLength > bodyLimit`：不读，直接入队。委托会按今天的检查拒绝，而那条检查不读体。
  2. 否则：
     - 若 `IHttpMaxRequestBodySizeFeature` 可写，设为 `bodyLimit`；
     - `EnableBuffering(bufferThreshold: bodyLimit, bufferLimit: bodyLimit)`，阈值等于上限，只用内存、不落临时文件；
     - `await Body.DrainAsync(RequestAborted)`，`Body.Position = 0`，再入队。
  3. 预缓冲抛 `BadHttpRequestException`（分块传输超限等）：
     - 把这个异常对象记下，把 `Request.Body` 换成一个读时抛出该异常的流，然后照常入队；
     - 委托在 CMD 内读体时得到同一个异常，按今天的映射回 400。过滤器自己不产生响应。
  - 委托里「`IsReadOnly: false` 才设上限」的写法，在体已读之后自然跳过，不需要改。
- **委托体逐字不动**：`Safe` 的映射、`try/catch`、失败审计、`Results.Ok(...)`、写 Cookie，都在 CMD 内。
  - 委托返回 `IResult` 之后，ASP.NET 在过滤器管线之外执行它（序列化、写响应），所以写响应在执行器外。
  - 前提是结果对象里没有「序列化时才遍历玩法状态」的惰性枚举。探针 §3.7 归零同时验证这一点：如果有，序列化时的访问会以中间件帧为入口重新出现在 (A) 中。
- **异常**：
  - 委托里没被 `Safe` 接住的异常（`logout`、`audit`）离开作业，执行器记一条 `Gameplay gm:… failed`（Error）后原样交回，`HttpApiExceptionMiddleware` 照旧回 500；
  - 多出的那条执行器 Error 属于双日志，交 T-008。

#### 2.3 显式入队（3 条凭据路由）

启动器注册、启动器登录、GM 登录都不能整体包进 CMD，因为口令哈希必须在外面做。三者都写成「读体 → reader（外）→ `HttpGameplay.RunAsync`（内）→ 映射结果」，并标 `EnqueuesItself`，过滤器不会再包一层。

### 3. 口令哈希移到执行器外

#### 3.1 限流器

`Dfo.Server.Application/Authentication/PasswordComputationLimiter.cs`，internal sealed，单例：

```
internal sealed class PasswordComputationLimiter(int capacity)
{
    public PasswordComputationLimiter() : this(Environment.ProcessorCount) { }
    internal int Capacity { get; }
    public async ValueTask<T> RunAsync<T>(Func<T> compute, CancellationToken ct)
    {
        GameplayJob.AssertNotOnExecutor("Password hashing");   // 在作业里调用即程序错误（AC-005 的判据）
        await slots.WaitAsync(ct);                              // SemaphoreSlim：异步等待者按 FIFO 排队，不拒绝
        try { return compute(); } finally { slots.Release(); }
    }
}
```

- 上限按处理器数（Plan 共享决定「重计算」）。测试经 internal 构造器指定上限。
- 计算就在调用线程上做（Kestrel 的线程池线程），不另开 `Task.Run`：今天也在请求线程上做，只是被全局门串行了。
- 只有调用方放弃（`RequestAborted`）时，等待才以取消结束；没有超时，也没有队列上限。这就是 RQ-007 的「排队而不是拒绝」。
- **槽位在入队 CMD 之前释放**：reader 返回时哈希已经做完，入队等待期间不持槽位（§10 的不变量）。
- 它是新的并发原语，不是玩法锁：执行器上的作业从不等它（`AssertNotOnExecutor`）。登记进附录 B「口令哈希等纯计算」一行；附录 F 的「保留」栏是否也要列，交主控方（待裁定第 6 条）。

#### 3.2 启动器注册

- `ILauncherCredentialReader.PrepareRegistrationAsync(loginId, password, ct)`：
  - 先用 `AuthenticationRuleService.Validate(registration: true)` 校验，不通过就返回字段错误，不哈希；
  - 通过则在限流器里 `Hash`，返回 `PreparedRegistration(LoginId, PasswordRecord)`。
- `RegisterAccountCommand(PreparedRegistration)` → `IAuthenticationDomainService.RegisterAsync(PreparedRegistration, ct)`：`TryCreateAsync(loginId, record)` → 初始化 → 结果。领域服务不再校验口令，也不再哈希。
- 端点：字段错误直接回 400，不入队；否则入队，命令结果的映射照旧。`InvalidRequest` 分支保留，供替身命令使用（`HttpApiPipelineTests` 的 Theory 依赖它）。
- 与今天的差异：重复账号也会先付一次哈希。今天是先哈希、再 `TryCreate` 才发现重复，所以没有差异。

#### 3.3 启动器登录

- `ILauncherCredentialReader.VerifyLoginAsync(loginId, password, ct)`，在执行器外：
  1. `Validate(registration: false)`，不通过返回 `InvalidRequest`；
  2. `persistence.FindAsync(loginId)`：只读查询，不开写事务，属于附录 B「只读 SQLite 的查询」；
  3. 查不到：在限流器里 `VerifyDummy`，返回 `InvalidCredentials`，不入队；
  4. 在限流器里 `Verify(record, password)`；失败且 `MayMigrate` 时，用 `VerifyLegacyMd5` 核对旧 MD5 记录。仍失败返回 `InvalidCredentials`；
  5. 需要迁移或重哈希时，在限流器里算出新记录；
  6. 返回 `VerifiedLogin(LoginId, AccountId, VerifiedRecord, ReplacementRecord?)`。
- `AuthenticateAndIssueTicketCommand(VerifiedLogin)` → `AuthenticateAndIssueTicketAsync(VerifiedLogin, ct)`，在 CMD 内：
  1. `AcquireWriteIntentAsync`，再次 `FindAsync`；
  2. 账号不在、账号 ID 不同、或 `PasswordHash != VerifiedRecord`：返回 `CredentialChanged`；
  3. 有 `ReplacementRecord` 时 `TryReplaceCredentialAsync(id, expected: VerifiedRecord, replacement)`，失败也返回 `CredentialChanged`。这个调用今天已经是比较后替换；
  4. `sessions.Issue(...)`：`LoginSessionRegistry` 属于附录 F「改为执行器内访问」，只在 CMD 内发生。
- **`CredentialChanged` 的处理：端点重做第一段一次**。
  - 今天的「写意向 → 读 → 校验」全在一个写事务里，看到的永远是最新记录。比如同一账号的两次登录同时触发旧 MD5 迁移，第二次会对着第一次写下的新记录校验成功。
  - 分两段之后，第二段看到的记录可能已被换掉。重做第一段一次，就能对着新记录校验，结果与今天相同。
  - 第二次仍是 `CredentialChanged` 时回 401。这需要第三方在两次之间又改了口令记录，今天的代码里没有这种写入方。
  - `CredentialChanged` 只在服务端内部流转，HTTP 契约不变。
- 失败的登录（查无账号、口令错）不再进执行器。今天它们在全局门内做一次哑校验或真校验；从此它们只占限流器，不占执行器。暴力尝试因此不会拖慢玩法（RQ-091(d) 的方向）。

#### 3.4 GM 登录

- 端点顺序：
  1. 体检查（不变）；
  2. `GmLoginLimiter.TryBegin`，GM 自身的内存状态，附录 B；
  3. `IGmCredentialReader.CheckLoginAsync(loginName, password)`，在执行器外：`ValidName` → `FindAdministratorAsync`（只读）→ 在限流器里 `Verify`，或 `VerifyDummy` → `GmCredentialCheck(LoginName, AdministratorId?, Record?, Accepted)`；
  4. `HttpGameplay.RunAsync(…, "gm:POST /api/gm/v1/auth/login", …)` 内发 `LoginGmAdministratorCommand(GmCredentialCheck)`；
  5. CMD 外：写 Cookie、`complete(accepted)`、映射结果。
- `LoginAsync(GmCredentialCheck)` 在 CMD 内：
  - `AcquireWriteIntentAsync`，再次 `FindAdministratorAsync`；
  - `accepted = check.Accepted && admin is { Enabled: true } && admin.Id == check.AdministratorId && admin.PasswordRecord == check.Record`；
  - 审计（成功与拒绝都记），这是 GM 登录必须进 CMD 的原因；
  - 成功时写会话。
  - 记录在两段之间变了，就按拒绝处理并记审计。今天没有修改管理员口令的写入方，所以不做重试。
- `GmLoginLimiter` 的 `complete` 仍在 `finally` 里，在 CMD 完成之后调用。

#### 3.5 GM bootstrap

- `IGmCredentialReader.PrepareBootstrapAsync(password, ct)`：口令长度不在 12～256 时返回 `null`，否则在限流器里 `Hash`。
- `BootstrapGmAdministratorCommand(loginName, passwordRecord?, permissions)` → `BootstrapAsync(loginName, string? passwordRecord, permissions, ct)`：
  - 原有判定的顺序不变：已有管理员 → `false`；名字、权限无效或记录为 `null` → `false`；
  - 只把 `passwords.Hash(password)` 换成传进来的记录。
- 与今天的差异：已有管理员时，启动也会做一次哈希，约 60 ms，在执行器外。

#### 3.6 领域服务不再注入哈希器

- `AuthenticationDomainService`、`GmAccessDomainService` 的构造参数去掉 `IPasswordCredentialRuleService`。
- `PasswordCredentialService.VerifyLegacyMd5` 只由 reader 调用。
- 由此「哈希不在执行器上」有三重证据：
  - 静态：H5 与 Required check 8 的 grep；
  - 运行期：限流器入口的 `AssertNotOnExecutor`，以及 H3；
  - 行为：H1 经真实 HTTP 管线记录每次哈希的执行位置。

### 4. 启动期工作作为作业

- `GameServerStartupService.StartingAsync` 的写库两处，原位改成入队并等待：

```
await queue.RunAsync(new GameplayWork(GameplayShardKey.World, GameplayCallChain.NewRoot(GameplayCallSource.StartupRecovery),
    "startup:GameServerStartupService/epic-book",
    ct => services.GetRequiredService<IEpicBookDomainService>().InitializeAsync(ct)), cancellationToken);
...
await queue.RunAsync(new GameplayWork(..., "startup:GameServerStartupService/experience-storage",
    ct => storagePolicy.InstallAsync(scale, ct)), cancellationToken);
```

- `GmBootstrapService.StartAsync`：
  - 读配置、校验权限的逻辑不变；
  - 外部 `PrepareBootstrapAsync`；
  - 再入队 `startup:GmBootstrapService`，作业体里 `commands.SendAsync(new BootstrapGmAdministratorCommand(name, record, permissions), ct)`。总线在作业里调用，按过渡规则内联执行。
- **直接用 `IGameplayQueue`，不用 `GameplayTimerWork`**：
  - GameServer 的项目引用表是 Abstractions、Infrastructure、Networking、Protocol，不含 Application；
  - 启动期以宿主的启动令牌入队就够了：启动被取消时作业不再执行，与今天「启动被取消就不做」一致。
- **来源 `StartupRecovery`**：调用链的来源种类里，只有它表示启动期。CMD 类型沿用 TASK-08 拆分原则第 4 条的 `startup:<名字>`，与 TASK-11 的 `startup:JoustMaintenanceService` 同形。
- **次序**：
  - 执行器在第一次入队时启动，不依赖托管服务的注册顺序（Plan「执行器的生命周期」）；
  - `StartingAsync` 在任何 `StartAsync` 之前完成，所以两个作业都在 Kestrel、TCP 监听、事件组循环启动之前执行完；
  - GM bootstrap 在 `StartAsync` 阶段，与其他服务的启动先后与今天相同。
- **RQ-061 的启动期例外**：本单元之后，附录 B 末行四项中，迁移与安装两项、GM bootstrap 都已在执行器上；建表仍不经工作单元入口。例外是否删去，由 T-004c 判定，Result 只登记现状。

### 5. 启动预热

#### 5.1 原则

- 预热放在 `GameServerStartupService.StartingAsync` 的 `if (archive is not null)` 分支里，与已有的内容初始化并列，在开始监听之前完成。
- 预热经规则服务进行（AGENTS.md：内容源只由规则服务注入）。已有初始化入口的，直接调用；没有的，按 `IWorldDropRuleService.Initialize` 的形状在该规则服务上加 `int Initialize()`，返回加载条数并记一条 Information，与相邻各项一致。
- 预热在执行器外（附录 B「PVF 内容源与缓存」）。预热完成之后，执行器上不应再发生这些源的首次加载；首次加载探针（§5.3）验证这一点。

#### 5.2 清单（基准上的静态检索：Infrastructure 与 Application 中含 `Lazy<` 的行共 36 行，分布在 22 个文件）

已经预热、本单元只登记进附录 B 的：装备索引、领主塔定义、独立掉落（`IndependentDropRuleService`）、额外掉落（`ExtraDropRuleService` 与 `PvfExtraDropContentSource`）、怪物声明掉落、世界掉落、次元副本（`DimensionDungeonRuleService` 与内容源）、角色基础属性、史诗书目录与配方、自动售货机、彩票、经验刻度。

本单元加入预热的：

| 内容 | 拥有者（规则服务） | 执行器上首次触达的位置 |
|---|---|---|
| 成就目录 `PvfAchievementDefinitionSource` | `AchievementRuleService` | 事件 CMD：`achievement.*` 订阅（TASK-12 Remaining 第 5 条） |
| 称号簿目录 `PvfTitleBookDefinitionSource` | `AchievementRuleService` | 事件 CMD：`title.unlock`；选角时的称号投影 |
| 技能规则（按职业，`CharacterSkillRuleService.jobs` 与 `PvfSkillPointDefinitionSource.catalogs`） | `CharacterSkillRuleService` | 选角准备（技能页、SP／TP 账本）；预热**全部受支持职业**，职业集合由 `CharacterCareer` 与 PVF 声明决定 |
| 金币携带上限 `PvfGoldCarryLimitSource` | `CharacterEntryRuleServices` 中的拥有者 | 选角准备 |
| 会员服务的 10 个 `Lazy`（`PremiumServiceRuleService`） | 同名 | 选角（PremiumService 状态同步）、进本加成、自动修理等 |
| 任务助手人偶 `QuestHelperPuppetRuleService` | 同名 | 选角、进本 |
| 地下城经验进度策略 `ExperienceRuleService.policy` | 同名 | 怪物死亡结算、清算经验 |
| 掉落物品池（按 `DropItemFamily`，`PvfDropItemPoolSource._pools`） | `DropRuleService` 等 7 个消费者中的拥有者 | 怪物死亡、清算、翻牌；预热**全部族**（枚举有界） |
| 金银号角 `PvfGoldSilverHornContentSource` | `GoldSilverHornRuleService` | 金银号角进本 |
| 地狱奖励 `PvfHellRewardDefinitionSource` | `HellRewardRuleService` | 地狱副本清算 |
| 守望之塔目录 `PvfSaoTowerContentSource` | `SaoTowerRuleService` | 进塔 |
| 婚姻：胶囊、典礼、住宅、求婚 4 个内容源 | `MarriageCeremonyRuleService`、`MarriageProposalRuleService` 等拥有者 | 婚姻相关入站、婚礼定时作业 |

- **按键、无界的缓存**：按副本的地图、房间角色与掉落（`PvfDungeonDefinitionSource`、`PvfDungeonMapDefinitionSource`、`PvfDungeonRoomActorSource`、`PvfDungeonRoomDropSource`），以及按物品的各类定义。这一类不进本表，处置按待裁定第 10 条：
  - 先由首次加载探针量出它们在冷启动负载中占多少；
  - 再量出全部预热会让启动多花多少时间；
  - 两个数写进 Result，由主控方决定。
- **范围的口径**：`Infrastructure/Content` 里带缓存的内容源共 70 个文件（`grep -il "cache|GetOrAdd"`）。本表只列「整目录 `Lazy`」与「有界按键」两类，以及首次加载探针实际抓到的项；其余的由探针决定是否补入。最终清单以 Result 为准，写入附录 B。

#### 5.3 首次加载探针（临时，不提交）

- **挂点**：`PvfArchiveProvider.Archive` 的 getter，各内容源每次加载都经它取档案对象。
  - 在作业里被调用时（`GameplayJob.IsOnExecutor`），记下 `GameplayJob.Current.CommandType` 与前 40 帧中的 `Dfo.Server.Infrastructure.Content` 帧；
  - 复用 `verification/probe-tools/GameplayProbe.cs` 的记录器与落盘方式。
- **局限**：缓存了档案引用、之后不再经 getter 的内容源看不到。冷启动测量的耗时作为补充证据。
- **两轮**：
  1. 在基准构建上跑 Required check 11 的负载，得到「执行器上的首次加载」原始清单，用来校对 §5.2；
  2. 预热之后再跑一次：§5.2 清单内的应为 0；清单外的逐条列出，按待裁定第 10 条处置。
- 还原证明同探针。

### 6. 新增测试

| 条 | 位置 | 驱动 | 断言 |
|---|---|---|---|
| H1 | Host.Tests（`GmTestHost` 开启 LauncherAuth；不行就仿 `CharacterizationHost`） | 真实 HTTP：启动器注册、登录，GM 登录，宿主启动时的 GM bootstrap | 装饰 `IPasswordCredentialRuleService`，记录每次 `Hash`／`Verify`／`VerifyDummy` 的 `IsOnExecutor`，全部为假。装饰 `IAuthenticationPersistence.TryCreateAsync`、`IGmAdministrationPersistence.SaveSessionAsync`，记录 `IsOnExecutor` 为真，`Current` 的来源与 CMD 类型分别为 `Http`／`http:POST /api/launcher/v1/register`、`Gm`／`gm:POST /api/gm/v1/auth/login` |
| H2 | Application.Tests | 上限 2 的限流器 ＋ 阻塞型口令替身；5 个并发 `VerifyLoginAsync` | 替身内同时在途的峰值等于 2；另 3 个在等，没有失败；此时入队一个标记作业，1 秒内完成（哈希不占执行器）；放行后 5 个全部 `Verified` |
| H3 | Application.Tests | 在容器作业里调用限流器 | 以 `GameplaySelfWaitException` 失败，作业被标为程序错误（AC-005 判据的反例） |
| H4 | Application.Tests | 解析默认注册 | `Capacity == Environment.ProcessorCount`；与两个 reader 拿到的是同一个实例 |
| H5 | Application.Tests | 反射：Application 程序集中实现 `*DomainService` 接口或 `ICommandHandler<,>` 的类型 | 构造参数里没有 `IPasswordCredentialRuleService`，也没有 `PasswordComputationLimiter`。另一条用例喂一个违规类型，断言被拒（RQ-072 式） |
| L1 | Persistence.Tests | reader 第一段之后、第二段之前，用 `Seed`／SQL 换掉口令记录 | 第二段返回 `CredentialChanged`。端点级（HttpApi 管线）：重做一次后成功；两次都变则 401。不签发会话 |
| L2 | Host.Tests | GM 登录第一段之后停用管理员 | 回 401，审计记一条 `Rejected`；没有写会话 |
| E1 | Host.Tests（`GmTestHost`） | 真实 HTTP：`mail/send`、`cera/grant`（有在线账号）、`characters?onlineOnly=true`、`auth/logout` | 下游装饰（邮件发送、点券发放、提醒、玩家目录持久化、审计）记录 `IsOnExecutor` 与 `Current`：每次请求的所有记录同一个 `Current.Id`，来源 `Gm`，CMD 类型为该路由的 `gm:…`。`send` 的准备、发送与失败审计，`grant` 的发放与提醒，各是同一个 ID |
| E2 | Host.Tests | 真实 HTTP：启动器注册与登录 | 与 H1 同一个用例时合并，计数按方法数算 |
| R1 | Host.Tests（`GmTestHost`，开启 LauncherAuth） | 枚举 `EndpointDataSource` 中全部 GM 与启动器路由 | 每条要么是 CMD（过滤器包住，或标 `EnqueuesItself`），要么标 `OutsideGameplay` 且理由非空；豁免集合恰好等于 §1.1 的 11 条加启动器 `bootstrap`；CMD 类型互不相同。反例：一条既没被包、又没标豁免的路由，被判据拒绝 |
| B1 | Host.Tests | 三种请求体：非 JSON、`Content-Length` 超过 32 KiB、分块传输超过 32 KiB | 状态码与 `message` 和改动前相同。先在基准上跑一次，把输出写进 `logs/task-14-step0.txt` 作为期望值 |
| B2 | Host.Tests | 原始 socket 只发出请求头与一半请求体，然后停住 | 此时入队的标记作业 1 秒内完成（执行器不等请求体）。补发另一半后请求成功。V2 会让它变红 |
| S1 | Host.Tests（`realContent: true`，无 PVF 时跳过） | 宿主启动 | 装饰 `IEpicBookDomainService.InitializeAsync`、`ICharacterExperienceStoragePolicy.InstallAsync`、`IGmAccessDomainService.BootstrapAsync`，记录 `IsOnExecutor` 为真，来源 `StartupRecovery`，CMD 类型分别为 `startup:GameServerStartupService/epic-book`、`/experience-storage`、`startup:GmBootstrapService`；bootstrap 的哈希不在执行器上 |
| W1 | Host.Tests 或 Infrastructure.Tests（真实 PVF，无 PVF 时跳过） | 跑完 `StartingAsync` | 对 §5.2「本单元加入」表的每一项，反射取其 `Lazy` 字段，`IsValueCreated` 为真；按职业、按族的缓存，键集合覆盖全部职业与全部族。断言里的清单就是附录 B 的清单，附录 B 引用这个用例 |

- 先写测试，再改生产代码。H1、H2、H3、L1、L2、E1、R1、B2、S1、W1 在改动前应当红（编译失败也算红），Result 记录红的形态。B1 是等价性用例，改动前后都应为绿。
- N 的预计：H 5 ＋ L 2 ＋ E 1 ＋ R 2（含反例）＋ B 2 ＋ S 1 ＋ W 1 ＝ 14，以实际方法数为准。冲突用例的改写不计入 N。

### 7. 冲突用例

#### 7.1 `Host.Tests/GmPlayerSamplingHttpTests.cs:14` `OnlineListUsesOneCapturedSetForTotalAndItemsEvenWhenDisconnectOccursDuringDatabaseRead`

- **机制**：GM 查询被 `BarrierPersistence` 卡在数据库读里。卡住期间，测试用 `TestGameplay.Run(host.Services, …)` 做 `Cleanup` 与断连，然后才放行。
  - 迁入之后，GM 查询在执行器上被卡住，`Cleanup` 的作业排在它后面；
  - 测试线程同步等 `Cleanup`，永远走不到放行。
- **处置：改写，断言一字不改**。把交错从「跨作业的门闩」改为「在同一条 CMD 的读回调里内联执行」，与同文件 `:41` 的写法一致：
  - `BarrierPersistence` 在 armed 时调用注入的回调；回调做 `Cleanup` 与 `TryDisconnectConnection`。`TestGameplay.Run` 发现自己已在作业里，会内联执行；
  - 删掉 `Entered`／`Release` 两个 TCS；第 34～37 行的断言不变。
- **保护意图不变**：在线集合在查询开始时捕获一次；查询中途状态变化，也不改变本页的总数与条目。
- 旧断言 → 新断言 → 保护意图，写进 `task-07-conflicts.md` §11。

#### 7.2 `Persistence.Tests/Accounts/AuthenticationConcurrencyTests.cs:10` `OverlappingPasswordReadsCannotLeaveTheSecondLoginWithAnUnwritableSqliteSnapshot`

- **机制**：两条登录经总线并发。口令替身在 `Verify` 里用 `ManualResetEventSlim` 卡住第一条，等第二条读到快照。今天两次校验都在写事务里。
- **本单元之后**：校验在第一段，在任何事务之外，由限流器调度。
- **处置：改写，断言一字不改**。
  - 两次登录改经 `fixture.LoginAsync(…)`，即 reader 加总线，`InterleavedPasswords` 照旧在 `Verify` 里交错；
  - 第 36～40 行断言不变：两次 `Success`、票据不同、都能消费；
  - 第 29～30 行的注释改为说明新结构：两次校验都在执行器外、在任何 SQLite 快照之外重叠，提交在执行器上串行。
- 限流器上限等于处理器数。机器只有 1 个核时，第二次校验在限流器上等，`Task.WhenAny(…, Delay(500))` 照旧放行第一条，用例仍然成立。
- **保护意图的变化**：「重叠的口令校验不会让第二次登录失败」保留。「第二次登录不会拿着不可写的 SQLite 快照」这一前提不再存在，因为校验不再持有快照。Result 记为结构变化导致的意图收窄，与冲突清单中的处置决定「按新的先哈希、后单条 CMD 结构重写」一致。

### 8. 既有测试

#### 8.1 处置分类

沿用 TASK-11 §9 的 (i)～(vi)，本单元特有的处置如下：

- **(i) 注册后果**：
  - `HttpApiPipelineTests` 的 `ApiHarness` 没有执行器，也没有 reader。加 `AddGameplayExecution()` 与一个 reader 替身：替身对注册返回带假记录的 `Prepared`，对登录返回 `Verified`；
  - Theory 的数据与断言不变。
- **(iii) 包裹区域里发 HTTP 请求**：在 `TestGameplay.Run(host.Services, …)` 区域里经 `HttpClient` 调用 GM 路由，会互等：作业等响应，响应的 CMD 排在作业后面。
  - 不报自我等待，因为 Kestrel 的请求流不继承测试的作业令牌，表现为挂起；
  - 处置：把 HTTP 调用移出区域。
  - 静态初筛：Host.Tests 中用 `TestGameplay` 的 4 个文件（`GmPlayerDirectoryHttpTests` 15 处、`GmOfficialMailHttpTests` 4 处、`GmCeraHttpTests` 1 处、`GmPlayerBrowserFixtureTests` 1 处）逐处确认。
- **(iv) 作业里等待 GM 效果**：同上，解开包裹。
- **(v) 新的交错型冲突**按 (ii) 判定，追加到 `task-07-conflicts.md` §11，并更新总数的加法。
- **(vi) 依赖「GM 请求与入站并发」的断言**：GM 请求从此与入站、事件、定时串行。先确认是否只是轮询窗口问题；断言本身依赖并发的，判为 (ii)，停下交主控方。

#### 8.2 只因签名改变而改驱动调用的 15 个文件

`new RegisterAccountCommand` 等 4 种命令的构造（基准 `2d8cec4d`），按文件计：

- Persistence.Tests：`AuthenticationConcurrencyTests` 5（冲突用例，§7.2）、`AuthenticationConsumptionTests` 5、`AuthenticationFailureTests` 8、`AuthenticationFixture` 2、`AuthenticationMigrationTests` 5、`AuthenticationRegistrationTests` 2、`AuthenticationTicketTests` 9、`LoginSessionQueuedCommandTests` 3、`LoginSessionReconnectTests` 11；
- Networking.Tests：`GameLoginIntegrationTests` 9、`BossTowerFrameIntegrationTests` 2、`LifecycleExecutorWireTests` 3；
- Application.Tests：`GameLoginCommandTransactionTests` 2；
- Host.Tests：`GmInitializationHttpTests` 4、`GmPlayerDirectoryHttpTests` 2。

加法：
- 文件数：9 ＋ 3 ＋ 1 ＋ 2 ＝ 15；
- 处数：5 ＋ 5 ＋ 8 ＋ 2 ＋ 5 ＋ 2 ＋ 9 ＋ 3 ＋ 11 ＋ 9 ＋ 2 ＋ 3 ＋ 2 ＋ 4 ＋ 2 ＝ 72。

做法：
- `fixture.Bus.SendAsync(new RegisterAccountCommand(a, b))` → `fixture.RegisterAsync(a, b)`；
- `…new AuthenticateAndIssueTicketCommand(a, b)…` → `TestCredentials.LoginAsync(services, a, b)`（Persistence 用 fixture 上的同名方法）。
- `AuthenticationFixture` 的构造参数 `IPasswordCredentialRuleService? passwords` 保留，替身经 reader 生效。
- 直接调用 `IAuthenticationDomainService` 的测试（`AuthenticationFailureTests`、`AuthenticationMigrationTests` 等）改为传入 reader 的结果。
- 替换表随 Result 给出，差异核对按 Required check 7 规范化后比较。
- 测试辅助在测试线程上执行，即执行器外。它代表生产的组合「reader → 命令」，不是新的入口；入口证据由 H1、E2 经真实 HTTP 取得。

#### 8.3 主动复核（即使全绿，也逐项写结论）

- **TASK-12 §5.1 的执行器外准入门持有者「未迁入的 GM/HTTP」**：本单元之后，生产中不再有。用 Required check 8 的总线调用方清单确认。
- **T-004c 冲突** `AuthenticationConsumptionTests.ExactCredentialBinds…`、`LoginSessionQueuedCommandTests.ReplacedLogin…`：只换驱动调用，断言不变；确认仍通过，留给 T-004c。
- **拉起整个宿主的 Host、Networking 用例**（表征测试、`GameLoginIntegrationTests`、`LauncherAuthenticationEndToEndTests`）：启动器登录变为两段，确认没有 (vi)。
- **TASK-08 §4 复核表中的 `AuthenticationFixture.Consume`**：它仍在容器作业里，竞争方不变，确认照常通过。

### 9. 附录 B 的追加（`spec.md`）

按 TASK-09 的先例，只在单元格末尾追加「**清单（TASK-14，基准 `2d8cec4d`）**」，不改原文：

1. 「GM 自身的内存状态」：`GmLoginLimiter`、`GmIconPackStore`，以及图标两条路由。
2. 「只读 SQLite 的查询」：
   - `GmAccessBoundary` 的身份识别、`auth/status`、邮件物品两条；
   - 启动器登录第一段的账号查询、GM 登录第一段的管理员查询；
   - `MarriageRelationAudit`。
   - 另注明：经总线的只读查询不在此列（§1.1）。
3. 「口令哈希等纯计算」：`PasswordComputationLimiter`，上限为处理器数，排队不拒绝；在执行器上调用即程序错误。
4. 「启动预热」：§5 的最终清单，引用 W1。
5. 「开始监听前的启动期工作」：
   - 已作为 `startup:` 作业运行的三项；
   - 仍在外面的：建表（Persistence 单例构造时直连 SQLite，不经工作单元入口）、频道脚本加载、内容初始化；
   - 注明 RQ-061 的例外取舍由 T-004c 判定。
- 另在「不碰玩法状态」类追加：`health`、页面、`csrf`、`session`、`server-info`、`tooltip`、启动器 `bootstrap`。附录 B 没有现成的行，就并入「PVF 内容源」行与「GM 自身的内存状态」行，逐条注明归入哪一行。待裁定第 12 条。

### 10. 部分迁移期间的不变量（Required check 8 的清单）

等待新迁入 CMD 的执行器外代码：

| 代码 | 等待时持有什么 |
|---|---|
| Kestrel 请求线程：过滤器、启动器两条、GM 登录 | 不持门。限流器槽位在 reader 返回前释放；`GmLoginLimiter` 只是计数，不是等待原语 |
| `GmBootstrapService.StartAsync` | 无 |
| `GameServerStartupService.StartingAsync` | 无 |
| 测试辅助 `TestCredentials`／`fixture.LoginAsync` | 无 |

- 反向情形：执行器上的 GM／HTTP CMD 等执行器外持有的门。生产中，本单元之后的持有者只剩 Required check 8 清单里的非零项（预期为 0）。测试中直接取门的写法属于 T-004c。
- 执行器上的作业从不等限流器（`AssertNotOnExecutor`），所以不成环。

### 11. 风险与可见差异

1. **GM 请求与玩法串行**：一个慢的 GM 查询（例如 100 条一页的角色列表）会占住执行器。由 M-1 按 `Gameplay gm:… completed` 的 Debug 耗时观察（RQ-091）。
2. **直发 2 秒期限扩大到 GM**：Cera 提醒在作业里直发，不读包的客户端会在 2 秒后被断开。VC-8 提前的范围扩到 GM，T-006 之后消失。
3. **队列中被放弃的 GM 请求不再留失败审计**（§2.1）：待裁定第 7 条。
4. **登录的两段式**：两段之间口令记录被换掉时重做一次（§3.3）。GM 登录在这种情况下直接拒绝（§3.4）。
5. **启动多一次哈希**（§3.5）与预热增加的启动时长（§5），都在开始监听之前；Required check 11 记录启动时长。
6. **双 Error 日志**：未被 `Safe` 接住的 GM 异常（§2.2），交 T-008。
7. **TASK-12 的「事件作业早于预热」**：`StartingAsync` 在所有 `StartAsync` 之前完成，事件组循环在 `StartAsync` 才开始，所以预热一定先于事件作业。TASK-12 的担心在于清单里还没有成就、称号目录，本单元补上。

### 12. 执行顺序（三批，一次提交；每批结束都要构建，并跑受影响的项目）

1. **第 0 步**：在基准上跑 B1 的三种请求、`AuthenticationConcurrencyTests`、`GmPlayerSamplingHttpTests`，记基线（`logs/task-14-step0.txt`）。同时跑首次加载探针第 1 轮与一次冷启动测量，作为 §5 的输入。
2. **批 A：口令哈希与凭据路由**
   - 写测试：H1～H5、L1、L2、E2，记录红的形态（`logs/task-14-red.txt`）；
   - 改生产代码：Data 记录 → 接口 → 限流器与两个 reader → 两个领域服务与四条命令 → 启动器两条与 GM 登录 → GM bootstrap 的准备；
   - 测试侧：`TestCredentials`、15 个文件、`ApiHarness`、§7.2；
   - 跑 Persistence、Application、HttpApi、Host、Networking。
3. **批 B：GM 路由与启动作业**
   - 写测试：E1、R1、B2、S1（红）；
   - 改生产代码：`HttpGameplay` 的过滤器与元数据 → `GmWebModule` 与豁免标记 → `GameServerStartupService` 的两个作业 → `GmBootstrapService` 入队；
   - 处置 §7.1 与 §8.1 (iii)；
   - 跑 Host、Networking。
4. **批 C：预热**
   - W1（红）→ §5.2 各项的 `Initialize` → `StartingAsync` 中的调用；
   - 首次加载探针第 2 轮与冷启动测量（Required check 11）。
5. 全量，带挂起检测。按 §8.1 逐条分类，重跑到稳定；做耗时比对。
6. 变异核对 → 探针复跑与归零分析、还原证明 → 对账行 → 附录 B 追加。
7. 正式全量 → 隔离复跑 → 新增测试 ×10 → 测试改动核对 → 边界 grep 与不变量复核 → 写 Result。

### 13. 为什么除可见差异外行为不变

- GM 端点的委托体逐字不动，只是换到执行器上运行；请求体从内存读出，字节相同。
- 凭据路径的每个判定与今天相同，只是分到两段：校验规则、哑校验、旧 MD5 迁移条件、重哈希、比较后替换、会话签发、审计。两段之间记录变化时重做一次，使结果与今天一致。
- 启动期的两处写库与 GM bootstrap，内容与次序不变，只是换到执行器上运行。
- 预热只是把首次加载提前到开始监听之前，内容相同。
- 可见差异见 §11 第 2、3 条。线上字节不变，由表征测试与既有线级测试守着。

## 待裁定

1. **拆分**：本单元的三批彼此独立，证据形态也不同。
   - 批 A 是接口与命令的重构，测试改动 72 处；
   - 批 B 是入口迁入；
   - 批 C 靠测量取证，涉及约 15 个规则服务。
   - 建议**把批 C 拆成 TASK-15（b6c 预热）**，原 b7 对账收口顺延为 TASK-16。批 A、B 保留为本单元，分两批、一次提交。理由：
     - 批 C 不改任何入口，与 A、B 没有文件交集；
     - 它的完成条件「执行器上首次加载为 0」需要冷启动测量反复迭代，混在一起会拖住 A、B 的结果审查；
     - 待裁定第 10 条的结论也要等测量出来。
   - 备选一：三批都留在本单元（本文件的写法）。
   - 备选二：A、B、C 各成一个单元。
   - 若拆，§5、W1、V9、Required check 11 与 §12 第 4 步原样移入新单元，本单元的契约升到 Revision 2。
2. **GM 端点粒度**：
   - 用「端点过滤器 ＋ 请求体预缓冲」，把端点委托整体作为一条 CMD。请求体在执行器外读入内存；3 条凭据路由显式分两段。
   - 备选：逐个改写 40 个委托，把「读体」提到 CMD 之外。代码更直白，但每个委托都要动，`mail/send`、`cera/grant` 的「读体失败不记审计」等分支要逐个重新核对。
   - 请确认本方案，以及 B1 的三种情形足以作为等价性证据。
3. **默认进执行器、豁免必须写理由**（§2.2）：豁免集合为 §1.1 的 11 条加启动器 `bootstrap`，由 R1 钉住。经总线的只读查询一律进执行器（§1.1 的三条理由）。请确认。
4. **CMD 类型与来源**：
   - `gm:<METHOD> <路由模板原文>`、`http:<METHOD> <路由模板原文>`、`startup:<类型名>[/<步骤>]`；
   - 来源 `Gm`／`Http`／`StartupRecovery`，一律新根。
   - 请确认。
5. **哈希结果作为命令输入**：
   - 四条命令的输入改为 reader 的结果；领域服务不再注入哈希器；测试 72 处改经辅助。
   - 备选：给总线加一个「入队前准备」钩子，零测试改动。但它在 `CommandBus` 上新增一个横切机制，T-008 拆总线时还要再拆一次，而且凭据路径会有两条。不推荐。
   - 请确认。
6. **限流器**：`SemaphoreSlim(Environment.ProcessorCount)`，排队不拒绝，入口处 `AssertNotOnExecutor`，登记进附录 B「口令哈希」行。附录 F 的说明是「发现本表以外的并发原语就补入」，它是否还要列进附录 F「保留」栏？
7. **以 `RequestAborted` 入队**（§2.1）：请求方在 CMD 开始前离开时，CMD 不执行，因此 `mail/send`、`cera/grant` 不再记那条失败审计。备选是以 `None` 入队，让委托在请求已放弃时照样运行并记审计。建议本方案：被放弃的请求没有提交任何东西。请确认，并确认这一条按「可见差异」登记，还是不算可见差异（客户端已离开，只差管理审计中的一行）。
8. **登录第二段发现记录已变**：
   - 启动器登录重做第一段一次，再变则回 401。新增的内部状态 `CredentialChanged` 不上线；
   - GM 登录直接拒绝并记审计。
   - 请确认。
9. **启动期作业**：
   - `EpicBook` 迁移、经验存储策略安装、GM bootstrap 作为 `startup:` 作业；
   - 建表、频道脚本、`MarriageRelationAudit`、内容初始化留在外面；
   - RQ-061 的启动期例外留到 T-004c。
   - 请确认。
10. **按键、无界的内容缓存**（按副本、按物品）的预热口径：
    - 本方案先量后定：首次加载探针给出冷启动负载中的占比，再量全部预热会让启动多花多少时间，两个数写进 Result，由主控方决定；
    - 主控方也可以先给一个启动时长的上限，例如 ≤ 10 秒则全部预热，超过就回报。
    - RQ-091 的原文是「全部 `Lazy` 内容源」。按键缓存算不算在内，需要裁定。
11. **两条冲突用例的处置**（§7）：都改写，断言一字不改。`:14` 意图不变；`AuthenticationConcurrencyTests` 的意图收窄，按冲突清单原定的「按新结构重写」执行。请确认。
12. **`spec.md` 附录 B 的追加**（§9）：
    - 按 TASK-09 的先例只追加清单；
    - 「不碰玩法状态」的几条路由，附录 B 没有现成的行，本方案并入相近两行并逐条注明。也可以新增一行，那是改 Spec。
    - 请确认采用哪一种。
13. **附录 A 第 9 行「GM 写操作 34 条」**：按代码逐条数，有 51 条路由，其中 40 条经总线。本单元按 51 条对账，并在 Result 写明「34 的口径不足以确定」。是否顺带修正附录 A 的这个数，由主控方决定，修正属于改 Spec。
14. **编号顺延带来的一条转交**：TASK-11 主控方补充中「交给 TASK-14（审查 N3）」的排空顺序判据（传输先于协调器），写的时候 TASK-14 指 b7 对账收口。按新编号，它属于 TASK-15（若第 1 条采纳拆分，则是 TASK-16），不在本单元。请确认。

## 主控方裁定（执行前）

1. **拆分**：批 C（预热）拆成 **TASK-15**，原 b7（对账收口）顺延为 **TASK-16**。本单元只做批 A、批 B，与预热有关的 Required checks、待裁定第 10 条以及冷启动测量全部移到 TASK-15。
2. **端点粒度**：接受「过滤器 ＋ 预缓冲」，把端点委托整体作为一条 CMD。B1 覆盖的三种请求体情形可以作为等价性证据。
3. **执行器归属**：接受「默认进执行器，加豁免清单」。经总线的只读查询也一律进执行器。豁免集合由 R1 钉住。
4. **CMD 类型命名与来源**：接受，统一为 `gm:`、`http:`、`startup:`，全部是新根。
5. **口令哈希**：接受「哈希结果作为命令输入」。测试中改动的 72 处驱动调用，统一经 `TestCredentials` 处理；断言在规范化之后必须保持不变，规范化用的替换表写进 Result。
6. **限流器**：不列入附录 F。它是 RQ-007 要求的纯计算并发上限，不是玩法路径上的锁，归附录 B「口令哈希等纯计算」那一行，在行内写明限流器的名字。
7. **入队令牌（改用备选方案）**：GM 和 HTTP 的 CMD 以 `CancellationToken.None` 入队，作业体照旧拿 `RequestAborted`。这与 TASK-11、TASK-12 的生产者形状一致。
   - 理由：如果以 `RequestAborted` 入队，请求在排队期间被放弃时，写操作本身和失败审计都会被静默跳过，这是附录 G 之外的可见差异。今天 handler 是立即开始执行的，用 `None` 入队最接近这一语义。
   - 测试：请求在排队期间被放弃后，作业仍然开始执行，作业体能看到已经取消的令牌，行为与今天 handler 在执行中观察到取消时一致。
8. **登录第二段发现口令记录已变**：接受。启动器重做一次，GM 直接拒绝；`CredentialChanged` 是只在内部使用的状态，不上线。
9. **启动期作业的范围**：接受按本文件划定的范围。
10. **无界按键缓存的预热口径**：移到 TASK-15，先测量再定。
11. **两条冲突用例的处置**：接受。断言一字不改；保护意图收窄的那一条，在 Result 中写明「旧前提 → 新前提 → 理由」。
12. **Spec 附录 B**：能并入相近行的就并入。确实需要新类别时，按 RQ-011 新增一行并附上安全理由。RQ-011 本身规定了「新增执行器外部分必须同步更新附录 B」，所以这不算修改需求。
13. **附录 A 第 9 行的「34 条」**：本单元按代码中的 51 条路由对账，并写明「34 的口径不足以确定」。附录 A 的文字在 TASK-16 统一修正。
14. **排空顺序判据**（TASK-11 补充）：归 TASK-16。

## Result

### Outcome

按主控方裁定第 1 条，本单元只做批 A、批 B；预热（§5、W1、V9、Required check 11、§5.3 首次加载探针、冷启动测量）全部移到 TASK-15，本单元没有做。

- **口令哈希移到执行器外**（RQ-007，AC-005 前半）：
  - `PasswordComputationLimiter`（internal，单例）是哈希、校验、哑校验的唯一入口：上限 ＝ 处理器数（本机 10），`SemaphoreSlim` 按到达顺序排队、不拒绝；入口先 `GameplayJob.AssertNotOnExecutor("Password hashing")`，在作业里调用即以 `GameplaySelfWaitException` 失败并标为程序错误。计算在调用线程上做，读回 reader 时槽位已释放。
  - 两个 reader（`ILauncherCredentialReader`、`IGmCredentialReader`）在执行器外做校验规则、只读账号／管理员查询和全部哈希，结果作为命令的输入：注册带 `PreparedRegistration`，启动器登录带 `VerifiedLogin`，GM 登录带 `GmCredentialCheck`，GM bootstrap 带口令记录（可为 null）。
  - 两个领域服务的构造参数去掉了 `IPasswordCredentialRuleService`（`AuthenticationDomainService` 还去掉了 `AuthenticationRuleService`）。H5 守着「领域服务与命令处理器都不注入哈希器和限流器」。
- **登录两段式**（裁定第 8 条）：启动器登录的 CMD 在写意向下重读账号，账号、ID 或记录与第一段不同就返回内部状态 `CredentialChanged`（不签发）；端点据此重做第一段一次，再变则回 401。GM 登录的 CMD 重读管理员，记录、ID 或启用状态变了就按拒绝处理并审计，不重试。`CredentialChanged` 不上线。
- **启动器路由**（附录 A 第 10 行）：注册、登录各是一条 HTTP CMD，`http:POST /api/launcher/v1/register`、`http:POST /api/launcher/v1/login`，来源 `Http`，新根；`bootstrap` 标 `OutsideGameplay`。
- **GM 路由**（附录 A 第 9 行）：51 条中 40 条是 GM CMD（过滤器 39 ＋ 显式 1，即 GM 登录），11 条豁免，与 §1.1 逐条一致；R1 打出全部 54 条（`logs/task-14-routes.txt`）。
  - `HttpGameplay.RunsOnGameplay(GameplayCallSource.Gm, bodyLimit: 32768)` 挂在 GM 路由组上：同时加端点过滤器与标记元数据 `GameplayCmdRoute`；没有 `OutsideGameplay`／`EnqueuesItself` 的路由，整个委托作为一条 CMD 运行。新加的 GM 路由不写豁免就自动成为 CMD（RQ-010、RQ-011）。
  - 请求体在执行器外读入内存：JSON 且未声明超过上限时，设 `MaxRequestBodySize`（可写时）、`EnableBuffering(32768, 32768)`、`DrainAsync`；读体抛出的异常原样换成一个读时重抛的流交给委托，委托照旧映射。B1 的三种形态与改动前逐字相同（`logs/task-14-step0.txt` 记了基线）。
  - 委托体逐字未动；`IResult` 的执行在过滤器管线之外，所以写响应在执行器外。
- **入队令牌**（裁定第 7 条）：GM／HTTP 的 CMD 以 `CancellationToken.None` 入队，作业体拿 `RequestAborted`；作业体经该令牌抛出的取消被带出作业、在 `HttpGameplay.RunAsync` 里重抛，执行器不记失败。Q1 证明排队期间被放弃的请求仍会执行，并且看到已取消的令牌。
- **启动期作业**（附录 B 末行）：`EpicBook` 迁移、经验存储策略安装、GM bootstrap 各是一个启动作业，来源 `StartupRecovery`，CMD 类型 `startup:GameServerStartupService/epic-book`、`startup:GameServerStartupService/experience-storage`、`startup:GmBootstrapService`。前两个以宿主的启动令牌入队（§4）；bootstrap 的哈希在入队前、执行器外完成。
- **冲突用例 2 条**都按裁定第 11 条改写，断言一字不改（`verification/task-07-conflicts.md` §11）。冲突总数仍为 138。
- **表征测试金样本没有变化**：8 通过、2 跳过，`Characterization/` 目录未改，停止条件没有触发。

### Changed

**生产代码**（`git diff --stat -- Server/src`：17 个改动文件 ＋204 −86，另 4 个新文件；全部在 Owned boundary 内，`logs/task-14-boundary.txt`）：

| 文件 | 改动 |
|---|---|
| `Data/Authentication/AuthenticationModels.cs` | `AuthenticationStatus` 加 `CredentialChanged`；新增 `PreparedRegistration`、`RegistrationPreparation`、`VerifiedLogin`、`LoginVerification`（带口令记录的两个 record 覆写 `ToString`，不输出记录） |
| `Data/Administration/GmAdministration.cs` | 新增 `GmCredentialCheck`（`ToString` 只输出 `Accepted`） |
| `Abstractions/Authentication/AuthenticationServices.cs` | 新增 `ILauncherCredentialReader`；`IAuthenticationDomainService.RegisterAsync`／`AuthenticateAndIssueTicketAsync` 改签名 |
| `Abstractions/Administration/GmAdministrationServices.cs` | 新增 `IGmCredentialReader`；`IGmAccessDomainService.BootstrapAsync`／`LoginAsync` 改签名 |
| `Application/Authentication/PasswordComputationLimiter.cs`（新） | 限流器 |
| `Application/Authentication/LauncherCredentialReader.cs`（新） | 校验、查账号、在限流器里校验口令并算出迁移或重哈希的新记录 |
| `Application/Authentication/AuthenticationDomainService.cs` | 注册只写库；登录在写意向下重读并比对，比较后替换，签发 |
| `Application/Authentication/AuthenticationCommands.cs` | 两条命令改为携带 reader 结果（保留只读的 `LoginId` 便捷属性） |
| `Application/Administration/GmCredentialReader.cs`（新） | GM 登录核对、bootstrap 口令准备 |
| `Application/Administration/GmAccessDomainService.cs` | `LoginAsync(GmCredentialCheck)` 重读并比对；`BootstrapAsync` 收口令记录；`ValidName` 改为 internal 供 reader 共用 |
| `Application/Administration/GmAccessCommands.cs` | 两条命令改为携带 reader 结果 |
| `Application/ApplicationModule.cs` | 注册限流器与两个 reader（3 行） |
| `HttpApi/HttpGameplay.cs`（新） | `RunAsync`、`RunsOnGameplay`（过滤器 ＋ 预缓冲）、`CommandTypeOf`、元数据 `OutsideGameplay`／`EnqueuesItself`／`GameplayCmdRoute` |
| `HttpApi/HttpApiApplicationExtensions.cs` | 注册、登录两段式；`bootstrap` 加豁免 |
| `GmWeb/GmWebModule.cs` | 路由组挂 `RunsOnGameplay`；`health` 与页面路由加豁免 |
| `GmWeb/GmAuthenticationEndpoints.cs` | 登录两段式、标 `EnqueuesItself`；`status`、`csrf`、`session` 加豁免 |
| `GmWeb/GmBootstrapService.cs` | 外部准备口令，再入队 `startup:GmBootstrapService` |
| `GmWeb/GmItemEndpoints.cs`、`GmServerInfoEndpoints.cs`、`GmOfficialMailEndpoints.cs` | 只给豁免路由加元数据，委托体未改 |
| `GameServer/GameServerStartupService.cs` | 两处写库改为启动作业 |

没有给任何规则服务加 `Initialize()`（随预热移到 TASK-15）。执行器、总线与管线、中间件、各 GM 领域服务（`GmAccessDomainService` 除外）、`ProtocolModule`、`MarriageRelationAudit`、`ChannelScriptStartupService`、`HttpApiStartupService`、Persistence 都未改。

**新增测试 N ＝ 16 个方法、19 条用例，删除 D ＝ 0**：

| 条 | 方法 | 项目 | 用例数 |
|---|---|---|---:|
| H1＋E2 | `CredentialEntryHttpTests.Password_hashing_runs_off_the_executor_and_credential_writes_run_as_http_and_gm_cmds` | Host | 1 |
| L2 | `CredentialEntryHttpTests.A_gm_administrator_disabled_between_the_credential_check_and_the_login_cmd_is_refused_and_audited` | Host | 1 |
| B1 | `GmGameplayRouteTests.Refused_bodies_keep_their_status_and_message`（Theory） | Host | 3 |
| R1 | `GmGameplayRouteTests.Every_gm_and_launcher_route_is_a_cmd_or_a_listed_exemption` | Host | 1 |
| R1 反例 | `GmGameplayRouteTests.A_route_that_is_neither_a_cmd_nor_an_exemption_with_a_reason_is_refused` | Host | 1 |
| E1 | `GmGameplayRouteTests.Each_gm_request_runs_downstream_as_one_gm_cmd` | Host | 1 |
| B2 | `GmGameplayRouteTests.A_slowly_sent_body_is_read_off_the_executor_before_its_cmd_is_queued` | Host | 1 |
| S1 | `GmGameplayRouteTests.Startup_writes_and_the_gm_bootstrap_run_as_startup_jobs`（PvfFact） | Host | 1 |
| H2 | `PasswordComputationLimiterTests.Hashing_beyond_the_limit_queues_without_failing_and_never_occupies_the_executor` | Application | 1 |
| H3 | `PasswordComputationLimiterTests.Hashing_inside_a_job_fails_at_once_as_a_program_error` | Application | 1 |
| H4 | `PasswordComputationLimiterTests.The_default_limiter_has_one_slot_per_processor_and_both_readers_share_it` | Application | 1 |
| H5 | `PasswordComputationLimiterTests.Domain_services_and_command_handlers_take_neither_the_hasher_nor_the_limiter` | Application | 1 |
| H5 反例 | `PasswordComputationLimiterTests.A_domain_service_that_takes_the_hasher_is_refused` | Application | 1 |
| L1 | `AuthenticationTwoPhaseTests.A_record_replaced_between_the_check_and_the_login_cmd_issues_no_session` | Persistence | 1 |
| L1 端点级 | `HttpApiPipelineTests.LoginChecksTheCredentialAgainOnceWhenItChangedBeforeTheCommand`（Theory：变 1 次 → 200，变 2 次 → 401） | HttpApi | 2 |
| Q1（裁定第 7 条） | `HttpGameplayTests.A_request_abandoned_while_queued_still_runs_and_sees_its_cancelled_token` | HttpApi | 1 |

加法：方法 8 ＋ 5 ＋ 1 ＋ 2 ＝ 16；用例 Host 1 ＋ 1 ＋ 3 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＋ 1 ＝ 10，Application 5，Persistence 1，HttpApi 2 ＋ 1 ＝ 3，10 ＋ 5 ＋ 1 ＋ 3 ＝ 19。计划预计 14 个方法：W1 随预热移走（−1），E2 并入 H1（计划已算 1），多出 H5 反例 1、L1 端点级 1、Q1 1。冲突用例的改写不计入 N。

测试支撑：`tests/Support/TestCredentials.cs`（链接进 Application、Persistence、Networking、Host 四个测试项目，先调 reader 再经总线发命令，登录照端点的做法遇 `CredentialChanged` 重做一次）；`Host.Tests/GameplayCallLog.cs`（`DispatchProxy` 记录型装饰器，记下每次调用时的 `IsOnExecutor`、`Current`、令牌是否已取消，可在调用返回后插入一次世界变化）；`Host.Tests/CredentialEntryHttpTests.cs` 里的 `LauncherHost`（GM 测试宿主外加启动器路由与游戏端口）。

**红的形态**（`logs/task-14-red.txt`）：
- 批 A：基准生产代码上，H1 以「`IAuthenticationPersistence.TryCreateAsync ran off the executor`」失败；L2 以 500 失败（第一段的管理员读与停用在同一个写事务里互等，SQLite 忙等 30 秒后抛出）；H2～H5、H5 反例、L1、L1 端点级、Q1 编译失败（`PasswordComputationLimiter`、`LauncherCredentialReader`、`ILauncherCredentialReader`、`IGmCredentialReader`、`CredentialChanged` 等不存在；编译证据来自 `git archive 2d8cec4d` 加当前测试的导出构建）。B1 在基准上为绿（等价性用例）。
- 批 B：批 A 之后、过滤器与启动作业之前（只先加了 `GameplayCmdRoute` record），E1（3 条下游调用都在执行器外）、R1（51 条 GM 路由「neither a CMD nor exempt」）、B2（`QueryAsync ran off the executor`）、S1（`IEpicBookDomainService.InitializeAsync ran off the executor`）都红；R1 反例依赖新类型，在基准上编译失败。

**既有测试文件**：

| 文件 | 类别 | 处置 |
|---|---|---|
| §8.2 的驱动调用文件 15 个（见下表）＋ `AuthenticationFixture.cs` | 签名改变 | 只换驱动调用，方法集合与规范化后的断言多重集合不变（`logs/task-14-diff-check.txt`） |
| `HttpApi.Tests/HttpApiPipelineTests.cs` | §8.1 (i) 注册后果 | `ApiHarness` 加 `AddGameplayExecution()` 与 reader 替身 `StubCredentialReader`（注册回带假记录的 `Prepared`，登录回 `Verified` 并计数）；`StubAuthenticationService` 换签名；新增 L1 端点级 1 个方法。原有 13 个方法与 52 条断言行逐字不变 |
| `Host.Tests/GmPlayerSamplingHttpTests.cs` | §7.1 冲突用例 | 改写，断言一字不改 |
| `Persistence.Tests/Accounts/AuthenticationConcurrencyTests.cs` | §7.2 冲突用例 | 只换驱动调用 ＋ 改第 29～30 行注释，断言一字不改 |
| 4 个测试 csproj | 链接 `TestCredentials.cs` | 各加一行 |

驱动调用文件与替换处（基准 `2d8cec4d`）：

| 文件 | 替换处 |
|---|---:|
| Application `Authentication/GameLoginCommandTransactionTests.cs`（另改 `CommitFailOnceAuthenticationDomainService` 的两个签名） | 2 |
| Host `GmInitializationHttpTests.cs` | 4 |
| Host `GmPlayerDirectoryHttpTests.cs` | 1 |
| Networking `BossTowerFrameIntegrationTests.cs` | 2 |
| Networking `GameLoginIntegrationTests.cs` | 9 |
| Networking `IllusionTowerStageFrameIntegrationTests.cs`（**计划清单漏列**：它写的是全限定名 `new Dfo.Server.Application.Authentication.RegisterAccountCommand`，计划的检索没有命中） | 2 |
| Networking `LifecycleExecutorWireTests.cs` | 3 |
| Persistence `AuthenticationConcurrencyTests.cs` | 4 |
| Persistence `AuthenticationConsumptionTests.cs` | 5 |
| Persistence `AuthenticationFailureTests.cs` | 8 |
| Persistence `AuthenticationMigrationTests.cs` | 8 |
| Persistence `AuthenticationRegistrationTests.cs` | 2 |
| Persistence `AuthenticationTicketTests.cs` | 8 |
| Persistence `LoginSessionQueuedCommandTests.cs` | 3 |
| Persistence `LoginSessionReconnectTests.cs` | 11 |

加法：文件 1 ＋ 2 ＋ 4 ＋ 8 ＝ 15（Application 1、Host 2、Networking 4、Persistence 8）；替换处 2 ＋ 4 ＋ 1 ＋ 2 ＋ 9 ＋ 2 ＋ 3 ＋ 4 ＋ 5 ＋ 8 ＋ 8 ＋ 2 ＋ 8 ＋ 3 ＋ 11 ＝ 72。与计划的出入：计划列的 `AuthenticationFixture`「2 处」在基准上并没有命令构造，它在本单元只新增了 `RegisterAsync`／`LoginAsync` 两个辅助；计划漏列 `IllusionTowerStageFrameIntegrationTests`；两者相抵，文件数仍是 15、处数仍是 72，但逐文件分布与计划不同（例如 `AuthenticationConcurrencyTests` 4 而非 5、`AuthenticationTicketTests` 8 而非 9）。这一条不是冲突、不改边界，按 §8.2 同样处理。

**替换表**（裁定第 5 条；`diffcheck14.py` 先用它规范化基准文本，再比较断言多重集合）：

| 编号 | 基准 | 现在 |
|---|---|---|
| R1 | `fixture.Bus.SendAsync(new RegisterAccountCommand(a, b))` | `fixture.RegisterAsync(a, b)` |
| R2 | `fixture.Bus.SendAsync(new AuthenticateAndIssueTicketCommand(a, b))` | `fixture.LoginAsync(a, b)` |
| R3 | `<bus>.SendAsync(new RegisterAccountCommand(a, b)[, t])` | `TestCredentials.RegisterAsync(<services>, a, b[, t])` |
| R4 | `<bus>.SendAsync(new AuthenticateAndIssueTicketCommand(a, b)[, t])` | `TestCredentials.LoginAsync(<services>, a, b[, t])` |
| R5 | `<领域服务>.AuthenticateAndIssueTicketAsync(<账号>, <口令>, default)` | `<领域服务>.AuthenticateAndIssueTicketAsync(<reader 对它们的 VerifiedLogin>, default)`（`AuthenticationMigrationTests` 两处，reader 调用挪到 `TestGameplay` 区域之外，因为限流器拒绝在作业里被调用） |
| R6 | `new AuthenticationDomainService(store, new PasswordCredentialService(100), clock, AuthenticationRuleService.From(…), …)` | `new AuthenticationDomainService(store, clock, …)`；「无白名单」改由 `new LauncherCredentialReader(…, AuthenticationRuleService.From(空配置), new PasswordComputationLimiter(1))` 表达 |
| R7 | `new AuthenticateAndIssueTicketCommand(loginId, password)`、`new RegisterAccountCommand(loginId, password)`（拼进日志检查串） | `new AuthenticateAndIssueTicketCommand(verified) + verified`、`new RegisterAccountCommand(prepared) + prepared`（`AuthenticationFailureTests`：命令现在携带 reader 结果，它们的 `ToString` 也不得泄露口令记录） |

`<bus> → <services>`：Networking 的 `commands` → `host.Services`（`LifecycleExecutorWireTests` 为 `server.Host.Services`）；Application 的 `commands` → `services`；Host `GmInitializationHttpTests` 的 `bus` → `host.Services`；`GmPlayerDirectoryHttpTests` 的 `commands` → `scope.ServiceProvider`。

**文档**：`spec.md` 附录 B 五行末尾追加「清单（TASK-14，基准 `2d8cec4d`）」，并按裁定第 12 条与 RQ-011 新增一行「不碰玩法状态的 HTTP 路由」（附理由）；`verification/task-07-conflicts.md` 追加 §11；日志 `logs/task-14-{step0,red,mutations,build-test,isolated,new-x10,boundary,diff-check,slow-isolated,routes,probe-summary}.txt` 与 `logs/task-14-probe-{Application,Persistence,Protocol,Host,Networking}.txt`。

### Why（本地决定）

- **带口令记录的 record 覆写 `ToString`**：`AuthenticationFailureTests` 守着「日志与结果的字符串里没有口令、账号、新旧记录、票据」，命令现在带着这些 record，默认的 record `ToString` 会把记录打出来。
- **CMD 类型从端点元数据算出**（`IHttpMethodMetadata` 的唯一方法 ＋ `RoutePattern.RawText`），过滤器、显式两段式路由与 R1 用同一个 `CommandTypeOf`，不手写字符串，一条路由只有一个类型。
- **过滤器工厂看不到端点元数据**（.NET 10 的 `EndpointFilterFactoryContext` 只有 `MethodInfo` 与 `ApplicationServices`），所以豁免判断在过滤器运行时读 `HttpContext.GetEndpoint()`；R1 看不到过滤器，所以 `RunsOnGameplay` 在挂过滤器的同时加 `GameplayCmdRoute` 元数据，二者同一次调用，不会只挂一个。
- **预缓冲重放任何读体异常**，不只 `BadHttpRequestException`：客户端中途断开（`IOException`、因 `RequestAborted` 的取消）时，委托在 CMD 内的同一个读点得到同一个异常，`mail/send`、`cera/grant` 的 catch 路径照旧走（计划 §2.2 第 3 条的推广）。
- **启动作业以宿主的启动令牌入队**（计划 §4），与 GM／HTTP 的 `None` 不同（裁定第 7 条只针对 GM／HTTP）：启动被取消时作业不再执行，与今天「启动被取消就不做」一致。
- **启动器登录的校验与重哈希放在同一个限流器槽位里**：两步都是纯计算，分开取两次槽位只增加排队。
- **GM 登录照旧把 `RehashNeeded` 当作通过、不重哈希**，与今天相同；reader 把 `Accepted` 与当时读到的 ID、记录一起交给 CMD 比对。
- **命令保留只读的 `LoginId` 便捷属性**，`HttpApiPipelineTests` 里 `login.LoginId == "player"` 这类模式无需改动。
- **`TestCredentials` 照端点做法重做一次**：测试辅助代表生产的组合，不应在并发迁移这类情形下比生产更早失败。
- **Q1 与 L1 端点级是计划外新增**：前者是裁定第 7 条要求的测试；后者是 L1 计划里「端点级：重做一次后成功；两次都变则 401」那一半，`ApiHarness` 是 `HttpApiPipelineTests` 的私有嵌套类，所以加在该文件里。

### 可见差异与待确认

1. **GM 请求与玩法串行**：慢的 GM 查询会占住执行器，由 M-1 按 `Gameplay gm:… completed` 的 Debug 耗时观察（RQ-091）。
2. **直发 2 秒期限扩大到 GM**：Cera 提醒在作业里直发，不读包的客户端会在 2 秒后被断开（VC-8 提前的范围扩大，T-006 之后消失）。
3. **入队令牌按裁定第 7 条用 `None`**：排队中被放弃的 `mail/send`、`cera/grant` 仍会执行并照旧记失败审计，计划 §2.1 担心的那条可见差异不存在。
4. **登录两段式**：两段之间记录被换掉时，启动器重做一次，GM 直接拒绝并审计；失败的登录（查无账号、口令错）只占限流器，不再进执行器。
5. **已有管理员时 GM bootstrap 也做一次哈希**（在执行器外、开始监听前；耗时没有单独测量）。
6. **双 Error 日志**：委托里没被 `Safe` 接住的异常，执行器记一条 `Gameplay gm:… failed`，`HttpApiExceptionMiddleware` 再记一条（正式全量中各 2 条，都来自 `GmAuthenticationHttpTests` 的注入审计故障），交 T-008。

### §8.3 主动复核（逐项结论）

| 项 | 结论 |
|---|---|
| TASK-12 §5.1「执行器外的准入门持有者：未迁入的 GM/HTTP」 | 生产中不再有：GmWeb 与 HttpApi 的每个 `SendAsync` 都在过滤器包住的委托、`HttpGameplay.RunAsync` 或启动作业里（`logs/task-14-boundary.txt` 逐行列出）；探针 §3.7、§3.9 归零 |
| T-004c 冲突 `AuthenticationConsumptionTests.ExactCredentialBinds…`、`LoginSessionQueuedCommandTests.ReplacedLogin…` | 只换驱动调用，断言不变，正式全量中通过，仍归 T-004c |
| 拉起整个宿主的用例（表征测试、`GameLoginIntegrationTests`、`LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`、`HostShutdownOrderTests`） | 正式全量与隔离复跑中全部通过；启动器登录变成两段没有带出 (vi) |
| TASK-08 §4 的 `AuthenticationFixture.Consume` | 仍在容器作业里，竞争方不变，照常通过 |
| §8.1 (iii) 初筛的 4 个文件 | 21 个 `TestGameplay` 区域（15 ＋ 4 ＋ 1 ＋ 1）里没有一处经 `HttpClient` 调 GM 路由（静态检索与运行都确认），不需要解包 |
| §8.1 计数 | (i) 1（`ApiHarness`）；(iii) 0；(iv) 0；(v) 新交错型冲突 0；(vi) 0 |

### Checks

1. **构建**：`dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-14-build-test.txt` 开头）。
2. **全量**（`logs/task-14-build-test.txt`；运行前后 `yes` 进程都是 0）：
   - 总数 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 969 ＋ 173 ＋ 4502 ＋ 176 ＝ 8130 ＝ 8111 ＋ 19 － 0。逐项对照 TASK-12（TASK-13 未改变计数）：HttpApi 50 → 53（＋3）、Persistence 475 → 476（＋1）、Application 4497 → 4502（＋5）、Host 166 → 176（＋10），3 ＋ 1 ＋ 5 ＋ 10 ＝ 19；Pvf 80、Protocol 1701、Infrastructure 969、Networking 173 相等。
   - 通过 53 ＋ 80 ＋ 1701 ＋ 476 ＋ 967 ＋ 171 ＋ 4484 ＋ 176 ＝ 8108；失败 2，就是那 2 条 `DimensionDungeonTests`；跳过 2 ＋ 2 ＋ 16 ＝ 20；8108 ＋ 2 ＋ 20 ＝ 8130。表征测试 8 通过、2 跳过；挂起转储 0。
   - 日志信号：
     - 为 0 的：`direct send deadline exceeded`、`did not drain within`、`GameplaySelfWait`、`Gameplay http:… failed`、`Gameplay startup:… failed`、`GameplayExecutorStoppedException`、`Gameplay timer:… failed`、`Gameplay event:… failed`。
     - `Gameplay gm:… failed` 2（`gm:POST /api/gm/v1/auth/login`、`gm:POST /api/gm/v1/auth/logout`），与 `HTTP API request failed` 2 成对：都来自 `GmAuthenticationHttpTests` 的注入审计故障（`GmFaultAudit` 抛 `Injected audit storage failure`），是可见差异第 6 条的双日志，单列，交 T-008。
     - 与本单元无关的非零项：`Gameplay inbound:X failed` 3（与 TASK-09～12 相同，用例故意注入）；`the drain tears it down` 3（TASK-10 的丢失关闭回调兜底，历次在 1～6 之间）。
   - **耗时比对**（与 `logs/task-12-build-test.txt` 逐条比，共同用例 8030 条，阈值 9 秒；`logs/task-14-slow-isolated.txt`）：
     - 多出 ≥ 9 秒的 6 条：`GmInitializationHttpTests.Real_registration_and_creation…` ＋16 s；`PaidCardFlipCostTests.A_dungeon_that_switched_its_paid_card_off…(3902)` ＋10 s；`LauncherAuthenticationEndToEndTests.HttpChannelAndGame…` ＋9 s；`SouthernDaleTests.Admission_charges_each_member_once…` ＋9 s；`PaidCardFlipCostTests.A_clear_that_levelled…` ＋9 s；`IllusionTowerPublicFlowTests.Public_handlers_complete…` ＋9 s。
     - 隔离复跑：两条 Host 用例 3 轮都是 1～3 秒，Application 的 4 条在 1 秒左右；全部判为负载波动：没有 30 秒的 SQLite 忙等形状，`did not drain within` 为 0，没有 10 秒的释放排空。「HTTP 请求排在执行器作业之后」在隔离下同样存在（同一宿主里排在它前面的只有本宿主的作业），隔离下不慢。
     - **Host 单列**：共同用例逐条耗时之和 1049.8 → 868.5 s；项目「总时间」2.81 → 2.71 min。GM 用例与事件、定时作业共用执行器之后，Host 没有整体变慢。
     - 各项目「总时间」（TASK-12 → 本单元）：HttpApi 8.4 → 7.3 s；Pvf 12.9 → 9.3 s；Protocol 12.7 → 9.6 s；Persistence 23.9 → 24.0 s；Infrastructure 49.9 → 44.8 s；Networking 57.9 → 51.3 s；Application 2.76 → 2.55 min；Host 2.81 → 2.71 min。
   - **第一次全量**（正式全量之前、变异核对之前，同一份源码；scratchpad `t14/full1.txt`，不作证据）另有 2 条失败，并有 9 条 Host 用例比 TASK-12 多 49～65 秒：
     - `InboundExecutorWireTests.A_game_and_a_channel_message…`：`Connection refused`，同一输出里 TouchSocket 记了 `Address already in use`，是下面第 3 项的端口争用；
     - `ChannelHostStartupTests.PackagedHost_FromUnrelatedWorkingDirectory…`：频道列表响应超时。宿主日志显示脚本命令已处理、15:40:00 定时作业在宿主执行器上完成，却没有任何频道列表命令到达宿主。这个进程不跑 GM 与启动器 HTTP（appsettings 里都关着），本单元在这条路径上只有两个启动作业，日志显示它们在开始监听前完成；
     - 9 条 Host 慢用例的形状是 Host.Tests 进程一次约 45～60 秒的整体停顿。**推断，未测到**：测试进程本身没有前进。正式全量时采样到 swap 在用量从 8.8 GB 涨到 13.3 GB，运行前就已 8.8 GB，机器在换页。
     - 复现尝试：这两条用例在负载下（并行跑 Application 与 Protocol）5 轮、隔离 5 轮全部通过；正式全量没有这两条失败，也没有停顿。
3. **隔离复跑**（`logs/task-14-isolated.txt`）：HttpApi 53 全过；Persistence 476 全过；Application 4502（4484 通过、2 失败即那 2 条、16 跳过）；Host 176 全过（含拉起打包 exe 的 `LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`、`HostShutdownOrderTests`、`ChannelHostStartupTests`）；Networking（`--blame`）两次各有 1 条 `Connection refused`（`ShutdownOrderWireTests.Stopping_the_host…`、`InboundExecutorWireTests.A_telemetry_frame…`），没有测试主机崩溃。
   - 端口争用的不归因明证：工作区隔离 ×8 有 3 轮失败（4 条），基准导出（`git archive 2d8cec4d`，工作区外构建）隔离 ×8 也有 3 轮失败（6 条）；每个失败轮都记了 `Address already in use`，失败全是客户端连接时的 `Connection refused`，与 TASK-09／TASK-10 记过的形状相同。工作区第 2 轮失败的是表征用例 `PartyCharacterizationTests.Leaving_a_party…`，失败在连接，不是转写比对。
4. **新增测试 ×10**（`logs/task-14-new-x10.txt`）：16 个方法的 19 条用例加 2 条改写的冲突用例，每轮 Host 11 ＋ Application 5 ＋ Persistence 2 ＋ HttpApi 3 ＝ 21，隔离连续 10 轮，10 × 21 ＝ 210 次通过，0 失败，0 跳过；正式全量负载下 21 条全部通过。
5. **表征测试**：8 通过、2 跳过，`Characterization/` 目录与金样本未改（表征宿主经启动器 HTTP 两段式注册、取票，线上字节不变）。
6. **变异核对**（`logs/task-14-mutations.txt`；每项都用 scratchpad 备份、逐字节还原，`filecmp(shallow=False)` 相等；7 个文件的 sha1 清单前后相同；做完之后以 `--no-incremental` 重新构建，0 警告 0 错误）：
   - V1 路由组不挂 `RunsOnGameplay`：E1、R1 红（B2 也红）；
   - V2 过滤器不预缓冲：只有 B2 红（标记作业 1 秒内等不到，`TimeoutException`）；
   - V3 reader 原样传口令、领域服务在 CMD 内哈希：H1 红（`Hash` 在执行器上）；
   - V4 限流器不限并发：只有 H2 红（峰值不是 2）；
   - V5 去掉 `AssertNotOnExecutor`：只有 H3 红（没有抛出）；
   - V6 登录第二段不比对口令记录：L1 红（签发了会话）；
   - V7 `EpicBook` 迁移改回直接调用：S1 红；
   - V8 GM bootstrap 改回直接发命令：S1 红。
   - V9 随预热移到 TASK-15，本单元没有做。
7. **测试改动核对**：新增测试见 Changed（N ＝ 16 个方法、19 条用例）；15 个驱动调用文件与 `AuthenticationFixture` 方法集合全部相等，按替换表规范化后断言多重集合全部相等（`logs/task-14-diff-check.txt`，scratchpad `t14/diffcheck14.py 2d8cec4d`）；规范化后仍不同的行只有：`GameLoginCommandTransactionTests` 的替身签名 6 行、`AuthenticationFailureTests` 的 reader 调用 3 行、`AuthenticationMigrationTests` 的 reader 构造与调用 5 行、`AuthenticationFixture` 的两个辅助 4 行、`LifecycleExecutorWireTests` 的 `using` 1 行、`AuthenticationConcurrencyTests` 的注释 4 行。冲突用例的「旧断言 → 新断言 → 保护意图」见 `task-07-conflicts.md` §11 与下面的「冲突用例」。
8. **生产改动的边界与不变量**（`logs/task-14-boundary.txt`）：
   - `git diff --stat -- Server/src`：17 个改动文件 ＋ 4 个新文件，都在 Owned boundary 内；
   - `IGameplayQueue`：与基准相比只多 `HttpGameplay.cs`、`GmBootstrapService.cs`、`GameServerStartupService.cs` 各 1 处，其余 30 个文件命中数相同；
   - `IPasswordCredentialRuleService|PasswordComputationLimiter`：只出现在接口定义、`PasswordCredentialService` 实现、限流器、两个 reader 与 `ApplicationModule` 的注册处；H5 是它的运行期版本；
   - `SuppressFlow|RunDetached`：14 行，与基准逐行相同；
   - **部分迁移期间的不变量**（拆分原则第 3 条）：等待新迁入 CMD 的执行器外代码只有四处，等待时都不持任何门——Kestrel 请求线程（过滤器的预缓冲在入队之前读完；启动器两条与 GM 登录的 reader 返回时限流器槽位已释放；`GmLoginLimiter` 只是计数，不是等待原语）、`GmBootstrapService.StartAsync`（准备口令之后只等作业）、`GameServerStartupService.StartingAsync`（只等两个启动作业）、测试辅助 `TestCredentials`。反向情形：执行器上的作业从不等限流器（`AssertNotOnExecutor`，H3、V5），不成环。
   - **生产中仍在执行器外调用 `ICommandBus.SendAsync` 的位置：0**。GmWeb 与 HttpApi 的 38 行 `SendAsync`（bootstrap 1、邮件 6、活动 7、点券 5、初始化 5、认证 3、玩家目录 5、每日补给 4、启动器 2，1 ＋ 6 ＋ 7 ＋ 5 ＋ 5 ＋ 3 ＋ 5 ＋ 4 ＋ 2 ＝ 38）全部在过滤器包住的委托、`HttpGameplay.RunAsync` 体或启动作业体里；R1 证明每条用总线的路由都是 CMD（豁免路由的委托都不注入 `ICommandBus`）；Application 中名字里有 `ICommandBus` 的 10 个文件是总线本身与其注册 3 个、事件订阅注册里「订阅处理器不得注入总线」的检查 1 个、TASK-11 已迁入的定时源与调度作业 6 个（Joust 维护、三个每日补给作业、在线过期物品清理、名牌过期），3 ＋ 1 ＋ 6 ＝ 10；探针 §3.7、§3.9 为 0；Host 排除轮 x1 的 0 与此一致，但 x1 不能单独作依据（见 Checks 9）。这是 TASK-15 与 T-004c 的输入。
9. **探针复跑**（`logs/task-14-probe-summary.txt`；原始记录 p1，`PROBE_LOG_PREFIX=task-14-probe`；Host 另有排除轮 x1）：
   - **本族归零**：§3.7 GM/HTTP 813／1660 → 0／0；§3.9 启动与停机（改正后）161／407 → 0／0；§3.8 那 10／124（都以 `GmBootstrapService.StartAsync` 为最近入口）→ 0／0。Persistence 两族在 TASK-07 r6 与本轮都是 0／0。没有残留，所以本族不需要排除复跑。
   - **§3.10 的 Host 部分**：Host (A) 238 条／664 次，其中带测试帧 218／566，没有测试帧 20／98（TASK-12 p1 为 834／2698）。这 20 条：13 条／53 次是撞到 40 帧上限、最外层可见入口是总线或管线行为的栈（钩子 P2 `LoginSessionRegistry` 6／18、P2 `OnlineStateRegistry` 6／12、P1 `DomainTransactionScope.Begin` 1／23）；7 条／45 次是完整栈、底部为线程池续体（P2 `OnlineStateRegistry` 3／24、P1 3／18、P3 `TransactionalExecutionGate` 1／3）；13 ＋ 7 ＝ 20，53 ＋ 45 ＝ 98。它们都带命令管线帧，即经总线、在执行器外进入。**归因是结构论证 ＋ 排除轮一致**（第 2 轮按审查非阻断 2 改写）：结构论证三项——R1 证明每条用总线的路由都是 CMD；GM／HTTP 的 CMD 整个跑在作业里，作业令牌跨 await 流动，它的续体不会在执行器外；豁免路由的委托不注入总线——所以执行器外经总线进入的只剩测试直接调用。排除轮 x1（Host 去掉 body 里直接调总线或生产者的 10 个测试类，余 66 条用例全过）(A) 记录为 0，与此一致；但被排除的 10 个类同时包含几乎全部 GM HTTP 用例，x1 的 0 也能用「GM 流量被一起排除」解释，**x1 不能单独作归因依据**。T-004c 翻转之后 Host §3.10 若仍有记录，不要拿 x1 归因。
   - **其余各族**（TASK-12 p1 → 本轮，四个项目，记录／命中）：3.1 Received 1／1 → 1／1；3.1 Connected/Closed 0／0 → 0／0；3.2 1／1 → 1／1；3.3 79／114 → 79／114；3.4 0／0 → 0／0；3.5 17／17 → 17／17（两轮改正后都是 0／0，TASK-12 note 1 的测试续行）；3.6 0／0 → 0／0；3.10 3513／10910 → 3518／10203。3.10 命中少了 707：TASK-12 note 3 说的那批「无测试帧、撞上限、落进 3.7 或 3.10 取决于中间件帧是否留在上限内」的 Host GM 栈，现在在执行器上。Persistence 3.10：40／193（r6）→ 46／180，全部带测试帧。
   - **还原证明**：p1 与 x1 两轮快照前后 sha1 都是 `811c910c0be8c6f4a41f9704d84117aef4efab29`，`cmp` 相等；`Server/src` 未跟踪文件前后逐字节相同；`snapcheck` 1582／1582，相等 1565，不等的 17 个就是本单元改的 17 个文件，未跟踪 4 个就是本单元的 4 个新文件；`patch.py revert` 恢复 25 个文件并删除 `GameplayProbe.cs`；`grep TestGameplay|GameplayProbe Server/src` 为 0；之后以 `--no-incremental` 重新构建，再跑正式全量。
   - 探针轮的失败不作为证据：只有那 2 条 `DimensionDungeonTests`。
10. **对账行**（AC-006）：
    - 附录 A 第 9 行：
      - 40 条 → `gm:<METHOD> <路由模板>` → R1（逐条列在 `logs/task-14-routes.txt`）、E1（`mail/send` 成功与失败、`cera/grant`、`characters?onlineOnly=true`、`auth/logout`）、L2 与 H1（`gm:POST /api/gm/v1/auth/login`）、探针 §3.7 为 0。40 个类型：`gm:DELETE /api/gm/v1/mail/templates/{id}`；`gm:GET` 的 `accounts`、`accounts/{accountId}`、`audit`、`cera/accounts`、`cera/operations`、`cera/operations/{id}`、`characters`、`characters/{characterId}`、`daily-refill`、`daily-refill/items`、`daily-refill/items/{id:int}`、`events`、`events/joust/current-round`、`events/joust/settings`、`events/weekly-attendance/settings`、`initialization`、`initialization/items`、`initialization/items/{id:int}`、`mail/draft`、`mail/operations`、`mail/operations/{id}`、`mail/recipients`、`mail/templates`、`overview`（24 条）；`gm:POST` 的 `auth/login`、`auth/logout`、`cera/grant`、`cera/preview`、`daily-refill`、`events`、`events/joust/current-round/champion`、`events/joust/settings`、`initialization/account`、`initialization/character`、`mail/preview`、`mail/send`、`mail/templates`（13 条）；`gm:PUT` 的 `mail/draft`、`mail/templates/{id}`（2 条）；1 ＋ 24 ＋ 13 ＋ 2 ＝ 40（路径前缀都是 `/api/gm/v1/`）。
      - 11 条豁免 → 附录 B 的理由（`health`、`/gm/{**path}`、`auth/csrf`、`auth/session` → 新增行「不碰玩法状态的 HTTP 路由」；`auth/status`、`mail/items`、`mail/items/{itemId:int}` →「只读 SQLite 的查询」；`server-info`、`items/{itemId:int}/tooltip` →「PVF 内容源与缓存」；`items/icon-pack`、`items/{itemId:int}/icon` →「GM 自身的内存状态」；4 ＋ 3 ＋ 2 ＋ 2 ＝ 11）；R1 钉住豁免集合。
      - 加法 40 ＋ 11 ＝ 51。附录 A 原文「GM 写操作 34 条」按今天的代码数不出 34 这个口径：**按 51 条路由对账；34 的口径不足以确定**（文字在 TASK-16 统一修正，裁定第 13 条）。
    - 附录 A 第 10 行：2 条 → `http:POST /api/launcher/v1/register`、`http:POST /api/launcher/v1/login` → E2／H1（`TryCreateAsync`、`AcquireWriteIntentAsync` 在执行器上、来源 `Http`、类型如上）；1 条豁免 `bootstrap` → 新增行；2 ＋ 1 ＝ 3。
    - 启动期：3 个启动作业 → S1（V7、V8 证明区分力）。附录 B 末行中留在外面的：建表与迁移（Persistence 单例构造时直连 SQLite，不经工作单元入口，原有一项）；频道脚本加载（只读文件，不碰玩法状态）；`StartingAsync` 中的 PVF 内容初始化（内容源，附录 B「PVF 内容源与缓存」）；`MarriageRelationAudit`（只读计数，附录 B「只读 SQLite 的查询」）。
    - 预热：移到 TASK-15，本单元没有对账行。
    - AC-005：「哈希不在执行器上」→ H1（真实 HTTP 管线，启动器注册与登录、GM 登录、宿主启动时的 bootstrap，每次 `Hash`／`Verify` 都在执行器外）、H3（在执行器上调用即程序错误）、H5（静态：领域服务与命令处理器不注入哈希器与限流器）；「并发有上限、排队不拒绝」→ H2（上限 2 时峰值 2、其余 3 个等待且不失败；用例里的标记作业跑在一个与 reader 无关的新执行器上，它只说明限流器的等待不经过任何执行器，**「哈希不占执行器」由 H1 与 H3 证明**，第 2 轮按审查非阻断 3 改写）、H4（默认上限 ＝ 处理器数，两个 reader 共用同一个实例）。「测量记录满足 RQ-091」那一半归 M-1。
    - 留给后续：T-004c（RQ-061 启动期例外的取舍；`SendAsync` 翻转；Host 中 10 个测试类的直接总线调用）；T-006（Cera 提醒改进发件箱，2 秒直发期限随之不再约束它）；T-007（GM／HTTP 与启动作业都用 `NewRoot`，集中在 `HttpGameplay.RunAsync` 与两处启动入队，没有要继承的链，只核对）；M-1（RQ-091 的测量判定）。
11. **冷启动测量**：移到 TASK-15（裁定第 1 条）。

### 冲突用例（旧断言 → 新断言 → 保护意图）

- `GmPlayerSamplingHttpTests.cs:14`：旧——查询卡在数据库读里时，测试在执行器外做 `Cleanup` 与断连再放行；本页 `total` 1、`items` 1、`isOnline` 真，刷新后 0／0 → 新——这 5 条断言一字不改，`Cleanup` 与断连改在查询自己的 CMD 里、数据库读之前内联执行（`ReadBarrier.DuringRead`） → 意图不变：在线集合在查询开始时捕获一次；刷新后的 0／0 仍证明交错确实发生。
- `AuthenticationConcurrencyTests.cs:10`：断言一字不改；**意图收窄**：旧前提「两次校验各在自己的写事务里，第二次可能拿着不可写的 SQLite 快照」→ 新前提「校验在 reader 里、任何事务之外重叠，各自的登录命令重读并比对记录」→ 理由：RQ-007 把哈希移出 CMD，校验不再持有快照；「重叠的口令校验不会让第二次登录失败」仍由它守着。

### Remaining

1. **预热整体在 TASK-15**：§5 清单、规则服务的 `Initialize()`、W1、V9、§5.3 首次加载探针、冷启动测量、待裁定第 10 条。TASK-12 Remaining 第 5 条（事件作业早于预热时的首次加载）随之仍未关闭。
2. **Networking 的端口争用偶发**（既有，基准上同频：隔离 ×8 各 3 轮失败）：用例以「绑定 0 端口、读出、释放」挑端口，宿主绑定前被别的套接字占走，TouchSocket 记一条 `Address already in use` 后不带那个监听启动。不在本单元边界内，没有改；如果要修，改法是让宿主自己绑定 0 端口再把实际端口告诉用例。
3. **Host.Tests 进程停顿未关闭**（Checks 2；第 2 轮定向复核**复现了**，触发「> 30 s 停下」，见下面「第 2 轮」）。受影响的用例集中在 GM HTTP。已用证据排除的是本单元的请求路径（预缓冲、执行器排队、`None` 入队后的完成路径）；没有排除的是「本单元新增的重型测试宿主加重了进程负载」与线程池的相互作用。**下次再现时的排查顺序**：先本单元路径——慢 GM 请求的「请求耗时 － 命令耗时」、测试里同步的 `TestGameplay.Run` 与 GM CMD 是否同时在场、同一轮 `StartingAsync` 内容加载是否同样变慢；再看换页（swap、testhost RSS）；有条件时在停顿当下取线程栈（`dotnet-dump`／`dotnet-counters`，本机未安装）。
4. **双 Error 日志**（可见差异第 6 条）交 T-008。
5. **VC-8 提前的范围扩大到 GM**（可见差异第 2 条），T-006 之后消失。
6. **T-004c**：RQ-061 启动期例外（本单元之后只剩建表不经工作单元入口）；Host 中 10 个测试类直接调总线（探针 §3.10 的 238 条）随翻转处理。
7. **附录 A 第 9 行「34 条」**的修正在 TASK-16。
8. **未提交**：没有 commit，也没有 stash。
9. **预缓冲的一种罕见不等价**（审查非阻断 4，代码路径推断，未实测）：过滤器运行时 `IHttpMaxRequestBodySizeFeature` 已只读、且请求是超过 32 KiB 的分块传输时，基准按服务器默认上限读完并处理，现在 `EnableBuffering` 的 `bufferLimit` 抛 `IOException`、被重放给委托，委托多半映射为 500。Kestrel 在端点阶段这个特性通常可写，B1 也证明正常路径一致，只记为残余风险，供 T-008 归并 GM 路由时参考。
10. **GM 登录第二段的记录比对没有专门的变异**（审查非阻断 5）：V6 只覆盖启动器一侧，L2 覆盖的是停用而不是口令记录被换。今天没有修改管理员口令的写入方，没有实际后果；将来增加「修改 GM 口令」的写入方时，应补一条「第一段之后换掉管理员口令记录 → 第二段拒绝并审计」的用例。

### 第 2 轮（审查非阻断项，2026-10-22）

审查结论 approved（`reviews/task-14-result-review.md`）。本轮没有改生产代码与测试代码。

- **非阻断 1**：`task-07-conflicts.md` §11 对第一次全量 2 条失败的归因改为与 Checks 2 一致（`InboundExecutorWireTests` 为端口争用；频道列表超时为进程停顿推断）。
- **非阻断 2**：Checks 8、Checks 9 与 `logs/task-14-probe-summary.txt` 中 Host §3.10 那 20 条的归因改为「结构论证 ＋ 排除轮一致」，并注明 x1 不能单独作归因依据。
- **非阻断 3**：Checks 10 中 H2 的「执行器同时空闲」半句改为说明证据范围（「哈希不占执行器」由 H1 与 H3 证明）。
- **非阻断 4、5**：写入 Remaining 第 9、10 条。
- **非阻断 6：定向复核触发了停止条件**（`logs/task-14-gm-recheck.txt`；只跑 Host.Tests，不同时跑其他项目，不加 `yes` 负载）：
  - 做了什么：Host.Tests 中全部 GM／HTTP 相关测试类（`Gm*`、`CredentialEntryHttpTests`、`LauncherAuthenticationEndToEndTests`、`LauncherHttpHostStartupTests`、`ChannelHostStartupTests`，共 160 条）隔离 10 轮，再整项目 3 轮，均用 detailed logger 记逐条耗时。
  - 结果：13 轮全部通过，但有 9 次用例超过 30 秒：第 6 轮 1 条（`GmInitializationHttpTests.Real_registration…` 33 s）、第 7 轮 1 条（`GmAuthenticationHttpTests.MissingBootstrapField…` 36 s）、整项目第 1 轮 7 条（52～63 s，7 个不同的测试类同时变慢）；1 ＋ 1 ＋ 7 ＝ 9。其余 10 轮最长 11～18 秒。第一次全量变慢的 9 条，在 10 轮过滤运行里最长 3～30 秒，在整项目 3 轮里最长 2～14 秒。
  - 按指示停下排查，没有做方向性改动。已用证据排除的：
    1. **本单元的请求路径不耗时**：这几轮里超过 3 秒的命令与请求只有 `SearchGmInitializationItemsCommand`（真实 PVF 的物品目录搜索，命令本身未改），它的「请求耗时 － 命令耗时」是 2～30 ms（整项目第 1 轮 55861 － 55850 ＝ 11 ms）。预缓冲、执行器排队、`None` 入队后的完成路径加起来不超过 30 ms。同一轮里另外 6 条 52～63 秒的用例，没有任何一个请求或命令超过 3 秒。
    2. **本单元没碰的代码同样变慢**：凡是有用例超过 30 秒的轮次（第 6、7 轮，整项目第 1 轮，第一次全量），`GameServerStartupService.StartingAsync` 的抽奖／世界掉落内容加载也慢到 11～54 秒（例如整项目第 1 轮抽奖 44.7 s，第一次全量 53.9 s）；其余各轮两种构建都不超过 5.2 秒。这段代码在执行器外、开始监听之前运行，本单元没有改它，所以变慢是整个进程的，不在本单元的请求路径上。
    3. 整项目工作区第 6 轮恰逢机器合盖睡眠（17:20:19～17:31:52），作废，不作证据；pmset 日志显示其余复核时段和第一次全量时段都没有睡眠。
  - **没能排除的**：基准导出（`git archive 2d8cec4d`，工作区外构建）同样条件下没有一轮超过 30 秒（过滤 0／10、整项目 0／3）；工作区有 4 次（过滤 2／10、整项目 1／9（去掉作废的第 6 轮）、第一次全量）。工作区 Host.Tests 多了 10 条会拉起重型宿主的用例，testhost 峰值 RSS 10.9～15.2 GB，基准 12.9 GB。停顿是由这些新增测试加重的负载引起，还是由线程池的相互作用或单纯换页引起，没能判定：带采样的轮次都没有停顿，能在停顿当下看线程的工具（`dotnet-dump`、`dotnet-counters`）本机没有，安装要下载，未做。
  - 等主控方裁定后再决定是否提交；本轮不往 Remaining 写「定向复核正常」，改写在 Remaining 第 3 条。


### 主控方裁定（定向复核之后，提交之前）

**接受，提交。** 定向复核复现了超过 30 秒的用例，裁定依据如下：

1. **请求路径已用直接证据排除。**
   - 停顿 55 秒的那一轮，同一请求的「请求耗时 － 命令耗时」只有 11 ms。这个差值已经包含预缓冲、执行器排队以及 `None` 入队后的完成路径。
   - 同一轮其余 6 条 52～63 秒的用例，没有任何一个请求或命令超过 3 秒。
2. **本单元未改动的代码在同一时刻同样变慢。** 开始监听之前、执行器外的启动内容加载，从不到 5.2 秒变成 11～54 秒。可见停顿发生在整个测试进程，不在请求路径上。
3. **基准与工作区的差异，最合理的解释是内存压力。**
   - 测试进程的内存峰值为 10.9～15.2 GB，机器 swap 接近满载（10 GB 里用了约 9.2 GB）。
   - 本单元新增了 10 条拉起重型宿主的用例，加重了负载。

**不安装 `dotnet-dump`/`dotnet-counters`。** 安装需要下载，而现有证据已经足以排除生产路径。

**残余与后续：**
- 「进程级停顿的确切机制（换页还是线程池相互作用）未定」作为未关闭的取证缺口，保留在 Remaining 第 3 条。
- **TASK-16 增加检查**：统计 Host.Tests 中拉起重型宿主（真实内容或网络）的用例数，以及测试进程的内存峰值。评估能否通过共享宿主、收集夹具降低内存，作为测试基础设施的建议记录下来。
- 后续单元的全量测试如果再出现大于 30 秒的停顿，先按 Remaining 第 3 条的顺序排查：先本单元路径，再换页，再线程栈。不得直接归因于负载。
