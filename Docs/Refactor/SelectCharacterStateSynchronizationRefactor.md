# SelectCharacter 接受与状态同步重构报告

> 当前实现快照：2026-08-15。线级身份、字段含义与兼容顺序来自服务端代码和既有测试，
> 尚未被真实 86JP 客户端或抓包确认；本次重构不提升这些推断的置信度。

## 结论

`SelectCharacter` 现在只表示一次入站选择工作流：服务端完成校验与准备后，先发送
`CMD 01/0004` 接受消息，再按既有兼容顺序发送可在游戏生命周期内复用的状态同步消息。
它不再拥有一个会展开成约 58 帧的出站 Response，也不再通过未来 header 的 `type +
occurrence` 从中心 switch 中选择 body。

协议 Handler 是这条工作流的协调者，因此可以顺序派发多条顶层命令。禁止边界只在
Command/CommandHandler 内部：它们不得持有 `ICommandBus` 并递归或间接派发另一条命令。

## 当前分层

| 层 | 负责 | 不负责 |
|---|---|---|
| Data / Application | 角色、账号、背包、任务、宠物、PVP、重连和清理事实；事务与状态变更；显式目标事实 | wire command/type、frame、encoder、socket、sender |
| Protocol | 把领域事实构造成具体消息；编码 body/frame；保持兼容顺序；协调命令和发送 | 数据库存取、领域事务、连接实现 |
| Networking | 按消息 CLR 类型查 encoder；校验 endpoint；向一个连接发送；执行通用显式收件人投递 | SelectCharacter、PVP、任务、宠物等协议/领域语义 |

`ExplicitMessageDelivery` 只描述两件传输事实：发给当前连接的必达消息，以及调用者已经
选好的尽力投递目标。必达消息保持顺序且失败向上传播；次要目标逐目标隔离，一个目标
失败后停止该目标的剩余消息，但继续后续目标；取消仍传播。

## 接受工作流

`SelectCharacterHandler` 在一个 `SelectedSessionTransitionGate` lease 内顺序执行：

1. `PrepareSelectedCharacterSessionCommand`：校验 generation、处理旧/被替换 session、
   准备角色与背包状态并返回清理通知计划。
2. 在任何接受输出前创建并编码预校验全部静态消息、X-02～X-06 前置必达消息和当前
   已准备的尾部装备锁输入；首条静态消息为 `CharacterEntryAcceptedMessage` (`01/0004`)。
3. 发送原 58 位兼容模板对应的具体状态消息。重复 wire identity 由多个具体消息实例
   表示，不再用 occurrence 路由。Dark Knight `00/01C0` 仍紧邻技能状态。
4. 发送可见性、克隆称号、账号角色表、荣誉与成长胶囊等具体同步消息。
5. `ApplySelectedCharacterDailyRefillCommand`：只为已提交的分组发送
   `InventoryDeltaMessage` (`00/000E`)。
6. `PublishSelectedCharacterSessionCommand`；如适用，发送 PVP lobby 快照，再由
   `MarkSelectedCharacterPvpReadyCommand` 产生显式 peer 目标并尽力发布参与者状态。
7. 发送尾部装备锁全量状态；随后顺序执行任务同步、宠物复活和地下城重连命令，
   只在命令返回实际变化/候选时发送对应消息。
8. 任一步失败后执行 exact-session 补偿；连接关闭在 lifecycle lease 释放后尽力执行，
   避免 close callback 与同一 gate 互等。若 exact cleanup 自身部分失败，异常携带在
   mutation 前已经解析好的通知计划，Protocol 仍尝试投递该计划，同时保留原失败。

## 全量与增量

- `InventorySnapshotMessage` 使用 `NOTI 00/000D`，表示适用容器的完整快照；加载成功但
  为空仍发送零项快照。
- `InventoryDeltaMessage` 使用 `NOTI 00/000E`，表示一次已提交的有界更新；没有提交
  变化时不发送。
- 两者复用同一套 item body 编码规则，但消息身份、输入和发送条件保持分离。

## 已移除的语义泄漏

- `SelectCharacterResponse`、静态 descriptor sequence、中心 raw type/occurrence
  body 路由 API 和 Response composer/encoder。
- `ISelectCharacterSupplementalProjection`、`ISelectCharacterProjectionComposer` 及其
  CLR-type switch。
- `SelectCharacterProjectionDelivery`、`SendProjectionAsync` 与 Networking 中的
  SelectCharacter 专用异常路径。
- PVP 同步不再携带整个 `SelectCharacterResult`；它只携带参与者编码所需的角色、公有
  状态、装备和宠物事实。
- Application 清理服务不再注入 sender；它返回领域清理通知计划，由 Protocol 转成
  `ExpertStoreClosedMessage` / `TownDepartureMessage`。

## 兼容与验证边界

- 已接受流的 wire identity、顺序、重复次数、条件省略及完整帧 SHA-256 由 transcript
  测试锁定；`01/0004` 必须在第一位。
- `00/000D` 与 `00/000E` 的 item 形状有独立向量测试。
- 每个静态及补充同步消息都必须恰好注册一个 Game encoder。
- 生命周期测试覆盖 stale generation、拒绝、必达发送失败、后段失败、补偿和关闭行为。
- Networking 测试覆盖必达顺序/失败传播、owner 排除、逐目标尽力隔离和取消传播。

本次只做行为保持的所有权重构。客户端是否把这些通知当作独立、全生命周期可接收的
状态同步，仍应由 Windows 侧反汇编、内存 dump 或真实抓包继续验证；任何据此改变
wire 顺序或省略消息的决定都需要单独评审。
