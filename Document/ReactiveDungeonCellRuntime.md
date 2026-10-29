# 响应式地下城 Cell 运行时

## 目标

地下城中的房间状态只保留一个真源：`DungeonCellState`。`DungeonRunState` 管理一次共享副本的
入场名单、布局、当前 Cell 和已经创建的 Cell 集合，成员由所属队伍回答（见“副本成员”）；Actor、待掉落物、地面物、清理状态及其版本
全部归属于 Cell。运行期不再用 Run 级死亡集合、怪物掉落集合或已拾取集合重新拼装房间。

普通地下城与剧情地下城共用这套运行时。二者可以在布局选择、任务约束等进入规则上不同，但
进入以后都操作相同的 Run/Cell 模型。

## 职责边界

| 组件 | 职责 |
|---|---|
| `DungeonRunDomainService` | 管理 `DungeonRunId`、所属队伍键与入场名单、迷宫、当前 Cell、Cell 集合以及受版本保护的提交 |
| `DungeonCellFactory` | 仅在 Cell 首次创建时读取房间内容、物品分类和掉落规则，产出完整且尚未发布的 `DungeonCellSeed` |
| `DungeonCellAggregate` | 独占 Actor 状态、pending loot、统一 GroundItems、clear 状态和 Cell revision；防御性复制构造输入 |
| 规则/content 服务 | 只提供 Cell 构造输入；Cell 发布后不得参与死亡、拾取或重访的运行态查询 |
| CMD/事务协调 | 预占领域事实，完成经验、任务、背包或金币等持久化，再提交或释放 Cell 预占 |

### 运行服务的窄接口

`DungeonRunDomainService` 只有一个实例，对外按用途拆成窄接口，调用方只注入自己用到的那个：

| 接口 | 用途 |
|---|---|
| `IDungeonRunQueryDomainService` | 两个 `Find`，查角色或 run 身份所在的 run；其余窄接口都继承它 |
| `IDungeonRunEntryDomainService` | 组队入场、加载就绪、入场回滚与移图 |
| `IDungeonRunCombatDomainService` | Actor 死亡、击杀效果、物件破坏与动态 Cell 的预占和提交 |
| `IDungeonRunParticipantDomainService` | 参与者自身状态：死亡、复活、每局一次的增益 |
| `IDungeonRunGroundDropDomainService` | 地面物的预占、暂存与释放 |
| `IDungeonRunSettlementDomainService` | 打开 clear 与逐人结算 |
| `IDungeonRunCardDomainService` | 翻牌布局与牌位预占 |
| `IDungeonRunTimerDomainService` | 通关回城、团灭回城、遗忘之地的探索与狂热时限，以及定时房间机关 |
| `IDungeonRunDepartureDomainService` | 单人离开或整局结束 |

`IDungeonRunDomainService` 只是继承以上全部的聚合接口，生产代码只有同时跨两类用途的调用方注入它。
组合根经 `AddDungeonRunViews()` 把每个窄接口登记为聚合接口的同一实例；自建容器的测试替换聚合接口后也要调用它。
接口成员都没有默认实现：新增成员时，实现类和每个替身都要明确作答。Protocol 测试的手写替身继承
`DungeonRunServiceStub`，只重写自己用到的成员；只用 `Find` 的替身直接实现查询接口。
实现按同样的用途拆成 `DungeonRunDomainService.*.cs` 分部文件。
| 协议投影 | 只消费 Cell snapshot 或同一次提交返回的 change；不访问 PVF 来补运行态事实 |

Session、Connection 和发送器不进入 Run/Cell 领域模型。它们只在协议投影层决定把已经提交的
change 发送给哪些当前连接。

## 副本成员

副本挂在一支队伍上，而不是挂在人身上；单人副本挂在该角色的虚拟队伍上。入场时由入场队长解析出
`PartyKey`，副本生命周期内不变。

- **谁在本局**只由队伍回答：副本成员是队伍当前成员中属于入场名单的角色，槽位与队长取队伍当前值。
  副本不持有可变成员集合，也不维护按角色到副本的索引。入场名单不可变，只提供账号和排除图外角色；
  每名入场者在本局的进度、准备、死亡、翻牌等状态仍按角色保存。
- **由角色找副本**：先用 `IPartyService.KeyOf` 得到角色所属队伍（不在真实队伍时为其虚拟队伍），
  再取该队伍绑定的副本。一支队伍同时最多绑定一个副本；同一队伍开新局前，旧局整局结束。
- **个人离开**（放弃副本、断线、退队、被踢）：离开者先退出队伍，再由 `EndAsync` 清理他在本局的
  状态。他仍在队伍中、且队伍还有其他成员时，`EndAsync` 拒绝执行。最后一名成员（含单人）离开即整局结束。
- **整局结束**（整队退出、队长选图回城、清场回城、全灭、入场中止、开新局）：对全体成员一次结束并
  移除副本，队伍不变。非队长选图回城不结束副本。
- **逐人副作用**：每条路径都释放占用登记、恢复虚弱回复、关闭宠物战斗、移除准备与死亡标记、清理结算
  与翻牌领取。离场生命周期与失败虚弱只在基准会产生它们的路径上产生：个人离开、整队退出、选图回城
  记录生命周期，未清场且布局适用时设失败虚弱；全灭记录生命周期，布局适用时设失败虚弱；清场回城只
  记录生命周期；入场中止与开新局两者都不产生。
- **运行中队伍不增员**：发起邀请与接受邀请的协议命令在任一方处于副本时拒绝，队伍服务不读取副本。
  即使队伍被构造为含图外角色，他也不是副本成员，得不到过图、拾取、结算或通知资格。
- 断线时若队伍清理本身失败，随后的副本清理会被拒绝，该角色的本局状态和占用登记保留到整局结束；
  此时队伍与副本对“他在不在本局”的回答一致。

## 首次创建与发布

```text
选择地下城或首次进入相邻 Cell
  -> 解析布局与目标 map
  -> DungeonCellFactory 创建未发布 seed
       -> 房间 Actor / 声明地面物
       -> 物品表现分类
       -> 每个怪物 Actor 的 pending loot
  -> Run 在版本门内采用一个完整 Cell
  -> 同时发布 Cell 并切换 CurrentCell
  -> 从 Cell snapshot 构建 START_MAP 投影
```

构造阶段发生异常或取消时，seed 从未进入 Run，当前房间和版本均不变化。并发创建同一坐标时，
多个准备过程可以在锁外完成，但只有满足旧 `RunId + RoomVersion` 的一个完整实例会被采用；其余
草稿不能覆盖已经发布的 Cell。

初始 Cell 也遵守相同原则：Cell 创建成功以前，参与者不能通过 Run 查询看到半成品。
`IsComplete=false` 或坐标/map 不匹配的 seed 会在发布前被拒绝；运行期不会用空 Cell、临时 Actor
或临时金币补齐缺失事实。

## 重访

重访直接从 Run 的 Cell 字典取得原实例，不调用房间、物品或掉落 content。因此已经死亡的
Actor、已经拾取的地面物和 Cell revision 都会保留。

A21 的重访线格式会省略客户端已经持有的 Actor 和地面物记录。这里的“空”只是一种协议投影：
`DungeonRoomVisit.Run.CurrentCellState` 仍然是完整 Cell 快照，不能把空投影保存成领域状态。

## Actor 死亡与掉落转移

Cell 创建时，每个怪物 Actor 已经持有确定的 `PendingLoot`，其中同时容纳地面产物和按角色直发的
任务物品。`IsMonster` 是规则服务在构造期解析并
冻结的领域事实，与用于客户端 AI/表现分类的 `IsAi` 分离；例如 actor type 4 不是怪物，不阻塞
清房、不给经验，也没有 pending loot。死亡流程如下：

```text
DIE_MONSTER
  -> 从当前 Cell 预占 Actor death
  -> 各参与者的 CMD 在同一事务中完成经验、任务和属于自己的直发物品入包
  -> 提交 Cell death
       -> Actor.IsDead = true
       -> 只将地面类 PendingLoot 转入同一 Cell.GroundItems；消费全部待发产物
       -> CellRevision + 1，Run RuntimeRevision + 1
       -> 返回 DungeonActorDeathChange
  -> 协议层用 change 中新增的 GroundItems 构造死亡/掉落通知，并向直发接收者发送背包和任务刷新
```

提交前失败会释放 death claim：Actor 仍存活，pending loot 不动，Cell revision 不增加。重复死亡
报告拿不到第二个 claim，因此不会重复产生经验、任务进度、直发物或地面物。

声明物和死亡掉落共享一个 `GroundItems` 编号空间。编号一旦使用，即使物品后来被拾取也不会
被后续掉落复用。来源字段只用于投影和审计，不形成第二套库存。

## 拾取与补偿

拾取从当前 Cell 的 `GroundItems` 按稳定编号预占完整物品快照：

1. 校验 `RunId`、`RoomVersion`、当前 Cell、归属和未被其他请求预占。
2. CMD 在领域事务中写入背包、金币和已有的任务拾取进度。
3. 持久化提交后，从 Cell 原子移除物品，Cell revision 和 Run runtime revision 各递增一次，并
   生成 `DungeonPickupChange`。
4. 持久化失败则释放 claim；地面物保持原样，可安全重试。

领取 claim 期间禁止切换 Cell。claim 同时绑定 `RunId + RoomVersion + CellRevision`，因此 A 房间
取得的预占不能在切到 B 房间后提交，也不会用 B 的版本广播 A 的移除。

其他队员收到的移除通知引用同一个 `GroundDropId(Cell, Number)`，不会从怪物集合或房间声明
重新推导另一个身份。

机关、脚本或其他非怪物来源通过同一个 `TryAddGroundItems` 领域入口向 Cell 添加地面物；它们
不会借用怪物掉落集合，也不会形成第二套编号或通知路径。

## 当前边界

- 本次自动入包只覆盖房间 Actor 的 `[monster reward item]`。`[enemy reward item]` 中的
  Passive Object 来源尚未进入 Cell Actor 模型；它的销毁报告使用不同的对象标识，不能把
  客户端任务计数帧冒充为一次对象销毁。普通建筑预声明地面物也暂不改写其现有 START_MAP 表现。
- 当前死亡请求没有可信坐标，因此地面物模型不伪造 `x/y`。获得客户端或上游证据后，可以在
  Cell 内扩充 placement 字段而不改变所有权边界。
- Boss 结算、翻牌、回城等待、任务规则、经验公式和掉落概率不在本次重构中改变；它们只迁移
  到读取 Cell 的等价事实。
- Cell 运行时为进程内状态。现有断线规则仍是离开副本回城，因此本次不增加副本持久化恢复。
