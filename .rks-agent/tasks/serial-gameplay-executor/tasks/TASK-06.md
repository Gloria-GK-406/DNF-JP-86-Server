# TASK-06 — T-004a1：测试辅助、共享夹具，以及 Persistence/Host/Networking/Protocol 的测试迁移

## Task Unit Contract

- Revision: 2。第 1 版是整个 T-004a 一个单元；主控方裁定拆成 TASK-06（T-004a1）与 TASK-07（T-004a2），本版是拆分后的 TASK-06。
- Outcome and references:
  - **测试辅助 `TestGameplay`**：放在 `Server/tests/Support/`，链接进需要它的测试项目。它把测试体交给一个**真实的 `GameplayExecutor` 实例**，作为一个作业运行（plan-review-r2 R-2）。配有辅助自身的测试 `TestGameplayTests`。
  - **共享夹具在内部改为经辅助运行**（§2.1 T3）。夹具的调用点不动，链接这些夹具的项目（包括 Application.Tests）随之生效。
  - **Persistence.Tests、Host.Tests、Networking.Tests、Protocol.Tests 的迁移**：把测试体自己做的准备和直接调用改为经辅助运行，范围是 §2.1 的 T1～T5。只改驱动方式：测试数量不变，断言不变。
  - **上述四个项目的冲突用例清单**（`task-06-conflicts.md`）：逐个给出处置决定和负责执行的单元，本单元**不执行**删除或改写（§0）。Application.Tests 的部分与合并后的总清单由 TASK-07 完成。
  - **本单元范围内的机械差异核对**（`task-06-diff-check.md`）。
  - 本单元也是「T-004 拆分与待办归属」（§7）的唯一归处。
  - 对应 Plan T-004「判据与测试的关系」「测试」两节，以及过渡期规则 1；对应 spec RQ-061（测试侧的前提）、RQ-072（为反例和辅助划界）、RQ-081、RQ-083（测试数量与断言的说明）。本单元不满足任何 AC。T-004c 要达到 AC-006、AC-017 的「零违规」，以 TASK-06 与 TASK-07 两个单元的迁移完成为前提。
- Owned boundary:
  - 新建 `Server/tests/Support/TestGameplay.cs`，以及 `Server/tests/Dfo.Server.Application.Tests/Execution/TestGameplayTests.cs`。
  - 在需要的测试项目 csproj 中加 `<Compile Include="../Support/TestGameplay.cs" Link=… />`：Application（辅助测试与链接进来的夹具要用）、Protocol、Persistence、Host、Networking。
  - 共享夹具（§3.3）：`tests/Support/TestSessions.cs`、`Persistence.Tests/Support/{AccountSide,CharacterSide,InventorySide}.cs`、`Persistence.Tests/Accounts/AuthenticationFixture.cs`、`Application.Tests/Support/DungeonRunTestEntryExtensions.cs`、`Protocol.Tests/Support/MoveItemFixture.cs`，以及执行时检索到的其他被多个文件共用的夹具级用例边界。
    - 这里只允许在夹具**内部**包裹，不改夹具的签名。
    - **Application.Tests 下除上面两个文件以外的测试文件，不在本单元边界内。**
  - Persistence.Tests、Host.Tests、Networking.Tests、Protocol.Tests 的测试文件，只改驱动方式；允许的改动形状见 §2.6。
  - 在 `.rks-agent/tasks/serial-gameplay-executor/verification/` 下新增本单元的产物：`logs/task-06-*.txt`、`task-06-diff-check.md`、`task-06-conflicts.md`。
- Required checks:
  1. `dotnet build Server/86JP.Server.slnx -c Release`：0 警告，0 错误。
  2. `TestGameplayTests` 隔离运行连续 20 次全绿，日志写入 `logs/task-06-helper-x20.txt`。
  3. **全量测试计数核对**：`dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`，日志落盘为 `logs/task-06-build-test.txt`。
     - 各项目的测试数与 TASK-05 的基线逐项相等：HttpApi 50、Protocol 1694、Pvf 80、Persistence 475、Infrastructure 969、Networking 138、Host 165。唯一的差值是 Application：4415 ＋ N，N 是 `TestGameplayTests` 的条数。Application.Tests 本身还未迁移，所以这个项目的计数变化只能来自辅助测试。
     - 合计 7986 ＋ N；通过 7964 ＋ N，失败 2，跳过 20。写出加法。
     - 失败的只能是那 2 条 `DimensionDungeonTests`；表征测试 8 条通过、2 条跳过。
     - 另外隔离跑一次 Networking.Tests 与 Host.Tests（满载会假红，见 AGENTS.md），日志写入 `logs/task-06-isolated.txt`。
  4. **断言核对**：对本单元改动过的每个测试文件和夹具，按 §5.1 用脚本比较 HEAD 与工作区：
     - 测试方法集合不变；
     - 断言行（`Assert.`、`Skip.`）去掉空白后，多重集合不变；
     - 其余非空白差异只能是 §2.6 的形状；
     - 例外逐条列出，并写明理由。
     - 结果写入 `task-06-diff-check.md`：按项目写出改动文件数，以及 HEAD 与工作区各自的方法数、断言行数，两两相等。
  5. **迁移点计数**：本单元四个项目里非只读 `.Begin()` 的处数，以及迁移点总数，**以执行时的检索为准，最终以 TASK-07 的探针结果为准**，并与 Plan 的 694 对账，说明差异：
     - 本单元负责 Persistence 60、Protocol 30 这两项；
     - 检索口径要能覆盖 §3.2 所说的强转写法。
     - 对账写入 `task-06-diff-check.md`。
  6. Persistence.Tests 的 27 个文件（§3.1）逐个出现在改动清单中，或作为夹具的调用方，标明「经夹具，调用点不动」。
  7. 不改生产代码：`git diff --stat -- Server/src` 为空；`grep -rn TestGameplay Server/src` 零命中。
- Dependencies:
  - TASK-04：`GameplayExecutor`、`GameplayJob.IsOnExecutor` 与 `Current`、令牌按执行器区分、嵌套与自我等待的语义；
  - TASK-05：全量基线 7986，以及各项目计数；
  - TASK-01：表征测试。
- Result review: required。理由：
  - **下游依赖**：TASK-07、T-004b、T-004c 都建立在这个辅助的语义上，包括作业边界、嵌套时内联、优先使用容器里的执行器，也都沿用这里定下的「不包裹」划界与冲突处置规则。辅助一旦有错，例如只铸令牌却没有真的运行在执行器上，或者嵌套时新建作业、因此丢掉了 AsyncLocal 状态，这个错误会被约 290 个文件继承。
  - **改动面**：共享夹具改动以后，Application.Tests 与 Protocol.Tests 的大量用例都要经过它。
  - **必需检查抓不住的错误**：
    - 包裹区域切错，但碰巧能过；
    - 误把以生产入口为对象的用例整段包住（R-2）；
    - 冲突清单漏项。
  - 这三类错误，全量测试和计数核对都发现不了，而 TASK-07 的探针只能发现其中第一类的一部分。
- Task-specific authorization: 不改 `Server/src`，不删除测试，不修改断言。

## Detailed Plan

### 0. 已裁定的边界

- **冲突用例只做决定，不执行。** 迁移中会在新语义下挂起或死锁的用例（Plan「测试」第 1 条），本单元只写处置决定。执行交给使这条用例失败的那个改动所在的单元，在同一个提交里完成：
  - 经入口驱动、靠交错制造时序的用例，归 T-004b，即迁入该入口的那个单元；
  - 直接构造门、并在门上制造争用的用例，归 T-004c，即开启门断言的那个单元。
  - 理由有三：提前删除，会在 T-004b 期间丢掉门的保护；提前改写，会让改写依据的执行器内语义早于该语义本身出现；处置与使它失败的改动放在同一个提交里，两者的证据才不会混在一起（RefactorPreferences §3.2）。
  - 冲突用例在本单元保持原样，**不包裹**。
- **附录 F 类型读写都包（T2 取超集）。** 理由见 §2.1。
- **拆分。** 本单元负责：辅助、共享夹具，以及 Persistence、Host、Networking、Protocol 四个项目。TASK-07 负责：Application.Tests、合并后的冲突总清单、跨两个单元的全量机械核对，以及探针。
  - 本单元遗漏的包裹点，由 TASK-07 的探针发现，并在 TASK-07 中补上（见 TASK-07 的 Owned boundary）。

### 1. 辅助的设计（`Server/tests/Support/TestGameplay.cs`，命名空间 `Dfo.Server.Testing`）

**API 形状**：全部是 `internal static`，与 `TestSessions` 相同。

```csharp
internal static class TestGameplay
{
    public static Task RunAsync(Func<Task> body, [CallerMemberName] string caller = "");
    public static Task<T> RunAsync<T>(Func<Task<T>> body, [CallerMemberName] string caller = "");
    // 有容器时用：容器里注册了执行器就用它，否则每次调用新建一个
    public static Task RunAsync(IServiceProvider services, Func<Task> body, [CallerMemberName] string caller = "");
    public static Task<T> RunAsync<T>(IServiceProvider services, Func<Task<T>> body, [CallerMemberName] string caller = "");
    // 同步：给同步测试、构造函数、同步夹具（TestSessions）用，阻塞等待
    public static void Run(Action body, [CallerMemberName] string caller = "");
    public static T Run<T>(Func<T> body, [CallerMemberName] string caller = "");
}
```

- 不提供 `Func<ValueTask>` 重载：一个异步 lambda 同时匹配 `Func<Task>` 与 `Func<ValueTask>`，会报 CS0121 二义性。
- 同步形式设两道守卫，调用时立即抛 `ArgumentException`：
  - `Run(Action)` 收到 async lambda（`body.Method` 带 `AsyncStateMachineAttribute`，即 async void）；
  - `Run<T>` 的 `T` 是 `Task`、`Task<>` 或 `ValueTask` 系。
  - 这两种情形下，作业会在 body 真正完成之前结束，等同于绕过执行器。

**执行器的来源**，按顺序判定：

1. **已经在执行器上**（`GameplayJob.IsOnExecutor` 为真）：直接调用 body，不新建作业。
   - 这是夹具嵌套的情形，例如被包裹的测试体调用 `InventorySide.InUseCaseAsync`，或调用内部已经包裹过的 `TestSessions.InWorld`。
   - 只有内联，才能保住外层作业里的 AsyncLocal 状态（环境事务、会话门帧）。新建的作业在 `SuppressFlow` 下运行，看不到这些状态。
   - 这条判断的是「已经在执行器上」，不是「执行器不在」，与 Plan 的过渡内联规则同构，不构成旁路。
2. **给了 `services`，且其中注册了 `GameplayExecutor`**：用容器里的那个实例。
   - 本单元期间，没有任何模块调用 `AddGameplayExecution`，这一分支只由辅助测试覆盖。
   - 从 T-004b 注册执行器开始，凡是写成 `RunAsync(f.Services, …)` 的调用，都会和 dispatcher、`SendAsync`、后台服务共用同一个执行器：测试内并发的发起方随之串行化；被误包的生产入口会因自我等待而**直接报错**，这就是 R-2 的自动检测。
3. **其余情形**：每次调用新建一个执行器，`new GameplayExecutor(NullLogger<GameplayExecutor>.Instance)`，运行完这一个作业就释放（异步用 `DisposeAsync`，同步用 `Dispose`），并断言 `Completion.IsCompleted`，保证不遗留消费循环。

**作业的形状**：

- `GameplayWork` 的字段：分流键 `GameplayShardKey.World`；调用链 `GameplayCallChain.NewRoot(<一个现有来源>, null)`；CMD 类型 `"test:" + caller`。
- 来源用一个现有的枚举值即可，不新增。用的是哪一个，执行者在 Result 中写明；它只影响测试日志。
- 委托体的第一行断言 `GameplayJob.IsOnExecutor`，不成立就抛 `InvalidOperationException`。这是「确实进了执行器」的就地证明。
- 取消令牌用 `CancellationToken.None`。
- body 抛出的异常对象原样交还调用方（执行器内部经 `TaskCompletionSource.SetException`），所以 xunit 断言异常、`SkipException`、`Assert.ThrowsAsync` 的行为都不变。

**与测试自建容器的关系，以及 xunit 并行下的隔离**：

- 每次调用新建的执行器，彼此不共享，所以并行运行的测试类之间不会相互影响。
- 容器执行器（T-004b 起生效）随容器走，一个测试一个容器。共享夹具（class fixture、collection fixture）的那些测试，在 xunit 下本来就是顺序执行的。
- 令牌按执行器区分（TASK-04 R2-3）：辅助执行器 E_t 上的作业，向容器执行器 E_c 入队或释放 E_c，都不会被判为自我等待。
  - 代价是：在第 3 种来源下，被误包的入口不会被自动发现，所以 §2.4 的划界要靠清单与审查。
  - 因此规定：**手里有容器的测试，一律写成 `services` 重载**。

### 2. 迁移规则（TASK-07 沿用本节）

#### 2.1 什么要包

| 类 | 识别 | 说明 |
|---|---|---|
| T1 写事务 | 非只读的 `.Begin()`，含 `((DomainTransactionScope)…GetRequiredService<IDomainTransactionAccessor>()).Begin()` 这种强转写法 | 只读的 `Begin(readOnly: true)` 不包（附录 B）。包裹区域要覆盖租约从 `Begin` 到提交或释放的整个生命周期 |
| T2 玩法状态 | 直接使用附录 F「改为执行器内访问」的 13 个类型或它们的门面：`OnlineStateRegistry`、`LoginSessionRegistry`、`DungeonRunDomainService`、`CreatureRuntimeDomainService`、`OnlineCharacterRuntimeDomainService`、`PartyService`、`TownAreaDomainService`、`DungeonParticipationDomainService`、`DungeonCardPresentationCoordinator`、`BossTowerDomainService`、`IllusionTowerDomainService`、`TournamentDungeonDomainService`、`AltarDungeonEngine`；门面如 `SelectedSessionDomainService`，以及 `runs.Begin(…)`、`positions.Begin(…)` 一类运行态入口 | **读写都包**（取超集）。这样本单元不依赖 T-004c 才确定的逐方法挂点粒度，也和 TASK-07 的锁探针口径一致；包裹读操作不改变行为。`new` 构造本身不必包 |
| T3 共享夹具 | §3.3 所列 | 在夹具内部包，调用点不动；在作业里调用时按 §1 规则 1 内联 |
| T4 门 | 测试直接 new 门，再交给 handler、Delivery 或 Coordinator | 只包不制造争用的调用；制造争用的列入冲突清单 |
| T5 直接调用的 handler | 直接 new 消息 handler 或命令处理器，然后调 `HandleAsync` | 通知 handler 的 `ComposeAsync` 只读，不包 |

#### 2.2 包多大

- **整个方法**：测试方法只做 T1～T5 的事、没有调用生产入口（§2.4）时，整个方法包起来，例如 `public Task X() => TestGameplay.RunAsync(async () => { …原方法体… });`。同步测试用 `TestGameplay.Run(() => { … })`，签名保持同步。
- **区域**：方法里同时调用了生产入口时，只包准备与直接调用那一段连续区域；入口调用留在区域之外；需要带出作业的值，用返回值带出。
- **AsyncLocal 边界**：区域内用到的 AsyncLocal 状态（环境事务、`NetworkCommandAuthorization`、`EffectOperationContext`、会话门帧），必须在同一个区域里建立并结束。

#### 2.3 「并发只生效一次」类用例

- 发起方是 `bus.SendAsync` 的，不动。
- 发起方是直接调用的，每个发起方**单独**包一层：`Task.WhenAll(TestGameplay.RunAsync(f.Services, a), TestGameplay.RunAsync(f.Services, b))`。**不得**把整段 `Task.WhenAll` 包进一个作业里。
  - 本单元期间，它们仍然并发；从 T-004b 起，它们落到同一个容器执行器上，自然串行。断言作为不变量保留。

#### 2.4 不包的（R-2）

以生产入口为测试对象的用例，入口调用本身不包，只包它的准备。入口包括：

- 真实 dispatcher（`INetworkMessageDispatcher`）；
- 事件分发器（`DomainEventDispatcher.DrainOnceAsync` 等）；
- 连接观察者与登录会话运行时；
- 后台服务的 tick 与调度；
- GM/HTTP 端点（Host.Tests 的 `Gm*HttpTests`、`GmTestHost`、`EndToEndHostHarness`）；
- 真实 TouchSocket 与真实进程（`Networking.Tests/Integration`、`Characterization`、`Outbound`，以及 Host.Tests 中起真实进程的用例）；
- `ICommandBus.SendAsync`。

附录 A 对账（AC-006）要的「测试或检索证据」，只能取自这类不包裹的用例。Result 中按项目列出本单元判为「入口用例」的文件。

#### 2.5 冲突用例（只决定，不执行）

`task-06-conflicts.md` 只收本单元四个项目的冲突用例。每行写明：文件:方法、冲突机制、处置决定、执行单元、理由。初始清单如下，执行时逐个阅读确认；Application.Tests 的部分（K1，以及 K2～K4 中属于 Application 的项）由 TASK-07 完成。

| 组 | 本单元范围内的用例 | 机制 | 执行单元（预判） |
|---|---|---|---|
| K2 登录竞态与排队 | Persistence：`AuthenticationConcurrencyTests`（第二个登录要等 40 秒）、`LoginSessionQueuedCommandTests` | 依赖两个登录并发 | T-004b（迁登录会话维护与口令哈希时）；如果拖到 T-004c 翻转时才失败，则由 T-004c 执行 |
| K3 用 TCS 或门制造交错 | Protocol：`PartyTownProjectionDeliveryTests.cs:134,188`、`TownProjectionHandlerTests.cs:164`、`DungeonLoadingProjectionDeliveryTests.cs:78,107`、`BossTowerProjectionConcurrencyTests`(11)、`IllusionTowerProjectionConcurrencyTests`(11)、`DungeonCardPresentationCoordinatorTests.cs:109,235` | 一个发起方卡住，等另一个推进 | 卡牌计时器那两条归 T-004b；直接调用 Delivery 或 Coordinator 的归 T-004c |
| K4 只有部分方法依赖门排队或交错 | Protocol：`PartyCommandHandlerTests`(5)、`LeaveDungeonTests`(3)、`SelectDungeonHandlerTests`、`PartySelectDungeonHandlerTests`、`TitleBookProtocolTests`(2)、`TitleBookMutationHandlerTests`、`TownAreaProjectionDeliveryTests`、`MarriageProposalCodecTests`；Persistence：`SqliteEntryLifecycleRecoveryTests`、`SqliteCharacterCarriedGoldTableTests`（含 `WhenAll`，先确认是否经过门） | 门排队或交错 | 逐个判定，规则同 K3。只有 K4 里那些「门在场但不争用」的方法才包 |
| K5 投递期限与重试 | Protocol：`CleanupNotificationDeliveryTests`、`PreparedBatchTests`；Networking：`TouchSocketNetworkMessageSenderExplicitDeliveryTests`、`HandlerFailureConnectionIntegrationTests`、`ExactConnectionCloseTests`、`OutboundSendFailureTests` | 投递语义 | T-006 改写；T-004b 加 2 秒直发期限时须核对它们仍然通过 |

Host.Tests 的 `GmCera`、`GmEvents`、`GmOfficialMail`、`GmLoginLimiter` 这几个并发用例经 HTTP 入口驱动，属于 §2.4，不算冲突用例。

#### 2.6 允许的改动形状（Required check 4 的白名单）

- 新增 `TestGameplay.Run(` / `TestGameplay.RunAsync(` 调用行，以及配套的 lambda 头、`{`、`}`、`});`、`=>`；
- 只有空白不同的行；
- 为接收作业返回值而把局部变量提到作业外，例如 `var x = await TestGameplay.RunAsync(async () => { …; return y; });`，或者解构；
- 包裹带来的方法签名 `async`/`Task` 变化；
- csproj 中新增 `Compile Include … Link`；
- 新增 `using Dfo.Server.Testing;`。

### 3. 现状清单（HEAD 0223e646；执行时以重新检索为准）

#### 3.1 Persistence.Tests：27 个文件，60 处非只读 `.Begin()`（R-1）

| 文件 | 处 |
|---|---:|
| `Accounts/AuthenticationConsumptionTests.cs` | 2 |
| `Accounts/AuthenticationFailureTests.cs` | 2 |
| `Accounts/AuthenticationFixture.cs` | 1 |
| `Accounts/AuthenticationMigrationTests.cs` | 2 |
| `Accounts/AuthenticationTicketTests.cs` | 1 |
| `Administration/GmAuditTransactionTests.cs` | 2 |
| `Characters/CharacterDungeonDifficultyTests.cs` | 5 |
| `Creatures/CreatureDomainStoreTests.cs` | 1 |
| `Database/AfterCommitEffectsTests.cs` | 3 |
| `Database/OneUseCaseSettlesEveryTableTests.cs` | 3 |
| `Database/UseCaseTransactionCoversReadsTests.cs` | 1 |
| `Inventory/AvatarCarrierRulesTests.cs` | 1 |
| `Inventory/AvatarDetailFollowsTheIdentityTests.cs` | 1 |
| `Inventory/CeraShopStorageTests.cs` | 5 |
| `Inventory/ClassificationRefusalTests.cs` | 1 |
| `Inventory/CreatureRevivalAddressesByIdentityTests.cs` | 1 |
| `Inventory/CreatureStateFollowsTheIdentityTests.cs` | 2 |
| `Inventory/EpicBookTests.cs` | 3 |
| `Inventory/ExpiredCharacterItemCleanupTests.cs` | 3 |
| `Inventory/Lifecycle/CreatureLifecycleTests.cs` | 4 |
| `Inventory/SqliteItemDomainStoreTests.cs` | 2 |
| `Inventory/TwoAvatarsDoNotShareOneIdentityTests.cs` | 1 |
| `Inventory/WorldRowSurvivesTheMoveTests.cs` | 1 |
| `Mail/SqliteMailboxRepositoryTests.cs` | 6 |
| `Support/AccountSide.cs` | 1 |
| `Support/CharacterSide.cs` | 2 |
| `Support/InventorySide.cs` | 3 |

- 加法：2＋2＋1＋2＋1＋2＋5＋1＋3＋3＋1＋1＋1＋5＋1＋1＋2＋3＋3＋4＋2＋1＋1＋6＋1＋2＋3 ＝ 60，共 27 项。
- 另有 2 处只读 `Begin`，不迁移。
- 此外有 5 个文件直接使用 T2 类型：`AuthenticationMigrationTests`、`AuthenticationTicketTests`、`LoginSessionQueuedCommandTests`（属 K2）、`LoginSessionReconnectTests`、`CreatureLifecycleTests`。

#### 3.2 本单元其余项目

- **Protocol.Tests**：
  - 非只读 `.Begin()` 30 处，分布在 9 个测试文件和 1 个夹具里：`CardRewardTests`、`GroundPickupTests`、`ItemPathFrameTests`、`AdvanceHuntEnemyQuestCommandTests`、`MonsterDeathHandlerTests`、`AcceptQuestCommandHandlerTests`、`SelectCharacterProductionStateTests`、`CreatureIdentityFitsTheWireTests`、`TownAreaProjectionDeliveryTests`（K4）、`TownProjectionHandlerTests`（K3）；夹具是 `Support/MoveItemFixture.cs`。
  - T1～T5 候选文件约 63 个，其中直接 new 门的 18 个、`new …Handler(` 的 51 个、使用 `TestSessions` 的 13 个，另有 27 个注入了 `ICommandBus`。注入总线的那些要按区域包。
- **Host.Tests**：`GmPlayerDirectoryHttpTests`（事务与运行态 `Begin`）、`GmPlayerSamplingHttpTests`（运行态 `Begin`）、`GmCeraHttpTests`（`TestSessions`、`OnlineStateRegistry`）。它们都是 HTTP 入口用例，只包准备部分。
- **Networking.Tests**：`Integration/CardRewardReconnectIntegrationTests.cs` 用 `runs.Begin(…)` 准备数据。它是真实 socket 用例，只包准备部分。
- **检索盲区**：经接口变量使用 T2 类型、强转写法 `Begin`，正则都会漏掉一部分。所以本单元的处数只作为执行时的对账起点（Required check 5），最终以 TASK-07 的探针为准。

#### 3.3 共享夹具（T3）

| 夹具 | 链接进 | 包法 |
|---|---|---|
| `tests/Support/TestSessions.cs` | Application、Protocol、Host | 每个公开方法的方法体用同步 `Run` 包；嵌套调用时内联 |
| `Persistence.Tests/Support/InventorySide.cs`、`CharacterSide.cs` | Persistence、Application、Protocol（`CharacterSide` 只链接进 Protocol） | `InUseCaseAsync`、`Settled` 等用例边界，在内部用 `RunAsync` 包 |
| `Persistence.Tests/Support/AccountSide.cs` | Persistence | 同上 |
| `Persistence.Tests/Accounts/AuthenticationFixture.cs` | Persistence | 同上 |
| `Application.Tests/Support/DungeonRunTestEntryExtensions.cs` | Application、Protocol、Networking | 对运行态入口的调用在内部包 |
| `Protocol.Tests/Support/MoveItemFixture.cs` | Protocol | 同上 |

执行时另行检索各项目 `*Fixture.cs` 与 `Support/` 下的 `Begin` 和 T2 用法。被多个文件共用的，都在本单元按夹具处理。

### 4. 为什么不改变行为（TASK-07 沿用本节）

1. **生产代码不变。** HEAD 上的 `Server/src` 中，除了 `Execution/` 与 `Outbound/` 两个目录，没有任何地方读 `GameplayJob`、`GameplayExecutor` 或 `IGameplayQueue`（检索退出码 1）。所以生产代码在作业里和在作业外走完全相同的分支。
2. **执行器在这里只做一件事：串行运行一个委托。**
   - 第 3 种来源下，一次调用对应一个执行器、一个作业；
   - 嵌套时内联；
   - 第 2 种来源在本阶段不会命中。
   - 所以测试原有的并发仍然并发，原有的顺序仍然是顺序。
3. **唯一的环境差异是 ExecutionContext 边界与线程。** 作业在 `SuppressFlow` 下运行于线程池，没有 xunit 的同步上下文。§2.2 的区域规则保证 AsyncLocal 状态不跨越这条边界。万一违反，典型表现有两种：
   - 作业内的服务找不到环境事务，于是自开事务、在执行器外落定。这会被 TASK-07 的探针 P1 记下来；
   - 已经开着事务时再开一个，直接抛异常、测试变红。
4. **异常与跳过原样传递**（§1）。
5. **冲突用例不包**（§2.5）。

**证明迁移真的进了执行器**：本单元靠辅助在委托体内的断言，以及 `TestGameplayTests` 的反例（§5.2 第 1、7 条）；按调用点覆盖的证明由 TASK-07 的探针完成。

### 5. 检查怎么做

#### 5.1 机械差异核对（TASK-07 复用同一脚本，范围扩到全部）

- 用一次性脚本完成，放在 scratchpad，不进仓库。脚本的命令行与输出摘要写入 `task-06-diff-check.md`。
- 对 `git diff --name-only HEAD -- Server/tests` 中的每个 `.cs` 文件，取 HEAD 版与工作区版：
  1. 用正则抽出测试方法（特性行加方法名），比较集合；
  2. 断言行去掉空白后，比较多重集合；
  3. 把 `git diff -w` 的增删行逐行与 §2.6 的白名单匹配，列出不匹配的行。
- 汇总表：按项目写出改动文件数、HEAD 与工作区各自的方法数和断言行数（两两相等）、白名单之外的行数，并逐条写出理由。
- 计数核对：从 `task-06-build-test.txt` 按项目抽出总数、通过、失败、跳过，与 TASK-05 的分项逐一比较，写出加法。
- 迁移点对账（Required check 5）：写出检索命令、检索到的处数，与 Plan 694 中属于本单元那两项的差异，并说明原因。

#### 5.2 辅助自身的测试（`TestGameplayTests`，约 10～12 条，N 以实际为准）

1. 委托体内 `IsOnExecutor` 为真，返回后为假；作业结束后令牌失效。
2. 返回值能带出作业；异常对象原样交还（用 `ReferenceEquals` 判定），包括 `SkipException`。
3. 嵌套调用内联：内层读到的 `GameplayJob.Current.RootId` 与外层相同；外层设置的 AsyncLocal 在内层可见。
4. 边界：外层的 AsyncLocal 在作业内不可见，作业内设置的在作业外也不可见。
5. 第 3 种来源下，两个同时发起的 body 并发执行，用带超时的屏障证明两者的执行区间重叠。这条证明包裹不改变测试原有的并发。
6. `services` 重载，容器里没有注册执行器：结果同第 5 条。
7. `services` 重载，容器里注册了执行器（`AddGameplayExecution`）：在 body 内对容器的 `GameplayExecutor` 调用 `RunAsync`，得到 `GameplaySelfWaitException`；两个同时发起的 body 串行执行，执行区间不重叠。
8. 同步的 `Run(Action)` 与 `Run<T>(Func<T>)` 都在执行器上运行；`Run` 收到 async lambda 抛 `ArgumentException`；`Run<Task>` 也抛 `ArgumentException`。
9. 每次新建的执行器在调用返回时 `Completion` 已经完成。观察方式由执行者选：给辅助加一个 internal 钩子，或者断言释放之后再入队会被拒绝。

### 6. 执行顺序

每一批做完：构建，跑受影响的测试项目，记下计数，并在 Result 的批次清单上打勾。

1. 先写辅助的测试并看它失败（按 test-driven-development），再写辅助，然后加 csproj 链接。
2. 共享夹具（§3.3）。改完之后跑 Persistence、Application、Protocol、Host、Networking 五个项目，因为夹具对这五个都生效。
3. Persistence.Tests 余下的文件（§3.1）。
4. Host.Tests 和 Networking.Tests（§3.2）。
5. Protocol.Tests：先处理直接 new handler 的（T5），再处理不制造争用的门（T4）和 T2。
6. 写 `task-06-conflicts.md`，逐个阅读 §2.5 所列的用例，确认归组。
7. 机械差异核对（§5.1）→ 正式全量运行 → 隔离复跑 → 辅助测试连跑 20 次 → 确认 `git diff --stat -- Server/src` 为空 → 写 Result。

### 7. T-004 拆分与待办归属

T-004 累积的待办按下表分配到各单元。T-004a1 即 TASK-06，T-004a2 即 TASK-07。

| # | 待办 | 归属 | 说明 |
|---|---|---|---|
| 1a | 测试辅助：测试体交给真实的执行器实例运行，不能只铸令牌（R-2） | **TASK-06** | §1；委托体内断言，加反例测试 |
| 1b | 辅助只用于准备与直接调用；以生产入口为对象的用例不包 | 划界：**TASK-06**（§2.4）；TASK-07、T-004b、T-004c 遵守 | 从 T-004b 起，`services` 重载会让误包的入口因自我等待而失败 |
| 1c | 令牌按执行器区分；测试自建容器里的执行器与辅助的执行器不是同一个实例；作业内向另一个执行器入队或释放它，不判为自我等待 | **TASK-06**（§1 来源规则、§5.2 第 7 条） | |
| 2a | 迁移：`.Begin()`、`TestSessions`、直接构造的门（非争用）、直接调用的 handler | 共享夹具与 Persistence、Host、Networking、Protocol：**TASK-06**；Application.Tests：**TASK-07** | Persistence 的 27 个文件见 §3.1 |
| 2b | 会挂起或死锁的用例逐个处置 | **决定**：TASK-06（本单元四个项目）、TASK-07（Application 部分与合并总清单）。**执行**：T-004b 负责入口驱动的 K2、K3、K4；T-004c 负责直接测门的 K1、K3、K4；T-006 负责 K5 | §0 |
| 2c | 「并发只生效一次」类：各发起方分别入队，不在一个作业内 `Task.WhenAll` | 直接调用的发起方逐个包裹：TASK-06 / TASK-07；经总线的不用改，T-004c 翻转后自然串行 | |
| 2d | 测试数量与断言变化逐文件记录 | 每个单元各记各的：TASK-06 为「迁移部分 0 变化，＋N 条辅助测试」；TASK-07 为 0 变化；T-004b、T-004c 记各自执行的冲突处置 | T-009 汇总加法 |
| 2e | 探针，证明按调用点都已覆盖，并产出生产入口清单 | **TASK-07** | 它的 (A) 清单作为 T-004b 的输入 |
| 3a | 附录 A 的全部入口迁入执行器 | **T-004b** | 对账输入之一是 TASK-07 探针的 (A) 清单 |
| 3b | 请求级帧（`IsBlocked`、`IsSuperseded`、`CaptureAuthority`、`NetworkCommandAuthorization.Enter`、效果账本 `DispatchAsync`）整体移进作业 | **T-004b** | 与 dispatcher 迁入同一步 |
| 3c | 口令哈希移到执行器外，并发有上限 | **T-004b** | RQ-007、AC-005 |
| 3d | 纯遥测 handler 留在执行器外，逐个列入附录 B | **T-004b** | |
| 3e | 分离点清单：逐一确认在 `SuppressFlow` / `RunDetached` 下启动，且不被作业 await（TASK-04 N-2）。范围包括 `TouchSocketNetworkConnectionCloser` 的分离关闭、卡牌计时器，以及 TASK-05 的准入循环与排空循环 | **T-004b** | TASK-05 的两个循环尚未接线，T-004b 只登记，接线（T-006）时复核 |
| 3f | 传输回调入口用 `RunDetached` 作为流边界 | **T-004b** | 与 dispatcher 迁入同一步 |
| 3g | 直接发送套 2 秒期限：「放弃等待并抛出」（WaitAsync 式，R-5），超时关闭连接 | **T-004b**，排在第一个会发包的入口迁入之前 | 配 R-b 那条测试；同时核对 K5 |
| 3h | 预热：`Lazy` 内容源、冷进程首次选角的 `PrepareSelectedCharacterState`、首次进本的内容加载 | **T-004b** | 列入附录 B |
| 3i | 停机顺序：TCP 与 Kestrel 停止接受 → 执行器排空 → 执行器停止 | **T-004b** | AC-007 |
| 3j | EpicBook 迁移等启动期工作，作为执行器作业运行（R-3 推荐方案） | **T-004b** | 这样 T-004c 不需要启动期开关 |
| 3k | 在组合根与测试容器中注册 `AddGameplayExecution` | **T-004b** 第一步 | 自此 `TestGameplay` 的第 2 种来源生效，测试内的并发开始串行；由此暴露的 K 组用例，在 T-004b 中处置 |
| 4a | `SendAsync` 语义翻转，作为最后一个动作 | **T-004c** | 过渡期规则 1 |
| 4b | 门断言：在执行器外取门，或在执行器上取门时需要等待，都立即抛出 | **T-004c** | 配反例 |
| 4c | RQ-061 判据：写 SQLite 的工作单元开启，以及附录 F 的玩法状态写入口 | **T-004c**：确定逐方法的挂点并实现 | T2 已按超集包裹、TASK-07 已做 P2 锁探针，所以挂点怎么定都不影响测试侧；反例按 RQ-072 |
| 4d | 结构判据：只有 `GameplayExecutor` 能派生 `GameplayJobHost`；`ExitJob`/`Fault`/`ProgramErrorOf` 改为实例方法，并断言令牌归属；`RunDetached` 只允许出现在传输回调入口与关连接器 | **T-004c** | TASK-04 补充与 Remaining 2 |
| 4e | 完成条件：全量与表征测试通过，门断言与判据在一次全量运行中零违规 | **T-004c** | |
| 4f | Plan Verification 的措辞按 R-4 理解（T-004 允许 VC-8） | **T-004b** 引入期限时登记；**T-004c** 完成核对 | TASK-06、TASK-07 不改生产代码，不涉及 |
| 5a | TASK-03 的「门外自开事务写」迁入执行器：`SetUserAreaHandler`、`CharacterStateAccess.cs:159` 的 `AloneAsync`、`CreatureRuntimeDomainService.cs:274` | **T-004b** | TASK-07 的探针报告中标出它们的记录 |
| 5b | 判据要能抓住同一类入口 | **T-004c** | 判据挂在 `DomainTransactionScope.Begin(write)` 本身；反例用 `AloneAsync` 在执行器外调用 |

T-004b 是否再切分，例如切成「入站与生命周期」「后台、调度与计时器」「事件、GM/HTTP 与启动」三块，等 TASK-07 的探针 (A) 清单出来后再定。

## Result

- Outcome：
  - 测试辅助 `TestGameplay` 已交付：把测试体交给真实的 `GameplayExecutor` 作为一个作业运行。辅助自身的测试 13 条，连跑 20 次全绿。
  - 共享夹具已在内部包裹，只有 `DungeonRunTestEntryExtensions` 例外（见「偏离」第 1 条）。
  - Persistence、Host、Networking、Protocol 四个项目已按 §2 迁移：
    - 90 个 `.cs` 文件、5 个 csproj，共新增 439 个包裹调用点；
    - 测试方法集合与断言行多重集合逐文件不变。
  - 冲突用例清单写在 `verification/task-06-conflicts.md`，本单元没有执行任何删除或改写。
  - 全量 7999 ＝ 7986 ＋ 13，失败的只有已知的 2 条 `DimensionDungeonTests`；`Server/src` 零改动。

### 偏离计划、需要主控方确认的两点

1. **`DungeonRunTestEntryExtensions` 没有在内部包**（§3.3 原写「对运行态入口的调用在内部包」）。
   - 包上之后，全量运行有 36 条失败，全部是 `Participation changes require a transaction.`（Application 35 条、Networking 1 条）。
   - 原因：调用方是在执行器外持有环境事务时调用它的，例如经总线的命令处理器、`ShopFixture.InTransaction`。新作业在 `SuppressFlow` 下运行，看不到那个事务。这是 §2.2 AsyncLocal 规则禁止的切法，夹具内部包裹在此不可行。
   - 已还原该文件，改为由调用方按 T2 包住它所在的整段区域。Protocol 的调用方已包；Application 的调用方交给 TASK-07。
   - 证据：`logs/task-06-fixture-dungeonrun-wrap.txt`；分布与加法见 `task-06-diff-check.md` §4。
2. **辅助多了两个同步容器重载** `Run(IServiceProvider, Action)`、`Run<T>(IServiceProvider, Func<T>)`。
   - §1 的 API 只列了不带容器的同步形式，但 §1 又规定「手里有容器的测试，一律写成 services 重载」。Host、Networking 和部分 Persistence 用例需要在有容器时做同步的 T2 调用，所以补上这两个重载。
   - 语义与异步版相同。用例 `The_synchronous_container_forms_run_on_the_container_executor` 覆盖它们。

### Changed

**辅助 `Server/tests/Support/TestGameplay.cs`（新建，命名空间 `Dfo.Server.Testing`，`internal static`）**

- 公开形状：
  - `RunAsync(Func<Task>)`、`RunAsync<T>(Func<Task<T>>)`；
  - `RunAsync(IServiceProvider, Func<Task>)`、`RunAsync<T>(IServiceProvider, Func<Task<T>>)`；
  - `Run(Action)`、`Run<T>(Func<T>)`、`Run(IServiceProvider, Action)`、`Run<T>(IServiceProvider, Func<T>)`；
  - 全部带 `[CallerMemberName] string caller`。
  - 另有只给辅助测试用的 `internal` 观察入口 `RunObservedAsync` / `RunObserved`：在新建的执行器释放之后，把它交给回调。
- 执行器来源按顺序判定：
  1. `GameplayJob.IsOnExecutor` 为真时直接调用 body，即内联；
  2. 容器里有 `GameplayExecutor` 时用它（`GetService(typeof(GameplayExecutor))`）；
  3. 否则每次调用 `new GameplayExecutor(NullLogger<…>.Instance)`，跑完这一个作业后释放（异步用 `DisposeAsync`，同步用 `Dispose`），并检查 `Completion.IsCompleted`，没完成就抛出。
- 作业形状：
  - `GameplayShardKey.World`；
  - 调用链 `GameplayCallChain.NewRoot(GameplayCallSource.Connection)`。来源选 `Connection`，因为被包的多是模拟客户端 CMD 的测试；它只影响日志；
  - CMD 类型 `"test:" + caller`；取消令牌 `CancellationToken.None`；
  - 委托体第一行检查 `GameplayJob.IsOnExecutor`，不成立就抛 `InvalidOperationException`。
- 同步守卫：在内联判断之前执行。
  - `Run(Action)` 收到 async 方法（`AsyncStateMachineAttribute`）时抛 `ArgumentException`；
  - `Run<T>` 的 `T` 属于 `Task`、`Task<>`、`ValueTask`、`ValueTask<>` 时同样抛 `ArgumentException`。
  - 注意：直接写 `Run(async () => …)` 会绑定到 `Run<Task>`，由第二道守卫拦下。
- 同步路径直接阻塞在执行器作业的 `Task` 上（`GetAwaiter().GetResult()`），不经过调用方的同步上下文。
- 异常对象原样交还：执行器走 `TaskCompletionSource.SetException`，`await` 与 `GetResult()` 都抛原对象。

**辅助测试 `Server/tests/Dfo.Server.Application.Tests/Execution/TestGameplayTests.cs`（新建，13 条）**

对应 §5.2：

| # | 用例 | 对应 |
|---|---|---|
| 1 | `The_body_runs_as_a_job_whose_token_dies_with_it` | 第 1 条：在作业内为真、返回后为假，捕获的上下文在作业结束后不在执行器上 |
| 2 | `A_value_comes_out_of_the_job_and_a_failure_comes_out_as_the_same_object` | 第 2 条：返回值带出；`InvalidOperationException` 与 `SkipException` 用 `Assert.Same` 判定 |
| 3 | `A_nested_call_runs_inline_in_the_outer_job_and_sees_its_flow` | 第 3 条：嵌套的异步与同步调用读到同一个 `RootId`，并且看得见外层的 AsyncLocal |
| 4 | `The_job_is_a_flow_boundary_in_both_directions` | 第 4 条 |
| 5 | `Without_a_container_two_bodies_still_run_at_the_same_time` | 第 5 条：带超时的互等屏障 |
| 6 | `A_container_without_an_executor_gives_each_call_its_own` | 第 6 条 |
| 7 | `A_container_executor_runs_the_body_so_waiting_on_it_from_inside_is_a_self_wait` | 第 7 条前半 |
| 8 | `A_container_executor_runs_two_bodies_one_after_the_other` | 第 7 条后半：执行区间不重叠 |
| 9 | `The_synchronous_container_forms_run_on_the_container_executor` | 新增的同步容器重载 |
| 10 | `The_synchronous_forms_run_on_the_executor` | 第 8 条前半，含异常原样交还 |
| 11 | `The_synchronous_forms_refuse_work_that_would_outlive_the_job` | 第 8 条后半：async void、async lambda、`Task`、`Task<int>`、`ValueTask`、`ValueTask<int>` 六种都被拒绝 |
| 12 | `An_executor_made_for_one_call_is_stopped_when_the_call_returns` | 第 9 条：选用 internal 钩子，断言 `Completion` 已完成，并且再入队被拒为 `GameplayExecutorStoppedException` |
| 13 | `An_executor_made_for_one_synchronous_call_is_stopped_when_the_call_returns` | 第 9 条的同步形式 |

加法：13 条，N ＝ 13。

先写测试、再写辅助：把辅助换成直接调用 body 的桩时，12 条里 11 条失败（`logs/task-06-helper-red.txt`）。唯一通过的是第 2 条，直接调用也满足它。当时第 9 条还没写。

**csproj（5 个，每个只新增一行 `Compile Include … Link`）**

Application、Protocol、Host、Networking 加在现有的链接组里；Persistence 新建一个 `ItemGroup`。

**共享夹具（内部包裹，签名不变）**

| 夹具 | 包裹点 |
|---|---|
| `tests/Support/TestSessions.cs` | `InWorld`、`Selected`、`EnsureInWorld`、`EnterWorldWithConnection`、`ReselectOnSameConnection` 五个方法体用同步 `Run` 包；嵌套调用时内联。`Directory()`/`Directory(out …)` 只做 `new` 构造，按 T2「构造本身不必包」不包 |
| `Persistence.Tests/Support/InventorySide.cs` | 私有 `Settled`，以及两个 `InUseCaseAsync` |
| `Persistence.Tests/Support/CharacterSide.cs` | `InUseCaseAsync`，以及 `PerCallUseCase.Settled` |
| `Persistence.Tests/Support/AccountSide.cs` | `GetOrCreateAsync` |
| `Persistence.Tests/Accounts/AuthenticationFixture.cs` | `Consume`，用 `RunAsync(Services, …)` |
| `Protocol.Tests/Support/MoveItemFixture.cs` | `RunAsync` |
| `Application.Tests/Support/DungeonRunTestEntryExtensions.cs` | **不包**，见「偏离」第 1 条 |

执行时检索到的其他夹具级文件：
- `Application.Tests/Support/` 下的 `LegacyTownAreaTestAdapter`、`PartyServiceTestAdapter`、`TowerPresenceProxy`、`UserDomainServices`、`DungeonVisitTestFactory`：按 Owned boundary 不属于本单元，交给 TASK-07。
- Protocol 的 `TowerDomainStub`、`ExperienceFrameStandIns`、`LegacySessionSnapshotFixture`、`VirtualOnlyPartyService`、`SharedTownMovementDomainService`：都是替身或工厂，没有标记，不改。

**各项目迁移**

| 项目 | 改动 .cs 文件 | 新增包裹调用点 | 形状 |
|---|---:|---:|---|
| Persistence | 28 | 56 | 见下 |
| Host | 2 | 16 | 见下 |
| Networking | 3 | 11 | 见下 |
| Protocol | 56 | 351 | 见下 |
| tests/Support | 1 | 5 | `TestSessions` |
| 合计 | 90 | 439 | |

加法：28 ＋ 2 ＋ 3 ＋ 56 ＋ 1 ＝ 90；56 ＋ 16 ＋ 11 ＋ 351 ＋ 5 ＝ 439。口径：`git diff -U0 HEAD -- Server/tests` 中含 `TestGameplay.Run` 的新增行。

- **Persistence（28 个文件）**：§3.1 的 27 个文件全部在改动清单里（Required check 6），另加 `LoginSessionReconnectTests`（T2）。
  - 夹具 4 个：InventorySide、CharacterSide、AccountSide、AuthenticationFixture。
  - 整个方法包裹 11 个文件、26 个方法：GmAuditTransaction 2、CharacterDungeonDifficulty 4、AfterCommitEffects 3、OneUseCaseSettlesEveryTable 3、UseCaseTransactionCoversReads 1、CeraShopStorage 4、EpicBook 2、ExpiredCharacterItemCleanup 2、CreatureLifecycle 1、SqliteItemDomainStore 1、SqliteMailboxRepository 3。加法：2 ＋ 4 ＋ 3 ＋ 3 ＋ 1 ＋ 4 ＋ 2 ＋ 2 ＋ 1 ＋ 1 ＋ 3 ＝ 26。
  - 文件内私有用例辅助在内部包，9 个文件、10 个辅助：
    - CreatureDomainStoreTests.`InTransactionAsync`
    - AvatarCarrierRulesTests.`TheLoadAnswersAsync`
    - AvatarDetailFollowsTheIdentityTests.`SettleAsync`
    - ClassificationRefusalTests.`InUseCaseAsync`
    - CreatureRevivalAddressesByIdentityTests.`ReviveAsync`
    - CreatureStateFollowsTheIdentityTests.`SettleItemsAsync`、`SettleCreaturesAsync`
    - SqliteItemDomainStoreTests.`InTransactionAsync`
    - TwoAvatarsDoNotShareOneIdentityTests.`SettleAsync`
    - WorldRowSurvivesTheMoveTests.`SettleAsync`
    - SqliteItemDomainStoreTests 同时有整方法包裹，所以纯辅助的文件是 8 个。
  - 区域包裹 5 个文件：Accounts 下的 Consumption、Failure、Migration、Ticket、LoginSessionReconnect。
    - 总线调用留在区域外，只包显式事务和 `LoginSessionRegistry` 的直接调用，一律传 `fixture.Services`。
    - Migration 的 `rolledBack` 用提出返回值的形状。
  - 加法：4 ＋ 11 ＋ 8 ＋ 5 ＝ 28。
- **Host（2 个文件）**：
  - `GmPlayerDirectoryHttpTests`：
    - 辅助 `Publish`、`Cleanup` 在内部包，`ConnectAsync` 里的 `RegisterConnection`/`GetRequired` 用表达式形状包；
    - 主用例的 T2 调用（`Select`、`positions.*`、`online.*` 读写）逐段包，都传 `host.Services`；
    - HTTP 调用全部留在外面。
  - `GmPlayerBrowserFixtureTests`：包 1 处 `StepTo`。
  - `GmCeraHttpTests` 只经 `TestSessions`（夹具），调用点不动。
- **Networking（3 个文件，都是真实 socket 用例）**：`BossTowerFrameIntegrationTests`、`IllusionTowerStageFrameIntegrationTests`、`CardRewardReconnectIntegrationTests`。
  - 只包准备阶段的 `Select`/`EnterWorld`、`online.TryGetByConnection`/`GetRequired`，以及不等待入口推进的 T2 快照断言，都传 `host.Services`。
  - 轮询入口进度的 `WaitUntilAsync` 不包。T-004b 起这些作业会落到同一个容器执行器上，轮询放在作业里会等不到入口。
  - 帧写入不包，经总线的种子命令也不包。
- **Protocol（56 个文件）**：
  - 分工：fork A 处理 22 个，fork B 处理 21 个，主控处理 12 个，另加 `MoveItemFixture`。加法：22 ＋ 21 ＋ 12 ＋ 1 ＝ 56。逐文件的决定记录在 scratchpad 的 `decisions_a.md`、`decisions_b.md`，主控的 12 个写在这里。
  - 默认是整个方法包裹：直接 `new` handler 或命令处理器再调 `HandleAsync`、直接调用 Delivery 或 Coordinator、直接用真实 `PartyService`/`DungeonRunDomainService`/`OnlineCharacterRuntimeDomainService` 等。
  - 共用的 handler 辅助在内部包：`SetPlayResultHandlerTests.Settle`、`ResultScreenTests.Confirm/Scroll`、`FinishQuestHandlerTests.Finish`、`GrantKillExperienceCommandTests.Award`、`TutorialLevelUpHandlerTests.AskForTheLevel`、`CreatureIdentityFitsTheWireTests.InUseCaseAsync`、`CardRewardTests.World` 的 `Send`/`Handle`（以及其构造函数的后半段）、`ItemPathLiveTests.Bind`。
  - 区域包裹：
    - `QuestClearVerticalTests`：真实总线留在区域外，用解构带出 4 个值；
    - `JoustQueryHandlerTests.QueryTwice`；
    - `MonsterDeathHandlerTests` 的 1 个用例，容器在方法体里建出；
    - `MarriageHousingCodecTests`；
    - `ItemUsageProjectionTests.Daily_worker…`：只包最后一行投递，worker tick 是入口；
    - `LeaveDungeonTests.Give_up_completes_through_real_command_locks…`：handler 内部用真实总线，这是生产中 handler 在作业里的样子；测试自己不调总线，所以从容器建出之后整段包，传 `provider`；
    - `PartySelectDungeonHandlerTests.The_leader_cancels_selection…`：从容器建出之后整段包；
    - `DungeonCardPresentationCoordinatorTests`：直接的协调器与 handler 调用、`Build`/`BuildParty` 的准备，按 `Delay.ReleaseAsync` 切成段分别包；计时器释放本身（入口）留在外面。
  - 冲突用例一律不包，Protocol 共 26 个方法；K5 也不包。

**验证产物**：`verification/task-06-conflicts.md`、`verification/task-06-diff-check.md`，以及 `verification/logs/` 下的 `task-06-build-test.txt`、`task-06-isolated.txt`、`task-06-helper-x20.txt`、`task-06-helper-red.txt`、`task-06-fixture-dungeonrun-wrap.txt`。

### Why（本地决定）

- **T2 的门面**：除了 §2.1 点名的 `SelectedSessionDomainService`，还把 `OnlineUserDomainService` / `IOnlineUserDomainService` / `IOnlineUserQueryDomainService`、`GameSessionEndpointDomainService` 算作 `OnlineStateRegistry` 的门面（AGENTS.md 写明三者读写同一份在线身份），读写都包。测试自写的替身（实现了接口的 stub）不算。
- **不包的**：
  - 通知 handler 的 `ComposeAsync`（§2.1 T5）、codec；
  - 对已在内部包裹的夹具的调用；
  - 入口调用：真实总线、真实 host/socket/HTTP、后台 worker 的 `RefreshAsync` tick、卡牌计时器的释放；
  - 等待入口推进的轮询。
- **K5 不包**：它们直接测发送器或真实传输，本来就不经 T1～T5。依照「冲突用例保持原样」，`CleanupNotificationDeliveryTests` 这一组直接调用的投递也一并不包。
- **构造函数中给 readonly 字段赋值**的部分放不进 lambda（`AdvanceHuntEnemyQuestCommandTests.ObjectPartyFixture`、`CardRewardTests.World` 的前半段）。它们只在已整体包裹的测试里被 `new`，运行时内联在外层作业中。
- **换行符**：包裹脚本按文本模式读写，把 3 个混用 CRLF 的文件写成了 LF，已按 HEAD 逐行恢复（`task-06-diff-check.md` §4）。
- **分工方式**：Protocol 的 53 个非冲突文件交给两个 fork 只读改写，它们不跑 dotnet；构建与测试由主控统一执行。两个 fork 的改动全部经过同一个机械核对脚本和全量运行。

### Checks

1. `dotnet build Server/86JP.Server.slnx -c Release --no-incremental`：0 个警告，0 个错误（`logs/task-06-build-test.txt` 开头）。
2. `TestGameplayTests` 隔离连跑 20 次：20 次都是「通过 13，失败 0」，退出码 0 共 20 次（`logs/task-06-helper-x20.txt`）。
3. 全量运行 `dotnet test Server/86JP.Server.slnx -c Release --no-build --logger "console;verbosity=detailed"`（`logs/task-06-build-test.txt`）：
   - 各项目：HttpApi 50、Pvf 80、Protocol 1694、Persistence 475、Networking 138、Infrastructure 969、Host 165，与 TASK-05 逐项相等；Application 4428 ＝ 4415 ＋ 13。
   - 总数 50 ＋ 80 ＋ 1694 ＋ 475 ＋ 138 ＋ 969 ＋ 4428 ＋ 165 ＝ 7999 ＝ 7986 ＋ 13。
   - 通过 7977 ＝ 7964 ＋ 13，失败 2，跳过 2 ＋ 2 ＋ 16 ＝ 20；7977 ＋ 2 ＋ 20 ＝ 7999。
   - 失败的只有 `DimensionDungeonTests` 那 2 条；表征测试 8 条通过、2 条跳过。
   - 隔离复跑（`logs/task-06-isolated.txt`）：Networking 138 条，136 通过、2 跳过；Host 165 条全部通过。
4. 断言核对（`task-06-diff-check.md` §2、§3）：
   - 90 个改动文件，方法 674 ＝ 674，断言行 2968 ＝ 2968；
   - 方法与断言行的多重集合逐文件相等；
   - 白名单外 1 处，是记号对齐造成的假象，已逐条说明。
5. 迁移点（`task-06-diff-check.md` §6）：
   - 写事务 `.Begin()`：Persistence 60 处、27 个文件，Protocol 30 处、8 个文件，与 Plan 相等；顺带复核 Application 604，加法 604 ＋ 60 ＋ 30 ＝ 694。
   - 强转写法在这四个项目里零处；运行态 `.Begin(…)` 另计 17 处（Protocol 12、Host 4、Networking 1），不计入 694。
   - 覆盖核对：90 处里 87 处脚本直接判在作业内，3 处逐条说明（1 处冲突用例、1 处缩进使脚本失灵、1 处替身总线在作业内被调用）。
6. Persistence 的 27 个文件全部出现在改动清单中。另有 21 个文件只经夹具、调用点不动：AuthenticationConcurrency、AuthenticationRegistration、AuthenticationSchema、LoginSessionQueuedCommand、SqliteAccountEntryStateRepository、SqliteAccountRepository、CharacterBodyCreation、CharacterIdentityRead、DungeonRecoveryOnAFreshDatabase、EntryLoadCharacterization、SkillCarrier、SqliteCharacterEntryStateRepository、SqliteCharacterLifecycle、SqliteEntryLifecycleRecovery、EntrySaveReconciliation、InventoryLoadCharacterization、OneThingInTwoPositionsIsRefusedOnLoad、SplitPile、SqliteEntryInventoryMutationRepository、SqliteInventoryEntryRepository、TitleBookCarrier。
7. `git diff --stat -- Server/src` 为空，`git status --short -- Server/src` 为空，`grep -rn TestGameplay Server/src` 零命中（退出码 1）。

批次清单：
- [x] 1 辅助：红灯 11/12，然后实现，绿灯 12/12；之后加同步容器重载，13/13。
- [x] 2 共享夹具：五个项目全跑，发现 `DungeonRunTestEntryExtensions` 的问题后还原。
- [x] 3 Persistence：475/475。
- [x] 4 Host 与 Networking。
- [x] 5 Protocol：整体构建 0 警告 0 错误，Protocol 1694/1694。
- [x] 6 冲突清单。
- [x] 7 机械核对 → 正式全量 → 隔离复跑 → 20 次 → `Server/src` 零改动。

### §2.4 入口用例（按项目，入口调用本身没有包）

- **Persistence**：
  - Accounts 下的认证与登录会话用例：AuthenticationConsumption、AuthenticationFailure、AuthenticationMigration、AuthenticationTicket、AuthenticationRegistration、AuthenticationConcurrency、LoginSessionQueuedCommand、LoginSessionReconnect，经 `fixture.Bus.SendAsync`；
  - `Database/ReadOnlyCommandTransactionTests`，经真实总线。
- **Host**：全部 Host.Tests 都经 GM/HTTP 端点或真实进程。本单元只包了 `GmPlayerDirectoryHttpTests`、`GmPlayerBrowserFixtureTests` 里的准备调用。
- **Networking**：`Integration/`、`Characterization/`、`Outbound/`。本单元只在上述 3 个集成测试里包了准备调用。
- **Protocol**：
  - `QuestClearVerticalTests`，经真实总线；
  - `ItemPathLiveTests`，真实 host 与 TCP；
  - `ItemUsageProjectionTests.Daily_worker…`，以及 `UsageQuotaProjectionTests` 的 3 条 worker 用例（后台 tick）；
  - `DungeonCardPresentationCoordinatorTests` 的计时器释放；
  - 冲突清单里经总线的两条：`LeaveDungeonTests.Exit_command_borrows…`、`SelectDungeonHandlerTests.Entry_transaction_cannot_reach…`。

### Remaining

交给 TASK-07：
1. Application.Tests 里所有调用 `runs.Begin`/`BeginParty`/`PreparePartyEntry`（`DungeonRunTestEntryExtensions`）的地方，要由调用方包住**含环境事务的整段区域**。夹具内部不能包，理由见「偏离」第 1 条。那 35 条失败用例所在的 6 个文件可以作为起点，但调用点不止这些。
2. `Application.Tests/Support/` 下被 Protocol 链接的适配器（`LegacyTownAreaTestAdapter`、`PartyServiceTestAdapter` 等）不在本单元边界内。Protocol 侧已经在调用方包住，Application 侧由 TASK-07 处理。
3. 探针 (B) 可能报出、本单元有意留在作业外的点，逐条如下：
   - Networking 集成测试里轮询入口进度的 `WaitUntilAsync` 读；
   - `DungeonCardPresentationCoordinatorTests` 里 `using var world` 在方法末尾的 `Coordinator.Dispose()`；
   - `GmPlayerSamplingHttpTests` 的 `duringRead` 注入回调；
   - `SelectCharacterProductionStateTests` 中只驱动 `SelectCharacterPreparationService`（协作者全是替身）的 6 个用例；
   - `ComposeAsync` 内部读到的 T2 状态。
   若 TASK-07 的口径把它们判为漏包，按同样形状补上即可，但轮询入口进度的那几处不能包（理由见 Changed 的 Networking 一条）。
4. `TestSessions` 的签名不收容器，所以有容器的测试经它做的选会话，永远落在新建的执行器上。这是夹具签名的限制；若 TASK-07 认为必须统一，可以加带容器的重载，不改现有签名。
5. 冲突总清单：合并 `task-06-conflicts.md`。本单元新增了 3 条计划初始清单没有的用例：
   - `AuthenticationConsumptionTests.ExactCredentialBindsAgainButOverlappingUncommittedBindsAreRejected`
   - `LeaveDungeonTests.Exit_command_borrows_the_projection_lease_through_the_real_pipeline`
   - `PartyNotificationTests.Revisions_of_one_party_generation_are_delivered_as_contiguous_phases`
   另有 4 处计数出入（PartyCommandHandler、两塔并发、TitleBookProtocol、TownAreaProjectionDelivery），记在冲突清单里。

交给 T-004b：
- 3 个冲突方法：卡牌计时器的 2 条；`AuthenticationConcurrencyTests` 1 条，随口令哈希移出一并处置。
- 后台每日刷新 worker（ItemUsage、金银号角、比武、镜像）迁入执行器之后，上面 4 条入口用例应成为附录 A 的对账证据。在迁入之前，它们会在 T-004c 的门断言下变红。
- 注册 `AddGameplayExecution` 之后，所有写成 `RunAsync(services/fixture.Services/host.Services/provider, …)` 的调用会改用容器执行器。届时需要核对：
  - `AuthenticationFixture.Consume` 与冲突用例 `ExactCredentialBinds…` 的相互等待（已列入冲突清单）；
  - Networking 真实 socket 用例里被包的短作业，它们与入口作业共用同一个执行器。

交给 T-004c：26 个冲突方法，即 Persistence 2 ＋ Protocol K3 7 ＋ Protocol K4 17。
交给 T-006：K5 共 22 个方法，即 Protocol 6 ＋ Networking 16（5 ＋ 3 ＋ 2 ＋ 2 ＋ 4）。

冲突方法合计：T-004b 3 ＋ T-004c 26 ＝ 29，其中 Persistence 3、Protocol 26。

### 主控方补充（结果审查通过之后，提交之前）

- **两处偏离一并接受**：一是 `DungeonRunTestEntryExtensions` 改由调用方包裹；二是新增两个同步容器重载。审查也接受了这两处。
- **TASK-06 审查的 N-1、N-2** 已并入 TASK-07 第 2 版。
- **交给 T-004b**：
  - **N-3**：`DungeonCardPresentationCoordinatorTests` 中有 21 处轮询包在作业里，其中几处等的是计时器入口推进之后的结果。迁移卡牌计时器时，要复核测试的作业和计时器 CMD 会不会落到同一个执行器上，从而互相等待。
  - **N-4**：注册执行器之后，要核对使用 services 重载的区域。除 Result 中已列出的以外，再加上 Protocol 的 5 个文件：LeaveDungeon、PartySelectDungeon、MonsterDeath、QuestClearVertical、Joust。
- **记载更正**（审查 N-5）：
  - `CardRewardTests.World` 有 31 个未包的方法直接 new 它。构造函数里没包的那一半只做对象构造，所以无害。
  - TitleBook 那个文件整份都是 CRLF，不是「混用」。
