# NOTI `0x025A` `RAID_RESULT`

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
| Type | `0x025A` | 同上 |
| 名称 | `RAID_RESULT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 10   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 结算类型 resultType：上游通关结算传 0，攻击超时失败传 1 | Confirmed | 上游 Network/Builders/Raid/RaidPacketBuilder.cs:247 WriteUInt32(resultType)；调用点 Network/Handlers/RaidHandler.Rewards.cs:300 传 0u、Network/Handlers/RaidHandler.Runtime.cs:245 与 :261 传 1u，取自 `[ebp - 0x5b]` |
| 1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x68]` |
| 2 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x64]` |
| 6 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x6c]` |
| 8 | 1 | `u8` | 本阶段用时秒数 clearTimeSeconds | Confirmed | 上游 RaidPacketBuilder.cs:249 WriteUInt32(clearTimeSeconds)；调用点 RaidHandler.Runtime.cs:263 传 failed.PhaseClearTimeSeconds，取自 `[ebp - 0x59]` |
| 9 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x51]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D764F0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21 静态证据

来源为 `ClientStatic`。原版 A21、分析镜像 SHA-256 见 [客户端证据绑定](../../../Document/tasks/anton-raid/research/04-jp86-protocol-client-pvf.md#0-绑定版本与标签)，以下地址为 VA。

VA `0x01176524`、`0x01176536`、`0x01176549`、`0x01176560`、`0x0117656B`、`0x01176581` 顺序读取 `u8/u8/u32/u16/u8/u8`，共 10 字节。首字段在 `0x01176611/0x0117661C` 分派 `0/2` 与 `1/3`，末字段在 `0x01176589/0x0117658D` 按是否为 0 传给结果处理。读宽和测试为 **Confirmed**；结果细分、名次与金币档的对应及资格显示待对照。

`0x01176C3C` 跳表的 0/2 选择成功 AVI、1/3 选择失败 AVI，第二字段 0/1 分别选择两阶段的资源（A21 字面量表的 `1-S`、`2-S` 和 `1-F`、`2-F`）。成功路径 `0x01176999–0x011769B9` 提交本地任务事件 `0x23`（`0x00E68B03` 对应 `QUEST_EVENT_RAID_PHASE_CLEAR`，`0x00E68D1A` 映射事件）。是否进一步发 `0x0021`、客户端任务显示及服务器去重待联调。
