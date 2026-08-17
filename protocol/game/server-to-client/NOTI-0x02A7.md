# NOTI `0x02A7` `DISCONN_DUNGEON_INFO`

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
| Type | `0x02A7` | 同上 |
| 名称 | `DISCONN_DUNGEON_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 9   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 可重连副本所属的队伍 id PartyId | Confirmed | 上游 Network/Builders/Dungeon/DungeonRejoinNotificationBuilder.cs:11 WriteInt32(partyId)；调用点 Network/Handlers/Dungeon/DungeonRejoinCoordinator.cs:100-104 传 candidate.PartyId，取自 `[ebp - 1]` |
| 4 | 4 | `u32` | 保留字段：上游恒写常量 0，不绑定任何数据 | Confirmed | 上游 DungeonRejoinNotificationBuilder.cs:12 WriteInt32(reservedInt32)；DungeonRejoinCoordinator.cs:26 const int ReservedInt32 = 0，:103 传该常量，取自 `[ebp - 0xc]` |
| 8 | 1 | `u8` | 重连 UI 初始状态 rejoinUiState；上游投递重连候选时恒写 0 | Confirmed | 上游 DungeonRejoinNotificationBuilder.cs:13 WriteByte(rejoinUiState)；DungeonRejoinCoordinator.cs:27 const byte InitialRejoinUiState = 0，:104 传该常量，取自 `[ebp - 1]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0008DCF0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
