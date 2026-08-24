# NOTI `0x00FA` `VILLAGE_ATTACKED_REWARD`

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
| Type | `0x00FA` | 同上 |
| 名称 | `VILLAGE_ATTACKED_REWARD` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：结构未知** — 静态分析无法得出字段边界。

原因：走遍可达调用图未发现任何取字段原语调用；该 handler 很可能不读 body（纯触发型）。**这是否证，不是证明**——需要正对照与盲区清单才能支撑该结论，参见 [game-settings 的否证方法](../../patterns/game-settings.md)。

本协议**没有可用的字段表**。上面的原因即其不可知的成因；
要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x01E31720` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
