---
kind: rks-verification
decision: pass
---

# TASK-05 有界验证

验证对象是TASK-05的塔内任务材料、公共能力守卫、物品使用场所和宠物/转职外观受众，不是整项迷妄之塔Spec或A21实机验收。工作目录为 `DNF-JP-86-Server/.worktrees/warehouse-storage`，分支 `task/warehouse-storage`，基线 `762d0cb4` 加已接受但未提交的TASK-01至TASK-04内容；执行者为 `tower_public`。所有.NET命令串行执行，每条涉及内容源的命令均在同一PowerShell进程设置当前PVF路径。

## 实际执行命令

任务材料、物品使用和公共守卫的初始聚焦检查：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --filter "FullyQualifiedName~CharacterItemUseTests.A_tower_member_uses_the_existing_dungeon_item_rules|FullyQualifiedName~WeaknessGoldRecoveryTests.Tower_member_cannot_buy_weakness_recovery|FullyQualifiedName~IllusionTowerDomainServiceTests&FullyQualifiedName~Quest" --nologo
```

exit 0：6通过、0失败、0跳过。

修正为既有GrantAsync与SharedMaterial语义后的任务材料聚焦检查：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --filter "FullyQualifiedName~IllusionTowerDomainServiceTests&FullyQualifiedName~Quest" --nologo
```

exit 0：5通过、0失败、0跳过。该集合包含真实PVF任务950、Quest+QuickSlot均满、SQL回滚/重试，以及账户共享材料3033的持有量与GrantLimit用例。

宠物和任务转职外观受众：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --filter "FullyQualifiedName~CreatureProjectionDeliveryTests|FullyQualifiedName~FinishQuestHandlerTests" --nologo
```

exit 0：24通过、0失败、0跳过。包含城镇与普通副本既有受众回归、塔内当前成员、旧连接排除、发起者替换，以及发送owner后peer连接被替换时不向新连接重绑定的竞态。

塔协议相关回归：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Protocol.Tests/Dfo.Server.Protocol.Tests.csproj -c Release --filter "FullyQualifiedName~IllusionTower" --nologo
```

exit 0：8通过、0失败、0跳过。

Application全项目回归：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --nologo
```

exit 0：2880通过、0失败、0跳过，耗时1分16秒。

命令层依赖边界：

```powershell
$env:PVF_ARCHIVE_PATH='E:/Project/DNF/DNFClient-A21-protocol/Script.pvf'
dotnet test Server/tests/Dfo.Server.Application.Tests/Dfo.Server.Application.Tests.csproj -c Release --filter "FullyQualifiedName~CommandHandlerArchitectureTests" --nologo
```

exit 0：13通过、0失败、0跳过。

临时接口与格式检查：

```powershell
rg -n "GrantIntoAsync|AddExactAsync|ForGrantExact" Server/src Server/tests
git diff --check
```

`rg`无匹配（其标准退出码为1），确认950专用尝试没有遗留接口、实现或测试调用；`git diff --check` exit 0。

## 已验证范围

| 条件 | 决定性结果 |
|---|---|
| 真实任务950 | 当前PVF规则、30%概率和种子42产生1至3个10089420；物品按真实kind=4进入Quest业务页；任务触发值按已提交持有量更新 |
| 发放上限与空间 | 无active quest不发；held=9只到10；held=10不发；Quest与QuickSlot均满时保留无掉落；共享材料3033从账户容器held=9只到10，receipt保留SharedMaterial槽 |
| 事务与幂等 | 任务物、经验、actor死亡和任务进度处于同一事务；SQL提交失败全部回滚，移除故障后可重试；重复同actor UID不二次发放 |
| 普通结果稳定 | `QuestEnemyDropRequest`只把随机盐从MazeCell改为显式x/y；普通Monster和PassiveObject适配器继续传原位置；塔传floor/0，不引入普通MazeCell |
| 物品使用 | 塔成员得到Dungeon场所事实，合法地下城物品沿用原规则；等级、职业、冷却等判断未绕过 |
| 城镇限定能力 | 塔内拒绝仓库转移、仓库扩容/金币查询、恢复疲劳及仓库整理；携带背包整理仍走既有路径 |
| 宠物受众 | 同run当前exact session可见；城镇旁观者和旧连接不可见；owner发送后peer换线时不会把旧成员通知重绑定给新连接 |
| 转职外观受众 | 同run当前exact session进入显式target；城镇旁观者和旧连接排除；发起者在重载后换线时不再投递同伴外观 |
| 结构与临时API | Application全项目及CommandHandlerArchitecture通过；临时GrantInto/AddExact/ForGrantExact接口已完全撤销 |

## 未覆盖与证据边界

- 未执行A21客户端实机，因此任务材料背包显示、宠物演化/死亡/复活和转职外观的实际客户端表现不是ClientVerified。
- 没有在本单元重复整套Protocol或解决方案全量；主流程将使用相同PVF路径执行最终Release全量。这里执行了TASK05直接相关Protocol集合和Application全项目。
- 未启动生产 `Dfo.Server.Host`、未启动客户端、未部署产物、未访问玩家数据库，也没有Git提交。
