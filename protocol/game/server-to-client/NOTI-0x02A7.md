# NOTI `0x02A7` `HELL_PARTY_MONSTER_INFO`

## 2026-10-07 消费链补证与接线

**ClientStatic / High**：存表RVA0134B4A0之后，消费者0134A7E0以key查询管理器（0237C6D0），缺失时调用创建器01D18F60、设置等级01419B80、登记0237CA90。创建器引用字符串VA03464820（RVA03064820），解密为 `AiCharacter Create Index :%d`。据此index用于AI角色资源索引，而非重连队伍ID；原始指令与字符串解码见[取证](../../../Document/tasks/manual-hell-entry/verification/client-02a7-consumer.txt)。

**CurrentImplementation**：首次进入深渊房时发送 `u32 count + count×(u32 APC code,u32 level)`，相同APC code去重，不发送普通monster代码，level来自冻结方案。该接线没有深渊WireCaptured/ClientVerified，后续战斗不在范围。

## 2026-10-07 A21客户端裁决

**ClientStatic / Confirmed**：RVA`00D483C0..00D48453`读取`u32 count + count×(u32 hellIndex,u32 level)`，消费长度`4+8n`。首字段直接控制循环，不能解释为重连队伍ID。两值传给RVA`0134B4A0`，原生日志解密为`hell index(%d) level(%d)`；index是否等于怪物ID、组ID或另一映射尚未确认。

直接分析的b1 PE SHA256为`CA7ECA4E7A3701B7FC8E259115AEB28502778A33DF73EED3D501DDEEAE388D14`，ImageBase`00400000`；同目录壳SHA256`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。地址均为RVA。

先前套入的上游`DungeonRejoinNotificationBuilder`字段不能证明A21语义，已从生成含义输入中移除。读取长度不证明原服务端不会发送未消费尾部；本轮没有深渊抓包或实机验收。

复现与指令证据见[客户端研究](../../../Document/abyss-research-20261007/client.md)及其`evidence/client-*`；跨消息流程见[研究总览](../../../Document/abyss-research-20261007/README.md)。

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
| Type | `0x02A7` | 同上 |
| 名称 | `HELL_PARTY_MONSTER_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4 + n×8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 深渊索引/等级记录数量count，控制后续循环；不是重连PartyId | Confirmed | ClientStatic：b1 SHA256 CA7ECA4E...388D14；RVA00D483D1读u32、00D483DB比较count、00D48449循环上限；Document/abyss-research-20261007/client.md，取自 `[ebp - 0xc]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | hell index；具体索引到组/演员的映射尚未确认，不能直接命名monsterId | Confirmed | ClientStatic：RVA00D483EA读第一u32；00D48410引用解密日志hell index(%d) level(%d)；两值传RVA0134B4A0；Document/abyss-research-20261007/client.md，取自 `[ebp - 4]` |
| &nbsp;&nbsp;↳&nbsp;4 | 4 | `u32` | level；客户端日志原名，等级缩放和用途尚待追踪 | Confirmed | ClientStatic：RVA00D483F3读第二u32、00D48400提交pair；b1 SHA256 CA7ECA4E...388D14；Document/abyss-research-20261007/client.md，取自 `[ebp - 4]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D483C0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。
