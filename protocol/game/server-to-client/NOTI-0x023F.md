# NOTI `0x023F` `ACCEPTED_QUEST`

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
| Type | `0x023F` | 同上 |
| 名称 | `ACCEPTED_QUEST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 4 + n×6   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | 本次任务状态条目数 count | High | A21 handler RVA 0x00D62451 ReadU32 并于 0x00D62470..0x00D624B2 用作循环次数;见 patterns/data/probes/a21-quest-repair-durability-readers-20260918.md，取自 `[ebp - 4]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 2 | `u16` | 任务 id (questId);0xFFFF 时跳过应用 | High | A21 handler RVA 0x00D62477 ReadU16,0x00D624A7 应用前检查 0xFFFF;见 patterns/data/probes/a21-quest-repair-durability-readers-20260918.md，取自 `[ebp - 8]` |
| &nbsp;&nbsp;↳&nbsp;2 | 4 | `u32` | 任务触发器当前值 triggerValue | High | A21 handler RVA 0x00D62483 ReadU32 后把该值与 questId 交任务状态对象;见 patterns/data/probes/a21-quest-repair-durability-readers-20260918.md，取自 `[ebp - 0xc]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D62440` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-18 组队循环任务同步（CurrentImplementation / ClientStatic High）

静态复核 RVA `00D62440..00D6250C` 及其任务入口 VA `00E75F17..00E75F32`：快照可从任务定义建立缺失任务，不进入 CMD0308/001F 的自动入场续接。当前组队循环在公共入场成功后，为本事务新补接任务的队员入队 `QuestProgressChanged`，复用既有通知路径发送完整任务快照；允许异步晚于地图入场，不新增本帧字段。

证据与限制见 [组队循环修复](/E:/Project/DNF/DNF-JP-86-Server/Document/PartyCycleEntryFix-20261018.md)。该新增触发源尚未标记为双端 ClientVerified。

## 2026-10-11 地图清理异步任务刷新（CurrentImplementation）

普通地下城清房与死亡／迷妄塔清层统一在原业务事务中入队 `DungeonMapCleared`。
可靠任务订阅者提交 trigger 更新后，继续经 EventBus 投递任务变化，通知订阅者通过
`IQuestQueryDomainService.ReadAcceptedAsync` 查询当前任务列表，再发送本 NOTI；镜像任务
保留原运行作用域及其任务变化通知。该刷新允许晚于清房 CMD／NOTI 到达，不与死亡
回复绑定。没有修改本帧的字段布局，也没有改为客户端 `SET_QUEST_TRIGGER` 计数。

服务端测试已核对实际编码的 `cmd=0,type=0x023F,count=1,questId=975,trigger=0`，
并验证持久化恢复及重复消费不重复产生任务变化。A21 实机的死亡之塔第 30 层任务
提示和交付仍待本次部署后验收。实现说明见
[任务进度事件入口](../../../Document/QuestProgressEventModel.md)。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 2026-09-18 当前投影与待验证范围

当前服务端把它作为进行中任务的**整份快照**：`u32 count` 后重复
`(u16 questId, u32 triggerValue)`。这是编码器、服务端测试与静态读取下界一致的
实现形状；静态机器区仍有间接调用盲点，且本页没有 A21 实帧，所以尚不能称为
客户端已无损消费的权威布局。

今天新增/修正的服务端触发源包括最终 Boss 通关任务事实、购买或丢弃改变持有量、
放弃任务，以及交任务前发现过期持有量。`SET_QUEST_TRIGGER` 自身走 CMD 回复，
不为同一次客户端报告额外推本通知。需要在 A21 分别记录这些触发源的原始通知、
与相邻 CMD 的次序，以及双方画面的任务计数；仅服务端测试或玩家看到任务变化
不足以独立证明每条触发源都正确归因到本帧。

## A21 读取路径复核（2026-09-18）

handler `RVA 0x00D62440` 先于 `0x00D62451` 读 `u32 count`，再循环
`count` 次读 `u16 questId`、`u32 triggerValue`；`questId=0xFFFF`
时跳过应用。静态直线路径支持 `4+6n` 的字段顺序及计数语义，
详见[`A21 静态复核`](../../patterns/data/probes/a21-quest-repair-durability-readers-20260918.md)。
机器区仍如实保留间接调用造成的“部分可得”裁决；未拿到本通知的 A21
原始帧及运行时读取 trace 前，不把 `4+6n` 升格为完整线协议，
也不把购买/丢弃/放弃等任一触发链标为 `ClientVerified`。
