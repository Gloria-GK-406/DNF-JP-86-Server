# NOTI `0x03D8` `INCREASE_CHANCE_LOTTERY_ALL`

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
| 名称 | `INCREASE_CHANCE_LOTTERY_ALL` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 204   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 204 | `bytes` | 204 字节定长的增幅抽奖进度全量状态块：offset 0 的 i32 恒写 2；offset 4 = 当前道具模板 ID（无则 -1）；offset 8 = 当前奖励序号+1（无则 -1）；offset 12 起是最多 8 条 x 24 字节记录，每条前 4 字节为道具模板 ID，其后最多 20 个字节各存一个已领取奖励序号+1 | High | 上游 Network/Builders/IncreaseChanceLotteryPacketBuilder.cs:10-13 常量 AllStateBodyLength=204/RecordCount=8/RecordLength=24/ClaimCapacity=20，:34-38 写头三个 i32，:39-53 写记录与已领取序号；发送点 Network/Handlers/LotteryItemResponseSender.cs:121-124，取自 `[ebp - 0xd0]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00263A50` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

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

**2026-08-24。** 上面的机器区由 **A12** 客户端（SHA-256 `F78F0AEA…`）的静态普查生成。
本节记录 **A21** 客户端（SHA-256 `1F8244D3…`）上不同的部分。判据除另有注明外，都是一份
A21 参照会话，归档于
[a21-world-entry-20260823.log](../../patterns/data/traces/a21-world-entry-20260823.log)
（section L 是入场序列的逐帧顺序）。

**A21 表里这个号叫 `COUPLE_ROOM`**，A12 表叫 `INCREASE_CHANCE_LOTTERY_ALL`。

号没有变，而且这一条的判据最硬：参照在 `0x03D8` 上发 **204 个 `0x00`**，本仓在同一位置
产出的 body 也是 **204 个 `0x00`，逐字节相同**（section M）。204 个零同时相等不可能是巧合——
这些载荷当年就是照这份抓包在这些号上抄下来的。曾被错改到 `0x0442`，已回退。
