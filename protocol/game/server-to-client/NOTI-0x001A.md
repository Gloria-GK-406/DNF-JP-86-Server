# NOTI `0x001A` `UDP_HOST`

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
| Type | `0x001A` | 同上 |
| 名称 | `UDP_HOST` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 1   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 上游恒写常量 0x00，无任何参数参与；唯一构造点名为 BuildUnavailable，疑为"是否提供 UDP 主机"的标志 | Hypothesis | 上游 Network/Builders/Service/UdpHostBuilder.cs:7-12 body 只有一句 WriteByte(0x00)；唯一调用点 Network/Handlers/Dungeon/DungeonEntryHandler.cs:115，取自 `[ebp - 0x211]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00911280` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

## 实测字段表（运行时读取跟踪）

客户端**实际**读取这条协议的方式，由运行时读取跟踪测得（做法见 [game-receive](../../patterns/game-receive.md#四个原语是唯一出口)）。

**本节优先于机器区的 Body 表。** 机器区那张表由静态走图产出，在 S→C 上普遍止步于打包区，给出的是下界；载荷多形时它只描述其中一形，却读起来像描述了整条协议。本节是实测，但**只给边界，不给含义**——`含义` 一列留给人填，偏移已确证之后用实机改值做差分是最便宜的填法。

### 形状 body 1 字节

| Offset | Size | Enc | 读取者 RVA | 实测值 | 含义 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | `0x009112BC` | 0 | — |

读取 1 次，消费到偏移 **1**。


## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

P2P UDP 主机通告。参照实现的 `UdpHostBuilder.BuildUnavailable()` 只写一个 `0x00`，
表示"没有可用主机"。

2026-08-17 实测：客户端读走了这一个字节（值 `0`），此后角色正常进城。
**这不证明"一个 `0x00` 就够了"**——进城由别的帧驱动，本帧发不可用时客户端只是没有 P2P 主机
可连，没有可观察的后果。主机可用时的 body 形状未知。

## 服务端实现映射

同 [`DungeonSelectionSlice.cs`](../../../Server/src/Dfo.Server.Protocol/Game/DungeonSelection/DungeonSelectionSlice.cs)，恒发不可用。

## 待实机补全

- [ ] 主机可用时的 body 布局
- [ ] 客户端拿它做什么（P2P 组队？）

## 逆向工作日志

**2026-08-17**：随进入世界流程打通而首次发出/确认——本帧的**字节**由上方读取跟踪确证
（客户端确实读了它）。但它经**哪条路径**送达尚未裁决：本页把它记在 `0x000F` 的响应上，
而[进入世界](../../patterns/world-entry.md)记载 `0x000F` 那条路径至今没被实机执行过。
读取跟踪只证明帧到了，不证明是这个 handler 发的。

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
