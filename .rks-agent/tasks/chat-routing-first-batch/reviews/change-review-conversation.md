---
kind: rks-review
verdict: approved
---

# Change Review: 1:1 对话窗口（chat-routing-first-batch）

## 目标与范围

- 目标：worktree `E:\Project\DNF\DNF-JP-86-Server-chat`，分支 `feature/61-chat-routing`，`git diff 5c7738f80..120c0adf7`
  （实现提交 `120c0adf7`）。审查时工作树只有未跟踪的 `reviews/`、`verification/`，没有未提交的代码改动。
- 治理：`TaskContract.md` revision 6（重点 SC-2、SC-6、SC-7、SC-11、SC-12）、`spec.md`（RQ-002、RQ-003、RQ-020、
  RQ-040～RQ-046；AC-040～AC-046）、`plan.md` 末尾「Amendment for TaskContract revision 5」、
  `research/conversation-re-20261112.md`。
- 仓库规则：`AGENTS.md`、`Document/Architecture.md`、`ProtocolCommands.md`、`EventsAndNotifications.md`、
  `GameplayExecution.md`、`CurrentCharacterFacts.md`（清理）、`ArchitectureReview.md`。
- 采用的审查准则：reviewing-work 的 Code 审查（完整变更）。`protocol/` 下的协议记录不在本轮范围内。
- 复跑：`dotnet test Server/tests/Dfo.Server.Application.Tests --filter "Chat|SelectedSessionPartyCleanup|GameplayWriteEntryTable"`
  119/119 通过。其余数字（全量构建 0 警告 0 错误、Application 6606、Protocol 2204、Networking 216、Host 243+1 skip）
  采用实现者的证据，没有复跑。

## 结论

对照客户端证据，建会话、窗口内发言、关窗这三条路径都正确，会话身份比较也精确。问题出在清理：
选中会话结束时，下线路径会把 `0x016E` 发给对方，**回选人与选角顶替两条路径不会**。
这违反 RQ-044、AC-044、SC-12，需要修改。

## 阻塞问题

### F-1：回选人、选角顶替旧会话时不发 `0x016E`，会话被静默结束

- 位置：
  - `Server/src/Dfo.Server.Protocol/Game/StateSynchronization/SupplementalSynchronizationMessages.cs:279-285`：只有
    `CleanupNotificationDelivery` 投递 `plan.ConversationDepartures`。它唯一的使用者是
    `OnlineUserConnectionObserver.cs:116/123`，也就是断线清理。
  - `Server/src/Dfo.Server.Protocol/Game/ReturnToSelection/GameReturnToSelectionCommand.cs:123-132`：本地
    `DeliverCleanupAsync` 只投递拥有者通知、城镇同伴和组队，**不读 `ConversationDepartures`**。调用点在 78、90。
  - `Server/src/Dfo.Server.Protocol/Game/SelectCharacter/GameSelectCharacterCommand.cs:761-772`：同样是本地副本，
    同样漏掉。调用点在 154、214、249、255，覆盖 588、623、648 三处 `cleanup.CleanupAsync` 产出的计划。
  - `Server/src/Dfo.Server.Application/Sessions/SelectCharacterLifecycleServices.cs:207-212`：这三条路径都会走到
    `EndAllOf`，会话确实从 owner 中删掉，departure 也写进了计划，只是计划被上面两个副本丢弃。
- 故障场景 1（回选人，必然发生）：
  1. A、B 之间有会话 id=N，双方窗口都开着。
  2. A 回到选人界面。`GameReturnToSelectionCommand` 第 121 行清理 A 的会话，`EndAllOf` 删除会话 N，
     departure 指向 B。
  3. 第 78 行的本地 `DeliverCleanupAsync` 把 departure 丢掉，B 收不到 `0x016E`。
  4. B 的窗口不显示「退場しました」，A 看上去还在。B 在窗口里继续发言，客户端本地照常回显，
     服务端 `Find(N)` 为 null，静默丢弃，A 永远收不到，B 也得不到任何提示。
  5. A 重新进入后再发起会话，B 得到 id=N+1 的第二个窗口，旧窗口仍显示 A 在场。
  RQ-044 明确列出「回选人」，SC-12 要求对方窗口显示该角色已离开，这两条都不满足。
- 故障场景 2（换频道，取决于时序）：
  1. 新 Game 连接登录后选角，旧连接的断线清理还没轮到执行（`login-session-lifecycle.md`：旧连接
     「排队清理身份、关闭传输」，异步执行）。
  2. `GameSelectCharacterCommand` 第 618-628 行以 `previous` 清理旧会话，经第 154 行的本地副本投递，
     departure 丢失。
  3. 之后旧连接的 `CleanupSelectedAsync` 发现 `StandingOf(selected)` 为 null（`OnlineUserConnectionObserver.cs:107`），
     直接返回，不会补发。
  结果与场景 1 相同。SC-12「换频道」这一项只在断线清理先于选角执行时才成立。
- 为什么测试没发现：Application 测试（`SelectedSessionPartyCleanupTests` 新用例）只断言计划里有 departure；
  Protocol 测试（`CleanupNotificationDeliveryTests` 新用例）只覆盖 `CleanupNotificationDelivery`。
  没有任何测试走回选人或选角路径来检查对方是否收到 `0x016E`。
  `CleanupNotificationDelivery` 的类注释写着「six places that deliver one … Here it is one」，与现状不符，
  选角和回选人各有一份手写副本。实现很可能就是被这条注释误导的。
- 最低修正：
  1. 在两个本地 `DeliverCleanupAsync` 中，组队投递之后对每个 departure 执行
     `projections.ProjectAsync(new NotificationAudience.TheseSessions([departure.Remaining.Session]), new ChatConversationLeft(departure), ct)`，
     放在 BestEffort 阶段，顺序与 `CleanupNotificationDelivery` 一致。也可以把这段投递抽成三处共用的协议层函数。
  2. 补测试：回选人时对方收到 `0x016E`（名字为离开者）；选角顶替同账号旧会话时对方收到 `0x016E`。
     两条都在 Application 的命令级测试世界里做。
  3. 修正 `CleanupNotificationDelivery` 的类注释，不再声称它是唯一的投递点。

## 非阻塞问题

- N-1（注释错位）：`SelectCharacterLifecycleServices.cs:203-212`。原注释「Position and its place in a town area, vitals …
  go together」说的是 `runtime.EndExact`，新代码插在注释和这一行之间，注释现在挂在会话块上方。
  建议把会话块移到这段注释之前，或把注释挪回 `runtime.EndExact` 上方。
- N-2（AC-044 后半句只间接覆盖）：「A 重新进入后再发 `0x01A3` 得到新 id」只在 `Leave` 之后断言过
  （`ChatConversationDomainServiceTests` 的 `A_member_leaving…` 和 `SendChatCommandTests.Conversation` 的
  `Closing_the_window…`），清理之后没有直接断言。两条路径共用 `Remove`，风险低。修 F-1 时可以顺带补上。
- N-3（替换过期会话时不通知）：`ChatConversationDomainService.cs:99-101`。同一对角色已有会话、但其中一方的会话已经换代时，
  旧会话被静默删除，请求方原窗口的 id 失效，请求方也收不到 `0x016E`。正常情况下清理会先结束旧会话，
  只有清理失败或被跳过时才会走到这里，比如 F-1 修好之前的路径。不需要单独处理。
- N-4：id 回绕（`next` 到 `uint.MaxValue` 后跳过 0 和 `0xFFFFFFFF`）只能靠阅读代码确认，没有测试。
  项目没有开启 `CheckForOverflowUnderflow`，`next++` 会正常回绕，逻辑正确。

## 核对通过的要点

- `0x016A` 名单顺序（RQ-040、AC-040）：`ChatConversationOpenedHandler` 按收件人组 `[Other(self).Name, self.Name]`，
  编码器写入 `u8 0, u32 id, u8 2, dstr, dstr`。编解码测试、命令测试、真实 TCP 测试（`ChatWireTests` 中 A 收到
  [TalkerB, TalkerA]，B 收到 [TalkerA, TalkerB]）都做了字节级断言，与客户端 `024EAFA0` 把 names[0] 当作对方一致。
- 进行中的会话不重发：`Open` 按无序角色对索引，双方绑定会话都还在时返回 null，处理器不发任何帧。
  测试覆盖了双方各自重复请求的情况。
- mode 43 不回 `0x0011`（RQ-041、RQ-042）：路由之后所有失败（正文长度、发送前检查、非成员、发言者名字）
  都经 `DeclineAsync` 只记日志。mode 43 的路由里没有喇叭和频道检查，`RefuseAsync` 到不了。
  解码器的正文上限是 1024，1024 字节的正文能解码，随后被静默丢弃，不会断开连接。测试覆盖了
  错误 id、非成员、空正文、1024 字节和对方离线，并断言没有 `SendChatRefusal`。
- `0x016F` 只发给对方：`ChatScope.Conversation` 只返回绑定会话仍是当前在世会话的成员，命令再去掉发言者。
  命令测试与真实 TCP 测试都断言发言者收不到 `0x016F`，旁人收不到任何帧。
- 会话身份精确：成员保存完整的 `SelectedSessionIdentity`（含连接 id 和代次，是 record 相等），
  `MemberFor`、`Leave`、`EndAllOf`、范围成员和清理时的对方过滤都按整个 identity 比较。
  后代会话不算成员，有测试覆盖（`generation: 2`，以及 `ChatScopeDomainServiceTests` 中重进后仍被排除）。
  `0x016E` 和 `0x016A` 都用 `TheseSessions` 投递，只到达精确的会话。
- 清理接入（在发得出去的路径上）：`EndAllOf` 放在 `runtime.EndExact`、`ReleaseExact` 之前，此时还能比较对方的绑定会话。
  它包在 `Run` 里，失败会进 `failures`，不会中断后续清理。失败时抛出的 `SelectedSessionCleanupFailure`
  仍然携带含 departures 的计划。`CleanupNotificationDelivery` 把会话通知放在最后的 BestEffort 段，
  通知总线按收件人逐个隔离失败。
- 并发与断言：整个进程只有一个 FIFO 执行器（`GameplayExecution.md`），读名字时的 await 与 `Open` 之间不会插入别的作业。
  会话 owner 内部的短 `lock(sync)` 符合「短锁保留」。三个写入口都调用了 `GameplayWriteAssertion.Writing`，
  `Find` 只读。类型和成员已登记进 `GameplayWriteEntryTableTests`（W/R 表），执行器外调用会被拒绝，有测试。
- 分层：字节布局、wire mode 43 与 aux 的含义、名字和正文上限都在 Protocol 切片 `Game/OneToOneChat/` 和 `SendChat/` 里。
  Data 层只放 id、成员、会话和 departure 记录。两个通知处理器没有构造依赖。CMD handler 只注入
  `*DomainService`、工作单元、投影和日志，在 `CommandHandlerArchitectureTests` 的允许范围内。
  `0x01A3` 经过同一个 `ChatPreSendChecks` 检查点。`ChatScopeDomainService` 只多了 owner 接口依赖，
  自己不保存成员集合（RQ-003、AC-046、SC-7）。
- `0x01A6`（AC-045）：有解码器、命令和薄 handler，测试经真实解码器和分发器断言不回帧。
- RQ-020：拒绝集合加入 33，去掉 43，穷举测试和映射表测试都已更新。

## 需要的后续动作

修正 F-1 并补对应测试后，需要对改动部分做一次独立复审。N-1、N-2 可以在同一轮顺带处理。

---

## 复审第 2 轮：F-1 修正（提交 `07322ce48`）

```yaml
kind: rks-review
verdict: approved
```

### 目标与范围

- 目标：`git diff 07322ce48^..07322ce48`（父提交 `bda5fda28` 只改协议文档）。审查时工作树只有未跟踪的 `reviews/`、`verification/`。
- 范围：F-1 是否完整、正确地解决；N-1 注释修正；新增测试是否有效；修正有没有引入新问题；
  `protocol/game/client-to-server/0x01A5.md` 与 `protocol/game/server-to-client/NOTI-0x016E.md` 对投递点的描述。
  第 1 轮已核对通过的部分不重审，结论沿用。
- 采用的准则：reviewing-work 的 Code 审查（复审未解决问题及其修正带来的回归），文档部分按 Deliverable 准则核对与实现是否一致。
- 证据：`artifacts/chat-routing/verify-conversation-f1.log`（构建 0 警告 0 错误；Application 6606、Protocol 2204、
  Networking 218、Host 243+1 skip，均通过）。复跑 `ChatWireTests`（设置 `PVF_ARCHIVE_PATH`）4/4 通过。
  “撤掉修正后回选人测试失败”采用实现者的说法，没有复现（复现需要改源码，超出本次只写审查记录的授权）。

### F-1：已解决

**投递点是否找全。** 在 `Server/src` 里搜索 `SelectedSessionCleanupNotificationPlan`、`DeliverCleanupAsync`、
`CleanupNotificationDelivery`、`ICommandSelectedSessionCleanup`、`ISelectedSessionCleanupDomainService` 的全部使用者。
清理计划只由 `SelectedSessionCleanupService` 产出，只有 `CommandSelectedSessionCleanup` 这一层包装。投递点只有三处：

| 投递点 | 调用位置 | 修正后 |
|---|---|---|
| 断线：`CleanupNotificationDelivery.DeliverAsync` | `OnlineUserConnectionObserver.cs:116`、`:123` | 改用 `ConversationDepartureNotices.For`，行为不变 |
| 回选人：`GameReturnToSelectionCommandHandler.DeliverCleanupAsync` | `GameReturnToSelectionCommand.cs:78`（正常）、`:90`（清理失败时） | 新增 |
| 选角：`GameSelectCharacterCommandHandler.DeliverCleanupAsync` | `GameSelectCharacterCommand.cs:154`、`:214`、`:249`、`:255`，覆盖 `:588`（同连接旧会话）、`:623`（其他连接旧会话）、`:648`/`:758`（新会话补偿）产出的全部计划 | 新增 |

没有遗漏的使用者。选角补偿清理的是刚选中的新会话。选角命令在单一 FIFO 执行器里运行，中间不会插入 `0x01A3`，
所以补偿计划的 departures 实际为空。它照样走同一个投递函数，即使不为空也不会被丢掉。

**顺序。** 三处都在拥有者通知（Required）、城镇同伴通知、组队通知之后才投递 departures，此时 `order.Delivery` 已经切到
BestEffort，与第 1 轮核对过的断线顺序一致。公共函数 `ConversationDepartureNotices.For` 对每个 departure 只产出一个
`TheseSessions([departure.Remaining.Session])` 加 `ChatConversationLeft`，与原来断线路径内联的写法逐项等价。

**失败隔离。** departures 的投影和同一函数里前面几项放在同一个 try 里，失败处理与相邻项相同：

- 断线：抛出后记录并吞掉。
- 回选人：正常路径抛出时进入外层 catch，关闭连接并重新抛出。清理失败的路径用 `CancellationToken.None` 投递，失败被吞掉，原始失败仍是主因。
- 选角：`:154` 抛出时进入通用 catch 并做补偿；`:214`、`:249`、`:255` 都在各自的 try 里吞掉投递失败。

departures 排在最后，它失败不会让前面的通知发不出去。这与第 1 轮建议的放置方式一致。

**测试。** `Returning_to_selection_tells_the_other_member_of_the_conversation` 走真实 TCP：A 发 `0x0007`，
断言 B 收到的 `0x016E` body 正好是 `u32 id + dstr "ReturnA"`。回选人路径不经过断线清理，所以这条测试正好能抓住 F-1 场景 1。
实现者说撤掉修正后它会失败，这与代码结构相符。

### N-1：已修正

`SelectCharacterLifecycleServices.cs:203-214`：会话块现在跟着自己的注释，“Position and its place in a town area …”
已移回 `runtime.EndExact` 上方。只移动了注释，代码顺序没变。

### 新的非阻塞问题

- **N-5（选角顶替这一投递点没有专门测试）**：`Logging_in_again_on_another_connection_…` 在真实 TCP 下先走断线清理，
  所以去掉 `GameSelectCharacterCommand.cs:772-773` 这两行，现有测试全部仍然通过。第 1 轮的最低修正要求过这条测试，这里只算部分完成。
  不定为阻塞，理由有三：
  - 缺陷已修好。这两行与回选人那两行逐字相同，都调用同一个公共函数。
  - `0x01A5.md` 已经如实写明这个缺口。
  - 这条重新登录的测试本身有价值：它在真实 TCP 下端到端证明了 SC-12「换频道」的常见时序，而且修正后无论先走哪条路径都能通过。

  建议补一条便宜的命令级测试：在 `Server/tests/Dfo.Server.Application.Tests/Game/SelectCharacterLifecycleTests.cs` 的
  `Fixture` 里，用 `fixture.Directory.Select(previous)` 放入一个同连接的旧会话，再让 `fixture.Cleanup.Plan` 返回带一个
  `ConversationDeparture` 的计划。然后断言留守会话收到 `ChatConversationLeft` 投影。可以参照该文件 `:250` 附近
  `A_selection_compensated_after_publication_…` 的写法。这条测试走 `:588 → :154`，能锁住这处投递。
  第 1 轮 F-1 的根因正是手写副本漂移，建议合并前补上。
- **N-6（类注释自相矛盾）**：`SupplementalSynchronizationMessages.cs:223-233`。新加的句子承认回选人和选角“有各自的副本”，
  但紧接着保留的旧句仍然写着“six places that deliver one … Here it is one”。按现在的代码，投递调用有 8 处，走这个服务的只有断线那 2 处。
  `DeliverAsync` 内的 XO-5 注释也只列到组队，没有提到会话。这只影响可读性，不影响行为，下次动这段代码时顺手改掉即可。

### 第 1 轮其余非阻塞问题

- N-2（清理之后重新发起 `0x01A3` 拿到新 id 没有直接断言）：仍未处理，风险评估不变。
- N-3、N-4：代码没变，结论不变。

### 文档

- `0x01A5.md` 与 `NOTI-0x016E.md` 都写成三个投递点（断线 `CleanupNotificationDelivery`、回选人、选角顶替旧会话）都经
  `ConversationDepartureNotices`，在其余清理通知之后发给仍在原选中会话的留守方。这与上表一致。
- `0x01A5.md` 对选角投递点没有专门测试的说明属实。`ChatWireTests` 的范围（关窗、回选人、同账号另一连接重新登录）与新增测试一致。
- 文档没有提到选角补偿也会经过同一个投递函数。补偿计划的 departures 实际为空，不构成失实。

### 结论

F-1 在全部三个投递点都已修正，顺序和失败隔离与各自相邻的通知一致。N-1 已修正，修正本身没有引入行为回归。
本轮核对范围内没有未解决的阻塞问题，结论为 `approved`。N-5 建议在合并前补上。这个结论不能代替完整 TaskContract 的验收验证。
