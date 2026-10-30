# NOTI `0x02F9` `LICENSE_DUNGEON_CLEAR_INFO`

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
| Type | `0x02F9` | 同上 |
| 名称 | `LICENSE_DUNGEON_CLEAR_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 21   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |
| 1 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| 5 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| 9 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |
| 13 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |
| 17 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D497B0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 当前实现

本仓采用21B：`guardian:u8, elapsedMs:u32, dailyItem:u32, dailyCount:u32, clearItem:u32, clearCount:u32`。
VA `011497B0` 证明读取宽度及顺序，奖励槽解释采用上游，仍待当前客户端差分验证。
所有值来自实际 Boss 清场冻结奖励，不采用客户端成绩计算。二星通关及升三星已有有限实机确认，
但本通知的奖励槽语义、守护者标志和两阶段结果展示尚无逐字段差分及完整双向样本，
不能将主流程反馈扩展为这些字段的 ClientVerified；见[遗迹流程](../../patterns/flow-darkelf-ruins.md)。

本协议尚未归档完整双向样本。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
