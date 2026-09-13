# TASK-03 — entry 与 move read-repair

## Task Unit Contract

- Revision: 1
- Outcome: newcomer/mover 与 existing observers 均在对应 `0x0002` 后收到去重的当前 `0x0009`。
- Dependencies: TASK-02 accepted result。
- Owned files: party read-repair delivery、SelectCharacter/SetUserArea 接入及对应 Protocol tests；本 Task 记录/证据。
- Required checks: TDD RED/GREEN；dedupe、无 party、current revision、replacement、town publish/send 顺序。

## Result

### TDD deviation

- 第一轮先写了带未使用构造参数的结构骨架，因仓库 `TreatWarningsAsErrors` 在目标测试执行前触发
  `CS9113`；该次只能算 compile-first feedback，不计为行为 RED。删除骨架后，以下三轮均由目标断言先失败、
  再由最小实现转绿。

### Round 1 — current-truth delivery

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PartyTownProjectionDeliveryTests"`
- RED exit: `1`
- RED evidence: 有当前 party 的精确 recipient 预期收到一个 refresh，实际集合为空（`Assert.Single` 失败）。
- GREEN command: 同一命令；exit `0`。另覆盖 recipient/party 去重、无 party、不含 member、gate 内重查、
  replacement generation、equal committed revision 可向新 observer 投影，以及 stale revision 抑制。

### Round 2 — move ordering

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~TownProjectionHandlerTests.SetUserAreaPublishesTownThenRepairsPartyViewsInStrictOrder"`
- RED exit: `1`
- RED evidence: 预期五个严格有序阶段，实际只有三个；缺少 mover 与 existing observer 的 party read-repair。
- GREEN command: 同一命令；exit `0`，目标测试 `1/1` 通过。

### Round 3 — select ordering

- RED command: `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~SelectCharacter"`
- RED exit: `1`
- RED evidence: newcomer 与 existing observer 的两个 party repair 预期存在，实际均为空。
- GREEN command: 同一命令；exit `0`。另覆盖 town publication `Failed` 时不会对未实际收到 newcomer
  `0x0002` 的 recipient 提前发送 `0x0009`。

### Completed result

- Changed: 新增 singleton `IPartyTownProjectionDelivery`，对 exact recipients/represented sessions 去重；按
  `PartyId + Generation` 稳定排序并使用 TASK-02 的共享 generation gate，在 gate 内重查当前 snapshot；
  SelectCharacter 与 SetUserArea 分别在对应 `0x0002` 成功路径后接入 newcomer/mover 和 existing observer
  read-repair。
- Why: 新 observer 不能只依赖后续 mutation；read-repair 必须用 current truth，同时不能推进 mutation 的全局
  revision，否则会错误抑制成员投影。equal committed revision 因此允许投给此前未见过该 party 的 observer。
- Checks: 三轮有效 RED/GREEN；Protocol party/town/select/cleanup 聚焦集 `52/52`；Application
  select/party/cleanup 聚焦集最终 `45/45`；两处 cleanup 断言的集合类型推断噪声已改为稳定的
  `WireUserId` 序列比较。
- Remaining: 组合回归、协议资料、运行时 handoff 与完整独立审查由 TASK-04 负责。
