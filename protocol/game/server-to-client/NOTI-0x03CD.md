# NOTI `0x03CD` `WEDDING_REQUEST`

## A21 人工字段裁定（2026-10-16）

ClientStatic / Confirmed：handler RVA00891140消费 **11B**，没有CMD结果信封。下方机器区仍标布局下界，字段含义以此人工裁定为准。

| Body offset | Size | Enc | 含义 |
|---:|---:|---|---|
| 0 | 4 | i32 LE | status；当前实现写0 |
| 4 | 4 | u32 LE | proposer/applicant WireUserId |
| 8 | 1 | u8 | action；1求婚，2婚礼预约 |
| 9 | 1 | u8 | action1写0；action2为tier，不能写waitIndex |
| 10 | 1 | u8 | 当前实现写0 |

证据：[求婚接收与窗口](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)、[婚礼字段裁定](../../../Document/MarriageReservation-20261016.md#证据与限制)。CurrentImplementation仅发送普通档tier0婚礼邀请。

与[C2S03F0](../client-to-server/0x03F0.md)及[CMD03F0](CMD-0x03F0.md)配合；窗口触发、发送顺序及验证边界统一见[流程](../../patterns/flow-marriage-proposal.md)。

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
| Type | `0x03CD` | 同上 |
| 名称 | `WEDDING_REQUEST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 11   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 11 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 2]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00891140` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

## 实帧边界

当前交互的有限范围ClientVerified见流程矩阵；尚未归档本次完整11B正文样本。机器区的unknown不代表上面的人工字段仍未调查。
