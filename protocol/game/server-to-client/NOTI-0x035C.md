# NOTI `0x035C` `ADVANCE_ALTAR_START_MAP`

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
| Type | `0x035C` | 同上 |
| 名称 | `ADVANCE_ALTAR_START_MAP` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 24 + n×12   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 20 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x50]` |
| 20 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x38]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 12 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x2c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D5C180` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21字段与实现

ClientStatic / Confirmed：A21镜像 SHA256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`，基址00400000，以下地址使用VA。70Reference只补充字段语义；A21采用紧凑布局，不能复制70的结构体对齐。CurrentImplementation为本工作区代码及自动测试；没有WireCaptured或ClientVerified。

头20B：stageType:i32（0普通、1生存）、round:i32、mapId:i32、totalDurationMs:i32、basisLevel:i32。随后u32 phaseCount + 每行12B（phaseIndex:i32、startOffsetMs:i32、phaseType:i32，0普通／1精英／2Boss）。**之后还有u32 initialActorCount及每行13B**：mapRecordIndex:i32、sequence:u16、resourceCode:u16、level:u8、rank:u8、unknown三字节当前0。完整长度`28+12P+13M`，机器区24+12P仅是下界。客户端`VA0115C4E5`以后的计数与记录读取补全尾部；生存round>1在`0115C3B6–0115C3FD`保留当前场景后返回，不重新Loading，初始actorCount为0。

原始摘录：[client-map-response.txt](../../../Document/tasks/guardian-altar/client-map-response.txt)。当前encoder与对应强类型消息已注册，自动codec测试核对完整读取边界。发送顺序见[祭坛流程](../../patterns/flow-guardian-altar.md)。
