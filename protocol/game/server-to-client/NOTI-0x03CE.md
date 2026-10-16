# NOTI `0x03CE` `WEDDING_RESPONSE`

## A21 人工字段裁定（2026-10-16）

ClientStatic / Confirmed：handler RVA00891960消费 **12B**，没有CMD结果信封。

| Body offset | Size | Enc | 含义 |
|---:|---:|---|---|
| 0 | 4 | i32 LE | status；0成功，当前有效邀请答复失败为-3 |
| 4 | 4 | u32 LE | responder WireUserId |
| 8 | 1 | u8 | action；1求婚，2婚礼预约 |
| 9 | 1 | u8 | 当前实现写0 |
| 10 | 1 | u8 | 当前实现写0 |
| 11 | 1 | u8 | outcome；0接受、1拒绝、2超时取消 |

ClientStatic / Confirmed：handler在判断status前先关闭申请者等待窗口。status0且action1/2时，outcome1到dstr0x11C8A普通拒绝提示，outcome2到dstr0x11C37超时取消提示。不能把二者混成同一枚举；当前客户端补丁选择在action2自动超时时发送outcome1，使邀请方与手动点“否”走相同提示分支。

证据：[原接收补证](../../../Document/outputs/marriage-proposal-20261016/client-evidence.txt)、[关闭窗口与action1/2文本分支的定向补证](../../../Document/MarriageTimeoutProtocolEvidence-20261016.md)。CurrentImplementation发送status0的真实答复，已匹配有效邀请的重校验失败才发送status=-3收尾通知。

与[C2S03F1](../client-to-server/0x03F1.md)及[CMD03F1](CMD-0x03F1.md)配合；发送顺序及自动超时的待验收范围见[流程](../../patterns/flow-marriage-proposal.md)。

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
| Type | `0x03CE` | 同上 |
| 名称 | `WEDDING_RESPONSE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 12   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 12 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 4]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00891960` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

## 实帧与实机边界

手动拒绝与接受的有限范围用户验收见流程矩阵；上一版自动超时的NOTI发送metadata已有记录，不能据此推定客户端显示了普通拒绝文本。当前outcome1自动拒绝的双端提示仍待用户确认，尚未归档其完整正文样本。
