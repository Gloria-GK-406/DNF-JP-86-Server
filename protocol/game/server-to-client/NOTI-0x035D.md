# NOTI `0x035D` `ADVANCE_ALTAR_SUMMON_MONSTER`

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
| Type | `0x035D` | 同上 |
| 名称 | `ADVANCE_ALTAR_SPAWN_UNIT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 38   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 38 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D4BCC0` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## A21字段与实现

ClientStatic / Confirmed：A21镜像 SHA256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`，基址00400000，以下地址使用VA。70Reference只补充字段语义；A21采用紧凑布局，不能复制70的结构体对齐。CurrentImplementation对应已合入主干的实现与自动测试；没有本任务完整WireCaptured，实机确认范围单列如下。

固定38B：sequence:16位；再36B SummonObject：id:i32、enemy:u8及3B padding、rank:i32（0/1/2）、monsterCode:i32、level:i16及2B padding、maximumLevel:i32、x/y/z:i32各。A21读取VA0114BCC0并交给01464750。字段语义结合70 _SummonObject。当前只投影敌方；0396成功的友方效果走客户端STK。

序号的客户端解释有符号：`VA0114BDE2`用`movsx`扩展后传入生成函数；035C初始对象则用无符号扩展。
70Reference的`ProcStage::reset`（0812DE3B）将序号计数器清零，初始对象（0812E177–0812E18E）
和后续召唤（0812EF9C–0812EFB0）共用递增计数器。祭坛不能直接沿用普通房间随机基址。
CurrentImplementation由祭坛Cell从0分配，后续035D继续递增，生存换轮不重置；普通副本的编号规则保持独立。
这是已核实的协议适配差异；自动编号与生命周期回归通过不代表已证明所有无怪现象的根因。
原始依据：[70序号计数器](../../../Document/tasks/guardian-altar/70-actor-sequences.txt)。

原始摘录：[client-spawn-info.txt](../../../Document/tasks/guardian-altar/client-spawn-info.txt)。当前encoder与对应强类型消息已注册，自动codec测试核对完整读取边界。发送顺序见[祭坛流程](../../patterns/flow-guardian-altar.md)。

## 验证范围

ClientVerified（限定）：玩家确认第1关简单正常、中等出怪恢复。该反馈覆盖已测敌军出现，不证明所有动态生成类型、长时间线、序号上限及生存轮次均已实测。
完整实机范围及未覆盖项见[祭坛验收记录](../../../Document/tasks/guardian-altar/acceptance.md)；该记录的总体反馈不替代逐字段WireCaptured。
