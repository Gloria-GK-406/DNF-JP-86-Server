# NOTI `0x04B9` `JOUST_INFO`

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
| Type | `0x04B9` | 同上 |
| 名称 | `JOUST_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 90   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 90 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x60]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x000D2330` |
| handler 所在区 | text |
| 注册路径 | forwarder |


支持客户端 SHA-256：`1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`

<!-- rks:machine:end -->

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

## 0.2.2 当前行为与证据（2026-10-06）

CurrentImplementation：90B八骑士资料由领域服务返回真实累计胜败、保存的状态及动态赔率。统计按整场已提交赛果更新，冠军三胜；领域计数64位，原生u16超界同比缩放以保留比值。预测来自历史/状态/攻击克制规则，不读取内定结果，不提前把冠军改成100%。

用户明确接受当前赔率（行为级ClientVerified）；状态0/1/2对应强势/常态/弱势、克制环和1.1..16倍率为ServerPolicy。客户端两图标职责已由 [静态报告](../../../Document/outputs/joust-client-reanalysis-20261006/report.md) 区分；完整统计数值、全部图标语义和超界显示没有逐项实机证明。下注中内定不冻结赔率，封盘之后倍率固定。


本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->


## 2026-10-06 A21 JOUST 接线（ClientStatic / CurrentImplementation）

EXE SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`；dump `E:/Project/DNF/DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin` SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`，image base `00400000`。下列地址均为 RVA。

90B：offset0 u16RoundNo；offset2起8×11B：+0 u8KnightIndex（FF无条目）、+1 u8展示内容选项、+2 f32Odds、+6 u16WinCount、+8 u16LossCount、+10 u8特殊隐藏flag。+6/+8 现已由下述资料窗口消费者确认；首版曾为0，当前接入已完成赛果累计。

reader `000D2330` ReadBytes0x5A，11B步长；`000D23B3..C0`将+10传给`000FA090`展示。首版内容选项0；特殊骑士index>=8在Betting/Locked flag1，比赛flag0为ReferenceImplementation/ServerPolicy。

实现：`Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`。固定布局测试和真实dispatcher测试属于CurrentImplementation；无本次抓包，无ClientVerified。2026-10-06 当前实现：轮号改为赛果成功入库时分配的全局累计序号；时间仅为场次字段，查询读取保存的结果。进行中场次使用下一候选序号作原生同步标记，不提前写入历史。详见 [期号与时间 ID](../../../Document/tasks/event-joust/knight-statistics-result.md#期号与时间-id2026-10-06)。详见[准备证据](../../../Document/tasks/event-joust/protocol-preparation.md)和[实机清单](../../../Document/tasks/event-joust/Human-Test-List.md)。

### 累计胜败接线（2026-10-06）

ClientStatic / High：骑士资料消费者 VA `004F5130`，RVA `000F56F3`、`000F570B`、`000F5734` 读取 +8/+6 计算胜率，文本 ID 12BC7/12BC8 显示胜败与整数百分比。顶部 12BC6“获胜次数”来自历史冠军记录遍历（RVA `000F523B..000F5255`），不从 B9 的胜场字段计算。

CurrentImplementation：Result 后整场统一增加累计胜败，写入上述字段；u16 超界时同比缩放以保留胜率，服务保留完整 64 位计数。客户端实机数值反馈仍待验。详见[统计实现与验证](../../../Document/tasks/event-joust/knight-statistics-result.md)。

### 状态与动态倍率（2026-10-06）

CurrentImplementation / ServerPolicy：新版本场次 +1 condition 使用保存的 0/1/2 状态，+2 赔率按历史基准、本期预测及本骑士累计投注计算，封盘后使用最终快照。ClientStatic 确认 condition 图片 index=值+3（RVA `000F54EF..000F54F6`）；0/1/2 被赋予强势/常态/弱势是服务端设计，不是客户端中文语义的确认。客户端图标匹配仍需实机观察，未修改客户端/PVF。详见 [赔率设计与兼容](../../../Document/JoustOdds.md)。
