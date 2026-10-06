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

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->


## 2026-10-06 A21 JOUST 接线（ClientStatic / CurrentImplementation）

EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；dump `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，image base `00400000`。下列地址均为 RVA。

3B：offset0 `u16 DailyRoundNo`，offset2 `u8 State`。0..5读取确认，语义和倒计时尚未实机闭合。

reader `000D2690` ReadBytes3；consumer `000FCBD0`：0→scene0，1→scene1并refresh，2→scene2，3未见UI变化，4→scene13prepare，5走scene5/8/11。当前服务器选择 Inactive0/Betting1/Locked3/比赛4/Result2 属ServerPolicy，不能视为客户端真实阶段枚举。

实现：`Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`。固定布局测试和真实dispatcher测试属于CurrentImplementation；无本次抓包，无ClientVerified。轮号由保存的UTC+8 schedule/start推导每日1..N，不使用当前已修改配置。详见[准备证据](../../../Document/tasks/event-joust/protocol-preparation.md)和[实机清单](../../../Document/tasks/event-joust/Human-Test-List.md)。

阶段映射首版沿Reference：Closed0/Betting1/StopBetting3/Racing4/ResultReview2；静态scene切换仅支持消费链，不能证明业务语义。04BBStage0..3推动对应比赛动画。State5备用路径保持Unknown，本实现不发送5。

### 重复state1的客户端副作用（2026-10-06）

ClientStatic（同上dump）：每次收到state1均走D2731→D2850跳表index0→D2742，取字符串ID12BCB，经D2818–D282F聊天输出；未见旧轮号/状态比较。ID12BCB对应用户截图“骑士马战大竞猜已开始。”属于High关联，正文资源尚未确认。主UI0x1FC打开时还会自动查询04E6，消费链详见[查询记录](../client-to-server/0x04E6.md)。

CurrentImplementation：04B8用于选角同步和后台签名(Active,RoundId,Phase)变化；查询、下注数据通知、重放和通用活动刷新只发详情。相同状态刷新不应重新公告。自动化回归保护消息边界，不替代修复后真实客户端验收。
