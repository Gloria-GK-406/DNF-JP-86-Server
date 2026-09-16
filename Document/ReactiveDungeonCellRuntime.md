# 响应式地下城 Cell 运行时

## 目标

地下城中的房间状态只保留一个真源：`DungeonCellState`。`DungeonRunState` 管理一次共享副本的
参与者、布局、当前 Cell 和已经创建的 Cell 集合；Actor、待掉落物、地面物、清理状态及其版本
全部归属于 Cell。运行期不再用 Run 级死亡集合、怪物掉落集合或已拾取集合重新拼装房间。

普通地下城与剧情地下城共用这套运行时。二者可以在布局选择、任务约束等进入规则上不同，但
进入以后都操作相同的 Run/Cell 模型。

## 职责边界

| 组件 | 职责 |
|---|---|
| `DungeonRunDomainService` | 管理 `DungeonRunId`、冻结参与者、迷宫、当前 Cell、Cell 集合以及受版本保护的提交 |
| `DungeonCellFactory` | 仅在 Cell 首次创建时读取房间内容、物品分类和掉落规则，产出完整且尚未发布的 `DungeonCellSeed` |
| `DungeonCellAggregate` | 独占 Actor 状态、pending loot、统一 GroundItems、clear 状态和 Cell revision；防御性复制构造输入 |
| 规则/content 服务 | 只提供 Cell 构造输入；Cell 发布后不得参与死亡、拾取或重访的运行态查询 |
| CMD/事务协调 | 预占领域事实，完成经验、任务、背包或金币等持久化，再提交或释放 Cell 预占 |
| 协议投影 | 只消费 Cell snapshot 或同一次提交返回的 change；不访问 PVF 来补运行态事实 |

Session、Connection 和发送器不进入 Run/Cell 领域模型。它们只在协议投影层决定把已经提交的
change 发送给哪些当前连接。

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

Cell 创建时，每个怪物 Actor 已经持有确定的 `PendingLoot`。`IsMonster` 是规则服务在构造期解析并
冻结的领域事实，与用于客户端 AI/表现分类的 `IsAi` 分离；例如 actor type 4 不是怪物，不阻塞
清房、不给经验，也没有 pending loot。死亡流程如下：

```text
DIE_MONSTER
  -> 从当前 Cell 预占 Actor death
  -> CMD 完成参与者经验和任务等持久化效果
  -> 提交 Cell death
       -> Actor.IsDead = true
       -> PendingLoot 原子转入同一 Cell.GroundItems
       -> CellRevision + 1，Run RuntimeRevision + 1
       -> 返回 DungeonActorDeathChange
  -> 协议层用 change 中新增的 GroundItems 构造死亡/掉落通知
```

提交前失败会释放 death claim：Actor 仍存活，pending loot 不动，Cell revision 不增加。重复死亡
报告拿不到第二个 claim，因此不会重复产生经验、任务进度或地面物。

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

- 当前死亡请求没有可信坐标，因此地面物模型不伪造 `x/y`。获得客户端或上游证据后，可以在
  Cell 内扩充 placement 字段而不改变所有权边界。
- Boss 结算、翻牌、回城等待、任务规则、经验公式和掉落概率不在本次重构中改变；它们只迁移
  到读取 Cell 的等价事实。
- Cell 运行时为进程内状态。现有断线规则仍是离开副本回城，因此本次不增加副本持久化恢复。
