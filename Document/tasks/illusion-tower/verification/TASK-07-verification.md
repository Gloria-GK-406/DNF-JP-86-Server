---
kind: rks-verification
decision: pass
---

# TASK-07 有界验证

验证对象是TASK-07的真实公共入口15层串联、0084返回/0010重入、旧到期sweep竞态及0092注册encoder向量，不是整项迷妄之塔Spec或A21实机验收。工作目录为 `DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`，基线 `762d0cb4` 加已接受但未提交的TASK-01至TASK-06、R2与R3内容；执行者为 `tower_integration`。所有.NET命令串行执行，并在每个PowerShell进程显式设置当前PVF路径。

## 实际执行命令

新增公共入口闭环及旧sweep竞态：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter "FullyQualifiedName~IllusionTowerPublicFlowTests" --no-restore --nologo
```

exit 0：2通过、0失败、0跳过，耗时约3秒。

0092注册encoder向量所在codec集合：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter "FullyQualifiedName~IllusionTowerCodecTests" --no-restore --nologo
```

exit 0：6通过、0失败、0跳过，耗时约0.1秒。

Application塔相关与CMD架构回归：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --filter "FullyQualifiedName~IllusionTower|FullyQualifiedName~CommandHandlerArchitectureTests" --no-restore --nologo
```

exit 0：70通过、0失败、0跳过，耗时约6秒。

Protocol塔、0010、0084与0025相关回归：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --filter "FullyQualifiedName~IllusionTower|FullyQualifiedName~SelectDungeonHandlerTests|FullyQualifiedName~LeaveDungeonTests|FullyQualifiedName~FinishLoadingCodecTests" --no-restore --nologo
```

exit 0：71通过、0失败、0跳过，耗时约0.1秒。

格式检查：

```powershell
git diff --check
```

exit 0，无已跟踪差异的空白错误；TASK-07新增文件另由编译和测试覆盖格式/语法。

## 已验证范围

| 条件 | 决定性结果 |
|---|---|
| 公共0010入场 | 真实 `SelectDungeonHandler` 消费interactive intent，扣4183一次，撤销TownArea并按序发送008E/008F；不直接调用塔领域入场绕过协议入口 |
| 真实PVF 15层 | 生产 `PvfIllusionTowerContentSource` 解析当前档案；实际访问15个预期map且每层演员非空，没有使用测试 `Rules` 的单actor房间 |
| 公共加载/开始/死亡/推进 | 0025、009F mode 1、逐演员0027与交替009F mode 2/002D驱动；第一层战斗未清场时推进保持同一level/version |
| 持久结算 | 真实Transactional CMD、奖励服务、物品放置与隔离SQLite执行；最终receipt为settled，SQLite总经验包含逐怪与最终结算分量，金币和receipt一致，实际奖励物品数与持久库存一致 |
| 0090/91/92 | 最终消息身份和顺序严格为Ranking、Reward、Continue；0092从真实注册encoder输出body `00`/`01` |
| 0084与重入 | 公共0084解除塔归属并恢复TownArea；第二张券与新intent经公共0010建立不同RunId，第二张券只扣一次并再次发送008E/008F |
| 旧sweep竞态 | `FindReturnsDue` 捕获旧context后、后续exact检查前阻塞；0084/0010完成替换后释放，旧context因RunId/RoomVersion不匹配不改变新run、不发消息、不重复0090/91/92、不写经验/金币 |
| 相关回归 | Application塔/CMD架构70通过，Protocol塔及0010/0084/0025相关71通过，全部零跳过 |

## 真实实现与测试接缝

- 真实：PVF档案及15层地图/演员、`PvfItemPlacementDefinitionSource`、`CharacterSkillPointDomainService`、选角当前视图/准备、塔领域、命令总线与事务管线、奖励服务、物品/经验/金币写入、SQLite、所有公共handler和协议注册encoder。
- 测试接缝：`ShopFixture` 提供每例独立SQLite并为同账号两个fixture角色补齐选角显示所需附表；本流程的 `ShopSender.SendExplicitAsync` 记录投影，不把这条handler串联称为全程TCP/编码测试。编码与注册另由codec向量及Networking塔帧集成覆盖。`NoopConnectionCloser` 只满足组合根中关闭器依赖，成功路径没有要求关闭连接。
- 竞态接缝：`SweepFindGate` 委托 `IIllusionTowerDomainService` 的每一个业务方法，只在 `FindReturnsDue` 返回非空后阻塞同一sweep线程的下一次 `Find`，因此控制的是确定的检查时刻，不替换塔状态、规则、事务或奖励行为。

## 未覆盖与证据边界

- 未执行A21客户端实机，因此15层画面、结果窗口、0092按钮、0084回城与重入仍不是ClientVerified。
- 没有在本单元重复完整solution或全项目Release验证；根流程将执行最终新鲜全量与独立累计review。
- 公共0010生产实现用 `Random.Shared` 生成run seed；验证不固定奖励候选ID，而断言每次实际receipt与SQLite持久结果一致。
- 未启动生产 `Dfo.Server.Host`、未启动客户端、未部署产物、未访问玩家数据库，也没有Git提交。
