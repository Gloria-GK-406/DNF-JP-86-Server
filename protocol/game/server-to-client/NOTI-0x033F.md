# NOTI `0x033F` `DAILYSCHEDULE_CONTENTS_STATE`

## A21 地下城开放状态补证（2026-10-15）

`ClientStatic / High`：VA `01192C10` 在 `01192C4A/53/5C/65` 连续读取四个 u32。网络字段顺序为 `category, state, contentId, opaque`，固定 16 字节。类别 0 用于地下城；本次实现开放状态 1、关闭状态 0，第四字段暂保持 0，不给它编造时间含义。

handler 将字段重排成 `{contentId,state,category,opaque}`，经 `01192D21 -> 011C2670` 写入类别容器。普通选图 `0271D854 -> 011C2160 -> 011C3030` 检查地下城是否属于受调度内容；若属于，再经 `0271D886 -> 011C2530 -> 011C2190` 查询类别 0 下相同 ID 的状态。只有状态 1 或 2 返回 true，缺记录或状态 0 返回 false，调用者在 `0271D88D` 跳过该地下城。该门在 `02736B90` 的活动 ID 69/75/2215 检查之前。

当前原生客户端 PID40704 的只读快照：69 和 2215 均存在；角色等级 70；3700 定义最低等级 27、关联任务仅 `-1`；调度地下城表包含 3700，但类别 0 的状态容器为空，最终候选仍为 `[3541]`。因此仅发 EVENT_INFO 不足以让 3700 显示。

证据：[调查目录](../../../../Document/outputs/tau-hidden-followup-20261015/)，关键文件 `live-state.json`、`filter-fields.json`、`schedule-state.json`、`noti033f-static.txt`；读取仅用 `PROCESS_VM_READ`，无注入或内存写入。没有把这次静态/RPM 补证升级为发送新状态后的 UI 实机通过。

`CurrentImplementation`：活动投影为牛头怪乐园生成 `ScheduledDungeonState(3700, Open)`，选角和活动刷新发送本通知，关闭时显式发送状态 0。保留现有 `0x02D5` 兼容消息，两条身份不能互换。

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
| Type | `0x033F` | 同上 |
| 名称 | `DAILYSCHEDULE_CONTENTS_STATE` | upstream NotiPacketType |
| 身份说明 | notification (server push only) | 普查 |

## Body

**状态：布局部分可得** — 已读出部分字段，但存在下列因素使其不完整。

原因：handler 的调用图上有跟不下去的地方——间接调用（经函数指针或虚表）、超出跟进深度、或递归重访。取字段可能就发生在跟不到的那一侧，因此本表**可能不完整**。**这不等于「载荷是多态的」**——它只说明静态走到此为止。

```text
BodyLength = 未知（字段集合已知不完整）
已读出字段合计 = 16   ← 下界，不是总长
```

| Offset | Size | Enc | 可能含义 | 置信度 | 证据 |
|---:|---:|---|---|---|---|
| 0 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x1c]` |
| 4 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x14]` |
| 8 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x18]` |
| 12 | 4 | `u32` | unknown | — | 接收指令，取自 `[ebp - 0x1c]` |

`可能含义` 为 `unknown` 表示**字段边界已确定但含义无证据**——这类未知靠实机改值做差分即可解决，与上面的「结构未知」不是一回事。

## 定位

| 项 | 值 |
|---|---|
| handler 入口 | `0x00D92C10` |
| handler 所在区 | text |
| 注册路径 | batch:batch |


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
