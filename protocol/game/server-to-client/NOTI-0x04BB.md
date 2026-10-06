# NOTI `0x04BB` `JOUST_MATCH_RESULT`

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
| Type | `0x04BB` | 同上 |
| 名称 | `JOUST_MATCH_RESULT` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 31   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 31 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x24]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x000D24A0` |
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

31B：offset0 u16RoundNo；offset2 u8Stage；offset3起7×4B：+0 u8WinnerKnightIndex，+1 u8WinnerAuxIndex，+2 u8LoserKnightIndex，+3 u8LoserAuxIndex。绝不能写成两u16或padding。阶段0/1/2/3累计4/6/7/7个结果；尚未提供结果用KnightFF/Aux0占位（ServerPolicy，未来槽读取未实机确认）。

reader `000D24A0` ReadBytes0x1F；consumer `000FCCA0/000F7A70`取+0/+1赢家、+2/+3输家，对方aux存对象+8。`016182F0`→`024FDC00`按Knight/Aux查Win/Loss表（+78/+8C），`024FD925/024FDA02`加载[win]/[loss]。PVF每表实际28整数=4×7，loader容量5选项不等于实际35整数。Aux0有效首项；真实原服选择策略Unknown。Stage3最终文本/scene12跳转仍Unknown。

实现：`Server/src/Dfo.Server.Protocol/Game/Joust/JoustSlice.cs`。固定布局测试和真实dispatcher测试属于CurrentImplementation；无本次抓包，无ClientVerified。2026-10-06 当前实现：轮号改为赛果成功入库时分配的全局累计序号；时间仅为场次字段，查询读取保存的结果。进行中场次使用下一候选序号作原生同步标记，不提前写入历史。详见 [期号与时间 ID](../../../Document/tasks/event-joust/knight-statistics-result.md#期号与时间-id2026-10-06)。详见[准备证据](../../../Document/tasks/event-joust/protocol-preparation.md)和[实机清单](../../../Document/tasks/event-joust/Human-Test-List.md)。


### 2026-10-06 冠军公告的客户端副作用

ClientStatic（Confirmed调用链；仍非WireCaptured）：本轮重新校验上述dump SHA256一致。RVA000D2512调用000FCCA0；Stage跳表RVA000FD438四项分别为000FCCFF/000FCE32/000FD036/000FD236。Stage2分支000FD1E2比较当前scene，除13/9外直接进入scene12；Stage3在000FD40E–000FD412无条件调用000FA1B0(scene12)。scene12入口000FB369；窗口打开分支000FB6C3–000FB742取冠军名字，000FB70C读取字符串12BBF，000FB73B→000FB742进入聊天输出。此路径无旧轮号/旧冠军去重检查。用户“打开赛马就再次出现获胜者”截图与该路径High关联，12BBF文本资源未单独导出。

CurrentImplementation：04BB已从所有普通快照中删除。后台八进四/半决赛事件可推Stage0/1；不再推Stage2终局预览，避免提前进入scene12。结束事件使用结算事务捕获的旧轮完整赛果，仅推Stage3一次；通知重试数0，启动恢复不补播。终局动画兼容性及中途进入后的显示仍需ClientVerified，不能把服务器单测当作原生界面证明。
