# TASK-02 — 接入组队请求解码与 Command

## Task Unit Contract

- Revision: 1
- Outcome and references: 交付四个 A21 C2S 入口的严格解码、Command 调用和注册，覆盖 Spec RQ-001、RQ-002、RQ-004、RQ-005、RQ-009、RQ-011，Plan T-002。
- Owned boundary: `Server/src/Dfo.Server.Protocol/Game/Party/`、相关协议枚举/常量/注册、仅用于拆分 `SET_UDP_IP_PORT` 的现有 map-entry 上报代码，以及对应 Protocol 测试；不得实现 S2C 编码或生命周期清理。
- Required checks: 定向测试覆盖两个 31 字节端点样本、畸形端点、`0x000A` 允许形态和范围外 type 无副作用、`0x000B` 最小兼容接受/拒绝形态、`0x000D` 严格空 body及生产注册。
- Dependencies: TASK-01 的领域接口、结果类型和单例实现。

## Result — Round 1

- **Changed:** 新增 `Protocol/Game/Party/PartyCommandSlice.cs`，提供 `SET_UDP_IP_PORT`、`REQUEST_PEER`、`RESPONSE_PEER`、`LEAVE_PARTY` 的类型化请求和严格 decoder，四个只调用 `IPartyDomainService` 的 Direct Command/handler，以及四个受 selected-session transition gate 保护的消息 handler；双账号操作按 AccountId 固定顺序持有双方 gate。补齐 `ClientMessageType`、`GameProtocol` 和生产 DI 注册，并将 `SET_UDP_IP_PORT` 从无领域语义的 map-entry report family 拆出。新增 28 个 decoder、命令路由、范围隔离和生产注册测试。
- **Why:** 当前两条 A21 端点样本按实际字节算术解出端口 `30976`/`63752` 与 MTU `1472`；固定前缀外的身份尾部只保留观察、不参与状态。`0x000A` 只接受 7/9B，当前 9B 样本的 peer 是 LE `64`；`0x000B` 只接受静态/旧服证据支持的 7/16B；非零 peer type 在进入 Command 前返回，确保交易/PvP 不会触及队伍状态。消息 handler 持有会话换代 gate，Command 保持 Direct/Unscoped，避免与同一 gate 的生命周期路径重入死锁。
- **Checks:** RED：新增测试首次因 `Dfo.Server.Protocol.Game.Party` 尚不存在而编译失败；GREEN：`dotnet test ... --filter FullyQualifiedName~PartyCommand` 为 28 passed、0 failed、0 skipped。完整 Protocol 首次运行因工作树未配置 `Script.pvf` 产生 43 个既有 PVF/物品失败；设置只读 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-debug\Script.pvf` 后重跑为 747 passed、0 failed、0 skipped，并从 TRX 逐项统计 `Passed=747`。`git diff --check` 通过。
- **Remaining:** TASK-03 将把成功 Command 结果中的类型化 `PartyNotificationPlan` 发布给专用 Notification handler；本 Task 依边界不实现任何 S2C encoder 或投递。等待 Round 1 独立 quick review。

## Independent Quick Review — Round 1 (IQR-01)

```yaml
kind: rks-review
verdict: changes_required
```

- **Reviewed execution:** `Result — Round 1`；Task Contract Revision 1，无 amendment；基线 `355aaf5cb6714044b4781185ec3118007bc97bec`，限定 TASK-02 owned boundary。
- **Owned-diff identity:** `PartyCommandSlice.cs`=`6D27249E877DEF51`、`PartyCommandCodecTests.cs`=`6C5C1AB8DCBE62A9`、`PartyCommandHandlerTests.cs`=`E9C9A635056F379F`、`ProtocolModule.cs`=`57BA862F6C36E832`、`NetworkMessages.cs`=`1DDB87EC9AC474B2`、`GameProtocol.cs`=`0F641F9DF85FA640`、`MapEntryReportSlices.cs`=`0B88A784ADFB26AA`、`MapEntryReportCodecTests.cs`=`1E13439465E49CCA`。
- **Evidence:** 独立复跑 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~PartyCommand` 为 28 passed、0 failed、0 skipped；`git diff --check` 通过。检查确认两个 `0x0002` 样本按 LE 解出端口 `30976`/`63752`、MTU `1472`，`0x000A` 只接受 7/9B、`0x000B` 只接受 7/16B、`0x000D` 只接受空 body；非零 peer type 在 Command 前返回，四个 decoder/handler 与 Direct/Unscoped Command 均有生产注册。

### Blocking Findings

- **IQR-01-F1 — 目标首次查找为空时，邀请可在未持有目标账号 transition gate 的情况下成功提交。** `RequestPeerHandler` 在进入 `PartyTransitionExecution.RunAsync` 前只查找一次目标（`PartyCommandSlice.cs:271-278`）；目标当时不存在时 `second=null`，执行器只取得邀请者账号 gate（`:187-199`），但 `PartyDomainService.RequestInvite` 随后会按 wire uid 再次查找目标并可直接成功（`PartyDomainService.cs:60-93`）。存在如下合法交错：handler 首次查找得到 null 后被调度挂起；目标账号的选角流程在持有自身 transition gate 时先把 session 放入目录，并在流程结束前发布 `StandingInTown=true` presence；handler 恢复后只持有邀请者 gate，领域重查便能接受该仍处于选角 transition 的目标。这样既违反 Plan BD-002“取得双方 gate 后再次校验”的原子边界，也会让 TASK-03 接入后把邀请通知插入目标尚未完成的选角/城镇投影序列；若该流程随后失败补偿，还会产生刚邀请即清理的竞态。请让双人命令只对已由租约覆盖的精确双方会话提交：可在获取租约后重读并比较双方 identity，发现目标从 null/不同 identity 变更时释放并按新账号稳定排序重试或拒绝；也可把精确目标 identity 传入 Command/领域并拒绝未覆盖的重解析结果。补一个可控交错测试，令首次目标查询返回 null、随后在目标 gate 被占用期间发布目标，断言 Command 不会绕过目标 gate；同时覆盖双方账号反向并发仍按稳定顺序完成且不死锁。

## Result — Round 2

- **Changed:** 闭合 IQR-01-F1：`REQUEST_PEER`/`RESPONSE_PEER` 首次无法解析对端精确会话时立即无副作用返回；能解析时按双方 AccountId 排序取得 transition gate，并在租约内重新读取、逐值比对双方 `SelectedSessionIdentity`，任一会话已替换则不派发 Command。单人端点/离队入口也在自己的租约内重读精确会话。新增“目标首次缺失后出现仍不派发”和“目标 gate 被占用期间会话换代，释放后仍不派发”的确定性交错测试。
- **Why:** Command 内部领域重读不能替代消息层对双方生命周期门的所有权；只有首次解析到的双方 identity 均被相应账号租约覆盖并在租约内保持一致，才能安全提交双人变更。缺失或变化时拒绝比无门重试更安全，也不会改变邀请/队伍状态。
- **Checks:** `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~PartyCommand` 为 30 passed、0 failed、0 skipped；`git diff --check` 通过。
- **Remaining:** 等待 Round 2 fresh quick review，重点复核 IQR-01-F1 的所有交错是否闭合。

## Independent Quick Review — Round 2

```yaml
kind: rks-review
verdict: changes_required
```

- **Reviewed execution:** `Result — Round 2`；Task Contract Revision 1，无 amendment；基线 `355aaf5cb6714044b4781185ec3118007bc97bec`，仅复核 IQR-01-F1 及其直接回归。
- **Reviewed change identity:** `PartyCommandSlice.cs`=`B0BC9FC4B1AFF1B6`、`PartyCommandHandlerTests.cs`=`17043CC23746174C`；Round 1 其余 owned 文件 hash 未变化。
- **Code disposition:** 原缺陷的生产路径已闭合。`REQUEST_PEER`/`RESPONSE_PEER` 在首次无法解析对端时直接返回；解析成功后，`PartyTransitionExecution` 按 `AccountId.Value` 升序取得双方排他租约，并在租约内重读且逐值比较发起方/响应方与对端的 `SelectedSessionIdentity` 后才派发 Command。该结构同时覆盖任一方在等待期间换代，固定全局锁序也消除了两端反向取得双租约的循环等待条件；单人端点/离队入口亦在自身租约内重读。
- **Evidence:** 独立复跑 `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~PartyCommand` 为 30 passed、0 failed、0 skipped；独立列出的 30 项测试中，Round 2 只新增 `A_target_that_was_missing_on_the_first_lookup_cannot_appear_behind_the_gate` 与 `A_target_replaced_before_its_gate_is_acquired_is_not_dispatched`；`git diff --check` 通过。

### Blocking Findings

- **IQR-02-F1 — IQR-01-F1 要求的双方换代与反向并发无死锁回归证据仍不完整。** 当前确定性交错测试只占用并替换目标账号（`PartyCommandHandlerTests.cs:130-166`），测试身份固定为 inviter AccountId=1、invitee AccountId=2（`:179-183`），因此只走“先持有邀请者、再等待目标”的锁序；没有覆盖目标账号更小、handler 先等待目标而邀请者 identity 在等待期间换代的对称分支，也没有启动双方反向的双账号操作来证明两条路径都遵守相同 AccountId 顺序并在超时界限内完成。IQR-01-F1 已明确要求这项测试，且本轮任务要求确认新增测试确实覆盖双方 identity 与死锁风险；仅凭当前实现目检不能替代该回归保护。请补一个可控的反向账号顺序交错测试，令未被持有的一方在等待期换代并断言不派发；再补双方反向并发、在有界时间内完成的测试，断言无死锁且没有绕过租约的 Command 派发，然后由 fresh reviewer 复核。

## Result — Round 3

- **Changed:** 补齐 IQR-02-F1 的对称与死锁证据。新增高账号邀请者向低账号目标发起时，同时占用低账号与邀请者 gate，在 handler 等待低账号期间替换邀请者 identity，随后释放并断言零 Command；新增双方反向请求共享一个被占用的低账号 gate 的并发测试，两条路径解除争用后都须在 2 秒界限内完成并各派发一个请求 Command。
- **Why:** 反向并发测试的人为争用会让未排序实现形成稳定 AB/BA 环（低→高路径等待低，高→低路径先持高再等待低，释放低后前者持低等待高）；当前统一按 AccountId 升序实现则两条路径都先排队等待低账号，因而能够顺序完成。它直接保护固定锁序，而不是依赖偶然调度。
- **Checks:** `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter FullyQualifiedName~PartyCommand` 为 32 passed、0 failed、0 skipped；两个并发测试均有 2 秒有界等待；`git diff --check` 通过。
- **Remaining:** 等待 Round 3 fresh quick review；无生产语义新增。

## Independent Quick Review — Round 3

```yaml
kind: rks-review
verdict: passed
```

- **Reviewed execution:** `Result — Round 3`；Task Contract Revision 1，无 amendment；基线 `355aaf5cb6714044b4781185ec3118007bc97bec`；仅复核 IQR-01-F1、IQR-02-F1 及直接回归。
- **Reviewed change identity:** `PartyCommandSlice.cs`=`B0BC9FC4B1AFF1B6`、`PartyCommandHandlerTests.cs`=`E1F8314019523492`。
- **Code disposition:** IQR-01-F1 的生产双账号 gate 边界已闭合：双人 handler 仅在首次解析到精确双方会话后进入执行器，执行器对去重后的 `AccountId.Value` 升序取排他租约，并在租约内重读、逐值比较发起方和对端 identity，任一方缺失或换代均不派发 Command。
- **Regression disposition:** 新增高账号邀请者用例在低账号 gate 等待期间替换未持有的邀请者 identity，可对称拦截邀请者换代后的派发。双向并发用例在低账号 gate 上按 FIFO 排队：若双账号未排序，低→高路径会先持低再等待已被高→低路径持有的高，与后者等待低形成可重现 AB/BA，必然触发 2 秒超时；当前升序实现下两路径顺序完成且各派发一个 Command。
- **Evidence:** 独立复跑 `FullyQualifiedName~PartyCommand` 为 32 passed、0 failed、0 skipped；两个 Round 3 并发用例额外连续复跑 20 轮均通过；`git diff --check` 通过。未发现未解决阻断项。
