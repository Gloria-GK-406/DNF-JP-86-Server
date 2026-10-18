# NOTI `0x03D3` `CHARAC_LOVE_POINT`

## 2026-10-17 心意点通知

ClientStatic / Confirmed：00892B80读8B=u32当前接收角色CharacterId+u32共享点数；首字段与本机角色ID比较，不能写配偶ID。写manager+6C。CurrentImplementation：成功后分别按双方自己的ID通知；重放只发送请求者当前点数，无ACK、无重扣。


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
| Type | `0x03D3` | 同上 |
| 名称 | `CHARAC_LOVE_POINT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 8   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 8]` |
| 4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xc]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00892B80` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->


## 2026-10-18 A21 婚房实现

角色 ID + 该角色个人心意点余额；不得以配偶余额覆盖接收者个人钱包。

CurrentImplementation：统一婚姻领域中的 Housing 逻辑；编解码与投影见 `Server/src/Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs`。依当前 A21 ClientStatic 和 PVF 实施，自动化通过，尚待此次实机验收，不提升为 ClientVerified。通用 CMD 接收层先消费 accepted 信封；旧静态字段宽度若从 status 开始，指的是信封之后的业务体。

新增定向证据见 [补查报告](../../../Document/outputs/marriage-housing-implementation-20261018/wire/README.md)，测试及限制见 [实施说明](../../../Document/MarriageHousing-Implementation-20261018.md)。
