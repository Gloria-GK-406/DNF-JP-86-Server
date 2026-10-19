# NOTI `0x042B` `GOLD_SILVER_HORN_INFO`

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
| Type | `0x042B` | 同上 |
| 名称 | `GOLD_SILVER_HORN_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 4]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00571730` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 人工复核：2026-10-19

ClientStatic / Confirmed：A21 handler RVA `0x00571730`精确读取4字节；offset0和offset2分别为little-endian `u16 goldUsed`、`u16 silverUsed`。客户端按各槽的最大次数减已用次数显示剩余；计数语义为ClientStatic / High。机器区的“布局部分可得”是旧提取下界，本段人工复核闭合为4B。

当前PVF只有`[silver horn dungeon] [dungeon index]4123 [max enter count]3`。loader将4123写管理器`+0x20`，入口匹配后读取第二槽，因此正文是`00 00 <used4123:u16>`，第一槽保持0，不能将4123计数复制到两槽。完整loader、字符串解密、入口消费地址与复现方法见[静态证据](../../../Document/GoldSilverHorn-Research-20261019/protocol.md)。

CurrentImplementation：选角、成功入场提交后和本服统一日切刷新权威已用次数。向量：0次`00000000`，1次`00000100`，3次`00000300`。实现与验收边界见[交付说明](../../../Document/GoldSilverHorn-20261019.md)。未取得本消息真实会话样本，不标记WireCaptured或ClientVerified。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
