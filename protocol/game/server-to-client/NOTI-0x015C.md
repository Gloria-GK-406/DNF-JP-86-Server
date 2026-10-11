# NOTI `0x015C` `TOD_CLEAR_REWARD`

<!-- rks:machine:begin -->

> **本区由静态分析自动生成，重新运行会整体覆盖；请勿手工编辑。**
> 需要修正字段含义时改 `patterns/data/meanings/*.tsv`，需要修正布局请改提取器。
> 本区之外的一切内容属于人工区，重新生成不会触碰。

## 身份

| 项 | 值 | 来源 |
|---|---|---|
| Endpoint | Game | 帧协议基线 |
| Direction | Server → Client | 接收注册表普查 |
| Command 族 | `0x00`（NOTI） | 客户端两张注册表的分表证据 |
| Type | `0x015C` | 同上 |
| 名称 | `TOD_CLEAR_REWARD` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 15   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 通关耗时 clearTimeMilliseconds（毫秒） | Confirmed | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:288 WriteUInt32(clearTimeMilliseconds)；调用点 Network/Handlers/Dungeon/DungeonSettlementHandler.cs:1052，取自 `[ebp - 0x20]` |
| 4 | 2 | `u16` | 层数 floor（上游 Math.Clamp(floor, 1, 100)，由 DungeonData.TryGetTowerOfDespairFloor 解析出） | Confirmed | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:289 WriteUInt16；来源 DungeonSettlementHandler.cs:1048，取自 `[ebp - 0x24]` |
| 6 | 1 | `u8` | 奖励槽位数 rewardSlotCount（常量 10） | Confirmed | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:286,290 const rewardSlotCount = 10 + WriteByte，取自 `[ebp - 0xd]` |
| 7 | 4 | `u32` | 第 1 个奖励槽的 itemId（空槽写 -1） | High | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:298,303 循环首次迭代 WriteInt32(itemId) / WriteInt32(-1)，取自 `[ebp - 0x14]` |
| 11 | 4 | `u32` | 第 1 个奖励槽的堆叠数 stackCount（空槽写 0） | High | 上游 Network/Builders/Dungeon/DungeonNotificationBuilder.cs:299,304 循环首次迭代 WriteInt32(stackCount) / WriteInt32(0)，取自 `[ebp - 0x18]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00A15BA0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：以下为当前人工核对布局，自动生成区保留原始提取结果；阅读完整布局以本节为准。 -->

# NOTI 0x015C — 绝望之塔通关奖励

日期：2026-10-11。证据：ClientStatic / High（A21 RVA 0xA15BA0 的 u32、u16、u8 前缀）；UpstreamReference / High（ServerS4A21 c20dba5f DungeonNotificationBuilder.BuildTowerOfDespairClearReward）。完整尾部参考上游，尚无当前客户端 WireCaptured / ClientVerified。

| 字段 | 类型 | 含义 |
|---|---|---|
| clearTime | u32 LE | 通关毫秒 |
| floor | u16 LE | 已通关楼层 |
| count | u8 | 后续奖励位置数，当前为 10 |
| itemId, quantity | count × (i32 LE, i32 LE) | 奖励物品及数量；空位 -1, 0 |

当前固定 body 87 字节。通知只展示已经提交的奖励；物品绝对状态通过 NOTI000E 同步。满背包时奖励进入邮件，015C 仍展示本层所得。前置通关权威来自运行态的 ClearOpen，重复 CMD002E 不重复发奖。

CurrentImplementation：DespairTowerDomainService → GrantClearExperienceCommandHandler → SetPlayResultHandler → DespairTowerProjection / DespairClearRewardEncoder。测试 DespairTowerPayloadTests 与 DespairTowerTests。部署后的实机状态待用户验收。
