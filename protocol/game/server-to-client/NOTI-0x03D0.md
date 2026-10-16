# NOTI `0x03D0` `WEDDING_INFO`

## 2026-10-16 普通婚礼预约接入

当前补充：独立预约Reserved记录投影state2，接受后双端推送，选角/重登恢复。NPC RVA021AB558在state2阻止重复预约；其它未知字段仍0，仅ClientStatic/High安全性证据，待用户实机验证。

CurrentImplementation：实现边界、A21证据及双端验收步骤见[本次报告](../../../Document/MarriageReservation-20261016.md)。不是婚礼完成或已婚状态；本节优先于下面的历史阶段说明。

## 2026-10-16 A21 人工裁定

ClientStatic / Confirmed：RVA008959B0 的读取链闭合为15B，无CMD信封：

| Offset | 大小 | 类型 | 已知用途 |
|---:|---:|---|---|
| 0 | 4 | u32 | 对象CharacterId，03F3回包与之比对 |
| 4 | 1 | i8 | 婚姻阶段，写manager+0x60 |
| 5 | 1 | i8 | 未闭合，写manager+0x64 |
| 6 | 4 | u32 | 未闭合，写manager+0x68 |
| 10 | 4 | u32 | 未闭合，写manager+0x6c |
| 14 | 1 | i8 | 未闭合，写manager+0x70及角色尾字段 |

RVA00895290同步manager与角色字段；阶段1..4调用RVA00893080查询对象。NPC菜单RVA021A68C7..021A6925读取+0x60：0/5/6/7显示1013申请订婚，1/2/3显示1015退婚和1014举办婚礼，4显示1016离婚。阶段1..3细分语义尚未全部确认。

CurrentImplementation：本期Engaged投影阶段1，peer为持久对象ID，其他字段0；未绑定投影15零。接受后双端推送，选角/重登同源恢复并显式清理未绑定角色旧缓存。不凭成功提示升级为已婚。菜单是否实际切换与重登显示仍待客户端验收。

证据：[只读探针与反汇编](../../../Document/outputs/marriage-engagement-20261016/client-evidence.txt)，镜像SHA256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`。本人工区补全消费链，自动区保留原生成内容。

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
| Type | `0x03D0` | 同上 |
| 名称 | `WEDDING_INFO` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 15   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 15 | `bytes` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x008959B0` |
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
