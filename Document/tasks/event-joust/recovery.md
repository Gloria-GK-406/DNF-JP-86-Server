# Crash 恢复点（2026-10-06）

主控基线仍为 `b4bf8d0d01113c39962c885b44002453dcb740c6`，生产改动未提交。首轮进度只有 `Data/Events/JoustModels.cs` 与 `Abstractions/Events/JoustServices.cs` 落盘，契约/spec/plan 已 ready。重启后原子代理不再存活，重新派发有界阶段，不能等待旧 worker。

## 当前执行责任

- 后端 T-002 是唯一生产写入阶段，完成报告 `backend-result.md`。
- 协议和前端 worker 当前只读；下一阶段先消费落盘 API，再由主控授予独占写权限。
- 主控维护本任务控制文档，读累计 diff；不部署、不操作真实 DB。

## 继承的协议发现（待协议 worker 复核并写正式记录）

来源为崩溃前 Sol6.1 agent 的 ClientStatic 报告，非 WireCaptured/ClientVerified：

| 入口 | 发现 |
|---|---|
| CMD 04E7 writer RVA 000F2B10/000F2B4D | 总 23B；前14B opaque、未初始化，不能用作请求ID。offset14 u8 KnightIndex，offset15 i32 MaterialSlotIndex，offset19 i32 Amount。参考读取 i16 槽位是差异。 |
| CMD 04E6/04E8 | 请求只有14B opaque，无历史分页参数；INFO ACK reader D2030、BET ACK D20B0、HISTORY ACK D25B0。历史成功 flag1+u32 result0+500×(u16 round,u8 winner,f32 odds)，round0停止显示。 |
| NOTI 04B8/04B9/04BA/04BB | reader D2690/D2330/D23E0/D24A0 分别精确 ReadBytes 3/90/46/31。其他字段语义仍由协议阶段核对。 |
| NOTI 04BB / RVA000F7A70 | 31B为 u16 round + u8 stage + 7×4B对局：winnerKnight/winnerAux/loserKnight/loserAux。Aux 传入 RVA016182F0，对方Aux存对象+8；不能直接称高字节padding，Aux意义待闭合。 |

DNF.exe：`E:/Project/DNF/DNFClient-A21-protocol/DNF.exe`，SHA256 `1F8244D3F7309BBE2FDE4299E1863A894F6B28D5707BF01732980585B20B00DD`。EventId2365来自参考实现，LinkKey候选`[joust event]`来自客户端字符串；尚须闭合事件日历消费语义。

## 已确认内容与 API 约定

- PVF `event/chn_event/chn_joust.evt`：minimumLevel17、maximumBetting1000、reward490002916、bettingReward490002925、materials490002916/490700609、12骑士。三物品分别骑士币、骑士马战神秘礼盒、简易骑士币，归档到期2016-11-17须活动局部规则处理。
- `ServerS4A21` Joust 是 ReferenceImplementation，不是原始70源码，也不是目标客户端实机证明。
- `IJoustDomainService` 以已落盘 JoustServices 为准：Settings/SaveSettings/State/Bet/History/Advance；Bet显式稳定RequestId，协议用会话+帧序号构造。
- HTTP GET/POST `/api/gm/v1/events/joust/settings`；POST `{expectedRevision,schedule}`；schedule六字段，固定3比赛阶段。独立revision，与通用活动revision分开。
- UI接线 `Web/gm-admin/src/events/activityViews.ts`，专属弹窗；PVF名称(ID)只读；冲突或写结果未知重读时保留draft。停用文案说明已有投注仍结算。

这些恢复提示不能替代实际生产结果和检查。完成每阶段后以实际 `*-result.md`、源码和验证证据更新主控进度。

## 后续静态复核修正

- Aux消费真实方法RVA024FDC00；RVA016182F0是内容getter。Aux为每骑士PVF `[win]`/`[loss]`展示向量二级索引；客户端容量5组，但当前PVF各28int=4×7，仅0..3有数据，首版选0合法。不可称padding/战绩。
- LinkKey必须有两处`/`：RVA01018410/010186B0拆分对象+74字符串，第一段由RVA0100F030解析窗口存+140，另外两段存+164/+168。`joust event/0/0`可跳窗口0x1FC，纯`joust event`会失败。日历点击RVA005A3ACD..AF0消费+140和+168。
- Quarter/Semi/Final开场需当前阶段累计4/6/7赛果以构建native晋级；后端调整展示数量，但结算仍仅Result阶段。
