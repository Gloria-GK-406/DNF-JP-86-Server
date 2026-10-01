# NOTI `0x006A` `EVOLUTE_CREATURE`

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
| Type | `0x006A` | 同上 |
| 名称 | `EVOLUTE_CREATURE` | upstream NotiPacketType |
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
| 0 | 2 | `u16` | 目标宠物脚本 species ID；70 传目标 creature ID 且写 u8，A21 接收端恰读 u16，属于不同版本线宽 | High | 70Reference CCreature::Evolute 0x083383E2-0x083383EF 传目标脚本ID，MakeNotipacketEvoluteCreature 0x08340842 写 u8；Document/tasks/creature-system/evidence/evolve-notify.asm.txt；A21 ClientStatic VA 0x01146890 读 u16，取自 `[ebp - 4]` |
| 2 | 2 | `u16` | 进化所属玩家的场景 uniqueId u16；A21 用该值找场景对象 | High | 70Reference MakeNotipacketEvoluteCreature 0x08340883-0x0834089B 写 CUser::get_unique_id；Document/tasks/creature-system/evidence/evolve-notify.asm.txt；A21 ClientStatic VA 0x01146890 读 u16 后查对象，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D46890` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21 进化通知（2026-09-29）

**ClientStatic / Confirmed**：A21 handler VA `0x01146890` 恰读 `u16 targetSpecies,u16 ownerUniqueId`，第二值用于查场景角色，第一值写其宠物状态字段。**70Reference / High 业务语义**：`CCreature::Evolute` 传目标 `.cre` 的 creature ID，70 通知写 `u8 targetCreatureId,u16 ownerUniqueId`；首字段版本线宽不同，不可将 70 的 3B 包直接发给 A21。70 自动进化改同一宠物对象和穿戴物品模板，UID及已有成长状态不重置。当前实现按 A21 4B 形状编码目标 species 与 owner ID，仅覆盖单目标、无任务前置的升级自动进化；A21 非空进化实帧和客户端效果待验证。见[70 通知反汇编](../../../Document/tasks/creature-system/evidence/evolve-notify.asm.txt)、[A21 接收切片](../../../Document/tasks/creature-system/evidence/a21-pet-noti-handlers.asm.txt)及[专项证据](../../../Document/tasks/creature-system/evidence/a21-protocol.md)。


## 当前验收边界（2026-09-30）

本服已接线普通单目标、无任务前置、升级触发进化，UID及名字/经验/饱食保留；自动验证通过。P01–P10没有独立验收进化动画、多人可见范围或特殊成长，保持ClientStatic + CurrentImplementation。 相关消息编排、事务提交与受众归[宠物生命周期流程](../../patterns/flow-creature-lifecycle.md)，此处不由服务端编码推定客户端实帧正确。
