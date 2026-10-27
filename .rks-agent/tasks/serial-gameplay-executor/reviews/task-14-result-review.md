---
kind: rks-review
target: tasks/TASK-14.md
scope: Task Unit result quick review（TASK-14 / T-004b6，Contract Revision 1 ＋「主控方裁定（执行前）」14 条，批 C 已按裁定第 1 条移出；执行轮次：Result 第 1 版，未提交）
verdict: approved
date: 2026-10-22
---

# TASK-14 结果快速审查

## 审查对象与依据

- 目标：`tasks/TASK-14.md` 的 Contract、Detailed Plan、主控方裁定与 Result（约第 691 行起）。
- 改动身份：基准 `2d8cec4d` ＋ 未提交改动。`git diff HEAD` 中生产 17 个文件、测试 22 个文件、`spec.md` 附录 B、`task-07-conflicts.md`；未跟踪的生产新文件 4 个（`HttpGameplay.cs`、`PasswordComputationLimiter.cs`、`LauncherCredentialReader.cs`、`GmCredentialReader.cs`）、测试新文件 7 个、`verification/logs/task-14-*` 16 个。
- 依据：`spec.md` RQ-007、RQ-010、RQ-011、AC-005、AC-006、附录 B；`plan-review-r2.md` R-3；TASK-08 的跨单元规则；TASK-11／TASK-12 中「生产者以 `None` 入队」的口径。
- 采用的准则：reviewing-work 的 Task Unit 快速审查 ＋ Code（限本地明显缺陷）＋ Deliverable（Spec 附录与冲突登记的一致性）。
- 本审查自己做的核对：
  - 逐行读了全部生产差异和 4 个新文件，以及 `SqliteDatabase.ReadAsync`、`GameplayExecutor` 的作业结束逻辑、GM 邮件的委托体与 `Read`、`ItemCatalogDomainService`、`GmAccessDomainService.IdentifyAsync`；
  - 读了新测试 `GmGameplayRouteTests`、`CredentialEntryHttpTests`、`PasswordComputationLimiterTests`、`HttpGameplayTests`、`TestCredentials`，以及两条冲突用例和 `HttpApiPipelineTests` 的差异；
  - 抽查了约 25 处驱动调用替换，分布在 `GameLoginIntegrationTests`、`LoginSessionReconnectTests`、`AuthenticationTicketTests`、`AuthenticationMigrationTests`、`AuthenticationFailureTests`、`GmInitializationHttpTests`；
  - 对照日志核了计数：8 个项目的总数、通过数、失败数、跳过数，以及 `Gameplay gm:… failed` 2、`GameplaySelfWait` 0、`startup`／`http` 的 failed 各 0；
  - 用当前文件复算了变异后的 sha1，7 个文件与 `task-14-mutations.txt` 的「after」清单逐一相同；
  - 确认 Release 产物比所有 `.cs` 都新，然后单独跑了一次 Host 的 `GmGameplayRouteTests`、`CredentialEntryHttpTests`、`GmPlayerSamplingHttpTests`：12 通过，0 失败，用时 9 s。
  - 没有修改仓库文件，没有做变异实验。

## 结论

没有阻断缺陷。分项判断如下。

### 1. 口令哈希移出执行器（RQ-007）

- **限流器的形状符合契约。**
  - `SemaphoreSlim(capacity)`，默认上限是 `Environment.ProcessorCount`；
  - 只按调用方令牌取消，没有超时，也没有队列上限，所以超出时排队、不拒绝；
  - 入口先调 `AssertNotOnExecutor`，计算期间才占槽位，`finally` 里释放。
  - H3 证明：作业体吞掉了 `GameplaySelfWaitException`，作业照样以程序错误失败（`ProgramErrorOf(token)`）。
- **reader 的查询确实是只读的，并且在执行器外。**
  - `FindAsync`／`FindAdministratorAsync` 走 `SqliteDatabase.ReadAsync`：没有环境事务时，单独开一条连接读，不登记写参与者；
  - reader 只在 Kestrel 请求线程或 `GmBootstrapService.StartAsync` 上被调用；
  - L2 的 `After` 钩子以 `!call.OnExecutor` 为条件，并实际命中了，这是运行期证据。
- **认证语义逐项与基准等价。** 按基准 `AuthenticationDomainService`／`GmAccessDomainService` 的分支逐条对照：

  | 场景 | 基准 | 现在 |
  |---|---|---|
  | 字段不合法 | 400 `invalid_request` ＋ 字段错误（领域服务返回） | 同一个 400，由 reader 返回，端点直接回 |
  | 账号不存在 | 哑校验 → 401 | 哑校验在限流器里 → 401，不入队 |
  | 口令错 | 401 | 401，不入队 |
  | 旧 MD5 且允许迁移 | 迁移后签发 | reader 算出新记录，CMD 比较后替换再签发 |
  | 需要重哈希 | 比较后替换再签发 | 同上 |
  | 注册时账号重名 | 先哈希，`TryCreate` 失败 → 409 | 先哈希，CMD 内 `TryCreate` 失败 → 409 |
  | GM 管理员停用、改名或不存在 | 哑校验 → 拒绝并审计 | 同左 |
  | GM 返回 `RehashNeeded` | 视为通过 | 视为通过 |
  | GM bootstrap | 判定次序如左 | 判定次序不变；长度检查移进 reader 返回 null，结果仍是 false |

  时序上的差异只有一处：失败的登录不再进入执行器。这是 RQ-091(d) 要的方向。账号不存在与口令错误仍然各付一次 KDF，看不出账号是否存在。
- **两段式的复核与重做在竞态下成立。** 以「两次并发登录都触发旧 MD5 迁移」推演：
  - A 先提交 `MD5 → R_A`；
  - B 的 CMD 重读到 `R_A ≠ VerifiedRecord`，返回 `CredentialChanged`；
  - 端点重做第一段，对 `R_A` 校验通过，B 签发。
  - 结果与基准「写意向内读到最新记录」相同。
  - 第二次仍变就回 401：要触发它，需要第三方在两次之间再写一次口令记录，今天没有这样的写入方（裁定第 8 条）。
  - 比较后替换失败，基准返回 401，现在返回 `CredentialChanged`，然后重做一次。这比基准更宽容，只发生在并发迁移的窗口里，不是缺陷。
  - GM 一侧要求 `Accepted`、`Enabled`、ID、记录四项同时成立，变了就拒绝并审计（L2）。
- **72 处测试替换没有掩盖断言变化。** 抽查的替换都是机械替换：`fixture.Bus.SendAsync(new XCommand(a, b))` → `fixture.XAsync(a, b)`，或 `TestCredentials.XAsync(services, a, b, t)`，所在的 `Assert` 行除被替换的子表达式外逐字未变。
  - `AuthenticationMigrationTests` 的 `noAllowlist` 一段：第一次登录已经把记录迁成现代格式，所以第二次走普通校验；改成「reader 在区域外产出 `VerifiedLogin`」后，断言的对象与含义都没变。
  - `AuthenticationFailureTests` 的泄露检查从「命令的 `ToString`」扩到「命令 ＋ reader 结果的 `ToString`」，是收紧，不是放松。
  - `diff-check` 表里只有 `HttpApiPipelineTests` 的方法集合不等（13 → 14），原因是新增了 L1 端点级用例；Result 已写明原有 13 个方法与 52 条断言行不变。

### 2. GM／HTTP 过滤器

- **预缓冲不改变今天的状态码与错误消息。**
  - 6 个读体的 GM 委托把 `ContentLength` 上限和 `MaxRequestBodySize` 都设为 32768，与过滤器的 `bodyLimit` 相同（`grep MaxRequestBodySize Server/src/Dfo.Server.GmWeb`）；
  - 非 JSON、或声明长度超限的请求，过滤器不读体，由委托照旧拒绝；
  - 分块传输超限时，`BadHttpRequestException` 原样重放给委托，映射到同一个 400；
  - 格式错误的 JSON：预缓冲只是读满，委托的 `ReadFromJsonAsync` 照旧抛 `JsonException`，与基准相同。
  - B1 的三种形态和 `step0` 基线逐字一致。
  - 读体失败时 `input` 为 null，`mail/send` 不记失败审计，与基准相同。
- **委托体逐字未改。** `GmItemEndpoints`、`GmServerInfoEndpoints`、`GmOfficialMailEndpoints` 的差异只有 `.WithMetadata(…, new OutsideGameplay(…))`。
- **入队令牌按裁定第 7 条处理。**
  - `queue.RunAsync(…, CancellationToken.None)`，作业体拿到的是 `context.RequestAborted`；
  - 经这个令牌抛出的取消被带出作业、在外面重抛，执行器不记 Error；
  - Q1 钉住了「排队期间被放弃的请求仍会执行，并看到已取消的令牌，不记 Error」。
- **12 条豁免的理由成立。**

  | 路由 | 核对结果 |
  |---|---|
  | `auth/status` | 只读 GM 管理员表 |
  | `mail/items` 两条 | `IdentifyAsync` 只读 GM 会话表与管理员表，物品目录来自 PVF 规则服务（`ItemCatalogDomainService`），都不读内存里的玩法状态。附录 B 该行原文就举了「GM 邮件物品查询」为例 |
  | `auth/session` | 只读中间件放进 `HttpContext.Items` 的身份，不读会话状态 |
  | `csrf` | 只发防伪令牌 |
  | `health` | 常量 |
  | `/gm/{**path}` | 静态文件 |
  | `server-info`、`tooltip` | 配置与 PVF |
  | 图标两条 | `GmIconPackStore` |
  | `bootstrap` | 就绪标志 |

  豁免委托都不注入 `ICommandBus`（`task-14-boundary.txt` 的 `SendAsync` 逐行清单）。
- **R1 钉住了全部路由。**
  - `Server/src` 里只有 GmWeb 与 `HttpApiApplicationExtensions` 两处映射路由，前缀都在 R1 的筛选范围内，一共 54 条（`task-14-routes.txt`）；
  - R1 断言：豁免集合恰好是那 12 条、CMD 类型互不相同、`gm:` 40 条、`http:` 2 条；
  - 反例用例证明它会拒绝「既没包又没豁免」和「理由为空」两种路由；
  - V1 证明 R1 能分辨过滤器是否挂上。

### 3. 启动作业

- **覆盖了 R-3。** R-3 列的附录 B 启动期工作有四项：EpicBook 迁移、经验存储策略安装、GM bootstrap、建表与迁移。前三项各成一个 `startup:` 作业。建表在 Persistence 单例构造时直连 SQLite，不经工作单元入口，留给 T-004c 判定。这与裁定第 9 条一致，附录 B 也登记了。
- **启动顺序不变。** 两个作业在 `StartingAsync` 的原位置入队并等待完成，前后的内容初始化与日志次序不动；GM bootstrap 仍在它自己的 `StartAsync` 里运行。
- **启动失败时宿主照旧失败。**
  - 执行器记一条 `Gameplay startup:… failed`，然后把同一个异常交还给 `StartingAsync`／`StartAsync`，宿主照旧启动失败；
  - 以宿主的启动令牌入队：启动被取消时作业不执行，得到取消。与今天「启动被取消就不做」同形。
- S1 在本机有 PVF 的条件下实际运行（全量 7 s，本审查复跑通过）；V7、V8 证明它能分辨两种改回直调的写法。

### 4. 冲突用例

- `GmPlayerSamplingHttpTests:14`：5 条断言一字未改。
  - 交错从「跨作业的门闩」改为「在同一条 CMD 的读回调里内联执行」；
  - 回调发生时，`onlineIds` 已经作为参数捕获，所以仍在验证「本页只用一次捕获的在线集合」；
  - 刷新后的 0／0 证明交错确实发生了。
- `AuthenticationConcurrencyTests:10`：断言不变，只改了注释，意图收窄按裁定第 11 条写成「旧前提 → 新前提 → 理由」。
- 两条都登记在 `task-07-conflicts.md` §11，总数加法 1 ＋ 75 ＋ 53 ＋ 1 ＋ 1 ＋ 3 ＋ 2 ＋ 2 ＝ 138 可以复算。

### 5. 探针

- 本族归零：§3.7 813／1660 → 0／0，§3.9 161／407 → 0／0，§3.8 的 10／124 → 0。
- 还原证明完整：快照 sha1 前后都是 `811c910c…`，未跟踪文件逐字节相同，`snapcheck` 1582／1565／4，`grep TestGameplay|GameplayProbe` 为 0，之后 `--no-incremental` 重建。
- Host §3.10 那 20 条「无测试帧」记录的归因：结论可以接受，但支撑它的是结构论证，排除轮 x1 本身分辨不了。见非阻断第 2 条。

### 6. Spec 附录 B

- 五行都只在单元格末尾追加「清单（TASK-14，基准 `2d8cec4d`）」，第一列和原文都没有动。
- 「启动预热」一行没有追加，因为它已随裁定第 1 条移到 TASK-15。
- 新增的「不碰玩法状态的 HTTP 路由」一行符合 RQ-011：列出了 5 条路由，附了安全理由，指明了判据（R1）。这是裁定第 12 条允许的「确需新类别就按 RQ-011 新增一行」。

### 7. 数字与诚实性

- 8130 ＝ 8111 ＋ 19 － 0，逐项目加法与日志第 2502～35219 行的计数一致；通过 8108、失败 2（两条 `DimensionDungeonTests`）、跳过 20，相加闭合。
- 第一次全量的处理：
  - 失败 2 条、慢用例 9 条，都写明了「不作证据」；
  - `InboundExecutorWireTests` 的端口争用给了基准导出的同频对照：隔离 ×8，工作区与基准各有 3 轮失败；
  - 进程停顿的原因写成「推断，未测到」，列进 Remaining 第 3 条，没有当成已解决。这样处理是诚实的。

## 非阻断发现

1. **冲突登记与 Result 对第一次全量 2 条失败的归因不一致。**
   - `task-07-conflicts.md` §11 写两条「都……判为负载下测试进程的停顿」；
   - Result Checks 2 把 `InboundExecutorWireTests` 归为端口争用（同一输出里有 `Address already in use`），只把 `ChannelHostStartupTests` 归为进程停顿。
   - 影响：读冲突登记的人会把一条端口争用当成停顿。建议把 §11 那句改成与 Result 一致。
2. **排除轮 x1 不能单独证明「Host §3.10 那 20 条来自测试的直接总线调用、不是 GM／HTTP 入口」。**
   - 被排除的 10 个类恰好包含几乎全部 GM HTTP 用例（`GmCeraHttpTests`、`GmOfficialMailHttpTests`、`GmEventsHttpTests` 等），所以 x1 得到 0 同样也能用「GM 流量被一起排除了」来解释。
   - 结论今天由另外三项撑着：R1 证明每条用总线的路由都是 CMD；GM／HTTP 的 CMD 整个跑在作业里（作业令牌跨 await 流动）；豁免路由不注入总线。所以结论不错。
   - 建议 Result 的措辞从「排除轮证明」改为「结构论证 ＋ 排除轮一致」。
   - 这一点会被 T-004c 继承：如果翻转之后 Host §3.10 仍有记录，不要拿 x1 当归因依据。
3. **H2 的「执行器同时空闲」半句证据偏弱。** 标记作业跑在一个新建的、与 reader 无关的执行器上，本来就不会被哈希占住。「哈希不占执行器」实际由 H1（真实 HTTP 管线中，每次 `Hash`／`Verify` 都在执行器外）与 H3 证明，所以 AC-005 的对账不受影响。
4. **预缓冲在一种罕见条件下与基准不等价**（代码路径推断，未实测）：
   - 条件：`IHttpMaxRequestBodySizeFeature` 在过滤器运行时已经只读，且请求是超过 32 KiB 的分块传输；
   - 基准：委托不设上限，按服务器默认上限读完并处理；
   - 现在：`EnableBuffering` 的 `bufferLimit` 抛 `IOException`，被重放给委托，委托多半映射为 500。
   - Kestrel 在端点阶段这个特性通常可写，B1 也证明正常路径一致，所以只记为残余风险，供 T-008 归并 GM 路由时参考。
5. **GM 登录第二段的「记录比对」没有专门的变异。** V6 只覆盖启动器一侧；L2 覆盖的是停用，不是口令记录被换。今天没有修改管理员口令的写入方，所以没有实际后果。将来加「修改 GM 口令」时，应补一条对应的用例。
6. **第一次全量 Host 进程停顿的受影响用例里，9 条中有 8 条是 GM HTTP 用例**，而本单元正是把 GM 请求迁到了执行器上。Result 把原因推断为机器换页，并且正式全量、隔离 ×3、新增 ×10 都没有复现，所以不构成阻断。建议 Remaining 第 3 条补一句：受影响用例集中在 GM HTTP，下次再出现时，先排除本单元的路径（例如测试里的同步 `TestGameplay.Run` 与 GM CMD 的共存），再归因于换页。

## 判定

```yaml
kind: rks-review
verdict: approved
```

`approved` 只覆盖本单元结果的局部核对，不代替 TASK-16 的对账收口，也不代替最终的完整变更审查。上面 6 条非阻断项由主控方决定是否在提交前修改措辞，都不需要改生产代码。
