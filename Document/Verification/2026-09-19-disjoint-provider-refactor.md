---
kind: rks-verification
decision: pass
---

# 分解供应商字段持久化与边界重构验证

## 验证声明

rev 3 客户端调查及其静态工件已经进入 Git 管理范围；`0x001A` offset 5 以
`DisjointProviderId` 保留在协议 DTO 和诊断日志中，不再穿透 Command 或分解领域服务。
offset 3 以 `DisjointToolSlotIndex` 解码，并在 Command 边界前转换为
`BusinessSlot? DisjointToolSlot`。本次不宣称便携模式取值已被实机确认。

## 证据

- `dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DisjointItem"`：5/5 通过。
- `dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EquipmentDisjoint|FullyQualifiedName~InventorySlotBoundaryArchitectureTests"`：17/17 通过。
- `dotnet build Server/86JP.Server.slnx -c Release --no-restore`：成功，0 警告、0 错误。
- `Tool/PvfProbe` 重新读取 A21 `Script.pvf`：NPC 1000=德利拉、NPC 20064=勇者装备分解机；便携分解机 item id 为 2600468、2600469、2600470、2600548。
- `_disjoint_workbench.py` 通过内存编译语法检查，并已移除对已清理 worktree 的固定路径依赖。

## 保留风险

- 便携分解模式下 `DisjointProviderId` 是 `0` 还是便携工具 item id，仍需后续样本反证；当前实现不据此授权、拒绝或计算产物。
- `0x0455 CHRONICLE_DISJOINT` 与玩家分解师 `0x00EB` 只完成调查记录，不在本次实现范围。
