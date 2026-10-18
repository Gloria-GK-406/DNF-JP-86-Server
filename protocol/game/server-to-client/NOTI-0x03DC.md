# NOTI `0x03DC` `MAHJONG_GAME_PLAY`

## A21 人工裁定：婚房/情侣树/胶囊（2026-10-18 / ClientStatic）

A21 名称 `COUPLE_TREE`（本页标题为旧版本名）。handler `0087E840` 读 **13B**，与 NOTI `0x03D8` 尾部完全相同，Confirmed：

| offset | 类型 | 含义 | 置信度 |
|---:|---|---|---|
| 0 | `u32` | 树种类 ID，0=无树（清树） | High |
| 4 | `u32` | 枯萎/到期时刻，UTC Unix 秒；`<= 客户端秒时钟 0124F710` 即显示已枯萎（`0x11D15`），并据此算剩余天数 | High |
| 8 | `u32` | 当前成长值；≥ 种类表阈值才可收割 | High |
| 12 | `i8` | 写树对象 +0x2C，语义未知 | Unknown |

写婚房 manager `[0x039ADF58]+D8..E4`；当前在 town 101 时交给树对象 `01049C90`。成长、浇水次数与收获资格全部由服务端维护，客户端不按时间推算阶段。

证据与反汇编：[A21 婚房静态调查](../../../Document/outputs/marriage-housing-20261018/a21/a21.md)（`builders-disassembly.txt`、`handlers-disassembly.txt`、`switches-housing.txt`、`dstr-housing.txt`）。客户端 SHA-256 `CA7ECA4E…388D14`，镜像 `6BD2746D…953507`；构包点与机器区所绑 `1F8244D3…` 客户端一致。尚无实帧。

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
| Type | `0x03DC` | 同上 |
| 名称 | `COUPLE_TREE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 13   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 13 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x0087E840` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。


## 2026-10-18 A21 婚房实现

爱情树快照，13B。最后 i8 暂投影当期已使用浇水次数，仍为 Hypothesis；客户端真实字段读者尚未闭合，不作为原服语义结论。

CurrentImplementation：统一婚姻领域中的 Housing 逻辑；编解码与投影见 `Server/src/Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs`。依当前 A21 ClientStatic 和 PVF 实施，自动化通过，尚待此次实机验收，不提升为 ClientVerified。通用 CMD 接收层先消费 accepted 信封；旧静态字段宽度若从 status 开始，指的是信封之后的业务体。

新增定向证据见 [补查报告](../../../Document/outputs/marriage-housing-implementation-20261018/wire/README.md)，测试及限制见 [实施说明](../../../Document/MarriageHousing-Implementation-20261018.md)。
