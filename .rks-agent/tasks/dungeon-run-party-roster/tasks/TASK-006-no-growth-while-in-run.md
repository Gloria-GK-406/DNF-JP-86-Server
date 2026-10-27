# TASK-006 — 运行中副本的队伍不能增员

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付 Plan T-004（BD-006），覆盖 Spec RQ-007、AC-009。邀请或接受加入一支已绑定副本的队伍时被拒绝；即使队伍被构造为含图外角色，该角色也得不到副本的任何资格。
- Owned boundary:
  - 生产代码：`Server/src/Dfo.Server.Protocol/Game/Party/PartyProtocolCommands.cs`（发起邀请、接受邀请两个 CMD），以及注入所需的注册改动（若有）。
  - 测试：`Server/tests/` 下新增或修改的组队、副本测试。
  - 队伍服务（`Server/src/Dfo.Server.Application/Parties/`）不得引用副本服务。
- Required checks:
  - 构建无错误；
  - 组队与副本相关测试类全部通过，最后跑一次完整测试；
  - 变异检查：去掉守护后，AC-009 的拒绝测试失败；
  - 代码检查：`Server/src/Dfo.Server.Application/Parties/` 下无 `IDungeonRunDomainService` 引用。
- Dependencies: TASK-005
- Result review: not-required — 守护只在两个 CMD 里各加一处前置判断。现有路径上不可达，所以不改变可达行为；拒绝测试与变异检查能直接暴露守护缺失或误放行，图外角色测试能暴露副本资格判定的回退。

## Detailed Plan

1. **守护**：在发起邀请与接受邀请的 CMD 中，调用队伍服务之前，判断“邀请方所在队伍”是否已绑定副本，即 `runs.Find(邀请方角色)` 不为空。绑定则返回拒绝，不调用队伍服务。
   - 拒绝原因选用现有 `PartyFailureReason` 中最贴切的一项，不新增枚举值。现有路径上这一分支不可达，不改变线上帧，原因选择写进 Result。
   - 接受邀请时还要再判断一次：被邀请方自己若在副本中，同样拒绝。
2. **AC-009 测试**：
   - 拒绝：用 `TestParties` 组两人队并入场，以真实 CMD（或经现有组队测试驱动）让第三名角色发起或接受加入，断言被拒，且队伍成员不变。
   - 图外角色：直接对真实 `PartyService` 执行邀请加接受，绕过守护，把第三名角色放进副本所属的队伍。然后断言：
     - 副本成员视图不含他；
     - 按他的角色 `Find` 为空；
     - 他发起过图、拾取、结算请求时副本不变；
     - 原有两人的视图仍恰有一名队长。
3. **检查**：按 Required checks 执行，并把变异检查结果写进 Result。

## Result

- Outcome: 完成。发起邀请（`REQUEST_PEER`）与接受邀请（`RESPONSE_PEER`）两个 CMD 在调用队伍服务前检查双方是否在副本中，任一方在副本中即拒绝；队伍服务未引用副本服务。AC-009 的拒绝测试与图外角色测试已补齐。未提交。
- Round 1，改动文件：
  - 生产代码（1 个）：`Server/src/Dfo.Server.Protocol/Game/Party/PartyProtocolCommands.cs`。两个处理器构造器各加注入 `IDungeonRunDomainService runs`；新增同文件内的 `PartyRunAdmission.Refusal`。注册无需改动：处理器经 `AddProtocolCommandHandler` 由容器构造，副本服务已注册。
  - 既有测试只改夹具（1 个）：`Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.cs`。类改为 `partial`；`DriveRequestPeer`、`DriveResponsePeer` 构造处理器时补传 `TestParties.Runs()`（无副本）。断言零改动。
  - 新增测试（2 个）：
    - `Server/tests/Dfo.Server.Protocol.Tests/Game/Party/PartyCommandHandlerTests.RunningParty.cs`（真实 `PartyService` + `TestParties.Runs` + 真实两个 CMD 处理器）；
    - `Server/tests/Dfo.Server.Application.Tests/World/DungeonRunDomainServiceTests.OutOfRoster.cs`（领域层图外角色）。
- Changed：
  - 守护位置：发起邀请放在 `InviteAsync` 中 `request.DemandCurrent()` 之后、`parties.RequestInvite` 之前；接受邀请放在既有的在线与城镇判断（`TryCapture`）通过之后、`parties.AcceptInvite` 之前。两处都写作 `PartyRunAdmission.Refusal(runs, a, b) ?? parties.Xxx(...)`。
  - 判定：`runs.Find(a) is not null || runs.Find(b) is not null`。发起邀请检查邀请方与目标，接受邀请检查被邀请方与邀请方。
- Why：
  - 两侧都查，而不只查“邀请方所在队伍”：同一个 `REQUEST_PEER` 在发送方无队伍、目标是队长时是“申请加入”（`PartyService.RequestInvite` 中 `membership.TryGetValue(target...)` 分支），此时运行中的队伍在目标一侧；接受申请时，`RESPONSE_PEER` 的发送方是队长（`AcceptJoinLocked`）。另外，独自在副本中的角色属于虚拟队伍，若接受他人邀请，`KeyOf` 会变为真实队伍，他自己的副本就失去唯一成员。两侧都查一次覆盖了这三种情况，与 Detailed Plan 第 1 条“接受时被邀请方自己在副本中同样拒绝”一致，并把同样的判定用于发起邀请。
  - 拒绝原因选 `PartyFailureReason.NotStandingInTown`，不新增枚举值：
    - 语义上，副本中的角色就是不在城镇；队伍服务对任一侧没有城镇目录范围时（`ValidateTownPair`）给出的正是这一项。
    - 线上不可观察：两个 CMD 失败时只写日志并返回 false，不发失败帧，`Failure` 只出现在日志里。
    - 未选 `CallerNotCurrent`：它表示会话不在世界中或拿不到城镇位置，与“已绑定副本”不符。
  - 守护放在既有判断之后：现有可达路径中，副本成员已离开城镇区域，`TryCapture` 先失败并以 `CallerNotCurrent` 拒绝，守护分支不被执行，日志中的失败原因也不变（Spec 可达性说明第 2 条）。
  - 发起邀请的守护放在 `DemandCurrent()` 之后，紧挨队伍写入，避免 `await memberStatus` 期间状态变化导致判断过期。
  - 注入合规：`IDungeonRunDomainService` 以 `DomainService` 结尾，符合 `CommandHandlerArchitectureTests` 的注入面规则，全量测试中该架构测试通过。
- Checks：
  - `cd Server && dotnet build 86JP.Server.slnx`：0 警告，0 错误。
  - 新增测试（7 个用例，全部通过）：
    - `Inviting_a_character_into_a_party_is_refused_while_the_party_has_a_run`（running: false/true）：
      - running=true 时，队长经 CMD 邀请第三人被拒，队伍服务中没有留下邀请；
      - 再直接经队伍服务造一份邀请（模拟入场前已发出），第三人经 CMD 接受被拒；
      - 队伍仍为两人，副本成员视图恰两人且一名队长，`EntryRevision` 不变，第三人无副本、仍在虚拟队伍。
      - running=false 的对照证明同样的两个 CMD 在无副本时成功，队伍变为三人。
    - `Applying_to_a_party_is_refused_while_the_party_has_a_run`（running: false/true）：第三人向队长申请被拒，预置申请后队长接受被拒，断言同上；对照组成功。
    - `A_character_alone_in_a_run_cannot_join_a_party`（running: false/true）：独自在副本中的角色被邀请、接受邀请都被拒，他仍在虚拟队伍、仍是自己副本的唯一成员；对照组成功组成两人队。
    - `A_character_put_into_a_running_party_has_no_part_in_its_run`（领域层）：直接经真实 `PartyService` 邀请加接受，把图外角色放进副本所属的队伍（`KeyOf` 等于副本的 `PartyKey`，队伍三人）。断言：
      - 两名成员的视图与按 `RunId` 的视图都只含入场两人，恰一名队长；
      - 按图外角色 `Find` 为空；
      - 他的过图（`TryMove` 两个重载）、拾取（`TryClaimGroundDrop`）都返回空，副本的 `EntryRevision`、当前格、房间版本、地面物品不变；
      - 成员拾取的变更 `Recipients` 只有入场两人；
      - 清场开启后，他的结算（`TryClaimSettlement` 两个重载、`TrySettle`）都返回空，成员仍未结算，而成员随后可以领取结算。
  - 变异检查（打补丁、构建、运行，再用备份还原）：
    - `Refusal` 恒返回 null（去掉守护）：3 个 running=true 用例失败。
    - 只查第一方：同 3 例失败；只查第二方：同 3 例失败。说明两侧检查都有测试守护。
    - 附加：去掉副本服务 `RunOf` 中的 `aggregate.IsMember(characterId)`：图外角色测试失败（`KeyNotFoundException`）。
    - 还原后 `git diff -- Server/src` 只含 `PartyProtocolCommands.cs` 的守护本身（两处构造器注入、两处调用、`PartyRunAdmission`），24 行增、4 行删。
  - 代码检查：`grep -rn IDungeonRunDomainService Server/src/Dfo.Server.Application/Parties` 无结果（退出码 1）。
  - `dotnet test Server/86JP.Server.slnx` 全量，失败均为 0：Pvf 73 通过／9 跳过；Persistence 484／0；HttpApi 59／0；Infrastructure 655／348；Protocol 1739／60；Networking 189／20；Host 200／12；Application 5497／493。与 TASK-005 相比，Protocol +6、Application +1，即新增用例数。跳过项均为缺 PVF 的内容测试。
- Remaining：
  - 守护在实际游戏中不可达（入场即离开城镇），协议测试用全员在城镇的替身才能走到守护；没有生产容器级用例。
  - 接口注释与 `Document/` 中“运行中队伍不增员”的说明留给 T-005。
  - TASK-005 Review 的 R4（开新局时对“入场者残留在其他副本”的防御）本单元未处理，与增员守护无关，仍留待最终审查决定。
