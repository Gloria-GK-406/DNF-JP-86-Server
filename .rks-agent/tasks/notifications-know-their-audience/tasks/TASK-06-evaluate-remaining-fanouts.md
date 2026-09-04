# TASK-06 — 评估其余两处扇出，按判定规则处理

## Task Unit Contract

- Contract revision: 1
- Goal and deliverables: 对「会话清理通告的尽力送旁人段」与「相邻入场/清理里的 `presence.InArea`」逐个评估并裁定；若判定迁移则一并落地。
- Referenced spec requirements: RQ-013
- Referenced plan sections: T-006, BD-001, BD-002, BD-008, BD-010
- Owned boundary: 视裁定而定；不迁移时只产出裁定与理由
- Permitted files or interfaces: `Server/src/Dfo.Server.Application/Sessions/`、`Server/src/Dfo.Server.Protocol/Game/`、`Server/src/Dfo.Server.Abstractions/Notifications/`、`Server/tests/`
- Dependencies: TASK-05
- Required dependency outputs: 已验证可用的通知总线与一处真实调用方
- Acceptance conditions:
  - 判定规则：一处扇出迁移，当且仅当它能在**收件人集合与帧内容都不变**的前提下迁移，且迁移之后它所在的位置不比现在更难读
  - 会话清理通告的「必送给本人」段不评估——那不是扇出
  - 每处都有明确裁定与依据；**不迁移不是失败**，但理由要写出来
  - 若迁移，需要 `InArea` 人群形态则一并加入封闭层级
  - 全量判据通过
- Task-specific authorization: 不建 Git 检查点、不提交。
- Required methods: reviewing-code
- Required checks: CK-026, CK-027, CK-028
- Required evidence: 两处的逐个评估与裁定；若迁移，迁移前后对照

## Execution Result — Round 1

### 先说一个候选的消失

计划把「相邻入场与清理里的 `presence.InArea`」列为候选之一。逐个看过之后：

**`SessionAdjacentEntryDomainService` 里那处根本不是扇出。** 它用 `presence.InArea` 给**入场者本人**
算「我周围有谁」（`:27-34`，结果进 `TownPresenceEntryState`），是一个人的快照，不是「把一件事告诉
一群人」。它不该被迁移，因为它压根不在这个抽象的范围里。**候选从两个变成一个。**

### 真正的候选：会话清理通告的「尽力送旁人」段

裁定：**迁移。** 而且这一条不是可做可不做——

**AC-004 写的是「枚举把角色解析成连接的位置 → 只有通知服务在做这件事」。**
TASK-05 之后仍有一处不是：`SelectCharacterLifecycleServices.cs:48`
（`peer.Session ?? sessions.FindByCharacter(peer.CharacterId)`，取的正是 `ConnectionId` 用于投递）。
它在 `SelectedSessionCleanupService` 里——一个领域服务。而 Spec 的 Goal 句就是
「领域服务不再需要知道连接的存在」。

**不迁移 = AC-004 不成立、Goal 句只对了一半。** 所以这不是「不迁移也是允许的结果」那一档。

### 判定规则逐条

| 规则 | 结论 |
|---|---|
| 帧内容不变 | 成立。通知映射 `SelectedSessionCleanupNotice → ExpertStoreClosedMessage / TownDepartureMessage` 不动 |
| 差异可逐条列出并指到代码窗口 | 与 TASK-05 同类，逐条列（见下） |
| 迁移后不比现在更难读 | **需要设计**，见下一节。直接拆成两次调用会更难读 |

### 一处必须先解决的设计问题

`CleanupSynchronizationDelivery.Create` 有 **6 个投递点**：`OnlineUserConnectionObserver` 2 处、
`SelectCharacterHandler` 4 处（含补偿路径与两条失败路径）。今天它们各自只有一次
`SendExplicitAsync`，而 `DeliverExplicitAsync` 保证「必送本人的帧先、旁人后」。

**天真的拆法**——每处改成「先 send 本人、再 publish 旁人」——会把那条顺序义务从投递计划的契约
搬进六段手写序列里，其中两段还在失败路径上。那比现在更难读，判定规则第三条不通过。

**改法**：把 `CleanupSynchronizationDelivery` 从静态映射器变成一个注入的投递服务，
顺序留在它里面；六个投递点仍各调一次。

### 人群形态：不加 `InArea`

计划写「若判定迁移，则人群需要 `InArea` 形态，一并加入封闭层级」。**不加**，理由是行为：

`InArea` 形态意味着在场查询发生在**发布时刻**，而清理里 `presence.WithdrawExact(session)` 在 `:86`
就跑了，晚于旁人枚举（`:44`）。把查询推迟到发布时刻会让**谁在场**这个集合按另一个时刻计算。
让清理照旧自己枚举旁人、交出**角色 id**，人群用 `TheseCharacters`——集合仍按清理当时算，
差异面也小一档。

**顺带**：今天 plan 里每个 target 各带一份**内容相同**的 notice 列表（`:50-57` 每轮重建 `values`，
内容不依赖 peer）。改成「一份 notice + 一组角色」是真的简化，不只是搬家。

### 交付

- `SelectedSessionCleanupNotificationPlan` 换形：`BestEffortTargets`（每个目标一份内容相同的 notice 列表）
  → `PeerAudience: IReadOnlyList<CharacterId>` + `PeerNotices: IReadOnlyList<SelectedSessionCleanupNotice>`。
  `SelectedSessionCleanupNotificationTarget` 随之删除。
- `SelectedSessionCleanupService`：不再解析连接，只收集旁人的**角色 id**；notice 列表建一次。
  注释写明为什么在清理当时枚举而不是推迟到发布时刻（`presence.WithdrawExact` 在几句之后就跑了）。
- 新增 `SessionCleanupAnnounced` 通知与 `SessionCleanupAnnouncedHandler`（`StateSynchronization` 切片，
  帧映射本来就在那）。`retryLimit: 0`——一条讲「谁走了」的通知，重试到达时它描述的人已经走了一秒钟，
  变成关于不存在的人的新闻。
- `CleanupSynchronizationDelivery`（静态映射器）→ `ICleanupNotificationDelivery` /
  `CleanupNotificationDelivery`（注入服务）：先送本人、再发布旁人，**顺序留在一个地方**。
  六个投递点各改一行。
- 新增 `Server/tests/Dfo.Server.Protocol.Tests/Game/CleanupNotificationDeliveryTests.cs`（5 条）。

### CK-026：AC-004 现在可以精确陈述了

`grep -rn "FindByCharacter" Server/src --include=*.cs` → 7 行。**为投递取连接的只有两处，都在通知服务：**

```
Application/Notifications/NotificationAudienceResolver.cs:40   人群解析
Application/Notifications/NotificationBus.cs:212               投递前复问在场
```

其余五处**从不读 `ConnectionId`**，逐条：

| 位置 | 用途 |
|---|---|
| `ISessionPresence.cs:85` | 接口声明 |
| `SelectedSessionDomainService.cs:48` | 实现本身 |
| `SelectCharacterLifecycleServices.cs:111` | 判 `is null` 的存活检查 |
| `SelectCharacterLifecycleServices.cs:154`（PlanPvp） | 取会话标识作 `runtimes.GetRequired(owner)` 的键 |
| `World/ExperienceBonusDomainService.cs:31` | 同上，作 `runtimeSessions.TryGet` 的键 |

**`SelectCharacterLifecycleServices.cs:48` 消失了**——那是 TASK-05 之后最后一处「领域服务为投递查连接」。
Spec 的 Goal 句「领域服务不再需要知道连接的存在」现在对**两处扇出**都成立。

### CK-027：迁移前后对照

**帧内容**：`SelectedSessionCleanupNotice → ExpertStoreClosedMessage / TownDepartureMessage` 的映射
一字未动（原 `CleanupSynchronizationDelivery.ToMessage` → 现 `CleanupNoticeMessages.ToMessage`，
只改了归属类名）。判据 `Each_half_becomes_the_frames_it_always_did` 钉住。

**顺序**：迁移前 `DeliverExplicitAsync` 保证必送本人先、旁人后；迁移后由
`CleanupNotificationDelivery` 保证，判据 `The_owner_is_told_before_the_people_around_them` 钉住。

**空半边不投递**：迁移前 `SendExplicitAsync` 带两个空列表是无操作；迁移后显式跳过，
判据 `An_empty_half_is_not_delivered`（三种组合）钉住。**这一条是新加的显式判断**——
publish 一个空人群会解析一次并产生一条空摘要，不再是纯无操作。

**收件人集合与投递语义的差异**：与 TASK-05 **同类同源**，逐条对应
（D-1 / D-2 / D-5 / D-7；D-4 不适用，本通知 `retryLimit: 0`）。
不重复列，因为源头是同一个：解析从「清理当时」推迟到「发布时刻」。
区别是这里的窗口更短——发布紧接在清理返回之后。

### CK-028 全量判据

```
60 + 515 + 52 + 256 + 6 + 223（跳过 2）+ 1012 = 2124 通过 / 2 跳过 / 0 失败
```

TASK-05 收尾为 2119，净 +5（Protocol.Tests 510 → 515）。构建 0 警告 0 错误。

### Executor Self-check — Round 1

- **既有判据的改动**：`SessionGenerationOwnershipTests` 三处、`OnlineUserServiceTests` 一处。
  全部是**同一个断言换一层说法**——从「旁人的连接与它的 notice 列表」变成「旁人的角色与那一份 notice」，
  因为 plan 现在按角色表达。**没有任何断言被削弱或删除**；
  `OnlineUserServiceTests` 那条（清理失败后旁人仍被通知）的意思一字未变，只是断在 plan 上而不是 delivery 上。
- **新增了一处显式行为**：空半边跳过（见 CK-027）。迁移前是隐式无操作。
- **未做**：`InArea` 人群形态未加，理由在上面的评估里（加了会改变集合计算的时刻）。
- **本任务只有一个候选真正被评估**：另一个（`SessionAdjacentEntryDomainService`）经检查不是扇出。

## Independent Quick Review — Round 1

- 裁定：**`changes_required`**
- 被审契约：Task Unit Contract **revision 1**（无修正案）；上游 `TaskContract.md` **revision 2**（含 Amendments）、
  `spec.md` 含 Corrections C-1/C-2/C-3。参照条款：RQ-004 / RQ-013 / AC-004 / AC-013、
  plan 的 T-006 / BD-001 / BD-002 / BD-008 / BD-010 与 Requirement Coverage 的 RQ-013 行；仓库约定 `AGENTS.md`。
- Owned diff 身份：工作区 `.worktrees/notifications-know-their-audience`，基线
  `b05d47e4e587d6ab85312ef7f5a3802716805e3c`，未提交。`git status --porcelain` 共 20 项，
  本轮声称的 9 个文件全部在内。
- 评审者与执行方不同上下文；**未改动任何实现文件或任务控制状态**。变异一律在验完后还原并核哈希。

### 核过的命令与证据

| 项 | 命令 / 动作 | 结果 |
|---|---|---|
| 全量 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`（落盘 2432 行，未用 grep 滤明细） | 退出码 0，七个项目全部「测试运行成功」 |
| 计数 | 60 + 515 + 52 + 256 + 6 + 223（跳过 2）+ 1012 | **= 2124 通过 / 2 跳过 / 0 失败**，与交付声称逐项相符；`警告` 0 次 |
| 净增 | Protocol.Tests 510 → 515 | +5，正是新文件的 2 个 Fact + 3 行 Theory |
| SC-11 | `git diff b05d47e4 -- "*CommandHandlerArchitectureTests*"` 与 `git status --porcelain --` 同 | 均为空；mtime `11:55`（检出批次）对比改动文件的 `17:00` |
| 变异 M1 | `DeliverAsync` 内两块顺序对调 | Protocol.Tests **失败 1**：`The_owner_is_told_before_the_people_around_them` |
| 变异 M2 | 两处空判断 `> 0` 改 `>= 0` | **失败 3**：`An_empty_half_is_not_delivered` 三行全红 |
| 变异 M3 | `new SessionCleanupAnnounced(plan.PeerNotices)` 改 `(plan.RequiredOwnerNotices)` | **失败 1**：`Each_half_becomes_the_frames_it_always_did` |
| 变异 M4 | `CleanupNoticeMessages.ToMessage` 的 `TownDeparture` 改映射到 `ExpertStoreClosedMessage` | **全量 2124 全绿**（见 F-1） |
| 还原 | `sha256 SupplementalSynchronizationMessages.cs` | `e842b8ab4ddaaa1dc48739c0f47c87e252a690cf09d886d58f589d1fe5f29198`，与动手前逐字节相同 |

### 成立的部分（逐条核过，不是采信）

**裁定本身（问题 1）全部成立。**

- (a) 基线 `git show b05d47e4:.../SelectCharacterLifecycleServices.cs` 确为
  `var peerSession = peer.Session ?? sessions.FindByCharacter(peer.CharacterId);`，
  其 `ConnectionId` 直接构成 `SelectedSessionCleanupNotificationTarget`——领域服务为投递取连接，
  属实；现已消失。
- (b) `grep -rn "FindByCharacter" Server/src --include=*.cs` 复跑得同样 7 行。
  逐条读过五处「不读 ConnectionId」：`ISessionPresence.cs:85`（声明）、
  `SelectedSessionDomainService.cs:48`（实现）、`SelectCharacterLifecycleServices.cs:111`
  （`is null` 存活检查，紧跟 `containers.Clear`）、`:154`（`runtimes.GetRequired(owner)` 的键）、
  `ExperienceBonusDomainService.cs:31`（`runtimeSessions.TryGet(session, ...)` 的键）——**五处皆属实**。
  另跑了更宽的 `grep -rn "ConnectionId" Server/src/Dfo.Server.Application --include=*.cs | grep -v /Notifications/`：
  余下全是「连接到用户」的反向（`OnlineStateRegistry`、`FindByConnection`），或
  `SelectCharacterLifecycleServices.cs:120` 把手上已有的 `session.ConnectionId` 装进计划的必送半边
  ——那是被契约明文排除在评估之外的一段，且不是一次解析。**AC-004 的「只有通知服务在做这件事」成立。**
  *一处口径备注（非缺陷）*：`:154` 与 `ExperienceBonusDomainService.cs:31` 传的是整个
  `SelectedSessionIdentity`，该记录本身带 `ConnectionId` 并参与字典比较。「从不读 ConnectionId」
  在「不为投递取连接」这一层成立，字面上则不是；建议措辞收一点。
- (c) 读过 `SessionAdjacentEntryDomainService.cs:27-39`：`InArea` 的结果被折成
  `PresentCharacterState` 列表放进**入场者本人**的 `TownPresenceEntryState`，收件人只有一个人，
  且全程不查连接。**「不是扇出」成立**，候选从两个减为一个的处理是对的。

**问题 3（空半边）成立，且比自检说得更干净。** 读了
`TouchSocketNetworkMessageSender.DeliverExplicitAsync:69-105`：两个空列表时
`ValidateExplicitDelivery` 通过（只判 null），两个 `foreach` 都不进，**既不查连接也不抛**；
`GameMessageSender.SendExplicitAsync` 也只是转投。所以迁移前确是纯无操作。
新加的显式跳过因此**是为保住原行为而加的护栏**（不加则 publish 会解析一次并产出一条空摘要），
不是行为差异。CK-027 把它列为「新加的显式判断」如实，分类也正确。

**问题 5（顺序义务）成立。** `grep -rn "CleanupSynchronizationDelivery" Server/` 零命中；
六个投递点各只调一次 `DeliverAsync`，无任何手写两次调用：
`OnlineUserConnectionObserver.cs:95,102`、`SelectCharacterHandler.cs:104,136,156,162`。
顺序义务确实只剩 `CleanupNotificationDelivery` 一处，且 M1 证明它被钉住。

**问题 4（新判据有牙）成立。** M1/M2/M3 三次变异分别红 1/3/1 条，红的正是对应用例，无连坐。

**问题 8（SC-11）成立。** `CommandHandlerArchitectureTests.cs` 与基线逐字节相同。

**问题 2 的一半成立。** `SessionGenerationOwnershipTests` 三处逐条读过，都是「连接换角色」的等价换写，
断言强度未降（原 `Assert.Single(BestEffortTargets)` 的「恰好一个」由
`Assert.Equal([peer.CharacterId], PeerAudience)` 承接）。
`OnlineUserServiceTests` 那条**就它自己要证的事而言没有变弱**：它证的是「清理失败后两个半边仍被交出去、
里面装的是谁与什么」，现在断在 `ICleanupNotificationDelivery` 这个接缝上，
`OwnerConnectionId`、通知种类、旁人角色四项一项不少。把断言挪到接缝上本身不是削弱——
**削弱的是接缝另一头**，见 F-1。

### 发现

#### F-1（阻断）｜有一条断言是**被删掉的，不是被搬走的**：旁人半边的帧映射现在无人钉

自检写「没有任何断言被削弱或删除」，CK-027 写「帧内容……判据
`Each_half_becomes_the_frames_it_always_did` 钉住」。**两句都不成立。**

基线 `OnlineUserServiceTests` 里有这一行，本 diff 删除且未被接住：

    -        Assert.Equal("TownDepartureMessage", Assert.Single(target.OrderedMessages).GetType().Name);

`Each_half_becomes_the_frames_it_always_did`（`CleanupNotificationDeliveryTests.cs:47-73`）
**只对本人半边断到帧**（`"ExpertStoreClosedMessage"`，`:60-62`）；旁人半边断的是
`SessionCleanupAnnounced.Notices` 的**种类**（`:68-72`），到不了帧。
而从 notice 到帧要经过 `SessionCleanupAnnouncedHandler.ComposeAsync`
（`SupplementalSynchronizationMessages.cs:193-204`）——
`grep -rn "SessionCleanupAnnounced" Server/tests` 显示**这个处理器全仓零判据**。

变异证明（M4）：把 `CleanupNoticeMessages.ToMessage` 的
`SelectedSessionCleanupNoticeKind.TownDeparture` 改映射到 `ExpertStoreClosedMessage`
（`SupplementalSynchronizationMessages.cs:214`），**全量 2124 条全绿、退出码 0**。
也就是说今天把「有人走了」这条帧换成「店关了」，判据不会有任何反应；而迁移**前**这个变异会让
上面那行断言红。这是本轮唯一一处判据强度实际下降的地方，且正落在 CK-027 声称被钉住的那一项上。

**要求的整改（局部可修）**：给旁人半边补回到帧的断言——把
`Each_half_becomes_the_frames_it_always_did` 走一遍 `SessionCleanupAnnouncedHandler.ComposeAsync`
并断 `[ExpertStoreClosedMessage, TownDepartureMessage]` 的类型与顺序，或另加一条处理器判据。
补完后复跑 M4 确认它会红。同时改掉自检里「没有任何断言被削弱或删除」这句与 CK-027 的「钉住」措辞。

#### F-2（阻断）｜差异清单：「同类同源、不重复列」不满足 revision 2 的口径

CK-027 写明「同 CK-024 的口径」，而该口径经契约 revision 2 与 spec 的 C-3 定死为
**「每一处差异都已逐条列出、每条可指到具体代码窗口」，是一条完整性要求**。
C-3 还写明这条今天「真正靠的是独立评审，不是任何自动化判据」，四轮里清单分别缺 3/2/1/1 条。

交付以「与 TASK-05 同类同源……不重复列」代替清单。我核过**机制确实同源**
（`NotificationAudienceResolver.cs:40` 只走 `FindByCharacter`，与迁移前
`peer.Session ?? FindByCharacter` 的取舍差别正是 D-1/D-2；`NotificationBus.cs:176/211` 的
`IsStillHere` 正是 D-5；`AttemptAsync` 的 `PerRecipientTimeout` 正是 D-7；
`retryLimit: 0` 使 D-4 不适用——这几条判断都成立）。
**但代码窗口不同源**：TASK-05 的 D-1/D-2 指的是 `SelectCharacterPhaseCommands.cs:91` 与
`SelectCharacterLifecycleServices.cs:100`，和本处扇出无关。本处的窗口是另一组，而且**有六个**：
人群在 `SelectCharacterLifecycleServices.cs:53-56` 于清理当时枚举，经 `:86 WithdrawExact`、
`:105 ReleaseExact`、`:117-120` 返回，再到 `OnlineUserConnectionObserver.cs:95,102` 与
`SelectCharacterHandler.cs:104,136,156,162` 六个解析时刻——其中 `:136` 与 `:162` 在 catch 里、
`:156` 在补偿命令返回之后，「发布紧接在清理返回之后」这句对 `:95` 成立，
对失败路径与补偿路径要单独说。按 C-3 的读法，让读者能指到代码正是这条要求的**全部执行力**，
交叉引用另一个任务的行号把它挡掉了。

**要求的整改（局部可修，不改代码）**：在本 Task 文件里补一份属于本次迁移的清单。
每条允许写「机制同 TASK-05 的 D-n」，但**必须带自己的代码窗口**，六个投递点各自的解析时刻要能对上。

#### F-3（记录，非阻断）｜owned diff 越出契约的 Permitted files，且未被记

契约 revision 1 的 Permitted files 是四条：`Application/Sessions/`、`Protocol/Game/`、
`Abstractions/Notifications/`、`Server/tests/`。实际改动里有两个文件在四条之外：

- `Server/src/Dfo.Server.Data/World/SessionEntryModels.cs`（计划记录换形、删 `...Target`）
- `Server/src/Dfo.Server.Protocol/ProtocolModule.cs`（`ICleanupNotificationDelivery` 注册与 `retryLimit: 0`）

引用的 BD-001/002/008/010 也都不覆盖 `Dfo.Server.Data`。两处改动本身都是这次迁移绕不开的、
而且是最小的，我不认为该退回；但「Owned boundary: 视裁定而定」不等于 Permitted files 自动放宽，
执行结果与自检里都**没有提到这次外扩**。请如实补记（或按流程走一次边界扩展）。

#### F-4（记录，非阻断）｜一个用例名与一个替身现在都名不副实

`OnlineUserServiceTests.DisconnectSelectedSession_AttemptsResolvedNotificationsWhenCleanupFails`：
这条路径上**什么都不再 resolve**（解析已移到通知服务），名字里的 `Resolved` 已失真；
同时它仍构造 `RecordingExplicitSender` 并传进去，但**没有任何断言再读 `sender.Deliveries`**——
与 TASK-05 快审记下的 N-5「双重死亡」是同一族。不阻断，供最终评审决定是否一并收拾。

### 结论

判定「迁移」的理由（不迁移则 AC-004 不成立）经核**成立**；候选减为一个的判断**成立**；
迁移本身的设计（顺序留在一处、不加 `InArea` 形态、人群用 `TheseCharacters`）经核**站得住**；
计数、SC-11、新判据的牙齿**全部属实**。

阻断的是两条**证据侧**的缺口：F-1 是一条真实存在的判据强度回退（且正好落在被声称「钉住」的那一项上，
已用变异证明全量不会红），F-2 是本任务欠一份自己的差异清单。两条都局部可修，
不需要推翻裁定，也不需要改设计。

**verdict: `changes_required`**

## Controller Disposition — after Quick Review Round 1

`task-rework`。两条阻断都成立，两条非阻断一并处理。

### F-1：那不是搬走，是删掉了一条断言

基线里 `Assert.Equal("TownDepartureMessage", ...)` 断的是**旁人半边的帧**。迁移后
`Each_half_becomes_the_frames_it_always_did` 只把旁人半边断到 notice 种类，
`SessionCleanupAnnouncedHandler` 全仓零判据——**旁人的 notice 变成哪个帧，没人管了**。

快审的变异是决定性的：把 `TownDeparture` 映射到 `ExpertStoreClosedMessage`，**全量 2124 全绿**。
迁移前那个变异会红。所以我自检里写的「没有任何断言被削弱或删除」**是假的**，
CK-027 里「帧映射一字未动，判据钉住」那句也只对了一半——映射确实没动，但钉住它的判据被我删了。

补两条：

- `The_peers_notices_become_the_frames_they_always_did`：直接问 `SessionCleanupAnnouncedHandler`，
  断言 `[ExpertStoreClosedMessage, TownDepartureMessage]` 两个帧类型名与顺序。
- `A_notice_kind_nobody_mapped_is_named`：映射是带 throwing default 的 switch，
  枚举里加了种类却忘了映射时，第一个人离开就会发现而不是永远不发现。

变异复核：

```
变异  SelectedSessionCleanupNoticeKind.TownDeparture => new ExpertStoreClosedMessage(...)
→ 失败 The_peers_notices_become_the_frames_they_always_did（7 条中恰好红这一条）
还原后 → 7 条全绿；SupplementalSynchronizationMessages.cs md5 dd7d0abf... 与变异前一致
```

### F-2：这处扇出的差异清单，带自己的代码窗口

「与 TASK-05 同类同源，不重复列」不满足契约 revision 2 的口径——那条口径的全部执行力就是
**读者能指到代码**，而 TASK-05 的行号指向 PvP 路径，与这里无关。补如下。

差异的**源头**与 TASK-05 相同（解析从「计划构建当时」推迟到「发布时刻」），但**窗口不同**：
本处的计划由 `SelectedSessionCleanupService.CleanupAsync` 构建，旁人枚举在
`SelectCharacterLifecycleServices.cs:53-57`；投递有**六个时刻**，其中**四个**在 catch 里、
一个在补偿命令之后：

| # | 投递点 | 性质 |
|---|---|---|
| 1 | `OnlineUserConnectionObserver.cs:95` | TCP 断开，正常路径 |
| 2 | `OnlineUserConnectionObserver.cs:102` | TCP 断开，**清理失败后**（catch 内） |
| 3 | `SelectCharacterHandler.cs:104` | 选人，顶替旧会话 |
| 4 | `SelectCharacterHandler.cs:136` | 选人，**工作流失败后** |
| 5 | `SelectCharacterHandler.cs:156` | **补偿命令之后** |
| 6 | `SelectCharacterHandler.cs:162` | 选人，**清理失败后**（catch 内） |

差异逐条：

- **C-D1｜旁人在枚举之后被顶替、同角色重新选入。** 枚举在 `:53-57`；解析在
  `NotificationAudienceResolver.cs:40`，发生在上表任一投递点。迁移前取的是
  `peer.Session ?? FindByCharacter`（在场记录里带的会话），迁移后取当前会话。
- **C-D2｜旁人所在账号换成了另一个角色。** 迁移前仍送到在场记录带的连接；
  迁移后 `FindByCharacter(旧角色)` 答 null，该旁人整个不出现在收件人里。
- **C-D5｜解析之后、轮到之前换连接。** `NotificationBus.cs:212` 的在场复问判 `Gone`，不发不重试；
  迁移前会送到仍然开着的旧连接。
- **C-D7｜每收件人 2 秒期限。** `NotificationBus.cs:166-167`；迁移前
  `TouchSocketNetworkMessageSender:91` 无期限。因本通知 `retryLimit: 0`，超时的旁人被静默丢弃。
- **D-4 不适用**：`retryLimit: 0`。
- **窗口比 TASK-05 更宽的一点**：真正远的是投递点 **3 与 4**——它们的计划在
  `PrepareSelectedCharacterSessionCommand` 里建好之后，要跨过 `PrepareAsync` / `PlaceAtSpawnAsync` /
  `PrepareLeaseAsync` 三步（`SelectCharacterPhaseCommands.cs:106-112`）才被投递。
  失败路径（2 / 5 / 6）本身不必然更远。**这一条是本处独有的，不是 TASK-05 的复制。**
  （初版把「更宽」归给失败路径，是 Round 2 快审 F-6 更正的。）
- **不可观察**：空半边不再进入投递（新增显式判断，见 CK-027）；发布前三步不在逐收件人守卫内
  （同 TASK-05 的 D-6，位置在 `NotificationBus.PublishAsync` 开头）；摘要被丢弃
  （六个投递点都不读 `DeliverAsync` 的返回——它本来就是 `ValueTask`，不带摘要）。

### 两条非阻断

- **owned diff 越出契约的 Permitted files**：`Dfo.Server.Data/World/SessionEntryModels.cs` 与
  `ProtocolModule.cs` 不在 TASK-06 契约的 `Permitted files` 里，而我的自检没提。
  两处都是迁移的必要构成（计划换形、接线），但**契约边界是契约边界**——如实记在这里，
  并在下方追加一份契约修正案。
- **两处名不副实**：`OnlineUserServiceTests` 里 `..._AttemptsResolvedNotificationsWhenCleanupFails`
  这个名字里的 "Resolved" 现在不成立（旁人不再在这一层被解析），而该用例仍构造
  `RecordingExplicitSender` 却无人断言它。**本轮不改**：改名与删替身都会给 CK-025 的
  「既有判据未被迁就」逐条归因多出无关改动，而这两处不影响任何断言的效力。如实留档。

## Task Unit Contract Amendment — Revision 2

- Contract revision: 2
- 改动范围：仅 `Permitted files or interfaces`。受理条件与判定规则不变。

原文只列了 `Server/src/Dfo.Server.Application/Sessions/`、`Server/src/Dfo.Server.Protocol/Game/`、
`Server/src/Dfo.Server.Abstractions/Notifications/`、`Server/tests/`。

实际迁移必须动的还有两处，补进来：

> - `Server/src/Dfo.Server.Data/World/SessionEntryModels.cs` —— 清理计划本身的形状
>   （`BestEffortTargets` → `PeerAudience` + `PeerNotices`）。计划以角色表达是这次迁移的全部要点，
>   而计划类型住在 Data 层。
> - `Server/src/Dfo.Server.Protocol/ProtocolModule.cs` —— 通知处理器与投递服务的接线。
>   AGENTS.md 写明「新协议只改一处接线：ProtocolModule」，所以这一处是约定要求的，不是越界。

**修正案是在越界发生之后补的**，不是事先的。记在这里而不是悄悄扩大边界。

### Round 2 全量判据

完整输出落盘，七个项目全部 `测试运行成功`，`测试运行失败` 出现 0 次：

```
517 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 1012 = 2126 通过 / 2 跳过 / 0 失败
```

Round 1 为 2124，净 +2（F-1 补的两条）。构建 0 警告 0 错误。

### Executor Self-check — Round 2

- **生产代码本轮零改动。** F-1 是判据、F-2 是证据、两条非阻断一条是契约修正案一条是留档。
- 判据改动：新增 2 条（旁人半边的帧映射、未映射的 notice 种类）。
- **上一轮我写错的那句要留在原处**：自检里的「没有任何断言被削弱或删除」是假的，
  已在 F-1 的处置里点明，不回去改原文——改掉就看不见我曾经这么说过。

## Independent Quick Review — Round 2

- 裁定：**`changes_required`**
- 被审契约：Task Unit Contract **revision 2**（含 `Task Unit Contract Amendment — Revision 2`，仅改 `Permitted files or interfaces`）。
  只审 Round 2 的整改（Controller Disposition、修正案、Round 2 全量与自检），Round 1 的段落一字未动。
- Owned diff 身份：工作区 `.worktrees/notifications-know-their-audience`，基线
  `b05d47e4e587d6ab85312ef7f5a3802716805e3c`，未提交。`git status --porcelain` 共 20 项，
  TASK-06 自己的 9 个文件全部在内。
- 评审者与执行方不同上下文；未改动任何实现文件或任务控制状态。变异在验完后逐字还原并核哈希。

### 核过的命令与证据

| 项 | 命令 / 动作 | 结果 |
|---|---|---|
| 全量 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`（落盘 2434 行，未用 grep 滤明细） | 退出码 **0**，七个项目全部「测试运行成功」 |
| 计数 | 517 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 1012 | **= 2126 通过 / 2 跳过 / 0 失败**，与声称逐项相符 |
| 警告 | 全量输出里「警告」出现次数 | **0** |
| 净增 | Protocol.Tests 515 → **517** | +2；该文件现 4 个 `[Fact]` + 1 个 `[Theory]`×3 = 7 例，Round 1 为 5 例 |
| 变异 M4（重做） | `CleanupNoticeMessages.ToMessage` 的 `TownDeparture` 改映射到 `ExpertStoreClosedMessage`（`SupplementalSynchronizationMessages.cs:214`） | **退出码 1，失败恰好 1 条**：`CleanupNotificationDeliveryTests.The_peers_notices_become_the_frames_they_always_did` |
| M4 异常根 | `Assert.Equal() Failure: Collections differ`，Expected `["ExpertStoreClosedMessage","TownDepartureMessage"]` / Actual `["ExpertStoreClosedMessage","ExpertStoreClosedMessage"]`，栈根 `CleanupNotificationDeliveryTests.cs:line 90` | 红的正是新判据，无连坐 |
| 还原 | `sha256 SupplementalSynchronizationMessages.cs` | `e842b8ab4ddaaa1dc48739c0f47c87e252a690cf09d886d58f589d1fe5f29198`，与动手前及 Round 1 记录的还原哈希**逐字节相同** |
| SC-11 | `git diff b05d47e4 -- "*CommandHandlerArchitectureTests*"`、`git status --porcelain -- "*CommandHandlerArchitectureTests*"` | 均为空 |

### 成立的部分（逐条自己动手核过）

**F-1 已消除，且比原断言更强。** M4 重做的结果决定性：迁移前会红、Round 1 全绿、Round 2 恰好红一条，
红的就是补的 `The_peers_notices_become_the_frames_they_always_did`。被删掉的原断言
（`Assert.Equal("TownDepartureMessage", Assert.Single(target.OrderedMessages).GetType().Name)`）确实被接回来了：
新判据走 `SessionCleanupAnnouncedHandler.ComposeAsync`，断 `["ExpertStoreClosedMessage","TownDepartureMessage"]`
两个帧类型名**与顺序**——原断言只钉一个帧，新判据钉两个帧加顺序。
接缝换了（从 observer 路径换成 handler 单测），但丢掉的那一类断言（notice → 帧）确实回来了。
第二条 `A_notice_kind_nobody_mapped_is_named` 钉 throwing default，判据本身正确
（见 F-5——那里说的是它的注释，不是它的断言）。

**F-2 的锚定要求已满足。** 六个投递点的行号**逐个核对，全部精确**
（`grep -n "cleanupDelivery.DeliverAsync"`：`OnlineUserConnectionObserver.cs:95,102`、
`SelectCharacterHandler.cs:104,136,156,162`）。性质描述属实：

- `:102` 在 `catch (SelectedSessionCleanupFailure)` 内 ✔
- `:136` 在 `catch (SelectCharacterPhaseFailure)` 内 ✔
- `:162` 在 `catch (Exception error)` 里嵌套的 `catch (SelectedSessionCleanupFailure)` 内 ✔
- `:156` 紧跟 `CompensateSelectedCharacterSessionCommand` 返回之后 ✔（补一句：它同时也在
  `catch (Exception error)` 内，表里只写了「补偿命令之后」；这是漏说而非说错，方向对执行方不利，不构成缺陷）

每条差异都能指到**本处**的代码窗口，无一处再交叉引用 TASK-05 的行号：
`C-D1 → NotificationAudienceResolver.cs:40`（`sessions.FindByCharacter` 就在该行）；
`C-D5 → NotificationBus.cs:212`（`IsStillHere` 的 `FindByCharacter` 在 211-213）；
`C-D7 → NotificationBus.cs:166-167`（`CreateLinkedTokenSource` + `CancelAfter(PerRecipientTimeout)` 正是这两行）；
枚举窗口 `SelectCharacterLifecycleServices.cs:53-57`（`foreach (var peer in presence.InArea(` 在 `:53`）。
`D-4 不适用（retryLimit: 0）` 经核成立：`Requeue`（`NotificationBus.cs:113`）只在 `used < RetryLimit` 时入队，
0 意味着永不重试，因此 TASK-05 里「重试导致重复到达」那条在此确实不存在。
（唯一一处小偏差：`TouchSocketNetworkMessageSender:91` 实际的无期限 send 在 `:90`，差 2 行，不影响可指认。）

**取消令牌（问题 2d 的一半）：迁移前后无差别，不构成漏列。** 逐点比对基线：
基线 `SelectCharacterHandler.cs:101` 用 `cancellationToken`，`:136/:159/:168` 与
`OnlineUserConnectionObserver.cs:97/107` 用 `CancellationToken.None`；
迁移后 `:104` 用 `cancellationToken`，其余五处仍 `CancellationToken.None`。
**六个点各自传的是哪个令牌，一个都没变。**

**契约修正案（问题 3）三问皆立。**

- (a) 越界属实。契约 revision 1 的四条 Permitted files 之外，TASK-06 自己改的 9 个文件里恰有两个：
  `Dfo.Server.Data/World/SessionEntryModels.cs`、`Dfo.Server.Protocol/ProtocolModule.cs`
  （工作区里其他越界文件如 `Abstractions/Sessions/`、`ApplicationModule.cs`、`Application/Notifications/`
  按 mtime 分属 12:04–15:00 的上游任务批次，不是 TASK-06 的 owned diff）。
  修正案如实——它自己明写「修正案是在越界发生之后补的」，没有伪装成事前授权。
  `ProtocolModule` 那条的依据也核过：`AGENTS.md:35`「`ProtocolModule` 是唯一接线点」、
  `:340`「新协议只改一处接线：`ProtocolModule`」——是仓库约定要求的，措辞属实。
- (b) 范围恰当。修正案只动 `Permitted files or interfaces`，判定规则与受理条件一字未改，逐句比对过。
- (c) **与 TASK-05 那次不是同一类。** `Permitted files` 是**边界声明**（能碰哪里），
  成功条件是**评分规则**（碰完了算不算过）。执行方自行改写成功条件等于自己给自己判分，
  这是 TASK-05 被判越权的原因；把边界补到实际必须碰的两个文件、同时**不动**评分规则，
  不产生自我评分。加上两处改动机械必需、最小、且已如实披露，快审层面**可接受**。
  但它终究是一次事后的范围扩张，**应由所有者在最终评审时明确认可一次**，不是执行方自己能封的口——
  记在这里供最终评审处置，不作为本轮阻断。

**两条留档（问题 4）的理由部分成立，接受不改。**
`RecordingExplicitSender` 删不掉：`OnlineUserConnectionObserver` 仍以 `IGameMessageSender gameSender`
为构造参数并在 `:47` 的 `SendNotiAsync` 用它，`CreateObserver` 必须传一个 sender，
留用现成替身是最小选择——「无人断言」是该用例自身关注点收窄的结果，不是死代码
（这与 TASK-05 快审 N-5 的「双重死亡」不完全同族）。
改名那半边理由较弱（纯改名的归因成本很低），但确实不影响任何断言效力，收益也确实小。
非阻断，同意留给最终评审。

**Round 2 生产代码零改动（问题 6）成立。** 三条互相印证：
① `SupplementalSynchronizationMessages.cs` 的 sha256 与 Round 1 快审记录的还原哈希逐字节相同；
② 其余生产文件 mtime 最晚为 `17:01:35`，而本轮唯一被编辑的测试文件
`CleanupNotificationDeliveryTests.cs` 为 `17:22:22`，晚于全部生产文件；
③ 全量净增恰好 +2 且全在 Protocol.Tests，与新增的两个 `[Fact]` 一一对应，无其他数目变动。

### 发现

#### F-5（阻断）｜差异清单漏了一整族：**旁人半边的帧映射失败，从「抛出」变成了「无声吞掉」**——而本轮新加的那条判据的注释正好断言了相反的事

清单里的 C-D1/C-D2/C-D5/C-D7 全属于「人群解析时刻推迟」这一族，都对。
但这次迁移还改了**异常可见性**这一族，清单一条都没提，方向是**丢失失败信号**：

**迁移前**，`CleanupSynchronizationDelivery.Create(plan)` 在调用点**同时且提前**把
**两个半边**的 notice 都映射成帧（基线 `SelectCharacterHandler.cs:103/138/161/170`、
`OnlineUserConnectionObserver.cs:96/105`）。一个没映射的 notice 种类会让
`InvalidDataException` 在**任何东西发出去之前**从 `Create` 抛到调用点——
在基线 `:101` 那处会一路冒到外层 `catch (Exception error)` 触发补偿。

**迁移后**只有**本人半边**仍是提前映射（`SupplementalSynchronizationMessages.cs:247`，
在 `GameExplicitMessageDelivery` 的实参里同步求值，抛出照旧）。
**旁人半边的映射搬进了 `SessionCleanupAnnouncedHandler.ComposeAsync`（`:202`）**，
而它是被 `NotificationBus.AttemptAsync` 在 `try` 里调的（`NotificationBus.cs:179-180`）：

- `NotificationComposer<T>.ComposeAsync`（`NotificationComposer.cs:24-28`）是纯转发，不拦截；
- 异常落进 `AttemptAsync` 的裸 `catch`（`NotificationBus.cs:196-201`）→ `NotificationOutcome.Failed`；
- `retryLimit: 0` 使 `Requeue`（`:113`）不入队 → 没有第二次；
- `NotificationBus` **不注入任何 logger**（`Notifications/` 下只有 `NotificationRetryDrainService.cs:76` 有 `LogError`，
  管的是重试整趟失败，不是单收件人 `Failed`）；
- 摘要也没人看：`CleanupNotificationDelivery.DeliverAsync` 返回 `ValueTask`，六个投递点无一读返回值。

**结果：旁人半边的组帧异常在生产中彻底无声。**（**已过时**——见文末的统一前向注记） 这正是 `retryLimit: 0` 在本处独有的第二个后果
（清单只写了它的第一个后果——超时旁人被静默丢弃，即 C-D7），也正是问题 2(d) 要找的那类本处独有差异。

更要紧的是，本轮为消除 F-1 而新加的
`A_notice_kind_nobody_mapped_is_named` 的注释（`CleanupNotificationDeliveryTests.cs:97-98`）写着：

> a kind added to the enum and forgotten here is found the first time somebody leaves rather than never.

**这句对旁人半边是假的**：本人半边只装 `ExpertStoreClosed`，新加的种类几乎必然落在旁人半边，
而旁人半边的那次抛出会被上面这条链路吃掉——恰恰是 `never`。
判据本身没问题（它直接问 handler，handler 确实抛），错的是注释对生产行为的外推。
这与 Round 1 阻断 F-1 的形状同类：**一句声称覆盖/保障的话，代码不支持**，且这次就长在为修 F-1 而加的用例上。

**要求的整改（局部可修，不需改生产行为，也不需改设计）**：

1. 在 C-D 清单里补一条本处独有的差异，带自己的代码窗口：旁人半边的组帧异常由
   「`Create` 在调用点抛出」变为「`AttemptAsync` 的 `catch`（`NotificationBus.cs:196-201`）→ `Failed`
   → `retryLimit: 0` 不重试 → 无日志、无人读摘要」，并说明本人半边（`:247`）不受影响，
   这个不对称是本次迁移引入的。
2. 改掉 `CleanupNotificationDeliveryTests.cs:97-98` 那句注释，让它只说这条判据保证了什么
   （映射表有 throwing default、种类会被点名），不要外推成生产里「第一个人离开就会发现」。

至于「是否顺带把这个无声吞掉修掉」（例如让摘要被读一次、或给 `Failed` 一条日志），
属于设计取舍，留给最终评审，不在本轮要求内。

#### F-6（记录，非阻断）｜「窗口更宽」的归因指错了地方，但结论方向没错

清单末尾写「投递点 2 / 4 / 6 在失败路径上，5 在补偿之后——这些时刻距离枚举更远」。逐点核过之后：

- **2 与 6**：拿的是 `cleanupFailure.NotificationPlan`，而该计划在 `CleanupAsync` 的
  `SelectCharacterLifecycleServices.cs:119` 构造、`:122` 立刻抛出，随即被相邻的 catch 接住投递。
  枚举→投递的**间隔与成功路径几乎相同**，只多一次栈展开。「更宽」在这两点上**不成立**。
- **5**：`compensated.Notifications` 由补偿命令内部的 `CleanupAsync` 现做，返回后立刻投递，
  同样是窄窗口。「在补偿之后」描述的是它在失败序列中的位置，不是枚举→发布的间隔。
- **真正宽的是 3 和 4**：`SelectCharacterPhaseCommands.cs:96-97` 为被顶替的会话先建好计划，
  之后还要跑 `PrepareAsync` / `PlaceAtSpawnAsync` / `PrepareLeaseAsync` 三步（`:105-111`），
  成功则由 `SelectCharacterHandler.cs:104` 投递、失败则经 `SelectCharacterPhaseFailure`（`:143`）
  由 `:136` 投递。**计划被携带着穿过三个准备步骤**——这才是本处窗口比 PvP 那处宽的原因。

也就是说：结论（本处窗口更宽）成立，但归因错了——宽是因为**计划被跨步骤携带**，
不是因为**在失败路径上**；而表里被标成「正常路径」的 `:104` 恰恰是最宽的两个之一。
建议顺手改正，不阻断。

#### F-7（记录，非阻断）｜一个旧护栏没了，但迁移本身让它失去了必要

迁移前 `TouchSocketNetworkMessageSender.DeliverExplicitAsync:84` 有
`if (target.ConnectionId == requiredDirectConnectionId) continue;`——
旁人目标若落在本人连接上会被跳过。迁移后旁人走独立的 `PublishAsync`，**没有对应护栏**。
但这条旧护栏防的是「陈旧在场记录指回本人连接」，而迁移后解析改为
`NotificationAudienceResolver.cs:40` 现查现用，且枚举本身已排除 `session.CharacterId`，
两个不同角色不会持有同一个选人连接——**迁移使这条护栏失去必要**，不是遗漏。
如实记一笔，供 CK-027 的清单决定是否收进去。

### 结论

Round 2 的整改**主体是成立的**：F-1 已用重做的 M4 决定性证实（恰红一条、正是新判据、异常与栈根已取到），
补回的断言比被删的那条更强；F-2 的清单已落到本处的代码窗口，六个投递点行号与性质逐个核对无误，
不再交叉引用 TASK-05；计数 2126/2/0 与七个分项逐项相符、0 警告；SC-11 一字未动；
Round 2 生产代码零改动经三条独立证据印证；契约修正案属实、范围恰当，且与 TASK-05 的越权不同类。

阻断的只剩一条，仍在**证据侧**：差异清单漏了「异常可见性」这一族——
旁人半边的组帧失败在迁移后被无声吞掉，而本轮为修 F-1 新加的那条判据的注释恰好声称了相反的事。
按契约 revision 2 / spec C-3「每一处差异逐条列出、可指到代码窗口」的完整性口径，这条必须补上，
那句注释也必须改。**不需要改生产代码，不需要改设计，不动裁定。**

关于收口：CK-026（AC-004 精确陈述）与 CK-028（全量判据）本轮证据充分，可收口；
**CK-027 还差 F-5 要求的那一条清单项与那句注释的更正**，补齐即可收口。
另请所有者在最终评审时就 `Permitted files` 的事后扩张明确认可一次（见上文 3(c)）。

**verdict: `changes_required`**

## Controller Disposition — after Quick Review Round 2

`task-rework`。F-5 成立，且它咬的正是我**这一轮新写的那句注释**——与 Round 1 的 F-1 同形，
第二次了。两条非阻断一并处理。

### F-5：漏了一整族差异，映射的时机变了

**迁移前**：`CleanupSynchronizationDelivery.Create` 在**调用点**把两个半边一起映射成帧。
未映射的 notice 种类会在那里抛出，抛给要这次投递的人。

**迁移后**：本人半边仍在调用点映射（`CleanupNotificationDelivery.DeliverAsync` 内），
**旁人半边的映射搬进了 `SessionCleanupAnnouncedHandler`**——它跑在
`NotificationBus.AttemptAsync` 的裸 `catch`（`NotificationBus.cs:196-201`）之下。于是：

> **C-D8｜旁人半边的映射失败从「抛给调用点」变成「彻底无声」。**
> 处理器抛出 → `AttemptAsync` 的 catch 吞掉 → 该收件人记 `Failed` →
> 本通知 `retryLimit: 0`，不重试 → 总线**没有 logger** →
> 六个投递点（`OnlineUserConnectionObserver.cs:95/102`、`SelectCharacterHandler.cs:104/136/156/162`）
> **没有一个读 `DeliverAsync` 的返回**（它是 `ValueTask`，本来就不带摘要）。
> 结果是：一条 notice 种类忘了映射，旁人收不到东西，而**没有任何地方会说出来**。
>
> **前向注记（本条写下之后才发生的事，不回改上文）**：所有者其后裁定「现在就修：给总线加日志」。
> `NotificationBus` 现在注入 `ILogger`，投递抛异常与到期限各记一条 `Warning`。
> 所以「总线没有 logger」「没有任何地方会说出来」这两句**今天已经是假的**——
> 该路径的映射异常现在会以 `Warning` + 异常本身的形态出现在日志里。
> 差异本身（映射时机从调用点挪进了处理器）仍然成立，变的只是它的后果不再无声。

**我新写的那句注释因此是假的**：「a kind added to the enum and forgotten here is found the first
time somebody leaves rather than never」——对本人半边成立，**对旁人半边不成立**。已改写为分别说明
两个半边，并指回本条。

**不改生产代码**：让它响需要给总线加日志，那是整个成果范围的事（不只是这处扇出），
而且会牵动 TASK-03 定下的「投递只交回摘要、不自己记账」。**记为已知限制交 TASK-07 写进仓库约定**：

> 通知投递失败今天是无声的——总线把结果放进摘要，而摘要的读者是可选的，目前没有一个调用方读它。
> （**已过时**——见文末的统一前向注记。）

### F-6：「窗口更宽」我归错了因

我写「投递点 2/4/6 在失败路径、5 在补偿之后，所以窗口比 TASK-05 更宽」。快审指出真正宽的是
**3 和 4**——它们被跨三个准备步骤携带。失败路径本身不必然更远。已按此更正归因。

### F-7：旧的自抑护栏消失，但迁移使其失去必要

留档，不处理。

## 一次终于取到身份的偶发失败 —— 它修正了本仓对这条偶发的描述

本任务此前四次遇到判据失败，**四次都没取到身份**（TASK-04 的快审记过 N-6，TASK-05 我自己又犯一次）。
这一次输出落了盘，取到了：

```
失败 Dfo.Server.Protocol.Tests.Game.ItemPathFrameTests.AMoveOfARealRowAcknowledgesTheSameBytesAsBefore

System.ObjectDisposedException : Cannot access a disposed object.
Object name: 'SQLitePCL.sqlite3'.

  at System.Runtime.InteropServices.SafeHandle.DangerousAddRef(Boolean& success)
  at SQLitePCL.SQLite3Provider_e_sqlite3...sqlite3_prepare_v2(...)
  at Microsoft.Data.Sqlite.SqliteCommand.PrepareAndEnumerateStatements()+MoveNext()
  at Microsoft.Data.Sqlite.SqliteDataReader.NextResult()
  at Microsoft.Data.Sqlite.SqliteCommand.ExecuteReaderAsync()
  at ItemPathFrameTests.Storage.ReadAsync() in ItemPathFrameTests.cs:line 189
  at ItemPathFrameTests.AMoveOfARealRowAcknowledgesTheSameBytesAsBefore() in ...:line 63
```

复跑：定向 3/3 全绿；全量 `517+52+60+256+6+223（跳过 2）+1012 = 2126 通过 / 2 跳过 / 0 失败`，
七个项目全部 `测试运行成功`。

### 它与既知偶发的关系：**异常类型相同，栈根不同**

本仓记的判别依据是「异常类型 + 栈根 + 复跑即绿」，栈根写的是
`CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`。

这一次的栈根是 `ItemPathFrameTests.Storage.ReadAsync → SqliteCommand.ExecuteReaderAsync`，
**不是那一条**。两者共同的是「一个已被释放的 SQLite 句柄在并行下被用了」。

**所以本仓那条描述比现象窄。** 该记的是（**下面这段在 Round 3 被推翻并重写，见其后的更正**）：

> 既知偶发是「`ObjectDisposedException: 'SQLitePCL.sqlite3'` + 复跑即绿」。
> 它在不同的栈根上出现过至少三处（`KeepWritersAndReadersApart → ExecuteScalar`、
> `DarkKnightComboBookTests` 的两条、以及 `ItemPathFrameTests.Storage.ReadAsync → ExecuteReaderAsync`），
> 所以**栈根不是判别依据的一部分，异常类型才是**；栈根只用来记录它去过哪里。

**这一条要写进 TASK-07 的仓库约定**，连同「遇到判据失败先落盘取身份再谈别的」。

### 不归因本成果的依据

`ItemPathFrameTests.cs` 里 `Notification` 出现 **0 次**；该判据走的是自建 SQLite 库的读路径，
与通知服务无交集。本轮改动只有一条测试注释与流程产物。

### Round 3 全量判据

```
517 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 1012 = 2126 通过 / 2 跳过 / 0 失败
```

与 Round 2 同值（本轮未增删判据条数）。构建 0 警告 0 错误。

### Executor Self-check — Round 3

- **生产代码本轮零改动。** 改的是：一条判据的注释（F-5）、差异清单加 C-D8、
  「窗口更宽」的归因更正（F-6）、以及上面那份偶发身份记录。
- **F-5 是同一个毛病的第二次**：Round 1 的 F-1 是「注释/自检里一句比代码强的话」，
  Round 2 的 F-5 咬的是我**为修 F-1 而新写的那句注释**。写注释时我又一次假设了「和迁移前一样」，
  而迁移恰恰改了那件事。
- **本成果的一条已知限制，交 TASK-07**（**已过时**，见文末统一前向注记）：通知投递失败今天是无声的——
  总线把结果放进摘要，而摘要的读者是可选的，目前**没有一个调用方读它**。

## Independent Quick Review — Round 3

- 裁定：**`changes_required`**
- 被审契约：Task Unit Contract **revision 2**（含 `Task Unit Contract Amendment — Revision 2`，仅改
  `Permitted files or interfaces`）。参照条款：`plan.md:222-224` 的 CK-026 / CK-027 / CK-028，
  其中 CK-027 的口径由 CK-024（`plan.md:196`）给出：「帧逐值相同；**收件人集合与投递语义**的
  每一处差异都在清单里并指到代码窗口」。
- **只审 Round 3 的整改**（Controller Disposition — after Quick Review Round 2、C-D8、F-6 归因更正、
  偶发身份记录、Round 3 全量与自检）。Round 1 / Round 2 的段落一字未动。
- Owned diff 身份：工作区 `.worktrees/notifications-know-their-audience`，基线
  `b05d47e4e587d6ab85312ef7f5a3802716805e3c`，未提交。`git status --porcelain` 共 20 项。
- 评审者与执行方不同上下文；未改动任何实现文件或任务控制状态。本轮无需变异（生产代码零改动、
  判据条数未变），结论全部来自直接读码与一次独立全量。

### 核过的命令与证据

| 项 | 命令 / 动作 | 结果 |
|---|---|---|
| 全量 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`（落盘 2435 行，未过滤明细） | **退出码 0**，七个项目全部「测试运行成功」，本轮**未出现任何偶发失败** |
| 计数 | 52 + 517 + 60 + 256 + 6 + 223（跳过 2）+ 1012 | **= 2126 通过 / 2 跳过 / 0 失败**，与声称逐项相符 |
| 警告 | 全量输出里「警告」出现次数 | **0** |
| SC-11 | `git diff b05d47e4 -- "*CommandHandlerArchitectureTests*"`、`git status --porcelain -- 同` | 均为空 |
| 生产零改动 | `sha256 SupplementalSynchronizationMessages.cs` | `e842b8ab4ddaaa1dc48739c0f47c87e252a690cf09d886d58f589d1fe5f29198`，与 Round 1、Round 2 记录的还原哈希**逐字节相同** |
| 生产零改动 | 全部生产文件 mtime 最晚 `17:27:05`（即 Round 2 快审 M4 还原的那一次），本轮唯一被编辑的 `CleanupNotificationDeliveryTests.cs` 为 `17:37:36` | 晚于全部生产文件 |
| 生产零改动 | Protocol.Tests 计数 517，与 Round 2 同值 | 本轮无判据增删，只改注释 |

### 成立的部分（逐条自己动手核过，不是采信）

**F-5 第 1 项（C-D8 的因果链）——每一环都属实，逐环核过：**

| 环 | 代码窗口 | 核对结果 |
|---|---|---|
| 旁人半边的映射在处理器里 | `SupplementalSynchronizationMessages.cs:195-203`（`ComposeAsync` → `notification.Notices.Select(CleanupNoticeMessages.ToMessage)` 在 `:202`），throwing default 在 `:215` | 属实 |
| 处理器在 `try` 里被调 | `NotificationBus.cs:179-180`（`composer.ComposeAsync`，位于 `:168` 起的 `try` 内） | 属实 |
| 中间无拦截 | `NotificationComposer.cs:24-28` 是纯转发表达式体，无 try | 属实 |
| 裸 catch → `Failed` | `NotificationBus.cs:196-201`；唯一放行的是 `:192` 调用方自身取消 | 属实 |
| `retryLimit: 0` 不重试 | 注册在 `ProtocolModule.cs:89-93`；`PublishAsync` 于 `:72-73` 调 `Requeue(..., used: 0)`，`Requeue` 的门是 `:113` `used < RetryLimit` → `0 < 0` 为假，**永不入队** | 属实 |
| 总线无 logger | `NotificationBus.cs:41-48` 七个构造参数无 `ILogger`；`grep -rn -i "logger" Application/Notifications/` 只命中 `NotificationRetryDrainService.cs:20/76`，管的是**整趟重试**的异常。**又因 `retryLimit: 0` 从不入队，这条日志对本通知连触发的机会都没有** | 属实（比执行方写的更强） |
| 六个投递点不读返回 | `grep -n "cleanupDelivery.DeliverAsync"` → `OnlineUserConnectionObserver.cs:95,102`、`SelectCharacterHandler.cs:104,136,156,162`，六处**全部是裸 `await`**；`DeliverAsync` 声明为非泛型 `ValueTask`（`:238`）；且在 `DeliverAsync` 内部 `notifications.PublishAsync` 的 `NotificationDelivery` 返回值本身就被丢弃（`:254`） | 属实 |

**结论：C-D8 描述的因果链每一环都属实，且带自己的代码窗口，满足 CK-024/CK-027 的「指到代码窗口」口径。**

**F-5 第 2 项（改后的注释）——没有比代码强的句子，本人/旁人两半边的区分准确。**
逐句核 `CleanupNotificationDeliveryTests.cs:96-104`：

- 「It names itself, and that is all this says.」——把断言范围收到了这条用例真正断的事（`:114` 只断
  异常消息里含 `"99"`）。上一轮那句外推的「found the first time somebody leaves rather than never」
  **已删除**。
- 「the owner's notices are still mapped at the call site」——属实：`:247` 的
  `[.. plan.RequiredOwnerNotices.Select(...)]` 是集合表达式，在构造 `GameExplicitMessageDelivery`
  实参时**同步求值**，早于 `await SendExplicitAsync`，抛出会从 `DeliverAsync` 冒到调用点。
- 「so a gap there reaches whoever asked for the delivery」——**字面属实**（异常确实到达调用点）。
- 「The peers' are mapped inside the handler ... becomes one recipient's `Failed`, is not retried at
  this notification's allowance of zero, and is read by nobody」——四个分句逐一对上上表四行。
- 「See the difference list in this Task's record, entry C-D8.」——注释与记录互指，是正确的收法。

*一处精度补记（非缺陷，反而加强 C-D8）*：注释画的这条不对称，**在生产里比它读起来还要窄**——
本人半边的抛出虽然到达调用点，但六个投递点里有**五个**当场把它吞掉：
`OnlineUserConnectionObserver.cs:95`（落到函数尾部的裸 `catch`）、`:102`（自带 `try/catch`）、
`SelectCharacterHandler.cs:136`（`catch { // Preserve the phase failure... }`）、
`:156` 与 `:162`（`catch { // The original workflow failure remains primary. }`）。
只有 `SelectCharacterHandler.cs:104` 会一路冒到外层 `catch (Exception error)` 触发补偿。
注释没有声称本人半边在生产里可见，所以**不构成 F-5 的复发**；记在这里是因为它进一步坐实了执行方
交给 TASK-07 的那条已知限制（「通知投递失败今天是无声的」）——事实上连**本人半边**的投递失败
今天也是六分之五无声的。

**F-5 第 3 项（不改生产代码）——理由成立，而且有一条比执行方给的更硬的理由。**
执行方给的理由（会牵动 TASK-03 定下的「投递只交回摘要、不自己记账」、属整个成果范围）经核属实：
TASK-03 的 Goal 句就是「总线的发布方法、投递实现……以及与投递同时产出的摘要」，其 Owned boundary
正是 `Application/Notifications/`。**更硬的一条**：`NotificationBus.cs` 位于
`Server/src/Dfo.Server.Application/Notifications/`，**不在 TASK-06 契约 revision 2 的 Permitted files
里**（四条原文加修正案补的两个文件都不含它）。给总线加 logger 需要**第三次**事后扩边界——
而 Round 2 快审刚判定第二次扩张「应由所有者在最终评审时明确认可一次」。
**本轮不该修，判断正确。**

**F-6 的更正——方向与结论成立，逐点复核：**
计划在 `SelectCharacterPhaseCommands.cs:97-98`（`cleanupNotifications.Add(await cleanup.CleanupAsync(displaced, includeOwner: false, ...))`）
构建，随后要跨过 `:106-112` 的三步才返回；成功则由 `SelectCharacterHandler.cs:104` 投递（投递点 3），
三步中失败则经 `SelectCharacterPhaseFailure` 由 `:136` 投递（投递点 4）。
而失败路径 2 / 6 拿的 `cleanupFailure.NotificationPlan` 与投递点 5 拿的 `compensated.Notifications`
都是**现做现投**，窗口并不更宽。**「真正宽的是 3 与 4、宽是因为计划被跨步骤携带」这个更正准确。**
（一处小瑕疵见 F-10。）

**计数、SC-11、生产零改动——全部属实。** 我自己跑的全量与声称逐项相符（52/517/60/256/6/223+2跳过/1012
= 2126/2/0），0 警告，退出码 0，**本轮未出现任何偶发失败**。SC-11 与基线逐字节相同。
Round 3 生产代码零改动由三条独立证据印证（哈希、mtime 顺序、判据条数未变）。

**偶发身份记录的取证本身——扎实。** `ItemPathFrameTests.cs:189` 确为
`await using var reader = await command.ExecuteReaderAsync();`，与记录的栈根行号**精确对上**；
`:183` 的 `Open(this.Path)` 确为自建库连接。这是本任务五次遇到判据失败以来**第一次真正取到身份**，
方法（落盘 + 不过滤明细）是对的，值得保留。

### 发现

#### F-9（阻断）｜偶发的**溯源清单被数错了**，而那个错误的计数正是「栈根不是判别依据」这条新规则的**全部依据**——并且该规则要被写进仓库约定

执行方写（`## 一次终于取到身份的偶发失败` 一节）：

> 它在不同的栈根上出现过至少三处（`KeepWritersAndReadersApart → ExecuteScalar`、
> `DarkKnightComboBookTests` 的两条、以及 `ItemPathFrameTests.Storage.ReadAsync → ExecuteReaderAsync`），
> 所以**栈根不是判别依据的一部分，异常类型才是**。

**这三项里第二项是假的，而且它恰好是把结论顶起来的那一项。** 逐条回到本仓的记录：

| 事件 | 出处 | 实际是什么 |
|---|---|---|
| `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` | `character-state-finds-its-owner/multi-spec-map.md:36-38` | 已取到身份，栈根 `KeepWritersAndReadersApart → ExecuteScalar` |
| `DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused` | 同上 `:45-49` | 已取到身份，**栈根同为** `KeepWritersAndReadersApart → ExecuteScalar`，原文明写「与 `OutsideTransactionTests` 那条是**同一个并行释放竞态**，只是打中了别的用例」 |
| `DarkKnightComboBookTests.ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead` | `TASK-05:139-162` | **身份从未取到**。TASK-05 自己写死：「我当时的输出过滤把明细滤掉了，**没有取到异常类型与栈根**……**不能说**：这次就是那条既知偶发」 |
| `ItemPathFrameTests.AMoveOfARealRowAcknowledgesTheSameBytesAsBefore` | 本轮 | 已取到身份，栈根 `ItemPathFrameTests.Storage.ReadAsync → ExecuteReaderAsync`，**确为新根** |

于是：

1. **`DarkKnightComboBookTests` 不是「两条」，是一条已识别 + 一条本仓明文拒绝归因的。**
   执行方把契约 `TaskContract.md:143`「已在**两条不同用例**上出现过」（说的是**同一个栈根**打中了
   两个用例名）读成了「两条不同栈根」，又把 TASK-05 那条无身份的失败并了进来。
2. **那条已识别的 `DarkKnightComboBookTests` 恰恰是反证。** 它与 `OutsideTransactionTests` **同根**，
   是「栈根跨用例名**稳定**」的证据——执行方把它当成了「栈根不稳定」的证据，用反了。
3. **修正后的证据基数是「两个栈根」，其中新的那个只见过一次**，而不是「至少三处不同栈根」。

**为什么这条阻断。** 用两个栈根（新的那个 n=1）去论证「把栈根整个踢出判别依据」，剩下的规则是
「`ObjectDisposedException: 'SQLitePCL.sqlite3'` + 复跑即绿」——**这两项对一次真实的并发回归同样成立**。
异常类型加复跑即绿不具备排除力：任何新引入的 SQLite 句柄释放竞态都会呈现为同一个异常类型、
并且大概率复跑即绿。执行方明写这条**要写进 TASK-07 的仓库约定**，也就是要把一条依据错误、
且会削弱唯一有分辨力的那一项的规则固化进 `AGENTS.md`。这与 Round 1 的 F-1、Round 2 的 F-5
**是同一族**：一句比证据强的话，写进了 durable 记录。第三次。

**要求的整改（局部可修，不改代码、不改设计、不动裁定）**：

1. 改正溯源清单：**两个**已识别栈根——`KeepWritersAndReadersApart → ExecuteScalar`（出现在
   `OutsideTransactionTests` 与 `DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`
   **两个用例名上，同一个根**）、以及本轮新增的
   `ItemPathFrameTests.Storage.ReadAsync → ExecuteReaderAsync`（n=1）。
   TASK-05 那条 `ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead` 与那次未记名的
   `Networking.Tests` 失败**必须留在「未归因」栏**，不得计入本族——TASK-05 自己拒绝归因过。
2. 把规则改成证据支持得住、且不丢分辨力的形态。建议口径：
   > 本族 = `ObjectDisposedException: 'SQLitePCL.sqlite3'` + 复跑即绿 + 栈根落在**已记录的根清单**里。
   > **用例名不是判别依据**（同一个根已打中过两个用例名）。遇到**清单外的新栈根**时，
   > 它不能仅凭异常类型自动归入本族：必须另外给出「该失败路径与本次改动无关」的明证，
   > 归入后把新根**追加进清单**。

   本轮正是这个流程的一次正确执行（执行方确实给了非归因明证），把它写成规则即可，
   不需要为此废掉栈根。
3. **补上本轮漏掉的那条不利事实**（见下）。

#### F-9 附带一项：漏掉的不利事实——TASK-06 抬高的正是**出事那个程序集**的并行负载

执行方在 TASK-05 主动记过一条不利事实（「本成果给 `Dfo.Server.Application.Tests` 加了 12 条判据……
**确实提高了同一程序集内的并行负载**。这不足以归因，但也不能拿『我没碰那段代码』当免罪」），
Round 1 快审也认可过这种诚实。**本轮该说而没说，且这一次的耦合比 TASK-05 那次更紧：**

- 本轮偶发出在 `Dfo.Server.Protocol.Tests`（`ItemPathFrameTests`）。
- TASK-06 给的正是 `Dfo.Server.Protocol.Tests`：510 → 517，**+7 条**
  （TASK-01 至 TASK-05 期间该项目一直是 510，见 `TASK-05:151` 的分项）。
- 新增的 7 条本身是纯内存替身、不碰 SQLite——**这支持「不归因」，但不构成免罪**，
  因为 xUnit 在程序集内并行调度，多 7 条用例就是多一分调度重叠。

**不改变「不归因本成果」的结论**（`ItemPathFrameTests.cs` 里 `Notification` 出现 0 次、
走自建库读路径，两条都经我复核属实），但按执行方自己在 TASK-05 立下的标准，这条必须写出来。
请补进偶发记录一节。

#### F-10（记录，非阻断）｜F-6 的更正把那三步准备**叫错了名字**

更正后的清单写：「它们的计划在 `PrepareSelectedCharacterSessionCommand` 里建好之后，
要跨过**补货、发布、PvP** 三段准备才被投递」。三步的实际身份是：

- `preparation.PrepareAsync`（`SelectCharacterPhaseCommands.cs:106-108`）
- `preparation.PlaceAtSpawnAsync`（`:109-110`）
- `inventoryEntry.PrepareLeaseAsync`（`:111-112`）

「PvP」尤其指错：`PlanPvp` / `Publish` 在 `SelectCharacterLifecycleServices.cs:147/201`，
由 `SelectCharacterHandler.ExecuteAcceptedWorkflowAsync`（`:213`）驱动，而它在 `:106` 被调用——
**晚于投递点 3（`:104`）**，根本不在被携带的窗口内。Round 2 快审 F-6 原文给的三个方法名是对的，
这一轮改写时换成了三个错的词。

结论（3 与 4 最宽、宽在跨步骤携带）不受影响，投递点 3/4 自己的行号在上方表里也是准的，
所以不阻断；但 CK-027 这条口径的全部执行力就是「读者能指到代码」，照着「补货、发布、PvP」
去找是找不到那三步的。建议顺手把三个方法名换回去并带上 `:106-112`。

### 结论

Round 3 对 F-5 的整改**主体成立且质量高**：C-D8 的因果链七个环节我逐环回到代码看过，
**每一环都属实**、每一环都带自己的代码窗口，是本任务差异清单里锚定最扎实的一条；
改后的注释删掉了那句外推、对本人/旁人两半边的区分**准确**，没有留下比代码强的句子；
「本轮不改生产代码」的判断**正确**，而且有一条比执行方自己给的更硬的理由（`NotificationBus.cs`
根本不在契约 revision 2 的 Permitted files 内，修它要第三次事后扩边界）。
F-6 的归因更正方向与结论**准确**。计数 2126/2/0 与七个分项逐项相符、0 警告、退出码 0、
本轮无偶发；SC-11 一字未动；Round 3 生产代码零改动经三条独立证据印证。
本轮**第一次真正取到偶发身份**，取证方法（落盘 + 不过滤明细）正确，`ItemPathFrameTests.cs:189`
与记录的栈根行号精确对上，「不归因本成果」的两条依据（`Notification` 出现 0 次、自建库读路径）
我复核后**成立、充分**。

阻断的是 F-9，仍在**证据侧**，且与前两轮同族：为了给这次取到的身份做归纳，
执行方把溯源清单数成了「三个不同栈根」，其中一项（`DarkKnightComboBookTests` 的「两条」）
经回查本仓记录是**假的**——一条与旧根同根（是「根稳定」的反证），另一条本仓明文拒绝归因；
而这个错误的计数正是「栈根不是判别依据」这条**准备写进仓库约定**的新规则的全部依据。
按修正后的证据（两个根，新根 n=1）不足以支持把栈根整个踢出判别依据，剩下的
「异常类型 + 复跑即绿」对一次真实的并发回归同样成立、不具排除力。
同时漏了一条本轮该说的不利事实：TASK-06 恰恰给**出事的那个程序集**（`Dfo.Server.Protocol.Tests`，
510 → 517）加了 7 条判据——不改变不归因的结论，但按执行方自己在 TASK-05 立的标准必须写出来。
**两条都只需改本 Task 文件的文字，不需要改生产代码、不需要改设计、不动裁定。**

关于收口，对照契约受理条件与 CK-026 / CK-027 / CK-028：

- **CK-026（两处的逐个评估与裁定）**：证据充分，**可收口**（Round 1 已逐条核过，本轮未触动）。
- **CK-027（迁移前后对照，口径见 CK-024）**：C-D8 补齐后，「收件人集合与投递语义的每一处差异
  都在清单里并指到代码窗口」这条完整性要求**已满足**，**可收口**；F-10 那三个错的方法名建议
  顺手改掉，但不构成不可收口。
- **CK-028（全量判据）**：我独立跑出 2126/2/0、0 警告、退出码 0，**可收口**。
- **还差什么**：三个 CK 本身已无缺口，**差的是 F-9**——它不属于任何一个 CK，而属于本 Task 文件
  作为 durable 记录的真实性，且执行方明确要把它转成 TASK-07 的仓库约定。
  一条依据被数错、方向被用反的规则不该以这个形态交下去。改正溯源清单、按上面第 2 点重述规则、
  补上那条不利事实，即可收口。
- 另请所有者在最终评审时就 `Permitted files` 的事后扩张明确认可一次（Round 2 快审 3(c) 已提，
  本轮复核仍然成立且未被认可）。

**verdict: `changes_required`**

## Controller Disposition — after Quick Review Round 3

`task-rework`。F-9 成立，而且它抓的是**这一整轮里我最得意的那条发现**。两条非阻断一并处理。

### F-9：我把一条反证当成了正证，而结论正是靠它顶起来的

我写「至少三处不同栈根」，其中第二项是「`DarkKnightComboBookTests` 的两条」。回查根图原文
（`character-state-finds-its-owner/multi-spec-map.md`）：

> TASK-09 的第二轮独立快审遇到 `DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`
> 抛同一个 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，**栈根同为**
> `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`，
> 与 `OutsideTransactionTests` 那条是**同一个并行释放竞态**，只是打中了别的用例。
> **所以判别依据是「异常类型 + 栈根 + 复跑即绿」，不是用例名。**

**那是「栈根跨用例名稳定」的证据，我把它当成了「栈根会变」的反证。** 一字之差，方向相反。

另一条 `ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead`（TASK-05 遇到的那次）**身份从未取到**，
TASK-05 自己就写死了「不能说这次就是那条既知偶发」，不得计入。

**真实基数：两个栈根，新根 n = 1。**

而如果照我原来的结论把栈根整个踢出判别依据，剩下的「异常类型 + 复跑即绿」对**一次真实的并发回归**
同样成立——**它不再有排除力**。这条规则还明写要进 `AGENTS.md`，所以错的代价不止在本文件里。

### 更正后的规则（这才是要进 TASK-07 的那条）

> 既知的 SQLite 并行释放偶发，判别依据是三样一起看：
> **异常类型 `ObjectDisposedException: 'SQLitePCL.sqlite3'` + 复跑即绿 + 栈根在已记录清单内。**
>
> 已记录的栈根清单（**三条**）：
> 1. `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`
>    —— 见于 `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed`、
>    `DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`、以及
>    `DarkKnightComboBookTests.TwoBooksAskedForTheSameCharacterAreTheSameBook`
>    （**同根，三个不同用例名**——这一条是「栈根跨用例名稳定」的三次独立佐证）
> 2. `ItemPathFrameTests.Storage.ReadAsync → SqliteCommand.ExecuteReaderAsync`
>    —— 本任务 Round 3
> 3. `OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → SqliteCommand.ExecuteReaderAsync`
>    —— 本任务 Round 4 的独立快审所遇；不归因明证：该文件与基线逐字节相同、`Notification` 出现 0 次、
>    走自建库读路径、定向复跑 4/4 绿
>
> **复现率**：本机比此前记录暗示的高——Round 4 的独立快审三次全量红了两次。
> 所以「复跑即绿」要真的**跑**，不能因为「一般不会遇到」就省掉。
>
> **遇到清单外的新栈根，不得直接归入偶发**：要另给不归因的明证（改动与该路径无交集等），
> 确认之后把新栈根追加进清单。
>
> 未取到身份的失败一律记为**取证缺口**，不得计入本族。已知的两条：
> `Dfo.Server.Networking.Tests` 的一次未记名失败（记在 `character-state-finds-its-owner`
> 根图的 `baseline_evidence` 里）、
> `DarkKnightComboBookTests.ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead`（本成果 TASK-05）。

### F-9(c)：一条对我不利、而我这轮没写的事实

本轮偶发出在 `Dfo.Server.Protocol.Tests`，**而 TASK-06 新增的 7 条判据正是加在这个程序集**
（510 → 517）。这比 TASK-05 那次（新增判据与偶发在同一程序集，但那次是 Application.Tests
且新增的是定时器判据）**耦合更紧**。

不改变「不归因」的结论——`ItemPathFrameTests.cs` 里 `Notification` 出现 0 次，走的是自建库读路径——
但按我自己在 TASK-05 立的标准，**这条必须写出来，不能拿「我没碰那段代码」当免罪**。本轮没写，补上。

### F-10：三步准备我也叫错了名字

我写「跨补货、发布、PvP 三段准备」。实际是 `PrepareAsync` / `PlaceAtSpawnAsync` / `PrepareLeaseAsync`
（`SelectCharacterPhaseCommands.cs:106-112`）。**PvP 尤其指错**：`PlanPvp` / `Publish` 由
`ExecuteAcceptedWorkflowAsync` 驱动，晚于投递点 3（`SelectCharacterHandler.cs:104`），
根本不在被携带的窗口里。已改。

### Round 4 全量判据与自检

本轮**只改流程记录**，未动任何代码或判据。全量判据见下（与 Round 3 同值）。

三轮里被打回的三条（F-1 / F-5 / F-9）是同一族的三次：
**一句比证据强的话**。而且一次比一次深——
F-1 是我说「没有断言被删」而它被删了；
F-5 咬的是我为修 F-1 而**新写**的注释；
F-9 咬的是这一整轮里我最得意的那条发现，它把一条反证当成了正证。

第三条尤其该记：**「我发现了一件事」比「我做了一件事」更需要人查。**

```
517 + 52 + 60 + 256 + 6 + 223（跳过 2）+ 1012 = 2126 通过 / 2 跳过 / 0 失败
```

七个项目全部 `测试运行成功`，`测试运行失败` 出现 0 次，构建 0 警告 0 错误，本轮无偶发失败。

## Independent Quick Review — Round 4

- 裁定：**`passed`**
- 被审契约：Task Unit Contract **revision 2**（含 `Task Unit Contract Amendment — Revision 2`，仅改
  `Permitted files or interfaces`）。参照条款：`plan.md:222-224` 的 CK-026 / CK-027 / CK-028，
  CK-027 的口径由 CK-024（`plan.md:196`）给出。
- **只审 Round 4 的整改**（`## Controller Disposition — after Quick Review Round 3` 及其下的
  更正后规则、F-9(c)、F-10、Round 4 全量与自检）。Round 1 / 2 / 3 的段落与三轮快审一字未动。
- Owned diff 身份：工作区 `.worktrees/notifications-know-their-audience`，基线
  `b05d47e4e587d6ab85312ef7f5a3802716805e3c`，未提交。`git status --porcelain` 共 20 项。
- 评审者与执行方不同上下文；未改动任何实现文件或任务控制状态。本轮未做变异（生产代码零改动、
  判据条数未变），结论来自直接读码、回查根图原文，与**三次独立全量**。

### 核过的命令与证据

| 项 | 命令 / 动作 | 结果 |
|---|---|---|
| 全量 #1 | `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf dotnet test Server/86JP.Server.slnx --nologo --logger "console;verbosity=detailed"`（落盘 2473 行，未过滤明细） | **exit 0 但出现 `测试运行失败`**：Application.Tests 1011 通过 / 1 失败，见 F-11 |
| 全量 #2 | 同上（落盘 2491 行） | **又一次 `测试运行失败`**：Application.Tests 1011 / 1，另一条用例，见 F-11 |
| 全量 #3 | 同上（落盘 2434 行） | **七个项目全部「测试运行成功」**；`517 + 52 + 60 + 6 + 256 + 223（跳过 2）+ 1012 = 2126 通过 / 2 跳过 / 0 失败` |
| 警告 | 三次输出里「警告」出现次数 | **均为 0** |
| 定向复跑 | `--filter "FullyQualifiedName~OneDatabaseOneParticipantTests"` | **4/4 全绿** |
| SC-11 | `git diff b05d47e4 -- "*CommandHandlerArchitectureTests*"`、`git status --porcelain -- 同` | **均为空** |
| 生产零改动 | `sha256 SupplementalSynchronizationMessages.cs` | `e842b8ab4ddaaa1dc48739c0f47c87e252a690cf09d886d58f589d1fe5f29198` —— 与 Round 1 / 2 / 3 记录的还原哈希**逐字节相同** |
| 本轮只改记录 | `find Server -name "*.cs"`（排除 bin/obj）按 mtime 排序 | 全仓**最新的 `.cs` 是 `17:37:36`**（`CleanupNotificationDeliveryTests.cs`，Round 3 改注释那次）；本 Task 文件 `17:53:45`。**Round 4 之后没有任何 `.cs` 被碰过** |
| 判据条数 | Protocol.Tests 517、Application.Tests 1012，与 Round 3 同值 | 本轮无判据增删 |

### 逐条核实结果

**1(a) F-9 的更正属实，方向确实曾被用反。**
我自己回读 `character-state-finds-its-owner/multi-spec-map.md` 的 `baseline_evidence` 原文，
执行方引用的那段一字不差地在那里：

> TASK-09 的第二轮独立快审遇到 `…DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`
> 抛同一个 `ObjectDisposedException: 'SQLitePCL.sqlite3'`，栈根同为
> `CharacterDatabaseSchema.KeepWritersAndReadersApart → SqliteCommand.ExecuteScalar`，
> 与 `OutsideTransactionTests` 那条是**同一个并行释放竞态**，只是打中了别的用例。
> …**所以判别依据是「异常类型 + 栈根 + 复跑即绿」，不是用例名。**

原文明写「栈根同为」，并由此得出「判别依据含栈根、不含用例名」。此前的归纳把这条读成了
「另一个不同栈根」，**确实是把一条反证当成了正证**。更正准确。
（一处引用瑕疵，非缺陷：Round 4 的引文静默略去了中间一句「两次复跑（962/0、2069/2/0）均恢复」
与末句「按名字对不上就当成回归是错的」，没有加省略号。略去的部分不改变原意，也未被歪曲。）

**1(b) 更正后的规则成立，且没有造出新的过强句子。**
「异常类型 + 复跑即绿 + 栈根在已记录清单内」把排除力还给了栈根：仅凭前两项，**任何**新引入的
SQLite 句柄释放竞态都能自动获得豁免——这正是 Round 3 阻断的理由。再加上「清单外的新栈根不得直接
归入、需另给不归因明证后追加」，规则变成可证伪且保守的。逐句看下来我没有找到比证据强的断言：
「同一个根已打中过两个用例名」在写下时准确（**我本轮的运行把它变成了三个用例名，方向一致**，见 F-11）；
「新根 n = 1」准确。

**1(c) 两条已记录栈根与两条取证缺口，与本仓实际记录一一对上。**

| 条目 | 我自己查到的出处 | 核对 |
|---|---|---|
| 根 1 `KeepWritersAndReadersApart → ExecuteScalar`，见于 `OutsideTransactionTests.ReadingACharacterOutsideAUseCaseIsAllowed` | 根图 `baseline_evidence` 第一段「其一」 | 属实 |
| 根 1 同样见于 `DarkKnightComboBookTests.APageTheEntryLoadWouldNotCarryIsRefused`（同根、不同用例名） | 根图 `baseline_evidence`「spec-001 期间修正的一条认识」 | 属实 |
| 根 2 `ItemPathFrameTests.Storage.ReadAsync → ExecuteReaderAsync`，n = 1 | 本文件 Round 3 记录；`ItemPathFrameTests.cs:189` 确为 `await command.ExecuteReaderAsync()` | 属实 |
| 缺口 1 `Dfo.Server.Networking.Tests` 一次未记名失败 | 根图 `baseline_evidence` 第一段「其二」：「**失败用例名未被抓到**」 | 属实（小瑕疵见 F-12） |
| 缺口 2 `DarkKnightComboBookTests.ARowTheEntryLoadWouldNotCarryIsRefusedWhenRead` | `TASK-05:139-141,163`：「**我当时的输出过滤把明细滤掉了，没有取到异常类型与栈根**」「**不能说**：这次就是那条既知偶发」 | 属实 |

**1(d) 被推翻的原文保留且已标注。** 那段仍在原位，前面挂着
「（**下面这段在 Round 3 被推翻并重写，见其后的更正**）」，错误的「至少三处」原话一字未删。
这与 Round 2「上一轮我写错的那句要留在原处」的做法一致，**是对的**。
（措辞小歧义：推翻它的是 Round 3 的**快审**，重写在 Round 4 的处置里；「见其后的更正」已消歧。）

**2. F-9(c) 的数字属实，自陈也足够——而且我的运行进一步支持「不归因」。**

- Protocol.Tests 510 → 517 = **+7**：`TASK-05` 全文的分项里该项目**一直是 510**
  （`TASK-05:152 / 183 / 340 / 458 / 877 / 1348 / 1772` 等处），当前三次全量均为 517。
- 这 +7 全在 TASK-06 新增的唯一一个 Protocol.Tests 文件里：
  `CleanupNotificationDeliveryTests.cs` 有 4 个 `[Fact]` + 1 个 `[Theory]`×3 `[InlineData]` = **7**；
  `git status --porcelain -- Server/tests/Dfo.Server.Protocol.Tests/` 只有它一个 `??`。**数字全部属实。**
- **自陈足够，不必改变结论**，而且我拿到了一条执行方手上没有的佐证：本轮**两次**偶发都落在
  `Dfo.Server.Application.Tests`（见 F-11），而 **TASK-06 给 Application.Tests 加了 0 条判据**
  （TASK-05 收尾即 1012，本任务三轮全部 1012）。**这一族偶发并不依赖 TASK-06 抬高的那个程序集的负载。**
  加上执行方自己核过的两条（`ItemPathFrameTests.cs` 里 `Notification` 出现 0 次、走自建库读路径），
  「不归因本成果」**成立，且证据比 Round 4 写的更足**。

**3. F-10 的三个方法名与「PvP 晚于投递点 3」全部核对属实。**
我自己读了 `SelectCharacterPhaseCommands.cs:90-115`：计划在 `:97-98`
（`cleanupNotifications.Add(await cleanup.CleanupAsync(displaced, includeOwner: false, …))`）构建，
随后三步与行号**精确对上**——`preparation.PrepareAsync`（`:106-108`）、
`preparation.PlaceAtSpawnAsync`（`:109-110`）、`inventoryEntry.PrepareLeaseAsync`（`:111-112`）。
差异清单（本文件 `:368-369`）已改成这三个名字并带 `SelectCharacterPhaseCommands.cs:106-112`，F-10 已闭。
「PvP 晚于投递点 3」也属实：`SelectCharacterHandler.cs:104` 是 `cleanupDelivery.DeliverAsync`，
`:106` 才调 `ExecuteAcceptedWorkflowAsync`（定义在 `:213`），PvP 的发布在其内部
（`:272` 的 `PublishSelectedCharacterSessionCommand`、`:291-293` 的 `PvpParticipantPublished`），
`PlanPvp` / `Publish` 在 `SelectCharacterLifecycleServices.cs:147/201`。**PvP 确实不在被携带的窗口内。**

**4.「本轮只改记录」成立，三条独立证据。**
① `SupplementalSynchronizationMessages.cs` 的 sha256 与前三轮记录的还原哈希逐字节相同；
② 全仓（排除 bin/obj）**最新的 `.cs` 是 `17:37:36`**，早于本 Task 文件的 `17:53:45`——
Round 4 之后没有任何 `.cs`（生产或判据）被写过；
③ 判据条数三轮同值（Protocol 517、Application 1012），无增删。
**SC-11**：`CommandHandlerArchitectureTests` 的 `git diff` 与 `git status` 均为空，与基线逐字节相同。

**5. 计数核对：第三次全量与声称逐项相符。**
`517 + 52 + 60 + 6 + 256 + 223（跳过 2）+ 1012 = 2126 通过 / 2 跳过 / 0 失败`，七个项目全部
`测试运行成功`，`警告` 0 次。前两次全量各红一条，身份已取到并按更正后的规则判别，见 F-11。

### 发现

#### F-11（记录，非阻断）｜我三次全量红了两次，其中一次是**清单外的新栈根**——按刚更正的规则，它需要被追加

按更正后的规则**先取身份再下结论**，两次都落了盘、未过滤明细：

**红 A（全量 #1）——新栈根，清单外。**

```
失败 Dfo.Server.Application.Tests.Transactions.OneDatabaseOneParticipantTests
     .AFailureInOneKindOfStateLeavesTheOtherOneUnchanged
System.ObjectDisposedException : Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'.
  at SQLitePCL...sqlite3_prepare_v2 → SqliteCommand.PrepareAndEnumerateStatements
  → SqliteDataReader.NextResult → SqliteCommand.ExecuteReaderAsync
  at OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync() in OneDatabaseOneParticipantTests.cs:line 229
```

异常类型对上本族；栈根 `OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → ExecuteReaderAsync`
**不在已记录的两条里**。所以按规则不得直接归入，我补的不归因明证：

- 该文件**与基线逐字节相同**（`git status --porcelain --` 空、`git diff b05d47e4 --stat --` 空）；
- 文件里 `Notification` 出现 **0 次**；`:229` 是 `await command.ExecuteReaderAsync()`，
  读的是本用例 fixture 自建的 SQLite 库，与通知服务无交集；
- **定向复跑 4/4 全绿**，全量 #3 该用例通过。

三项齐备，**归入本族成立**。**请把这条新栈根追加进清单**（两条 → 三条），
连同 TASK-07 要写进 `AGENTS.md` 的那一份。

**红 B（全量 #2）——已记录的根 1，打中了第三个用例名。**

```
失败 Dfo.Server.Application.Tests.Characters.DarkKnightComboBookTests
     .TwoBooksAskedForTheSameCharacterAreTheSameBook
System.ObjectDisposedException : Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'.
  … at SqliteCommand.ExecuteScalar()
  at CharacterDatabaseSchema.KeepWritersAndReadersApart(SqliteConnection) in CharacterDatabaseSchema.cs:line 103
  at CharacterDatabaseSchema.Ensure → SqliteDatabase..ctor → DarkKnightComboBookTests.Fixture..ctor:294
```

栈根**正是清单里的根 1**，用例名却是第三个（前两个是 `OutsideTransactionTests.…` 与
`DarkKnightComboBookTests.APage…`）。这是对更正后规则「**用例名不是判别依据、栈根跨用例名稳定**」
的一次**独立新鲜佐证**——也再次说明此前那条「栈根不是判别依据」的归纳方向确实反了。

**两条附带观察，供 TASK-07 采纳：**
① 本环境下的复现率比记录暗示的高得多——**三次全量红两次**。「复跑即绿」成立（定向与第三次全量均绿），
但把它当成「偶尔才会遇到」会低估取证成本；
② 两次都落在 `Dfo.Server.Application.Tests`，**而 TASK-06 给这个程序集加了 0 条判据**——
这一族与 TASK-06 抬高的 Protocol.Tests 负载无关，进一步坐实 F-9(c) 的「不归因」。

#### F-12（记录，非阻断）｜两处措辞小瑕疵

- 取证缺口 1 标注为「spec-001 根图」，实际记在**根图建立基线**那一段（`baseline_evidence` 第一段），
  时间上早于 spec-001 的工作。指向无歧义，建议改成「`character-state-finds-its-owner` 根图的基线记录」。
- Round 4 对根图那段的引用静默略去了中间一句与末句，没有加省略号。原意未被歪曲，建议补省略号。

### 结论

Round 4 的四项整改**逐条核实全部成立，且没有引入新的过强句子**——这是三轮以来第一次。

- **F-9(a)**：我回读根图原文，执行方引用属实、逐字对得上；「把一条反证当成正证」的自我更正准确。
- **F-9(b)**：更正后的规则把排除力还给了栈根，并用「清单外新栈根需另给不归因明证」封住了自动豁免的口子。
  规则成立；我逐句找过，没有比证据强的断言。
- **F-9(c) 的清单**：两条已记录栈根、两条取证缺口，我自己回查根图与 TASK-05，**四条一一对上**。
- **F-9(d)**：被推翻的原文保留在原位并已标注，错话一字未删——与 Round 2 的做法一致，是对的。
- **F-9 附带的不利事实**：510 → 517 = +7 属实（TASK-05 全文分项一直是 510；新文件恰好 4 Fact + 3 Theory 行）。
  自陈**足够**，不该改变「不归因」的结论；我本轮两次偶发都落在 TASK-06 加了 **0** 条判据的
  `Application.Tests`，**反而是比执行方手上更强的一条不归因证据**。
- **F-10**：三个方法名与 `:106-112` 我自己读码核对，精确；「PvP 晚于投递点 3（`:104` vs `:106`）」属实。
- **只改记录**：全仓最新 `.cs` 为 `17:37:36`，早于本 Task 文件；哈希与前三轮一致；判据条数未变。
  **SC-11 与基线逐字节相同。**
- **计数**：第三次全量 `2126 通过 / 2 跳过 / 0 失败`、七项全绿、0 警告，与声称**逐项相符**。
  前两次的红我按更正后的规则取到了身份并逐条判别（F-11），两条都归入本族，**不构成 CK-028 的缺口**。

关于收口，对照契约 revision 2 的受理条件与 CK-026 / CK-027 / CK-028：

- **CK-026（两处的逐个评估与裁定）**：Round 1 已逐条核过，此后未触动，**可收口**。
- **CK-027（迁移前后对照，口径同 CK-024）**：C-D1/2/5/7/8 各带自己的代码窗口，Round 3 快审逐环核过；
  本轮 F-10 修掉了最后一处指不到代码的措辞（三个方法名 + `:106-112` 我复核精确）。
  「每一处差异都在清单里并指到代码窗口」**已满足，可收口**。
- **CK-028（全量判据）**：我独立跑到 `2126/2/0`、0 警告、七项全绿，**可收口**。
- **契约受理条件逐条**：判定规则已逐条应用（帧内容不变 / 差异可指认 / 不比现在更难读）；
  「必送本人」段未评估（契约要求）；两处都有明确裁定与依据；`InArea` 形态不加的理由是行为性的、成立；
  全量判据通过。**受理条件全部有证据支撑。**
- **还差什么**：就本 Task Unit 而言，**没有阻断项了**。剩下两件都不属于本 Task 的可收口性：
  ① F-11 那条新栈根应在交给 TASK-07 之前被**追加进清单**（两条 → 三条），
  这正是更正后规则自己规定的动作，我已把身份与不归因明证备齐；
  ② `Permitted files` 的**事后扩张**（Round 2 快审 3(c)、Round 3 复核仍成立）**至今未被所有者认可**，
  请在最终评审时明确认可一次——那不是执行方能自己封的口。

三轮打回的 F-1 / F-5 / F-9 是同一族的三次「一句比证据强的话」；本轮我按同样的标准把每一句
更正后的话都拿回证据里对过一遍，**没有找到第四次**。

**verdict: `passed`**

## Controller Disposition — after Quick Review Round 4

`passed`。最新的执行结果、自检、快审裁定与 owned diff 指向同一轮。

两条非阻断就地处理，因为它们要进 TASK-07 的仓库约定，留到那时会带着错的内容进去：

- **F-11**：快审自己三次全量红了两次，其中一次是**清单外的第三个栈根**
  （`OneDatabaseOneParticipantTests.Fixture.ReadItemsAsync → ExecuteReaderAsync`），
  它按更正后的规则先取身份、再补不归因明证，正是这条规则要的动作。已追加进清单（两条 → 三条）。
  另一次红的栈根是清单里的根 1，**用例名却是第三个**——对「栈根跨用例名稳定」的又一次独立佐证。
  同时记下：**本机复现率比此前记录暗示的高**（三次全量红两次），所以「复跑即绿」要真的跑。
- **F-12**：取证缺口 1 的出处写成「spec-001 根图」，实为根图的 `baseline_evidence` 记录。已改。

**快审另给了一条比我更硬的不归因证据**：它那两次偶发都落在 `Application.Tests`，
而 TASK-06 给该程序集加了 **0** 条判据。

**两件不属本 Task 可收口性、需带到最终评审的**：

1. 契约 `Permitted files` 的事后扩张（Revision 2）**至今未被所有者认可**——Round 2 就提了。
2. 本成果的已知限制：通知投递失败今天是无声的（总线把结果放进摘要，而没有一个调用方读摘要）。

登记表置 TASK-06 为 `done`，`tasks.active` 前移至 TASK-07。


## 统一前向注记 —— 「投递失败无声」这件事已经不成立了

本文件里多处写着「通知投递失败今天是无声的」「彻底无声」。**这些句子在写下时为真，今天已经是假的。**

TASK-07 之后，所有者裁定「现在就修：给总线加日志」。`NotificationBus` 注入了
`ILogger<NotificationBus>`，在三处记 `Warning`：投递抛异常（带异常本身）、到了每收件人期限、
以及重试额度用尽而彻底放弃某收件人。弃权与「人不在了」不记——它们不是出了问题。

**不回改上文**：那些句子当时是对的，而且正是它们促成了这次修复。

**受影响的是本文件里每一处「无声」「没有任何地方会说出来」「不注入任何 logger」及其同义说法，
一处不例外。** 这里**按内容索引，不列行号**——初版列了行号，而这是个只会追加的文件：
我自己在上面插了一行，清单里的行号就全差了 1，还漏掉三处。
**在会被插入行的文件里，行号比个数更脆。**

**差异清单本身不受影响**：C-D8 描述的差异是「映射时机从调用点挪进了处理器」，那仍然成立；
变的只是它的后果不再无声。
