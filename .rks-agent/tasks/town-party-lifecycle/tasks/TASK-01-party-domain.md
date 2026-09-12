# TASK-01 — 建立组队领域状态与不变量

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付进程内队伍、邀请、成员端点及不可变通知计划的唯一原子写模型，覆盖 Spec RQ-003、RQ-005、RQ-006、RQ-009、RQ-010、RQ-011，Plan T-001。
- Owned boundary: `Server/src/Dfo.Server.Data/Parties/`、`Server/src/Dfo.Server.Abstractions/Parties/`、`Server/src/Dfo.Server.Application/Parties/`、Application 组合注册以及对应 `Dfo.Server.Application.Tests/Parties/`；不得写协议 decoder、encoder 或生命周期调用点。
- Required checks: 先以失败测试证明领域能力缺失，再使定向测试通过；覆盖自邀、域不一致、非队长、满员、一次性/过期/会话换代邀请、slot 分配、普通成员退出、队长解散、精确旧会话清理和端点更新；确认 Application 不新增 Infrastructure 引用。
- Dependencies: 已批准的 Spec 与 Plan，无前置 Task。

## Result — Round 1

- **Changed:** 新增 `Dfo.Server.Data/Parties/PartyModels.cs` 的不可变端点、成员、队伍快照、通知计划与失败原因；新增 `Dfo.Server.Abstractions/Parties/IPartyDomainService.cs`；新增 Application 单例 `PartyDomainService`，集中实现端点、邀请、接受、slot、离队、解散和精确会话清理；生产组合根注册该唯一写服务；新增 10 个领域/组合测试。
- **Why:** 队伍使用稳定 wire party id 加不复用的进程内 generation 区分客户端标识与邀请代际；30 秒邀请窗口集中在一个时钟常量；成功匹配到双方精确会话的响应立即把邀请变为一次性，即使后续位置或队伍代际校验失败也不能等状态恢复后重放；普通成员离开保留原 slot 空洞，让后续成员占最小空 slot，队长离开按批准边界直接解散。
- **Checks:** RED：首次定向运行因 `Dfo.Server.Data.Parties`/`Dfo.Server.Application.Parties` 尚不存在而编译失败；GREEN：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~PartyDomainServiceTests` 为 10 passed、0 failed、0 skipped；邻接回归：同一 Application 测试项目为 1238 passed、0 failed、116 skipped（依赖实际 PVF 的既有条件跳过）；`git diff --check` 通过；Application csproj 未出现对 Infrastructure 的 ProjectReference。
- **Remaining:** 等待 Round 1 独立 quick review；本轮 owned production diff 为 `Server/src/Dfo.Server.Data/Parties/`、`Server/src/Dfo.Server.Abstractions/Parties/`、`Server/src/Dfo.Server.Application/Parties/`、`ApplicationModule.cs` 中组队注册，owned tests 为 `Server/tests/Dfo.Server.Application.Tests/Parties/`。

## Independent Quick Review — Round 1 (IQR-01)

- **Reviewed execution:** `Result — Round 1`；Task Contract Revision 1，无 amendment。
- **Owned-diff identity:** 基线 `355aaf5cb671`；审查路径为 `Server/src/Dfo.Server.Data/Parties/`、`Server/src/Dfo.Server.Abstractions/Parties/`、`Server/src/Dfo.Server.Application/Parties/`、`Server/src/Dfo.Server.Application/ApplicationModule.cs` 的组队注册、`Server/tests/Dfo.Server.Application.Tests/Parties/`；文件内容短哈希依次为 `60A7CDF2385B92D5`、`4452CCF94CB8F30`、`89DDE8623F5CF5E8`、`AFCFC893C6546340`、`91EA7D6B4AF50402`。
- **Evidence:** `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~PartyDomainServiceTests` 复跑为 10 passed、0 failed、0 skipped；`git diff --check` 通过；`dotnet list Server/src/Dfo.Server.Application/Dfo.Server.Application.csproj reference` 仅列出 Abstractions 与 Data，未新增 Infrastructure 引用。
- **Verdict:** `changes_required`
- **Finding IQR-01-F1 — 无效接受响应会提前消费邀请，违反 RQ-005 的无副作用约束。** `PartyDomainService.AcceptInvite` 在过期、邀请者当前会话、同域和队伍代际检查之前执行 `invitations.Remove(invitee.WireUserId)`（实现第 114–149 行）。因此，例如邀请目标临时离开区域后响应会先得到域错误，但在 30 秒内回到原区域再次响应时会变成 `InviteNotFoundOrStale`；第一次换线响应已经改变了邀请状态。RQ-005 明确要求过期、换线、换角色或队伍变化的响应无副作用，当前 Result 中“后续校验失败也立即一次性消费”的选择与该要求直接冲突。最小修正是把精确邀请的删除移动到全部有效性检查通过之后、创建或扩展队伍之前，并补充“同域失配响应后恢复位置，在有效期内仍可接受”的回归测试；若产品确实要求任何精确响应都销毁邀请，则应先修订并批准 Spec/TaskContract，而不是由 Task 执行自行改变语义。

## Result — Round 2

- **Changed:** 修复 IQR-01-F1：`AcceptInvite` 只在双方精确会话、有效期、同域、目标归属、邀请者队伍代际/队长/容量全部验证通过后，才在同一锁内消费邀请并提交成员变更；新增临时跨区域响应失败、恢复同域后在有效期内成功接受的回归测试。
- **Why:** RQ-005 把换线、换角色、过期和队伍变化响应定义为无状态副作用；因此一次性语义只约束成功消费，不能把失败校验本身变成邀请状态写入。
- **Checks:** 定向命令 `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter FullyQualifiedName~PartyDomainServiceTests` 为 11 passed、0 failed、0 skipped；`git diff --check` 通过。
- **Remaining:** 等待 Round 2 fresh quick review；owned diff 沿用 Round 1，并新增 `AcceptInvite` 消费时点与对应测试。

## Independent Quick Review — Round 2 (IQR-02)

- **Reviewed execution:** `Result — Round 2`；Task Contract Revision 1，无 amendment；限定复核 IQR-01-F1 及其直接回归。
- **Owned-diff identity:** 基线 `355aaf5cb671`；审查路径与 Round 1 相同；当前文件内容短哈希依次为 `60A7CDF2385B92D5`、`4452CCF94CB8F30D`、`B7993960FDAE4DB1`、`AFCFC893C6546340`、`9E1F3DE80E39BA8D`，其中 Round 2 变化落在 `PartyDomainService.cs` 与 `PartyDomainServiceTests.cs`。
- **Evidence:** `AcceptInvite` 在查找精确邀请后，先完成过期、邀请者当前会话、同域、目标成员资格以及队伍代际/队长/容量校验（实现第 107–145 行），随后才在同一锁内删除邀请（第 157 行）；新回归用例证明跨区域失败后恢复同域仍可在有效期内成功接受（测试第 185–203 行）。独立复跑定向命令为 11 passed、0 failed、0 skipped；`git diff --check` 通过；`dotnet list Server/src/Dfo.Server.Application/Dfo.Server.Application.csproj reference` 仅列出 Abstractions 与 Data。
- **Verdict:** `passed`
- **Findings:** 无。IQR-01-F1 已闭合，未发现由本修复引入的直接回归。
