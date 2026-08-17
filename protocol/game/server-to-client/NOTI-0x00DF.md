# NOTI `0x00DF` `POWER_WAR_RESULT`

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
| Type | `0x00DF` | 同上 |
| 名称 | `POWER_WAR_RESULT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局已闭合** — 字段序列与宽度由构包/接收指令逐条读出，无分支、无变长原语。

```text
BodyLength = 25
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| 1 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x1c]` |
| 5 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| 9 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| 13 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x10]` |
| 17 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| 21 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008F60D0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
