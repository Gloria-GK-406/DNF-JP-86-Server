# Remediation 2：验证阶段发现的源码规则冲突

## 发现

验证阶段在 `10bc4fd5e` 上全量运行 `dotnet test`，`Dfo.Server.Application.Tests.Execution.LockRemovalSourceTests.No_other_waiting_primitive_is_used_in_the_production_sources` 失败，报错为 `Dfo.Server.GmWeb/GmAccessBoundary.cs:94 uses Lock.TryEnter(...)`。日志见 `verification/final/dotnet-test.log`。

这条源码规则用正则 `\.TryEnter\(` 识别 `System.Threading.Lock` 的显式进入。remediation 1 新增的 `PlayerRequestLimiter.TryEnter(int)` 只是业务计数器的方法名，内部用的是允许的 `lock` 语句，并非锁原语，但在文本层面撞上了这条规则。

## 修正

- 把 `PlayerRequestLimiter` 的方法 `TryEnter` / `Exit` 改名为 `TryAcquire` / `Release`。
- 同步修改调用点 `GmAccessBoundary.cs` 和 `PlayerPortalRequestLimitHttpTests.cs`。
- 行为不变，没有修改规则测试本身。

## 检查

| 检查 | 结果 |
|---|---|
| `dotnet build Server/86JP.Server.slnx` | 0 警告 0 错误 |
| `LockRemovalSourceTests` | 18/18 通过 |
| Host.Tests `~PlayerPortal` | 49/49 通过 |
| 行尾 | 三个文件均为 `i/lf w/lf` |
| 测试服产物 | 重新 Release publish 到 `artifacts/player-portal-329/host`；`wwwroot/gm` 的入口文件与 `Web/gm-admin/dist` 一致 |
