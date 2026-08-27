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

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
