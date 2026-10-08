# NOTI `0x04B8` `JOUST_STATE`

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
| Type | `0x04B8` | 同上 |
| 名称 | `JOUST_STATE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 3   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 3 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x36]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x000D2690` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 0.2.2 当前行为与证据（2026-10-06）

当前载荷3B：u16原生轮号+i8状态。ClientStatic确认state1..4有直接公告副作用，不能当作任意资料快照。CurrentImplementation仅在选角入场初始化及后台实际生命周期事件发送；查询、下注资料刷新、重放和通用活动配置刷新不发送。0x80静默扩展和0190_JoustStateSync已移除，不需要马战状态补丁。

本服映射：0关闭、1投注、3封盘、4比赛、2结果；state5按既有BB计数组推进场景，原服发送时点Unknown，本服不发送5。2026-10-08 CurrentImplementation：开场/结束由马战维护服务在生命周期命令提交后直接通过NotificationBus投递；DomainEventBus及其转发订阅者已移除。启动恢复不补播历史通知。入场初始化允许一次原生公告。

ClientVerified（行为级）：用户确认登录出现一次开启通知、内部操作不再公告。没有本次完整实帧，不能据此证明所有原生state时序。下面高位插件、登录不发状态等记录属于已废弃方案，保留用于解释被推翻原因。见 [收口](../../../Document/closeout-0.2.2-20261006.md)。


## 2026-10-06 静默初始化兼容扩展

CurrentImplementation：选角和通用配置刷新额外发送状态byte=`0x80 | NativeState`的04B8，由客户端插件0190_JoustStateSync配套解码。该标记是本项目扩展，不是原生协议结论。正常生命周期04B8仍使用0..5；查询/下注/重放不返回状态初始化。

ClientStatic（下述同一EXE/dump身份）：reader `000D26F9`对byte作movsx，`000D2700`调用setter `000D2150`写管理器`[this+4]`；`000D2728`调用UI consumer `000FCBD0`。`000D272D`再次读取packet原始byte，`state-1`作unsigned比较，大于4则跳过公告。插件仅在上述两个consumer处将-128..-123还原为0..5，packet不修改。窗口打开链 `000F60CF`读取管理器状态，`02446D50`返回`[this+4]`；为0时`000F60D9`显示字符串12BC0固定时间弹窗。此前完全移除入场状态包的策略已废弃。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->


## 2026-10-06 A21 JOUST 接线（ClientStatic / CurrentImplementation）

EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；dump `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，image base `00400000`。下列地址均为 RVA。

3B：offset0 `u16 RoundNo`，offset2 `u8 State`。0..5读取确认，语义和倒计时尚未实机闭合。

reader `000D2690` ReadBytes3；consumer `000FCBD0`：0→scene0，1→scene1并refresh，2→scene2，3未见UI变化，4→scene13prepare，5走scene5/8/11。当前服务器选择 Inactive0/Betting1/Locked3/比赛4/Result2 属ServerPolicy，不能视为客户端真实阶段枚举。

实现：`Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`。固定布局测试和真实dispatcher测试属于CurrentImplementation；无本次抓包，无ClientVerified。2026-10-06 当前实现：轮号改为赛果成功入库时分配的全局累计序号；时间仅为场次字段，查询读取保存的结果。进行中场次使用下一候选序号作原生同步标记，不提前写入历史。详见 [期号与时间 ID](../../../Document/tasks/event-joust/knight-statistics-result.md#期号与时间-id2026-10-06)。详见[准备证据](../../../Document/tasks/event-joust/protocol-preparation.md)和[实机清单](../../../Document/tasks/event-joust/Human-Test-List.md)。

阶段映射首版沿Reference：Closed0/Betting1/StopBetting3/Racing4/ResultReview2；静态scene切换仅支持消费链，不能证明业务语义。04BBStage0..3推动对应比赛动画。State5备用路径保持Unknown，本实现不发送5。

### 重复state1的客户端副作用（2026-10-06）

ClientStatic（同上dump）：每次收到state1均走D2731→D2850跳表index0→D2742，取字符串ID12BCB，经D2818–D282F聊天输出；未见旧轮号/状态比较。ID12BCB对应用户截图“骑士马战大竞猜已开始。”属于High关联，正文资源尚未确认。主UI0x1FC打开时还会自动查询04E6，消费链详见[查询记录](../client-to-server/0x04E6.md)。

CurrentImplementation：04B8用于选角同步和后台签名(Active,RoundId,Phase)变化；查询、下注数据通知、重放和通用活动刷新只发详情。相同状态刷新不应重新公告。自动化回归保护消息边界，不替代修复后真实客户端验收。


2026-10-06 CurrentImplementation更新：04B8不再用于选角同步或一般快照。后台轮次事件通过IDomainEventBus→INotificationBus广播；开场同轮一次，结束以已提交结算记录为依据一次，启动不补播。查询、登录、重放、投注资料更新均不返回04B8。


### 当前实现：恢复原生初始化（2026-10-06）

用户接受初始化公告。选角入场初始化发送原生u16轮号+i8状态0..5；后台实际生命周期变化继续发送原生状态。高位0x80与0190_JoustStateSync已移除。通用活动刷新（含签到领取）/查询/下注资料更新/重复帧重放不发送04B8或04BB。上述较早“登录不发送04B8”及“高位静默扩展”仅是历史实现。

ClientStatic：state1..4直接公告且不比较旧state/round；state0/5无直接公告，state5按已由04BB设置的counter进入scene5/8/11，第三组可能进入scene12并公告冠军。实际原服state5发送时点未知，本服当前不发送5。自动回归不代表ClientVerified。
