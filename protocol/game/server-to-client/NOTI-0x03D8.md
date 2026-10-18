# NOTI `0x03D8` `INCREASE_CHANCE_LOTTERY_ALL`

## 2026-10-16 空婚房兼容隔离已实现

CurrentImplementation：选角使用 `CoupleRoomEmptyCompatibilityMessage`，固定发送既有204零空态；非空旧彩票不再进入A21婚房opcode，旧彩票数据库记录保留。回归以非空旧彩票输入仍得到204零保护，未擅自换号或缩短为17B。此改变不表示非空婚房已实现。

## A21 人工裁定（优先于机器区与 A12 历史区）

> **2026-10-16 / ClientStatic / Confirmed。**A21 将 `0x03D8` 注册为 `COUPLE_ROOM`。
> 下方机器区 offset 0 的“204 字节增幅抽奖状态”说明来自旧实现/A12 语义，不能用于解释
> A21 非零 body；`rks:machine` 与 `rks:a12` 块为保留的生成记录和历史证据。

A21 handler RVA/VA 为 `008820F0/00C820F0`，实际消费结构是：

```text
u32 count
repeat count times:
    u16 slot
    u32 value_or_item_id
u32 tail0
u32 tail1
u32 tail2
i8  tail3

ConsumedLength = 17 + count * 6
```

重复项宽度 6 和尾部宽度 13 为 ClientStatic / Confirmed；`slot` 来自 entry helper
`00881510/00C81510` 对参数低 16 位的明确收窄。`value_or_item_id` 及四个尾字段的业务名称
仍为 Unknown。

既有 A21 入场会话在该 identity 上捕获到 204 个零。它在上述 parser 中只表示
`count=0`、13 字节零尾部：handler 消费前 17 字节，分发层容忍其余 187 字节。因此它是
**WireCaptured 的空态兼容体**，不证明 204 字节是非空婚房的固定长度，也不验证旧彩票
非空布局。

当前 `LotteryProgressStateMessage` 无记录时也产生 204 个零，因而偶然兼容空态；有记录时
offset 0 写 `2`，A21 会将其解释为两个婚房条目，再从错误位置读取尾部。A21 发送路径不得
继续发送非空旧彩票形状。婚姻一期可保留 204-zero 空态兼容体，非空婚房需按上述 framing
另建 encoder 并等待实帧取值。

专项证据、当前实现风险和 113 字节参考体的长度算术见
[A21 婚姻协议静态调查](../../../Document/outputs/marriage-research-20261016/a21/a21.md#7-03d8-冲突为何空态能进城非空彩票会错位)，
窄反汇编见
[focused-excerpts.txt](../../../Document/outputs/marriage-research-20261016/a21/focused-excerpts.txt)。

### 2026-10-18 补充：条目与尾部语义（ClientStatic）

- 尾部 13B 与 NOTI `0x03DC COUPLE_TREE` 的读法、写入字段完全相同（`008821B2..008821EF` 对照 `0087E853..0087E890`），**即爱情树快照**：
  `u32 树种类(0=无树)`、`u32 枯萎时刻(UTC Unix 秒)`、`u32 成长值`、`i8 未知`（语义 High，见 NOTI 0x03DC 人工区）。
- 6B 条目的 `u32` 是**家具物品 ID**（High）：进入 slot→itemId map（`00881510`），在 town 101 时生成家具对象（`008802B0`），并由 `00881DF0` 逐件读物品数据汇总后写角色 +0x388。
- **协议不含坐标**（High）：命中测试 `0087FC90` 用 `0273D000(slot)` 取得婚房布局固定点，再加上物品矩形；slot 是预定义摆放点编号。
- 113B 参考常量（ReferenceOnly）：16 个值位于 `10002692..10002802`；尾部为 `kind 3 / 2025-10-31T21:58:06Z / 30 / 1`。
- 详见 [A21 婚房静态调查](../../../Document/outputs/marriage-housing-20261018/a21/a21.md)。

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
| Type | `0x03D8` | 同上 |
| 名称 | `COUPLE_ROOM` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 17 + n×6   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 204 字节定长的增幅抽奖进度全量状态块：offset 0 的 i32 恒写 2；offset 4 = 当前道具模板 ID（无则 -1）；offset 8 = 当前奖励序号+1（无则 -1）；offset 12 起是最多 8 条 x 24 字节记录，每条前 4 字节为道具模板 ID，其后最多 20 个字节各存一个已领取奖励序号+1 | High | 上游 Network/Builders/IncreaseChanceLotteryPacketBuilder.cs:10-13 常量 AllStateBodyLength=204/RecordCount=8/RecordLength=24/ClaimCapacity=20，:34-38 写头三个 i32，:39-53 写记录与已领取序号；发送点 Network/Handlers/LotteryItemResponseSender.cs:121-124，取自 `[ebp - 0x18]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 6 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x20]` |
| 4\* | 13 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |

带 `*` 的偏移是**名义值**：偏移累加时**没有计入其前重复组的任何一次重复**，即按重复次数 **n = 0** 排布。真实位置 = 名义值 + Σ(该组宽度 × 实际重复次数)。实现时必须按计数字段累加，不能照抄这个数字。

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008820F0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- rks:a12:begin -->

> **本区结论得出于 A12 客户端**（SHA-256 `F78F0AEA…`），**A21 上尚未复核**。
> 按迁移规则，本协议在 A21 上重做完成后整区删除；在那之前它是脚手架，不是 A21 的记录。
> 迁移状态见[协议清单](../../catalog.md#a12-内容的退场)。

## 实测字段表（运行时读取跟踪）

客户端**实际**读取这条协议的方式，由运行时读取跟踪测得（做法见 [game-receive](../../patterns/game-receive.md#四个原语是唯一出口)）。

**本节优先于机器区的 Body 表。** 机器区那张表由静态走图产出，在 S→C 上普遍止步于打包区，给出的是下界；载荷多形时它只描述其中一形，却读起来像描述了整条协议。本节是实测，但**只给边界，不给含义**——`含义` 一列留给人填，偏移已确证之后用实机改值做差分是最便宜的填法。

### 形状 body 204 字节

| Offset | Size | Enc | 读取者 RVA | 实测值 | 含义 |
|---:|---:|---|---|---|---|
| 0 | 204 | `bytes`(204) | `0x00263A9A` | `0000000000000000`… | — |

读取 1 次，消费到偏移 **204**。


## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

观察记录见上方「实测字段表」，其证据是归档的运行时读取跟踪
[read-trace-20260817-entry-batch](../../patterns/data/traces/read-trace-20260817-entry-batch.log)。
尚无单帧 hex 样本；取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`。

## 服务端实现映射

尚未实现。**注意**：本协议已在实机流量里出现（见上方实测字段表），"尚未实现"指的是
服务端侧没有对应 slice，不是"没观察到"。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->

<!-- rks:a12:end -->

## A21

**2026-08-24 WireCaptured。** 上面的机器区由 **A12** 客户端（SHA-256 `F78F0AEA…`）
的静态普查生成。本节记录 **A21** 客户端（SHA-256 `1F8244D3…`）上不同的部分。
一份 A21 参照会话归档于
[a21-world-entry-20260823.log](../../patterns/data/traces/a21-world-entry-20260823.log)
（section L 是入场序列的逐帧顺序）。

**A21 表里这个号叫 `COUPLE_ROOM`**，A12 表叫 `INCREASE_CHANCE_LOTTERY_ALL`。

参照在 `0x03D8` 上发 **204 个 `0x00`**，本仓空彩票 encoder 在同一位置产出的 body 也为
204 个零（section M）。这证明当前零体能复现该次入场字节，不证明两代协议共享非空语义。
2026-10-16 的 A21 handler 复核已确认 parser 只按 `17 + 6n` 消费；对 204 零而言 `n=0`，
只消费前 17 字节。曾把 identity 改到 `0x0442` 的做法没有线证据，保持回退；正确处置是隔离
A21 `03D8` 的非空旧彩票发送，而不是猜测另一个 opcode。


## 2026-10-18 A21 婚房实现

共享婚房快照，非空状态为 17+6n 字节；每项槽位+模板 ID，尾部为 13B 树。空房选角继续保留 204 个零的历史兼容体。

CurrentImplementation：统一婚姻领域中的 Housing 逻辑；编解码与投影见 `Server/src/Dfo.Server.Protocol/Game/MarriageHousing/HousingSlice.cs`。依当前 A21 ClientStatic 和 PVF 实施，自动化通过，尚待此次实机验收，不提升为 ClientVerified。通用 CMD 接收层先消费 accepted 信封；旧静态字段宽度若从 status 开始，指的是信封之后的业务体。

新增定向证据见 [补查报告](../../../Document/outputs/marriage-housing-implementation-20261018/wire/README.md)，测试及限制见 [实施说明](../../../Document/MarriageHousing-Implementation-20261018.md)。
