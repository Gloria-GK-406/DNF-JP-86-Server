---
kind: rks-verification
decision: pass
---

# TASK-04 有界验证

验证对象是 TASK-04 的服务器协议入口、结算投影/重放、主动与定时回城、断线清理及普通入口兼容，不是整项迷妄之塔 Spec 或 A21 实机验收。工作基线为 `762d0cb4` 加已接受但尚未提交的 TASK-01/02/03 内容；执行者为 `tower_protocol`。所有 .NET 命令串行执行。

## 实际执行命令

协议项目编译：

```powershell
dotnet build Server/src/Dfo.Server.Protocol/Dfo.Server.Protocol.csproj --no-restore
```

exit 0，0警告、0错误。

使用既有当前PVF路径运行塔领域、真实SQLite资产和协议投影组合检查：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~IllusionTower"
```

exit 0：44通过、0失败、0跳过。该集合包含 option3 无回城期限且立即0048 ACK，以及首次结算发送失败后从已提交receipt重放、金币/经验/装备均未二次写入的真实SQLite用例。

协议切片、009F畸形输入、并发身份与普通选图分流：

```powershell
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore --filter "FullyQualifiedName~IllusionTower|FullyQualifiedName~SelectDungeon"
```

exit 0：43通过、0失败、0跳过。

真实帧解析、注册与派发路径：

```powershell
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore --filter "FullyQualifiedName~IllusionTowerStageFrameIntegrationTests"
```

exit 0：1通过、0失败、0跳过。测试在临时端口使用真实 `HostBuilder`、隔离SQLite、生产帧解析器、已注册decoder/dispatcher/handler、真实塔领域与命令管线；raw 009F mode1 帧使挑战进入 Fighting，断开连接后等待塔清理完成。它没有启动生产 `Dfo.Server.Host`，没有使用用户配置、部署目录或玩家数据库。

命令层依赖边界：

```powershell
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore --filter "FullyQualifiedName~CommandHandlerArchitectureTests"
```

exit 0：13通过、0失败、0跳过。

Protocol 项目完整回归：

```powershell
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore
```

exit 0：1192通过、0失败、59跳过。该次独立命令未重新设置进程级 `PVF_ARCHIVE_PATH`，需要PVF的既有用例被跳过；不将跳过计作通过。最终解决方案验证将显式配置当前PVF覆盖这些用例。

工作树格式检查：

```powershell
git diff --check
```

exit 0。

## 已验证范围

| 条件 | 决定性结果 |
|---|---|
| 008E/008F/0090/0091/0092 编码 | 008E固定8字节；008F演员字段顺序与空地图物品；0090完整305字节基线；0091四个稀疏队伍槽、空项与展示值均有字节向量；0092单资格字节完成注册并在receipt投影中出现 |
| 009F 输入 | 仅接受mode1的1字节形态与mode2的17字节形态；其他长度/模式拒绝；raw TCP帧经过生产解析、注册和handler进入真实挑战 |
| 入口分流 | 11001由塔领域能力判定，在普通布局前分流；普通SelectDungeon回归通过；过期入站sequence不会消费既有选图意图 |
| 生命周期 | 全员加载才Ready，009F mode1才启动；清场前不换层，mode2推进下一层；最后层结算后不再换层 |
| 投影与一次资产事实 | 即时击杀award与最终settlement receipt均被表达；`SettlementCreated`限制首次主动结算投影；显式receipt重放不再写金币、经验或装备 |
| 0048与回城 | 成功option选择立即回0048 ACK；option3期限为null；期限sweep捕获run/version，旧回调不结束新run；单个due run或接收者失败不阻断后续run |
| 并发与失败隔离 | run门后按AccountId排序获取账号门并重验exact身份；投影按成员捕获失败；连接关闭在门释放后执行，避免cleanup重入死锁 |
| 断线与离塔复用 | cleanup提交的离塔结果进入队列；公共已提交离塔入口不再执行Leave，只投影仍匹配的剩余成员，并只回城当前且未进入其他塔的离塔session |
| 项目结构与DI | 六条协议各自拥有message/codec目录；共享commands/delivery位于Game endpoint根；生产DI可解析塔handler、delivery、timer与真实奖励依赖；Application不引用Infrastructure的架构检查通过 |

## 重放与公共接口约束

`ReprojectCompletionAsync` 是传输恢复入口，不是领域重试入口。它要求当前session、run和room version仍与调用上下文一致，并要求已有 `Completion.Settled=true` 与非空receipt；只从receipt组装经验、任务、库存、金币、0090/0091/0092帧。首次失败与后续重放之间不会再次调用 `Kill`、`Die`、`MarkSettled` 或奖励服务。

`ProjectCommittedDepartureAsync` 接收已经提交的 `TowerActionResult`。它不调用 `Leave`，统一使用 run独占门→按账号排序的transition门；在锁内重新筛选相同run/version的剩余成员，按需投影Ready或本次新结算。可选回城成员必须仍是当前exact session且此时不属于任何塔。发送或城镇投影失败被记录，相关连接只在全部门释放后关闭。`LeaveAndReturnAsync` 与断线投影均复用该入口，后续TASK-05的party离塔可沿用同一契约。

## 未覆盖与证据边界

- 未执行 A21 客户端实机，因此 008E–0092 的最终UI、0090/0091相对时序、0048四个选项与默认60秒返回体验均不是 `ClientVerified`；这些项目保留在 human test list。
- 0092当前由直接单字节encoder、注册检查与receipt投影类型检查覆盖，没有单独的编码后帧字节向量用例；不能把这一覆盖描述为A21 wire实测。
- codec依据 ClientStatic、70Reference与当前补证；服务器向量和fixture通过不能证明原服所有动态字段或随机序列。
- 真实009F集成测试启动的是测试内隔离Host，不是生产 `Dfo.Server.Host`。本单元没有启动生产Host、没有部署产物、没有启动客户端、没有修改玩家数据库，也没有Git提交。
