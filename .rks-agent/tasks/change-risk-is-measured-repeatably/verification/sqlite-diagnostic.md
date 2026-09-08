# 采集中观察到的 SQLite 失败

2026-09-08，运行 `.crap/2026-09-08_21-14-59` 真实结果为 2391 通过、2 跳过、1 失败，工具退出码1；Run.Status=partial、AllTestsPassed=false。完整采集输出保存在 fresh-run-2.log，原始调用栈见该运行 logs/test.log 第819行起，原始TRX也保留。该次成果不能作为全绿基线。

失败用例：`Dfo.Server.Protocol.Tests.Game.ItemPathFrameTests.ARefusedMoveOfARealRowAcknowledgesTheSameBytesAsBefore`。

异常为 `ObjectDisposedException: Cannot access a disposed object. Object name: SQLitePCL.sqlite3`；实际应用栈根是 `SqliteDatabaseParticipant.CommandAsync:87 → SqliteConnection.BeginTransaction → SqliteTransaction → ExecuteNonQuery`，上层为 `SqliteItemDomainStoreSession.ReadAsync → StoreBackedItemContainer.ReadSlotsAsync → MoveToAsync → MoveItemSpaceCommandHandler.HandleAsync`。这不属于 AGENTS 列出的三个已知栈根，不按既有豁免直接放行。

单例真实复跑命令（PVF_ARCHIVE_PATH 与采集相同）：

```powershell
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-build --nologo --filter 'FullyQualifiedName~ARefusedMoveOfARealRowAcknowledgesTheSameBytesAsBefore' --logger 'console;verbosity=detailed'
```

结果1/1通过、退出码0，见 sqlite-isolated-rerun.log。隔离通过不能证明根因，也不会替换失败采集。

独立只读调查者 Schrodinger（agent 01a08111-61fa-79b1-8d86-c666d46838b7）对照 `git show addbeb52` 与失败运行快照，确认相关生产代码、用例、隔离配置未改，SQLite包版本未改。用例自身顺序等待读取/提交，最后才释放 boundary/storage，未找到自身提前释放连接路径。

**Hypothesis：既有测试隔离竞争。** Protocol 测试没有禁用类间并行；多个同程序集fixture（包括任务接受/完成测试与 ItemPathFrameTests.cs:219）清理时调用进程级 `ClearAllPools()`。GUID数据库文件名不能隔离全局清池。上述安排在基线已经存在，但日志没有清池线程与句柄身份，无法证明竞争交错；也不能排除覆盖率插桩改变时序。

结论：未发现本任务引入的相关代码回归，根因仍未证实。保留失败证据，另取新的完整采集与不启用覆盖率的服务端回归结果。连接池隔离修复不在本任务边界内，不修改生产代码或既有测试。

## 故障注入运行中的第二处观察

`.crap/2026-09-08_21-25-32` 在测试结束后成功触发指定的 `after-tests` 异常，工具退出70、状态failed。此前真实测试仍为2391通过/2跳过/1失败；失败为 `ContainerInventoryProjectionTests.A_container_supplied_snapshot_encodes_to_the_bytes_the_loader_produces`，栈根是该测试 `SqliteStorage.CreateAsync:116 → ExecuteNonQueryAsync → sqlite3_changes`，也不归入旧三个根。完整日志见 failure-injection.log 及原运行 logs/test.log 第703行起。该位置是在向临时数据库插入fixture数据时失败；相关既有测试源码没有改动。仍不声称已定位释放者。

随后不启用覆盖率的完整 `dotnet test Server/86JP.Server.slnx --nologo --logger 'console;verbosity=detailed'` 退出0，2392通过/2跳过/0失败；server-regression.log 第740、741行明确记录上述两个用例通过。另一次完整覆盖采集 `.crap/2026-09-08_21-21-45` 也为2392/2/0。

失败注入验收只证明指定阶段的异常会被完整留证，不将该运行当作健康基线。verify_history.py逐份读取原始TRX，核对真实计数、失败标志和退出码，并核对清单的文件存在状态；不会要求失败运行伪装为全绿。
