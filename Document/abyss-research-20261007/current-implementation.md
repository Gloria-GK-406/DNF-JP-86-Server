# 当前 JP86 深渊接入面

研究日期：2026-10-07。基线 `81f4373f8e5d686257420080b3f1b0c7cc7fc83c`，分支 `task/abyss-research`。
证据种类：**CurrentImplementation**；这些结论说明代码的现状，不证明客户端协议字段的正确业务含义。

## 入场链路

`SelectDungeonDecoder → SelectDungeonHandler → DungeonEntryProjectionDelivery → IDungeonVisitDomainService → DungeonLayoutRuleService / DungeonCellFactory → DungeonRunDomainService`。

| 接入点 | 现状与证据 | 对深渊的影响 |
|---|---|---|
| 请求解码 | `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntrySlice.cs:391–409`：最低长度15B，读取u32地下城、offset4普通难度；offset5、6保存为`HellPartyRequested/HellPartyDifficulty` | 这两个名称目前不能代替客户端语义证据；offset7以后的字段未进入请求模型 |
| 请求后续消费 | 同文件`:502–506`构建的`DungeonEntryFacts`只有普通难度、任务与选图intent；全库检索这两个属性仅命中定义和测试 | 设置请求字节不会自动建立深渊状态 |
| 入场facts与用途 | `Server/src/Dfo.Server.Abstractions/World/IDungeonLayoutRuleService.cs:26–65`：用途只有普通/任务关联；facts不含主动/随机深渊或深渊难度 | 普通迷宫选择不能承担深渊的完整准入/布局决策 |
| 内容模型 | `Server/src/Dfo.Server.Abstractions/Content/DungeonDefinitionContent.cs:101–110`，`Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonDefinitionSource.cs:34–45`：未声明深渊门票、深渊区域/房间、演员组等 | 不是缺一条handler即可接通，需要专门内容输入和规则 |
| 入场事务 | `Server/src/Dfo.Server.Application/World/TransactionalDungeonVisitDomainService.cs:12–29`，`DungeonVisitDomainService.cs:62–116`：全队疲劳检查、首房准备、扣点与run发布共用事务边界 | 可作为未来门票扣除与run准备的接入基础；研究阶段未修改它 |
| 全队共享run | `Server/src/Dfo.Server.Application/World/DungeonRunDomainService.cs:63–122`：冻结成员与唯一leader，同一run聚合绑定所有成员 | 随机深渊应绑定同一个run结果，不应每名玩家各抽一次 |

## UI与地图投影

- 选图页 `Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs:467–484`：深渊任务被阻止成员槽数量固定0；后续成员信息固定普通值。
- 入场组包 `Server/src/Dfo.Server.Protocol/Game/DungeonEntry/DungeonEntryProjectionDelivery.cs:216–233`：支援列表、地下城布局、首房、疲劳投影，没有深渊演员清单。
- `DungeonEntrySlice.cs:164–180`：NOTI001C的相关标志固定0。
- 同文件`:220–230,303–304`：NOTI001D的固定模式字节2、附加字节0、深渊迷雾0。**这些固定字节当前适用于已工作的普通场景；深渊值必须按客户端消费证据重新确定。**
- `ProtocolModule.cs`未注册`HELLPARTY_VALUE/HELL_PARTY_MONSTER_INFO/HELL_PARTY_CLEAR`等专属链路（按类型名检索后需与客户端实际身份对账，不能仅按旧opcode注册）。

## 房间、演员与掉落

`Server/src/Dfo.Server.Application/World/DungeonCellFactory.cs:18–92`先读取普通房间声明，再为演员生成地面/个人掉落，冻结到`DungeonCellActorState.PendingLoot`。APC使用独立掉落来源。这是现有房间构造方式，不能直接推定深渊的晚刷演员或事件奖励已支持。

`Server/src/Dfo.Server.Application/World/MonsterDropDomainService.cs:28–44`组合普通、声明、独立、世界和特殊来源；APC分支在`:33–34`直接返回独立掉落。`MonsterDropDomainService.Sources.cs:40–41`的`GenerateSpecialMode`明确返回空列表。

普通死亡提交在`DungeonRunDomainService.cs:675–759`依赖actor身份、房间及claim，并发布预生成掉落；Boss死亡也有自动清理其他普通演员的处理。未来深渊演员如果加入同一房间，必须明确是否参加普通清房条件、Boss清理、击杀经验、任务判定，以及谁是深渊奖励唯一结算触发点。

物件规则：`Server/src/Dfo.Server.Infrastructure/Content/PvfDungeonRoomDropSource.cs:140–145,158–166`识别`[hellparty]`变长块，但只跳过，不执行深渊生成。普通可破坏物事务入口`DungeonRunDomainService.Objects.cs:9–74`处理一次性物件奖励预留/消费，不会因此变成深渊柱状态机。

`PvfDropRateTableSource`针对普通`[drop prob]`形状，深渊表是`[dungeon difficulty drop prob]`，不可直接换路径复用普通解析器。普通Drop规则的分母/循环/补偿也不能无证据复制到深渊。

## 实现前需要厘清的能力

1. 选图条件查询与真正入场变更分开；查询不扣门票、不抽新的随机深渊、不刷奖励。
2. 同一run冻结主动/随机来源、深渊强度、演员组、柱状态和结算身份；这些维度与普通地图难度分离。
3. 深渊规则服务独占PVF读取入口，领域服务获取玩家/队伍/任务/库存事实并编排准入。
4. 门票扣除、疲劳与run发布采用现有事务能力；重复入场消息不重复扣除。
5. 深渊房间首次进入、柱触发、演员死亡与最终清理各有明确的状态变更入口；重访读取冻结结果。
6. 深渊奖励按确认后的生命周期和合资格组/批次结算一次，不能把整个组奖励按每个APC重复生成，也不能把多组事件简化成只发一组。
7. 协议投影只消费已提交领域结果，普通掉落/拾取系统继续持有物品身份与归属。

以上是**待讨论的接入方向**，不是已经确定的接口设计。待70、上游和客户端三路证据合并后再决定实现范围。
