# NOTI `0x006C` `EVENT_INFO`

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
| Type | `0x006C` | 同上 |
| 名称 | `EVENT_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 56 + n×2   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x2780]` |
| 2 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x2780]` |
| 4 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x55]` |
| 5 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 0x56]` |
| 6 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x48]` |
| 8 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x44]` |
| 12 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x4c]` |
| 16 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x54]` |
| 20 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x5c]` |
| 24 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x60]` |
| 28 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x68]` |
| 32 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x64]` |
| 36 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x70]` |
| 40 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x7c]` |
| 44 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x78]` |
| 48 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x80]` |
| 52 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x74]` |
| **重复组 g1 ×n** |  |  | *以下字段按运行时计数重复；其后字段的偏移随重复次数变化* |  |  |
| &nbsp;&nbsp;↳&nbsp;0 | 2 | `u16` | unknown | — | 接收指令，取自 `[ebp - 0x50]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x01019E30` |
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

### 形状 body 3 字节

| Offset | Size | Enc | 读取者 RVA | 实测值 | 含义 |
|---:|---:|---|---|---|---|
| 0 | 2 | `u16` | `0x00B7F219` | 0 | — |
| 2 | 1 | `u8` | `0x00B7E394` | 0 | — |

读取 2 次，消费到偏移 **3**。


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

## A21 活动日历闭环（2026-10-05）

证据等级：**ClientStatic / High**；尚未 ClientVerified。当前客户端与 PVF 身份、VA01419E30 注册、VA014196B0 detail reader 和 VA01417650 tail reader 原始证据见 [A21 数据契约](../../../Document/tasks/event-weekly-attendance/evidence/client-data-contract.md)。旧机器区的静态下界不是丰富 entry 的完整长度，A12 空列表跟踪仅证明空形状。

A21 主表：u16 entryCount；每项 u16 eventId、u32 未知值、开始/结束通知各 dstring、u8 hasDetail；有 detail 时 u8 flagA、u8 flagB、标题/短名/第三字符串各 dstring、u32 startUnix、u32 endUnix、linkKey/description 各 dstring、u8 enabled。当前2208首例随后写空 extra 表的 u8 count=0；空形状仍是3B。原始 tail reader 在 manager 存在 event9 时还有前置 `u8 count + count×u16` 条件分支，然后才读 extra 表（每项 u16 eventId+12×u32）。event9 的赋值时序未完全闭合，当前 encoder 没有覆盖该活动组合，不能视为全部活动的通用布局。

所有 dstring 为 u32 编码字节长度+窄字符串。当前006C统一使用严格 **UTF-8**，长度为实际编码字节数；GM配置接受合法Unicode文案，拒绝孤立代理项，不静默替换。

2026-10-05编码结论修正（Confirmed）：先前按 [calendar-text-encoding.txt](../../../Document/tasks/event-weekly-attendance/evidence/calendar-text-encoding.txt) 的原始客户端静态默认值936选择GBK，遗漏实际客户端补丁。`DNF-JP-86-ClientHack/ClientHack/plugins/0020_client_compatibility/client_compatibility.c` 的A21分支在RVA `00026BE1`把初始化立即数936改为65001。对正在运行的 `E:/Project/DNF/DNFClient-A21-protocol2/DNF.exe`（PID30016）只读核查：VA `00426BE0`为 `68 E9 FD 00 00`（push65001），全局VA `03B89610`实际为65001；用户截图同时显示活动日历和原生签到窗口已打开、动态标题乱码。原始静态证据保留，但不再代表打补丁后的运行环境。UTF-8修正后的客户端显示仍待复测，不扩大为完整签到玩法ClientVerified。

当前实现由注册活动实例投影日历，2208 detail 的 flagA=0/flagB=5 来自上游 seed，含义尚未闭合；第三字符串和 tail 为空。linkKey `total attendance check event/0/0` 满足原生解析语法，静态映射到窗口0x1E1，但可点击效果仍须实机确认。配置有效期、启停和内容有效性同时控制日历与玩法。

CurrentImplementation：`Server/src/Dfo.Server.Protocol/Game/WeeklyAttendance/WeeklyAttendanceSlice.cs` 的 EventInformationMessage；选角、有效通关、签到和 GM 配置后经统一通知刷新。自动编码/隔离HTTP与消息派发证明服务端闭环，不能替代真实窗口验收。
