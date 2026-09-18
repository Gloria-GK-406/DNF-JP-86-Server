---
kind: rks-verification
decision: pass
---

# 完成前验证

验证对象：`task/remove-session-login-snapshot` 工作区中经独立审查的结果，相对基线 `1ca4d30e4e1ce12ff865703a6e9670342f6d9800`。`reviews/change-review.md` 的最终结论为 `approved`；其发现的区域名册 N² 重建问题已修正并复核。

| 条件 | 当前证据与裁决 |
| --- | --- |
| SC-1 | `ISelectedSessionRuntimeDirectory` 接口和具体目录只存精确会话与 `ShouldPersistPosition`；新增当前视图不写入目录。目录/代际测试通过。满足。 |
| SC-2 | 耐久测试在同一会话修改持久化物品后重新读取当前视图，确认新耐久且未重做登录恢复；副本选择、进入、回城、队伍与角色查看消费者的数据流已迁移，相关协议/应用测试通过。自动验证满足；客户端实际显示尚未实机验收。 |
| SC-3 | 任务、等级、技能、宠物和其他迁移路径经完整消费者审计及全方案测试；不再使用 `ReplaceExact` 修补登录结果。满足自动验证范围。 |
| SC-4 | 当前视图在异步读取前后校验 `FindByConnection` 和精确发布状态；旧连接退出/替换及延迟读取回归测试通过。满足。 |
| SC-5 | 修正后执行 `dotnet build Server/86JP.Server.slnx --no-restore -v:q`：成功、0 警告、0 错误。设置 `PVF_ARCHIVE_PATH=E:\Project\DNF\DNFClient-A21-protocol\Script.pvf` 后执行 `dotnet test Server/86JP.Server.slnx --no-restore -m:1 -v:q`：3421 通过、2 跳过、0 失败。`git diff --check` 无错误，架构文档已更新。满足。 |

定向红绿验证：先增加区域名册两人刷新只加载两次当前视图的断言，旧实现失败；按精确会话做本次刷新内复用后通过。独立审查修正后的完整代码和文档，结论为通过。

验证边界：本次没有修改或启动主干服务器，也没有对 A21 客户端做实机测试。上述 `pass` 仅对应任务契约要求的代码与自动化证据，不能解释为游戏画面已经验收。
