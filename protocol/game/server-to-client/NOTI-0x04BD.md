# NOTI `0x04BD` `RAID_USER_FINISH_LOAD`

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
| Type | `0x04BD` | 同上 |
| 名称 | `RAID_USER_FINISH_LOAD` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：结构未知** — 静态分析无法得出字段边界。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

本协议**没有可用的字段表**。上面的原因即其不可知的成因；
要取得布局，需要运行时手段（断点、真实样本）而非更多静态分析。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D8C0B0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21 静态证据

来源为 `ClientStatic`。原版 A21、分析镜像 SHA-256 见 [客户端证据绑定](../../../Document/tasks/anton-raid/research/04-jp86-protocol-client-pvf.md#0-绑定版本与标签)，以下地址为 VA。

VA `0x0118C0B0` 取攻坚管理器并尾跳 `0x00C23050`，后者查询时钟并在 `0x00C23059` 保存结果。该路径不读 body 字段（**Confirmed**）。上游在团本城镇到场时发空 body，发送触发、回城是否共用及客户端可见效果待实机。

## 招募接线

当前招募切片在选角完成后、确认团本频道到场时发空载荷，即使当前等待区域没有其他角色也投递。服务端出站样本及显示效果待实机。
