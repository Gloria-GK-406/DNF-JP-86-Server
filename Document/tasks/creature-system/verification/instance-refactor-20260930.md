# 宠物实例模式重构 — 2026-09-30

用户授权：认可实例模式方案后要求完成重构。行为基线为已验收的P01–P10；孵化基线提交 `0ae85bbb`，本次为其后的结构重构。

## 调用方式

以下代码运行在CommandBus开启的同一用例事务中；外层命令负责提交、回滚及喂食/改名资源消费。实例不能跨事务缓存，下一用例重新查找；集合可以作为角色身份入口重复使用。

```csharp
var pets = creatures.Of(characterId);
var pet = await pets.FindPetAsync(petUid, ct);
if (pet is not null)
{
    await pet.AddStomachAsync(30, ct);
    await pet.AddExpAsync(20, ct);
    await pet.RenameAsync(nameBytes, ct);
    var latest = await pet.ReadAsync(ct);
}
var egg = await pets.FindEggAsync(eggUid, ct);
var born = egg is null ? null : await egg.BirthAsync(ct);
// born 是同UID的新宠物实例；同一个 egg 再次 BirthAsync 返回 null。
```

`CurrentPetAsync` 取得当前佩戴的宠物；实例按角色+UID定位身份，容器负责移动、交换和穿戴。`BirthAsync` 返回 `IPetInstance`（实际为 `PetInstance`），事务回滚后本次返回的蛋/宠物句柄全部失效，重新查询会读到原状态。

## 实现边界

- `PetInstance` 内聚饱食增量、成长和装备经验加成、名字验证、普通进化以及饱食时间推进的状态变更；`EggInstance` 内聚蛋→产物检查、出生初始化和库存core转换。
- `ICreatureRuleService` 提供PVF定义、装备加成和出生期限，实例不直接访问内容Source。
- `CreatureInstanceContext` 连接事务内最新持久化状态、库存位置和在线余额；读出的名字数组为副本，多个句柄不会拿旧快照覆盖彼此。事务完成后拒绝继续使用该实例。
- 运行服务保留在线会话、调度、战斗切换、房间奖励去重和提交后投影；实际修改经实例。实例喂食与在线小数余额在同一事务更新，出生作废旧蛋余额，下一时钟从100开始。静态 `CreatureRules` 保留为可复用的纯算术，状态转换和保存由实例负责。
- 原 `ICreatureStore.SaveAsync` 已删除；剩余快照读取供登录/库存/协议投影使用。持久化session仍是内部存储边界，选角时的条件复活保留既有流程。
- 改名卡和饲料仍由原命令链校验/扣除，实例不会自行提交事务或发送网络包。UID、数据库结构和协议布局均未改动；特殊任务/多选/物品成长等原未完成范围保留。

## 验证

| 检查 | 结果 |
|---|---|
| Release完整解决方案构建 | 0警告、0错误 |
| Application相关回归 | 83通过，0失败/跳过；包含宠物、命令架构、选角生命周期 |
| Protocol完整回归 | 1137通过，60跳过，0失败 |
| Networking相关回归 | 19通过，0失败/跳过 |
| Persistence相关回归 | 22通过，0失败/跳过 |
| 新增行为保护 | 多句柄累计修改与小数饱食；战斗切换后快照整数/小数一致且只读；读快照名字不影响实例；事务完成/回滚句柄失效；移动后蛋同UID出生、重复拒绝、返回宠物可成长改名 |
| 真实PVF+生产模块+当前数据库副本 | 退出码0；副本UID960槽3孵化到63023，同UID/槽，实际编码0066/000E/0069，重复拒绝、下一时钟100、库存重载；公共实例API两句柄修改累计正确 |

TRX位于相应测试项目的 `TestResults/creature-instance-*.trx`。副本探针及日志位于 `E:/Project/DNF/DNF-JP-86-Server/artifacts/creature-instance-20260930/`；通过SQLite在线备份建立独立副本，原生产库只读。

生产DI检查最初遇到探针自定义命令的签名和注册错误，修正为生产 `AddCommandHandler` 后重新建立副本运行通过；没有改生产命令实现来迁就探针。

## 交付状态

独立代码复核 [approved](../reviews/instance-refactor-review-20260930.md)，最终Release构建0警告/错误，`git diff --check`通过。代码与文档已完成，可在同一命令事务内使用实例API。本次未重启实机服务；当前用户使用的仍为此前验收通过的孵化部署。测试通过不等于重构后客户端已经重新实机验收，也不表示完整宠物功能完成。
