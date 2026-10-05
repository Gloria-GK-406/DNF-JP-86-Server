# A21 累计周签到客户端与 PVF 数据契约取证

日期：2026-10-05。仅静态分析、只读 PVF 探针；未启动客户端/服务、未 attach、未修改客户端、PVF 或数据库。全部地址为 VA，ImageBase=0x00400000。静态支持不等于 ClientVerified。

## 输入身份

- 客户端：E:/Project/DNF/DNFClient-A21-debug/DNF.unpacked-b1-debug.exe；SHA-256 D7618A102251F1DB724F2514C74A4CC855DF26E6A4BC308F8EE9F2397DB0A99A。
- PVF：E:/Project/DNF/DNFClient-A21-protocol/Script.pvf；SHA-256 3B2FE3DA115FEE949282558A864A319FC9CA197E1D1F22E656E74316A0F04B06。
- 静态辅助库的历史 SHA 已过期，本轮使用当前实际 SHA。协议索引 handler 地址使用 RVA，本文件均加 ImageBase 转为 VA。
- 原始反汇编：a21-client-disassembly.txt；PVF 原始 dump：pvf-total-attendance.txt。

## 收发契约

NOTI 0x042D，VA009A9B90：009A9BB4 push450，009A9BC7 ReadBytes，随后009A9BE8调用009A92F0更新窗口0x1E1。本处理器只消费这450字节。

| Body offset | 宽度 | 客户端消费者与实施含义 |
|---|---:|---|
| 0x000 | 4 | 本轮未确认消费者，保留0 |
| 0x004 | 4 | 已签到次数；009A9358读取，009A938D后按PVF事件总周数限制 |
| 0x008 | 400 | 100个u32存储槽；本轮未确认语义，保留0 |
| 0x198 | 20 | 已签到周序列；009A9361..9387复制，009A948A逐字节读取并减1索引对应周格 |
| 0x1AC | 4 | 未确认，保留0 |
| 0x1B0 | 4 | 推荐地下城进度；009A94D5/94E7读取，009A964A与PVF目标+0x24比较 |
| 0x1B4 | 4 | 未确认，保留0 |
| 0x1B8 | 4 | 额外/bonus状态值；009A93B3读取到window+0x1AC，009A9600用于显示分支。首例保留0 |
| 0x1BC | 4 | 当前活动周序号；009A9323与PVF总周数比较，009A941A等用于当前周显示 |
| 0x1C0 | 1 | 可签到按钮flag；009A934C复制到window+0x1A8 |
| 0x1C1 | 1 | bonus显示flag；009A93A7复制到window+0x1A9。首例0 |

CMD 0x0441 请求：VA009A91E0的控件回调，009A9264构建type0x441，009A927E显式写入14B（009A9288 WriteBytes）。14字节来自UI控件记录的一部分，本轮未解析字段语义；服务端可严格验证长度并保留opaque bytes，不用客户端body授权或累计。不要宣称空body或全零。

CMD 0x0441响应：VA009A9D50，009A9D6A ReadBytes(4)，009A9D72比较u32=0后执行成功显示；非零不进入成功分支。具体失败码含义未核对。

## PVF 实际加载路径与奖励

VA029D68D0=TotalAttendanceEventScript::importScript；029D6901解密路径Event/chn_event/chn_TotalAttendance.evt，明确排除根路径event/chn_totalattendance.evt的另一份旧配置。

当前配置：duration12；condition4、44；12组周奖励490003187..490003198（顺序见原始dump）；累计4、8、11次分别490003219、490003220、490003221，各1个；reward by the week0；bonus attendance count1。

029D6AE2把condition第一数写config+0x24；029D6B0F把第二数写config+0x28。客户端第一数显示推荐完成进度目标4；第二数用于bonus第二进度条目标（009A94F9/009A954E等）。没有证据把44命名为角色等级门槛。

PVF [xui path]：UI\Event\chn_event_2016\chn_eventTotalAttendance_20161020/EventTotalAttendanceCheckWindow.xui。归档中该UTF16资源存在；PVF dump工具只打印token脚本，此次未用它解析UTF16 XUI正文。

## 日历到窗口

客户端内置键名total attendance check event：014120FE解密该键，01412124关联内部keyID0x603；switch 01412E89按0x5C3计算表索引，0x0141383C[0x603-0x5C3]=9，0x014137A4[9]=0x014120F2；已初始化分支014120F8跳01412EED，将窗口ID设0x1E1。窗口打开009A3A90检查record+0x140并调用02304C70。

可用于首例的LinkKey静态候选：`total attendance check event/0/0`。01418410/014186B0按最后两个/切出窗口键与两个数字参数；没有&时014186B0退回01418410。解析器将窗口键交0140F030查内置映射。该候选的映射和语法有静态依据，但仍须实机确认日历点击能打开0x1E1；不可把PVF的原始XUI路径简单当成同等专用窗口入口。

FlagA/FlagB：仅证实写record+4/+8，语义未闭合。上游TotalAttendanceRepository.cs seed为0、5，可作为首例候选默认值，但属于ServerCode而非ClientVerified；DetailsEnabled=1使01418449的解析门通过。应在实机清单确认日历分组、条目可见与点击。

## 待实机

1. 发布EVENT_INFO条目2208，FlagA0/FlagB5、上述LinkKey，查看日历并点击，确认0x1E1周签到窗口。
2. 发送450B状态核对当前周、已签到周序列、推荐次数、按钮状态；关闭后再开、重登核对恢复。
3. 正常完成推荐地下城4次后点击，捕获实际CMD0441的14B请求、ACK和刷新；复点不重复领奖。

首例推荐目标4、总周数12及奖励直接来自客户端实际脚本，不从另一份根路径旧配置推循环规则。失败码、14B字段语义和Flag枚举未完成，不阻止服务器状态权威、严格长度、opaque保存的实现。

## 追加：bonus 模式的实施边界

上游 TotalAttendancePacketBuilder.cs:34固定body+0x1B8为0，:41固定body+0x1C1为0；Service普通CheckThisWeek以推荐目标、周已签到和累计次数授权，没有解析第二目标44或bonus attendance count。

客户端body+0x1C1复制到window+0x1A9，009A95A3依据它切换第二目标44的显示分支；body+0x1B8复制到window+0x1AC，009A9600依据该值选择显示状态。PVF [bonus attendance count]在029D6D03写config+0x4C。

CMD0441的已知唯一构包009A9264没有按上述bonus字段选择其他opcode；14B opaque请求没有完成选择字段解析。可以确认“客户端有第二进度/bonus显示模式”，不能确认它必然是补签、额外签到，或同一按钮怎样对不同模式请求授权。

首例交付边界为普通每周签到和累计奖，1B8/1C1保留0；bonus玩法属于尚未闭合的后续变体，不声称复刻全部客户端周签到变体。PVF原始配置不修改。

## 追加：当前周序号投影

上游TotalAttendanceRepository.cs:354调用ResolveCurrentEventWeekNo，:366-376返回Math.Min(eventDurationWeeks, Math.Max(1,totalAttendanceWeekCount))，不依赖本周Checked、不加1。第一签到成功Count=1后当前序号仍1。无条件Count+1会立即显示2，与该上游参考不同。

客户端009A9323读取body+0x1BC并检查不超过PVF总周数；009A941A/009A9424以它区分当前周格（序号减1）。这些客户端事实没有证明该值应为真实日历周，亦未证明它必须为下一待签到阶段。按Checked条件加1属于额外服务端裁定，不能称上游现状。
