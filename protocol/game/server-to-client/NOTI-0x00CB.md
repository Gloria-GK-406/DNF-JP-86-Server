# NOTI `0x00CB` `REQUEST_DISJOINT_ITEM`

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
| Type | `0x00CB` | 同上 |
| 名称 | `REQUEST_DISJOINT_ITEM` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 9   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 1 | `u8` | 常量 1：上游 builder 在本 NOTI 首字节恒写 1，不随运行时数据变化（失败不走本 NOTI，走同 type 的 CMD 响应） | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:138 BuildOwnerDisjointNotification 首字节 WriteByte(1)；失败路径见同文件 110-116 BuildDisjointError（发给请求者的 CMD 0x00EB 响应），取自 `[ebp - 1]` |
| 1 | 4 | `u32` | 摊主结算后的金币余额（int32）：非自用拆解时已把 store.Cost 加进来 | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:139 WriteInt32(ownerGold)；ExpertJobStoreHandler.cs:341 传 result.OwnerGold；DisjointMachineService.cs:95-96,119，取自 `[ebp - 0xc]` |
| 5 | 4 | `u32` | 拆解机本次扣减后的剩余耐久（int32）：<= 0 时上游随即关闭该摊位 | Confirmed | 上游 ExpertJobStorePacketBuilder.cs:140 WriteInt32(endurance)；ExpertJobStoreHandler.cs:342 传 result.Endurance；DisjointMachineService.cs:113,120；关摊见 ExpertJobStoreHandler.cs:359-366，取自 `[ebp - 8]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00931D10` |
| handler 所在区 | text |
| 注册路径 | batch:batch |

NOTI 的 body **没有**结果信封，整个 body 归 handler——这与 CMD 相反，见 [game-receive](../../patterns/game-receive.md#cmd-body-的两字节结果信封每一条-cmd-都有)。

支持客户端 SHA-256：`F78F0AEA5137DF06B63ED0F7470B66F3A24D061B69D4DA13758E65574A7FC04C`

<!-- rks:machine:end -->

## 证据与样本

<!-- 人工区：重新生成不会触碰本节及其之后的一切内容。 -->

尚无实机样本。取得后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，并在此说明产生它的客户端动作。

## 服务端实现映射

尚未实现。

## 待实机补全

- [ ] 触发时机（什么玩家动作/服务端状态导致它出现）
- [ ] 响应流程（是否有响应、响应身份与顺序）
- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
