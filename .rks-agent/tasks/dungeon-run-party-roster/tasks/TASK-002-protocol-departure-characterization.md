# TASK-002 — 协议层离开与结束路径的表征测试

## Task Unit Contract

- Revision: 1
- Outcome and references: 在未改动的基准代码上，用真实服务和两人真实队伍，钉住各离开与结束路径在协议层的可观察行为：每个收件人收到的帧类型和顺序、队伍通知的收件人、投递登记中剩下的人、队伍成员与队长。覆盖 Plan T-001 的协议部分，服务 Spec RQ-010 与 AC-011，并为 AC-005～AC-008 提供基准。
- Owned boundary: 只修改 `Server/tests/` 下的测试文件和测试夹具，包括为现有测试驱动补充可选的注入参数。不改任何生产代码。
- Required checks:
  - `dotnet build Server/86JP.Server.slnx` 无错误；
  - 运行所有新增或改动测试所在的测试类，全部通过；
  - 对每个新增测试，临时打乱或去掉它所钉住的一项生产行为（例如跳过给剩余成员的队伍通知、跳过离场登记），确认测试失败；只需确认，不保留改动，在 Result 中记录。
- Dependencies: TASK-001
- Result review: required — T-003 会改变放弃副本和断线拆除的内部顺序，判断协议帧有没有变的唯一依据就是这些测试。断言若太宽（只看帧数、不看类型、顺序和收件人），顺序改变后的行为差异将无人察觉。

## Detailed Plan

先在现有夹具里找能用真实服务、两人真实队伍的入口，按盘点结论优先复用：
- `CharacterDeathFailureBoundaryTests.cs` 中 `ShopFixture` 加 `Enter(f, peer)`；
- `DungeonExitLifecycleTests.WireOn`；
- `PartyMutationTestDriver` 与 `PartyClientProjectionService`+`PartyService`；
- `SelectCharacterReturnTests` 的清理服务构造。

断言收件人与帧时，统一按“每个连接收到的帧操作码序列”记录。基准中帧的确切内容已有专门测试，这里只钉住类型和顺序。

测试清单（A 为队长，B 为队员）：
1. **放弃副本**，B 和 A 各发起一次：
   - 发起者的帧序列；
   - 剩余成员收到的帧序列（含队伍通知）；
   - 投递登记只剩剩余成员；
   - 发起者已退队、剩余成员为队长。
2. **断线拆除**，B 和 A 各断线一次：剩余成员保持原 `RunId` 并为队长；剩余成员收到的帧序列；清理返回的队伍通知收件人。
3. **副本内退队、被踢**：
   - 现有 `PartyCommandHandlerTests` 中“投递登记”的断言实际是空转。给测试驱动增加注入投递登记的可选参数，让它真正生效。
   - 钉住被踢者与剩余成员的帧序列，以及投递登记。
4. **副本内转让队长**：两人的帧序列；`RunId` 不变；副本视图的队长为新队长；投递登记不变。
5. **整队退出（Eplp）**，两人真实队伍、队长发起：两人的帧序列与顺序（对端的决定先于队长的应答）；副本被移除；队伍成员与队长不变。
6. **选图回城（队长、有副本）**：两人的帧序列；两人的副本都已结束；投递登记为空；队伍不变。非队长分支属于 T-003 的新行为，不在这里表征。
7. **单人断线**：副本整局结束；发起连接以外没有帧。

如果某条路径在现有夹具里构造代价过高，比如需要 PVF 才能运行，就选最接近的真实服务组合，并在 Result 中说明用了哪种替代以及它没覆盖什么。

**检查方法**：先在基准上运行确认通过；再按 Required checks 第 3 项逐项检查；最后还原。

## Result

- Outcome: done
- Round: 1。改动文件（生产代码未改，`git diff -- Server/src` 为空）：
  - 新增 `Server/tests/Dfo.Server.Application.Tests/World/DungeonDepartureProtocolCharacterizationTests.cs`；
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyMutationTestDriver.cs`：`Handler` 增加可选参数 `deliveries`（`DungeonRunDeliveryRegistry`）；
  - `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs`：两个副本内退队、被踢用例注入投递登记。
- Changed：新增 8 个测试方法、11 个用例。共用夹具 `TwoMemberRun`：`ShopFixture`（生产容器：真实队伍、副本、投递登记、分发器、发件箱、编码器）+ `BossTowerDomainServiceTests.AddMember` 建第二个在线角色 + `CharacterDeathProtocolLifecycleTests.Enter(f, peer)` 建真实两人队伍、副本并登记投递。请求经 `DungeonExitLifecycleTests.WireOn` 走真实解码与分发，断线经 `INetworkConnectionLifecycleDispatcher.DispatchDisconnectedAsync`。帧按“两个连接合并后的交付顺序 + 收件方 + 操作码”整体断言；状态断言只读副本视图与 `Find`、队伍快照、投递登记。A 为队长，B 为队员，“队伍通知”指剩余者收到的 0x99、0x0B、0x09；若离开者是队长，前面还多一个换队长的 0x09。
  - `Giving_up_..._leads(leaderGivesUp)`（B、A 各一次）：先是离开者收到 0x09，再是剩余者收到队伍通知，最后离开者收到 0x2A、0x21、0x03、0x17、0x18。离开者不在副本中，所在队伍为虚拟队伍；剩余者保持原 `RunId`，在副本视图中是唯一成员且为队长；投递登记只剩剩余者；队伍只剩剩余者，由其任队长；无关闭连接。
  - `Disconnecting_..._told(leaderDisconnects)`（B、A 各一次）：只有剩余者收到队伍通知，断线者没有任何帧（队伍通知的收件人以实际投递来观察）。断线者已不在世界中；副本、投递登记、队伍状态的断言同上。
  - `Leaving_the_party_in_a_run_..._without_a_town_return(leaderLeaves)`：离开者只收到 0x09，之后是剩余者收到队伍通知，离开者不回城。状态断言同上。
  - `Removing_the_member_in_a_run_..._town`：先是被踢者收到 0x09，再是队长收到队伍通知，然后被踢者收到 0x03、0x17、0x18、0x21（回城）。状态断言同上。
  - `Transferring_leadership_in_a_run_...`：队长依次收到 0x1A、0x09，队员随后收到 0x1A、0x09。两人的 `RunId` 都不变；副本视图恰有一名队长，即 B；两人都在视图中；投递登记仍为 [A, B]；队伍成员为 A、B，队长为 B。
  - `Eplp_by_the_leader_..._before_the_leader_ack`（[1,2]）：B 的 0x48 先于 A 的 0x48；之后 A 收到 0x03、0x17、0x18、0x21，B 收到同样的四帧；最后是 A、B、A、B 各一个 0x09。副本按 `RunId` 和按两人查找都为空；投递登记为空；队伍成员与队长不变。
  - `Back_to_village_by_the_leader_with_a_run_...`：直接给两人记录入场意图，构造基准中不可达的防御分支（见 Spec“可达性说明”）。A 先收到 0x84，之后的回城帧与 0x09 序列同 Eplp。两人的副本都已结束；投递登记为空；队伍不变。非队长分支不在本单元表征。
  - `Disconnecting_alone_ends_the_whole_run_and_sends_nothing`：单人虚拟队伍副本，另有一名不在队伍中的在线角色。断线后两条连接都没有帧，也无关闭；副本按 `RunId` 查找为空；投递登记为空。
  - `PartyCommandHandlerTests`：注入投递登记后，原退队用例的 `deliveries.Find==[Inviter,Invitee]` 断言（原先因未注入而空转）按基准实际行为改为 `[Invitee]`；被踢用例补充断言 `[Inviter]`。
- Why：
  - 没有用 `PartyMutationTestDriver` 的组件替身，改用 `ShopFixture` 的生产容器。只有这样才能在同一处观察真实编码后的帧和跨连接的交付顺序，并让断线经过真实的生命周期分发与清理通知投递。
  - 当前视图用 `EntryFailureProbe.CompleteView` 替代（与 `DungeonExitLifecycleTests` 相同）。夹具让队长离开城镇区域，以对应真实入场；第二个角色从未进入城镇区域。
  - 未经入场与选图流程，因此本单元不覆盖：入场或选图带来的城镇、目录状态，Eplp option 0 的重挑意图恢复，祭坛与两塔变体，编码或作用域失败分支。这些已有各自的测试。
  - 帧内容不在这里断言。
  - 修改 `PartyCommandHandlerTests` 的断言属于“让空转断言生效后按基准行为改写”，不是行为修正；T-002、T-003 迁移时应保持新断言不变。
- Checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告 0 错误。
  - `dotnet test tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~DungeonDepartureProtocolCharacterizationTests"`：11 通过、0 失败。
  - `dotnet test tests/Dfo.Server.Protocol.Tests --filter "FullyQualifiedName~Dfo.Server.Protocol.Tests.Game.Party"`：115 通过、0 失败（含 `PartyCommandHandlerTests` 19 个）。
  - 变异检查：每项单独打补丁、构建、运行上述筛选，再用 `git checkout` 还原。最终 `git diff -- Server/src` 为空。
    - M1 放弃副本跳过 `ParticipantDeparted`：放弃副本 2 例失败。
    - M2 放弃副本跳过队伍通知：放弃副本 2 例失败。
    - M3 放弃副本把 0x2A 应答移到队伍通知之前：放弃副本 2 例失败。
    - M4 断线清理跳过 `ParticipantDeparted`：断线 2 例与单人断线失败。
    - M5 断线清理丢弃队伍通知计划：断线 2 例失败。
    - M6 断线清理跳过 `EndAsync`：断线 2 例与单人断线失败。
    - M7 退队、被踢跳过 `ParticipantDeparted`：退队 2 例与被踢失败；`PartyCommandHandlerTests` 两个副本用例也失败，证明断言已生效。
    - M8 退队、被踢、转让跳过 `ProjectPartyAsync`：退队 2 例、被踢、转让失败。
    - M9 被踢跳过回城：被踢失败。
    - M10 Eplp 把队长应答提前到对端决定之前（原位置的应答保留）：Eplp 失败。
    - M11 Eplp 跳过 `ParticipantDeparted`：Eplp 失败。
    - M12 选图回城跳过 `ParticipantDeparted`：选图回城失败。
    - M13 选图回城只结束请求者：选图回城失败。
- Remaining：
  - 基准中“退队、被踢先改队伍后 `EndAsync`”的内部顺序，在协议层不可观察：已用 M14（把 `EndAsync` 提到改队伍之前）实测，上述筛选与 `PartyCommandHandlerTests` 全部仍通过。这里未表征，T-003 改顺序时以领域层 TASK-001 的测试为准。
  - 断线清理返回的通知计划对象本身没有直接断言，以实际投递到的连接代替。

## Review Round 1

```yaml
kind: rks-review
verdict: approved
```

- 类型：Independent Quick Review（Task Unit 快速审查），独立审查者。
- 审查对象：合同 Revision 1，执行 Round 1 的 Result；HEAD `80d77249` 上的未提交改动：新增 `Server/tests/Dfo.Server.Application.Tests/World/DungeonDepartureProtocolCharacterizationTests.cs`，修改 `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyMutationTestDriver.cs`、`PartyCommandHandlerTests.cs`。`git status` 中 `Server/src` 无改动。
- 依据：`reviewing-work` 的 task-unit 与 code 准则（仅限局部明显缺陷）；本合同与详细计划；Spec RQ-005、RQ-006、RQ-008、RQ-010、AC-005～AC-008、AC-011 与“可达性说明”；Plan T-001、T-003、BD-005；TASK-001 Result。对照的生产代码：`GamePartyTransitionCommandHandler.cs`（Eplp、`GiveUpAsync`）、`.Stages.cs`（`MutatePartyStateAsync` 与退队、踢人的 `ParticipantDeparted`）、`.Exit.cs`（`EndOrdinaryRunCoreAsync`）、`SelectCharacterLifecycleServices.cs`（断线清理）、`DungeonSelectionCommandPhases.cs`（选图回城）；测试支撑 `Support/RecordedOutbound.cs`、`CharacterDeathFailureBoundaryTests.cs`（`Enter`、`CharacterDeathOracle.Types`）。
- 复跑检查（审查者本地）：
  - `dotnet test tests/Dfo.Server.Application.Tests --filter "FullyQualifiedName~DungeonDepartureProtocolCharacterizationTests"`：11 通过、0 失败。
  - `dotnet test tests/Dfo.Server.Protocol.Tests --filter "FullyQualifiedName~Dfo.Server.Protocol.Tests.Game.Party"`：115 通过、0 失败。
  - 变异检查 M1～M14 未复做（审查只读），按 Result 记录采信；逐项对照断言形态，各变异都会改变被整体比较的序列或状态，结论可信。

### 结论

1. **作为 T-003 帧与收件人不变判据是否足够**：足够。`Sequence()` 取 `RecordedOutbound.Entries`（已交给发送端与仍在发件箱中的登记，按登记顺序，跨连接合并），对“收件方 + 操作码”整表 `Assert.Equal`，同时钉住类型、顺序、收件人和条数；多发、少发、换序、换收件人都会失败。`SideOf` 对第三条连接直接抛错，单人断线用例还让一个在线的非队员连接接受检查，收件人扩大也能发现。关闭连接另以 `Closes` 为空断言。投递登记、队伍成员与队长、副本视图都有精确断言。未发现过宽断言。
2. **新模型下断言是否仍然成立**：成立。状态断言只读 `Find(characterId)`、`Find(runId)`、副本视图的 `Participants`/`IsLeader`、队伍快照与投递登记，不触及 `ActiveParticipants` 或 `runsByCharacter`。在“副本成员 = 队伍当前成员 ∩ 入场名单”下，离开者已退队，因此 `Find` 为空；剩余者是唯一成员并任队长；转让后视图队长为 B。这些与 RQ-005、RQ-009 一致。队伍投影代码（`Protocol/Game/Party`）不读副本状态，T-003 调整“退队与清理副本”的先后不会改变队伍帧的收件人与内容来源。
3. **`PartyCommandHandlerTests` 由 `[Inviter, Invitee]` 改为 `[Invitee]`**：属实，是基准真实行为。原用例没有把登记传入处理器，夹具另建了一个登记，测试持有的登记从未被改动，所以原断言是空转。处理器在退队成功、离开者已无副本时调用 `registry.ParticipantDeparted`（`GamePartyTransitionCommandHandler.Stages.cs:804-805`），离开者 Inviter 被移出，剩 `[Invitee]`；踢人剩 `[Inviter]` 同理。这不是把测试改得去迁就缺陷。
4. **选图回城的构造**：合理。基准中，入场意图在进入选图界面时按整队记录，队长回城时连带消耗各队员的意图（`ResolveReturningMembersAsync`）；给两人都记录意图，与真实选图后的意图分布一致，只是叠加了基准中不可达的“仍有副本”。基准对两人逐个调用 `EndAsync`，结果与 RQ-006 规定的“队长请求、整局结束”一致：副本移除、两人回城、登记清空、队伍不变。T-003 改为“仅队长整局结束”后，返回集合仍是两名持意图成员，帧序列与状态断言应保持通过。非队长分支（AC-008）未在本单元表征，与合同一致。
5. **Result 的覆盖与未覆盖说明**：与代码和复跑结果一致。用例数（8 个方法、11 个用例）、各路径帧序列、夹具替代（`EntryFailureProbe.CompleteView`、未经入场与选图流程）、`EndAsync` 先后在协议层不可观察（M14）、断线通知计划以实际投递代替，均如实。
6. **可运行性**：两条命令均通过，见上。

### 需控制者处置（不阻断本单元）

- **整队退出、选图回城的 RQ-008 逐人副作用，T-001 整体尚无表征**：基准中两条路径都对每名成员走 `EndAsync`（Eplp 经 `EndOrdinaryRunCoreAsync`，选图回城在 `DungeonSelectionCommandPhases.cs:80-90`），按 RQ-008 应当产生离场生命周期，并在未清场且布局适用时产生失败虚弱。TASK-001 已注明这两条路径留给协议部分；本单元合同只要求帧、收件人、登记与队伍状态，测试只比较帧类型，0x21 的内容不受约束。`DungeonExitLifecycleTests` 的 Eplp 用例也不断言生命周期或失败虚弱。BD-005 把这两条路径改用“整局结束”入口，若该入口沿用清场回城、入场中止那样的副作用组合，就会少记生命周期或失败虚弱，而现有测试都不会失败。建议控制者在 T-003 前补这两条路径的 RQ-008 副作用表征（领域层或协议层皆可），或在 T-003 的合同中明确由它补齐。此项超出本单元合同，所以不影响本轮裁定。

### 非阻断建议

- `PartyMutationTestDriver.Handler` 的新参数类型用了完全限定名 `Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry`，可改为 `using`，与同文件其他参数一致。
- 本单元的表征依赖 `EntryFailureProbe.CompleteView` 与 `ShopFixture` 的生产容器；T-002 迁移夹具时，应只改 `TwoMemberRun.EnterAsync` 的构造，保留各断言原样，作为 AC-011 证据。
