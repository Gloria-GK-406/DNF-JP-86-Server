# TASK-01 — 大厅房间部分纯搬迁到房间 owner

## Task Unit Contract

- Revision: 1
- Outcome and references: 房间（`PvpRoom` 及 `Offer`/`Withdraw`/`OfferedOn`）从 `PvpLobbyDomainService` 搬到新的 `PvpRoomDomainService`（读面 `IPvpRoomQueryDomainService`、写面 `IPvpRoomDomainService`），所有调用方改读新 owner；行为不变。Plan T-003；Spec RQ-002 的 owner 部分。
- Owned boundary: `Abstractions/Sessions/ISessionPresence.cs`（移出房间部分）、新 `Abstractions/Pvp/`、`Application/Sessions/PvpLobbyDomainService.cs`、新 `Application/Pvp/`（服务与 `PvpRegistration`）、`ApplicationModule` 一行、`SessionAdjacentEntryDomainService`、`SelectCharacterLifecycleServices` 中读取房间处；测试：`GameplayWriteEntryTableTests`、`GameplayWriteAssertionTests`、`PvpLobbyEntryTests`、`SessionAdjacentEntryTests`、`RegistryExistenceTests` 等引用处。
- Required checks: 解决方案构建通过；`Dfo.Server.Application.Tests` 中 Sessions、Execution（写入口表、写断言、状态普查）、Commands 架构测试通过；Networking `SelectionLifecycleWholeCommandTests` 中 PvP 用例结果与基线相同。
- Result review: not-required — 纯搬迁，行为不变；写入口表、状态普查与注册检查会在漏登记或重复注册时失败，选角 PvP 用例覆盖读取链。

## Detailed Plan

1. 在 `Abstractions/Pvp/PvpRooms.cs` 定义 `PvpRoom`（原样搬迁，命名空间改为 `Dfo.Server.Abstractions.Pvp`）、`IPvpRoomQueryDomainService { OfferedOn(int listenerPort) }`、`IPvpRoomDomainService : IPvpRoomQueryDomainService { Offer; Withdraw }`。
2. `Application/Pvp/PvpRoomDomainService.cs`：原 `_rooms` 字典与三方法原样搬入（含写断言名改为新类名）。`Application/Pvp/PvpRegistration.cs`：`AddPvp()` 以同一实例注册两个接口面（拍卖行写法）；`ApplicationModule` 调用。
3. 从 `IPvpLobbyDomainService` / 实现中删除房间成员；`SessionAdjacentEntryDomainService`、`SelectCharacterSessionEntryService.PlanPvpAsync` 改注入 `IPvpRoomQueryDomainService`。
4. 测试引用改到新类型；写入口表把三个房间成员行移到新类型，`Types` 加新类型。
5. 构建并跑上述测试。

## Result

- Outcome: done
- Changed: 新增 `Abstractions/Pvp/PvpRooms.cs`（`PvpRoom` 原样迁入，`IPvpRoomQueryDomainService`、`IPvpRoomDomainService`）、`Application/Pvp/PvpRoomDomainService.cs`、`Application/Pvp/PvpRegistration.cs`（`AddPvp()`，一个实例两个接口面），`ApplicationModule` 调用；`IPvpLobbyDomainService`/`PvpLobbyDomainService` 删去房间成员；`SessionAdjacentEntryDomainService`、`SelectCharacterSessionEntryService` 改读 `IPvpRoomQueryDomainService`；测试引用与写入口表迁到新类型。
- Checks: `dotnet build Server/86JP.Server.slnx` 通过；`Dfo.Server.Application.Tests`（Sessions/Execution/Commands/Architecture/Registration 过滤，963 项）全通过；`Dfo.Server.Host.Tests` 架构类 19 项通过；`Dfo.Server.Networking.Tests` `SelectionLifecycleWholeCommandTests` 2 项通过。
