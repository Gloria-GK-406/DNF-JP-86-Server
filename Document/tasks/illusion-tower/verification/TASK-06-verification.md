---
kind: rks-verification
decision: pass
---

# TASK-06 有界验证

验证对象是TASK-06的塔/Party协调、Leave/Remove/ChangeHost/GiveUp入口、leader同步、失败收敛与R1 attached entry intent修复，不是整项迷妄之塔Spec或A21实机验收。工作目录为 `DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`，基线 `762d0cb4` 加已接受但未提交的TASK-01至TASK-05内容；执行者为 `tower_party`。所有.NET命令串行执行，并在同一PowerShell进程设置当前PVF路径。

## 实际执行命令

塔领域、真实SQLite组合、leader权限和R1入场intent：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~IllusionTowerDomainServiceTests|FullyQualifiedName~IllusionTowerRewardRuleServiceTests|FullyQualifiedName~IllusionTowerRuleServiceTests"
```

exit 0：51通过、0失败、0跳过，耗时约4秒。集合包含结算SQL失败后party/tower/经验不变及移除trigger后一次成功重试、双人成功入场消费attached intent、塔内踢人回TownArea、同步leader后的start/next权限。

Party、塔、选图、普通副本离场及死亡/击杀回归：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~IllusionTower|FullyQualifiedName~PartyCommandHandlerTests|FullyQualifiedName~PartySelectDungeonHandlerTests|FullyQualifiedName~SelectDungeonHandlerTests|FullyQualifiedName~LeaveDungeonTests|FullyQualifiedName~CharacterDeath|FullyQualifiedName~MonsterDeath"
```

exit 0：159通过、0失败、0跳过。协调测试覆盖塔提交后取消、Party写后包装器异常、门外可重入关闭、虚拟GiveUp、外部party leader离开、target已回城的显式转移与后续重新同步、无权限/空slot拒绝、第三成员并发断线后的重新捕获、发送失败时替换session不被关闭。

塔帧真实TCP集成：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Networking.Tests/Dfo.Server.Networking.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~IllusionTower"
```

exit 0：1通过、0失败、0跳过，耗时约1秒。

架构边界：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~Architecture"
```

exit 0：39通过、0失败、0跳过。

格式检查：

```powershell
git diff --check
```

exit 0，无空白错误。

## 已验证范围

| 条件 | 决定性结果 |
|---|---|
| 旧请求边界 | Leave、Remove、ChangeHost、GiveUp在任何当前run/party捕获前丢弃 `InboundOrdinal>0 && !SequenceIsFresh`；未宣称能识别任意fresh序列旧载荷 |
| 锁与重校验 | run gate先于按AccountId排序的全体账号gate；普通路径门内发现tower后门外重路由；party身份、slot、exact session和run/version变化触发重试或拒绝 |
| 权限拒绝 | 非leader踢人、空slot、虚拟party普通Leave在塔写前拒绝，塔快照与Party保持不变 |
| 事务失败 | 真实SQLite结算trigger失败时塔、party与经验保持原值；移除trigger后同一动作成功一次，dead member只结算一次 |
| 取消与第二阶段异常 | 塔提交后取消不会跳过Direct Party CMD；Party内存已写后包装器抛错会在全部gate释放后隔离exact受影响连接 |
| GiveUp与回城 | 真实party GiveUp走离塔加退队；虚拟单人GiveUp只离塔；真实双人塔内踢人后目标离塔、离party并回到TownArea |
| leader一致性 | 自动继任使用实际塔成员slot；塔内显式目标同步host；已回城target只成为party leader，塔host保持实际成员并可随后重新同步 |
| 并发与替换身份 | 第三成员在捕获期间断线会重新验证并完成；投影失败后若原成员已被替换，不关闭旧连接ID或替换连接 |
| 入场intent R1 | 成功塔party入场提交后、Refresh/发送前消费exact非requester intent；失败前不消费，requester仍由handler原路径一次消费/恢复 |
| 普通路径与结构 | 相关普通Party、选图、离场、死亡、击杀回归通过；Application Architecture 39通过 |

## 未覆盖与证据边界

- 未执行A21客户端实机，因此踢人/换队长/GiveUp后的客户端窗口、UDP host表现和回城画面仍不是ClientVerified。
- 没有在本单元重复Application全项目、Protocol全项目或解决方案全量；根流程将使用相同PVF路径执行最终Release全量和独立累计review。本单元执行了TASK-06直接相关集合、真实SQLite组合、Networking塔集成和全部Application Architecture测试。
- 未启动生产 `Dfo.Server.Host`、未启动客户端、未部署产物、未访问玩家数据库，也没有Git提交。
