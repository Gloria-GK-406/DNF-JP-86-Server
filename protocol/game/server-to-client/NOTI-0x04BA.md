# NOTI `0x04BA` `JOUST_BETTING_INFO`

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
| Type | `0x04BA` | 同上 |
| 名称 | `JOUST_BETTING_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 46   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 46 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0xa]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x000D23E0` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 0.2.2 当前行为与证据（2026-10-06）

CurrentImplementation：46B资料只投影本场个人总投注及八名骑士的个人投注数量。成功04E6按ACK→04BA→04B9返回；下注后的在线资料更新及重放只发送资料，不发送状态/赛果操作消息。查询无任何世界状态写入。轮号映射与04B8/历史一致；布局来源仍为ClientStatic和codec验证，不把没有归档的实帧补记成WireCaptured。


本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->


## 2026-10-06 A21 JOUST 接线（ClientStatic / CurrentImplementation）

EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；dump `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，image base `00400000`。下列地址均为 RVA。

46B：offset0 u16RoundNo；offset2 i32PersonalTotal；offset6起8×5B（u8KnightIndex,i32PersonalBet）。

reader `000D23E0` ReadBytes0x2E，5B步长。

实现：`Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`。固定布局测试和真实dispatcher测试属于CurrentImplementation；无本次抓包，无ClientVerified。2026-10-06 当前实现：轮号改为赛果成功入库时分配的全局累计序号；时间仅为场次字段，查询读取保存的结果。进行中场次使用下一候选序号作原生同步标记，不提前写入历史。详见 [期号与时间 ID](../../../Document/tasks/event-joust/knight-statistics-result.md#期号与时间-id2026-10-06)。详见[准备证据](../../../Document/tasks/event-joust/protocol-preparation.md)和[实机清单](../../../Document/tasks/event-joust/Human-Test-List.md)。
