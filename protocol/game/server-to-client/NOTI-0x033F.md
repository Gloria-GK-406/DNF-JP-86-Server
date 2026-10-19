# NOTI `0x033F` `DAILYSCHEDULE_CONTENTS_STATE`

## A21 地下城开放状态补证（2026-10-15）

`ClientStatic / High`：VA `01192C10` 在 `01192C4A/53/5C/65` 连续读取四个 u32。网络字段顺序为 `category, state, contentId, opaque`，固定 16 字节。类别 0 用于地下城；本次实现开放状态 1、关闭状态 0，第四字段暂保持 0，不给它编造时间含义。

handler 将字段重排成 `{contentId,state,category,opaque}`，经 `01192D21 -> 011C2670` 写入类别容器。普通选图 `0271D854 -> 011C2160 -> 011C3030` 检查地下城是否属于受调度内容；若属于，再经 `0271D886 -> 011C2530 -> 011C2190` 查询类别 0 下相同 ID 的状态。只有状态 1 或 2 返回 true，缺记录或状态 0 返回 false，调用者在 `0271D88D` 跳过该地下城。该门在 `02736B90` 的活动 ID 69/75/2215 检查之前。

当前原生客户端 PID40704 的只读快照：69 和 2215 均存在；角色等级 70；3700 定义最低等级 27、关联任务仅 `-1`；调度地下城表包含 3700，但类别 0 的状态容器为空，最终候选仍为 `[3541]`。因此仅发 EVENT_INFO 不足以让 3700 显示。

证据：[调查目录](../../../../Document/outputs/tau-hidden-followup-20261015/)，关键文件 `live-state.json`、`filter-fields.json`、`schedule-state.json`、`noti033f-static.txt`；读取仅用 `PROCESS_VM_READ`，无注入或内存写入。没有把这次静态/RPM 补证升级为发送新状态后的 UI 实机通过。

`CurrentImplementation`：活动投影为牛头怪乐园生成 `ScheduledDungeonState(3700, Open)`，选角和活动刷新发送本通知，关闭时显式发送状态 0。保留现有 `0x02D5` 兼容消息，两条身份不能互换。

## 南部溪谷与任务调度补证（2026-10-19）

`ClientStatic / High`：同版只读 RVA dump（SHA-256 `6BD2746DA3858E6F29B3E2D386AF4938056269BAD6C9FFF9455E133E7D953507`）中，`011C2670` 验证类别 0..4 后写入对应容器。`011C2530` 查询类别0；`011C2550` 查询类别1，其调用者 `00E4F660` 在首参数为1时要求指定内容状态等于1。任务相关调用点 `00E58D98`、`02371D6C` 传入记录 `+C0` 标志与 `+0` ID；`+C0` 的PVF字段写入来源仍未完全闭合，不将类别1的任务映射写成Confirmed。

`ClientStatic` 证据文件：`DNFClient-A21-protocol/dumps/dnf-dump-20260906-224825-165-31384/DNF-memory-rva.bin`（工作区只读）；`ClientStatic` 与 `CurrentImplementation` 分开：本服现为南部发送类别0/DGN100、类别1/任务20549，状态随南部受管活动2330启停，在选角与在线活动刷新中共用投影，关闭时显式发送0。PVF的 `etc/chn_schedule.etc` 声明地下城100与任务20549，DGN100与任务定义均带schedule2330。2330仅作为本服配置身份，不据此断言原服EVENT_INFO身份。

本轮没有新增原服抓包或原生UI实测；仍需验证山麓入口、任务显示与关闭后消失，不升级为WireCaptured/ClientVerified。

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

## 2026-10-19 领主之塔3505入口

ClientStatic / High：同版20260922 A21 dump SHA-256 `AAAE24216E0E260AFA528445775E767D104F658FD68FDAD21EC7BB601B9C8A3D`。`011C4B54`加载etc/chn_schedule.etc，`011C525B/011C52E5`将[dungeon]记录纳入类别0名单。当前PVF SHA-256 `C5CCE5346F60D21128D756A086EEF96E70F75F2F2FE063B6C61C00506C588F06`的同名单明确包含3505。

选图`0271D854→011C2160→011C3030`识别调度内容，`0271D886→011C2530→011C2190`要求同contentId状态1或2，缺项/0过滤；后置`02736B90`也检查，已开放调度路径绕过69/75隐藏拒绝。建议用类别0、状态1、contentId3505、opaque0；正文`0000000001000000B10D000000000000`。2329仅是本服拟接入的活动配置身份，不能代替3505，也不需机械追加69/75或改PVF入口。

CurrentImplementation：本次只读确认当前未注册2329活动、未投影3505，用户确认入口不可见。缺开放状态为高置信原因，未向现服注包；正常接线后实际显示与点击仍待验证。[完整补证及设计](../../../Document/LordTower-Design-20261019.md)。

本协议尚无观察记录。取得样本后按 `<type>-<日期>-<序号>.hex` 存入同级 `samples/`，
并在此说明产生它的客户端动作。证据规则见 [evidence.md](../../evidence.md)。

## 待实机补全

- [ ] 字段真实含义（把上表中的 `unknown` 逐个换成有证据的结论）
- [ ] 用真实样本校验 `BodyLength` 规则是否闭合

## 逆向工作日志

<!-- 走过的死胡同也要写：它能避免下一个人重复同样的尝试。 -->
