# NOTI `0x021E` `SET_USABLE_COUNT_INFO`

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
| Type | `0x021E` | 同上 |
| 名称 | `SET_USABLE_COUNT_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 1 + n×5   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xcd]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0xd4]` |
| &nbsp;&nbsp;↳&nbsp;4 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0xce]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D85740` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## A21

**2026-08-24。** 上面的机器区由 **A12** 客户端（SHA-256 `F78F0AEA…`）的静态普查生成。
本节记录 **A21** 客户端（SHA-256 `1F8244D3…`）上不同的部分。判据除另有注明外，都是一份
A21 参照会话，归档于
[a21-world-entry-20260823.log](../../patterns/data/traces/a21-world-entry-20260823.log)
（section L 是入场序列的逐帧顺序）。

A21 的入场序列里有这一帧，紧跟 `0x0465`（section L 第 23 帧）。

body 1 字节，参照值 `00`。服务端按常量下发（`UsableCountBaselineMessage`）。含义未解。

## A21物品每日次数（2026-09-29）

**ClientStatic + CurrentImplementation，非空通知效果待ClientVerified。** 本轮直接走读当前A21 dump，确认RVA0x00D85740处理器：`u8 count + count*(u32 itemId + u8 usedCount)`，无其他网络字段。0条目清空客户端两张次数表；非空仅更新指定物品。

RVA0x012EB360写入使用次数；RVA0x012E07B0读取，RVA0x012E08A0以模板上限减去该值计算剩余。因此该byte是“已用”，不是“剩余”；上限来自物品模板。分组表分支的+0x434业务配置名尚未闭合，本服按用户要求不同疲劳药独立计次。

服务端在登录、物品使用成功/业务拒绝及在线跨业务日投递完整快照：清空帧后按最多255项分包。次数超byte范围显式报错，不截断领域计数。这里取代原先仅登录下发空基线的行为，LIST_ITEM实例字段未改。

完整证据与边界：[物品使用次数服务](../../../Document/outputs/fatigue-system-20260929/item-usage-service.md)。

## 2026-10-10 次数职责统一

次数存储写入迁至统一配额领域服务，消耗品成功事实经EventService.DirectAsync同步处理，和物品消费参与同一事务。提交后的变化事件只触发权威快照，不再次扣次。021E及00AC的body、清空/分块/冷却规则不变；登录和每日刷新保留恢复入口。此次架构重构的新增实机验收待执行，不撤销先前已有验收，也不将其扩展为本次所有边界已验收。见[任务Spec](../../../Document/tasks/usage-quota-direct-events-20261010/spec.md)。
