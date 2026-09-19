---
kind: rks-verification
decision: manual_required
---

# 系统分解非零上下文修复验证

## 验证声明

服务端不再把 A21 `CMD 0x001A` 末尾的客户端/UI 上下文值当作系统分解资格，
非零上下文可进入与零上下文相同的原子分解事务；Release 服务已部署，等待实机确认客户端 UI。

## 已验证条件

- RED：`dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~EquipmentDisjointTests.Opaque_client_context_does_not_reject_system_disjoint"` 在旧逻辑下失败，源装备仍保留且耐久为 45。
- GREEN：同一命令在修复后通过，源装备删除、材料入包并产生成功响应。
- 分解应用层回归：同项目筛选 `FullyQualifiedName~EquipmentDisjoint`，8/8 通过。
- 协议回归：`Dfo.Server.Protocol.Tests` 筛选 `FullyQualifiedName~DisjointItem`，5/5 通过。
- 真实 PVF 回归：设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后运行 `PvfEquipmentDisjointDefinitionSourceTests`，1/1 通过。
- Release 构建：`dotnet build Server/86JP.Server.slnx -c Release --no-restore`，0 警告、0 错误。
- 部署运行态：Host PID 10860；TCP 7001、10010、10011、18080 与 UDP 12311、12312 均监听；HTTP 18080 返回预期 404，证明 Launcher HTTP 宿主可达。

## Unverified Conditions

- 需要用 A21 客户端重新执行一次普通分解和一次批量分解，确认成功动画、背包删除与材料展示均正常。
