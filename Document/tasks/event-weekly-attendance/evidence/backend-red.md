# 后端行为 RED 执行记录

接受基线：`af0715492d9b9850756afdd908f8a93614795ea6`；同工作区，在活动 HTTP 路由实现之前加入 `GmEventsHttpTests.Registered_events_are_available_to_authorized_administrator`。

保护结果：All 管理员登录后可以读取已注册活动。路由遗漏或未绑定 GM 权限 metadata 会使用户无法管理活动；现有测试没有活动端点覆盖。测试观察 HTTP 公开行为，允许内部服务与路由结构重构。奖励/去重/回滚测试随后实现，属于回归证据，不宣称全部遵循 RED/GREEN。

实际命令（单节点与处理器限制）：

```powershell
$env:DOTNET_PROCESSOR_COUNT='2'
dotnet test Server/tests/Dfo.Server.Host.Tests/Dfo.Server.Host.Tests.csproj --filter FullyQualifiedName~GmEventsHttpTests --no-restore -m:1 -nr:false -v minimal
```

实际退出码：1。决定性输出（从本会话 `write_stdin` 对 session 5686 返回的 chunk `05af3c` 复核摘录）：

```text
Registered_events_are_available_to_authorized_administrator [FAIL]
Assert.Equal() Failure: Values differ
Expected: OK
Actual:   Forbidden
失败: 1，通过: 0，已跳过: 0，总计: 1
```

结果原因：基线没有 `/api/gm/v1/events` 路由，该路径没有 GM 许可 metadata，被既有访问边界拒绝（403）。这是缺失公开行为，不是故意制造的内部接口编译失败。完整初次重定向日志 `backend-red.log` 为0B，没有保存原始输出；上述内容是对本会话真实返回值的摘录记录，**不是事后重跑或构造的红测日志**。最初默认 MSBuild 并行尝试没有得到有效行为结果，不计作 RED。

GREEN：相同测试随新增集成测试通过；新鲜结果见 `backend-green.log`（7/7）和最终 `backend-host-regression-pvf.log`。均使用隔离 Host、数据库和端口，没有连接真实玩家数据。
