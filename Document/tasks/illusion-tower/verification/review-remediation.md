# 完整结果评审修正验证

## R2：协议覆盖检查器的同名常量

检查器按声明类型保存常量，并拒绝歧义短名、错误限定名及动态实例属性回退。回归 fixture 同时注册两个使用同名常量的 encoder，交换输入文件顺序后仍分别映射角色经验 `0025`、宠物经验 `0066`；另覆盖 record 常量与 `message.WireType` 不得误绑定的情况。

- 新反例在修复前：`python protocol/patterns/data/check-server-documentation-sync.py --self-test`，exit 1，`same-name constants in distinct types changed encoder identities`。
- 修复后同一命令：exit 0，failures 0。
- `python protocol/patterns/data/check-server-documentation-sync.py --repo-root . --write-catalog`：exit 0；再执行无写入参数的检查同样 exit 0。当前123个C2S、178个S2C身份（81 CMD、97 NOTI），30行有来源的动态映射。

停止错误回退后，仓库扩容的动态 `WireType` 显式暴露为不可静态求值；依照 `GameInventorySnapshotDelivery.cs` 的两个分支登记 `0198/0132`，不再错借物品增量常量。生成清单与顶部历史覆盖表已修正角色/宠物经验的错误关联。此验证证明源码覆盖映射，不证明协议语义或实机表现。

## R3：生产奖励通知的有界重投

`ProjectActionAsync` 现在对每个接收者的已构造批次最多发送两次。首发异常后在原run/account门内重新验证exact session、RunId、RoomVersion及结算receipt，再发送同一批次；不重跑CMD或重新构造奖励。第二次失败沿原路径在门外关闭连接并清理。非末层击杀的经验/任务物与末层结算均使用此入口。此行为不提供进程重启或已断连接后的持久化重投队列。

新增 `IllusionTowerRewardProjectionTests` 使用真实DI、塔领域、命令与隔离SQLite，仅内容/传输使用有界替身。修复前4项中3项因只有一次发送而失败；替换session拒绝发送的既有边界通过。修复后4项全过：

- 非末层击杀、最终击杀的首败次成：两次发送持有同一个batch对象；首发时已提交的经验/金币/装备与次发后相同，不关闭连接。
- 持续失败：恰好两次后关闭，真实 `ISelectedSessionCleanupDomainService` 清除旧run、receipt与selected session，已提交资产不变。
- 首发期间换session：无旧批次重发、无误关；真实旧会话cleanup后新身份保留，重新入塔后再次旧cleanup也不影响新run。

所有.NET命令串行，均先设 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-protocol/Script.pvf`，工作目录为本任务worktree：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~IllusionTowerRewardProjectionTests
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore -c Release --filter 'FullyQualifiedName~IllusionTower|FullyQualifiedName~CommandHandlerArchitectureTests'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore -c Release --filter FullyQualifiedName~IllusionTower
```

结果依次为4、68、16通过，全部exit 0、0失败、0跳过。集合有重叠，不相加为唯一测试数。
