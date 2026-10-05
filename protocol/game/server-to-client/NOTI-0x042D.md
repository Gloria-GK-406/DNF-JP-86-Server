# NOTI `0x042D` `EVENT_TOTAL_ATTENDANCE`

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
| Type | `0x042D` | 同上 |
| 名称 | `EVENT_TOTAL_ATTENDANCE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 450   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 450 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x005A9B90` |
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

## A21 450B 状态投影（2026-10-05）

证据等级：**ClientStatic / Confirmed** 的固定长度与消费偏移；业务含义依上游/PVF交叉核对为 High，未 ClientVerified。当前输入身份与原始反汇编见 [A21 数据契约](../../../Document/tasks/event-weekly-attendance/evidence/client-data-contract.md)。VA009A9B90 一次 ReadBytes(450)，不是450B下界。

| Offset | Width | 当前投影 |
|---:|---:|---|
| 0x004 | u32 | 累计已签到次数 |
| 0x198 | 20B | 已签到的1基周序号，剩余0 |
| 0x1B0 | u32 | 当前业务周推荐通关次数 |
| 0x1B8 | u32 | bonus状态，首例保留0 |
| 0x1BC | u32 | 当前格序号：min(duration,max(1,累计次数)) |
| 0x1C0 | u8 | 普通签到按钮达标状态 |
| 0x1C1 | u8 | bonus显示，首例保留0 |

其它字节均0；关闭/未开始/过期/规则不可用时整450B为0。当前格序号沿用上游 ResolveCurrentEventWeekNo，不等于下一奖品序号；下一次奖品为累计次数+1。原生脚本路径是 `event/chn_event/chn_totalattendance.evt`，条件4/44为进度目标，44不是等级。首例只开放普通周签到和累计4/8/11奖励，额外签到/补签语义尚未闭合。

CurrentImplementation：WeeklyAttendanceStateMessage / EventPayloadCodec.Attendance；真实SQLite和隔离消息派发验证长度、奖励、重复请求及关闭清空。待实机核对真实窗口数值、按钮与周格。
