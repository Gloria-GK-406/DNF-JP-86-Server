# NOTI `0x01FA` `INTEGRATE_MATCHING_COMPLETE`

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
| Type | `0x01FA` | 同上 |
| 名称 | `INTEGRATE_MATCHING_COMPLETE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：结构未知** — 静态分析无法得出字段边界。

原因：handler 的取字段调用进入了被打包/混淆的代码区，静态跟不下去。

本协议**没有可用的字段表**。上面的原因即其不可知的成因；
要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。

静态跟不下去的调用目标：`0x05800213`、`0x05902F66`

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00E0F8D0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

**ClientStatic / Confirmed。** RVA `00E0F8D0` 及其后续 `00E0F800–00E0F8C8` 不读取包体，更新匹配完成提示。

**ThirdPartyReference / CurrentImplementation。** 双方加载和 P2P 报告齐全后发送空载荷，与 [NOTI 0x011A](NOTI-0x011A.md) 和房间开战通知同批投递。
