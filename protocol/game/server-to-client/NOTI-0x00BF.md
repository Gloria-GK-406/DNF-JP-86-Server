# NOTI `0x00BF` `GUILD_AGIT_INFO`

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
| Type | `0x00BF` | 同上 |
| 名称 | `GUILD_AGIT_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 1   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 1]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D8F230` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

**ClientStatic / S4A21 参照。** A21 b1 的 `0x0118F240` 读取一个字节。上游 `GuildAgitPacketBuilder.Entrance` 以同一 A21 处理器为依据，将基地入口状态编码为 1、2 或 4。客户端地图选择处分档进入城镇 8 的区域 0、1 或 3。A21 字符串 21484 指向赛丽亚房间，开发 PVF 的 `town/guildagit.twn` 声明对应区域。

**CurrentImplementation / WireTest。** 在公会资料后投影初始值 1。入口走 `CMD 0x0024`，服务端验证当前成员关系，按公会实例组织大厅。线路已验证赛丽亚入口、同公会可见、跨公会隔离、非成员拒绝、原路返回和重登。

已撤回基地升档试验。当前通知仍发送初始值 1，旧验收库保存过的档位不参与投影。[旧升级取证](../../../Document/tasks/guild-system/a21-guild-base-upgrade.md)

## 待实机补全

- [ ] 捕获原生客户端实际入场和基地阶段变化

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
