# NOTI `0x00FE` `ELEVATOR_CLEAR_TIME_CHECK`

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
| Type | `0x00FE` | 同上 |
| 名称 | `ELEVATOR_CLEAR_TIME_CHECK` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 2   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 2]` |
| 1 | 1 | `u8` | unknown | — | 接收指令，取自 `[ebp - 2]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D47600` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 2026-10-10 电梯机制接线

ClientStatic（High）：继续跟进 handler RVA `0xD47600`，读取两个 u8 后遍历当前房间对象，匹配对象代码 `0x459`（1113，ElevatorControl），调用阶段 setter VA `0x01BCB410` 和结果 setter VA `0x01BCB400`。该路径没有按疑惑之村地下城编号判断。对象更新函数 VA `0x01BCCFF0` 根据阶段及结果选择动画，结果 1、2 分别进入正常与坠落分支。

CurrentImplementation 的 body 为 `u8 stage + u8 outcome`，其中 stage 为 0～4，outcome 为 0 运行中、1 正常停梯、2 坠落。编码器输出 17 字节完整帧。上游参考采用 15 秒阶段与 60 秒分界；该时间规则不能单靠接收 handler 证明。

领域状态变化经 EventService 投影；全员加载后启动、清怪时结束，加载恢复可发送当前快照。服务端不重复生成客户端 ElevatorSummon 的定时怪物。

实现与验证见 [房间机制说明](../../../Document/dungeon-room-mechanisms-20261010.md)。目前为 **HumanPending**，尚未取得本次部署的实机动画及切房验收。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`；证据规则见 [evidence.md](../../evidence.md)。
