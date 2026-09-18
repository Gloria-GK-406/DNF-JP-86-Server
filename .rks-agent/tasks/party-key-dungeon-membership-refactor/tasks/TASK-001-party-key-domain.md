# TASK-001 — 可反查的真实与虚拟 PartyKey

## Task Unit Contract

- Revision: 1 (identifies this contract and its detailed plan)
- Outcome and references: 真实队伍拥有稳定 Key；虚拟队伍按角色拥有唯一 Key；组队服务可按 Key 反查当前成员、队长和设置，区域目录不依赖客户端 ID。覆盖 T-001、RQ-001、AC-001，为 RQ-003/RQ-005 提供查询接口。
- Owned boundary: `PartyModels.cs`、`IPartyService.cs`、`PartyService.cs` 及对应应用层测试；必要的兼容性调用点只可做编译修正，不在本单元迁移协议投影或副本业务。
- Required checks: 新的真实/虚拟 Key 行为测试；现有 `Dfo.Server.Application.Tests` 组队测试；解决方案编译，确认临时兼容桥不改变现有网络行为。
- Dependencies: 无。

## Detailed Plan

1. 在 `Dfo.Server.Data/Parties/PartyModels.cs` 定义具备真实/虚拟种类的值型 `PartyKey`。真实 Key 使用现有仅在建队时分配的 `Generation`（但不再把它等同客户端代际）；虚拟 Key 使用角色 ID，显式保证不同角色不碰撞。
2. 在 `PartySnapshot` 加入 Key，保留原有 `PartyId` 作为 T-002 移除的临时兼容字段；更新真实快照构造及虚拟快照工厂。不能在 T-001 直接移除旧字段，否则协议消费者在 T-002 迁移前无法通过编译。
3. 将 `PartyService` 的真实队伍及区域目录索引改为按 Key 管理；旧 `ushort` ID 仅暂时用于既有快照/协议调用。新增按 Key 返回领域成员镜像的接口，只含 `CharacterId`、槽位、当前队长等必要信息，不要求 `WireUserId` 或 session 才能查。虚拟 Key 可直接由其中的角色 ID 形成单人成员镜像；角色规则所需的其他资料由各自角色/规则服务随后获取，不在 PartyService 中补一份会话镜像。
4. 先写能观察 Key 唯一、稳定、反查及队长退出连续性的行为测试并确认基线失败，再做最小实现。若相关测试只能冻结内部字段/构造形状而没有忠实行为 oracle，按 TDD 价值门改用现有回归与编译，不伪造 RED。
5. 运行组队相关测试和解决方案编译；记录 T-002 所需的协议桥接事实，不提前改动其发送顺序。

## Result

- State: complete, 2026-09-17. `PartyKey` 区分真实/虚拟队伍；`PartyService.FindByKey` 返回不含网络连接的当前成员镜像。真实队伍及区域目录改由稳定 Key 索引；`PartyId` 暂作为 T-002 的兼容桥保留。
- TDD 价值门：接口/结构调整先于新测试，独立 RED 只会得到接口或测试适配器编译错误，不能证明行为缺陷，故未伪称行为 RED。新增队长离队后的 Key/成员查询测试；旧组队回归未改动预期。
- Evidence: `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore` 通过 1388、跳过 116；`dotnet build Server/86JP.Server.slnx -c Release --no-restore -p:BaseOutputPath=bin/codex-review/ -v:q` 0 警告/0 错误。
- T-002 handoff: `PartyService` 仍分配 `PartyId`，`PartyNotificationPlan` 仍含 `ClearPartyId` 等字段；`PartyNotificationSlice` 的 `PartyProjectionStreamRegistry` 按 `Generation` 排序，同一代际的队长退出先发 `LeadershipTransfer`，再只清离队者并刷新余者。需要把换届后的旧编号清除、新编号建立做成同一发送序列，并保持 Key 不变。
