# 选角单连接出站上界（TASK-05 §4）

日期 2026-10-22，基准提交 `5a52a13a`。口径：一次 SELECT_CHARACTER 对请求者单连接登记的全部帧（含 15 B 帧头），不含步骤栅栏应答 CMD 0x04DD。PVF 取工作区根 `Script.pvf`（73,070,078 B，与 TASK-03 测量同一份），读法为把 `Tool/PvfProbe` 复制到仓库外构建运行。逐行数据：`selection-outbound-bound.tsv`（列：帧类型、新号实测 body、最大 body、实测帧数、最大帧数），与 `OutboundQueueLimitTests.Selection` 一一对应。

## 结果

- 最大 body 合计 450,680 B，最大帧数 237。
- 上界 ＝ 450,680 ＋ 237 × 15 ＝ 450,680 ＋ 3,555 ＝ **454,235 B**（`OutboundLimits.SelectionOutboundBound`）。
- 2 × 454,235 ＝ 908,470 B；队列上限取 **1 MiB ＝ 1,048,576 B**（`OutboundLimits.QueueByteLimit`）。
- 自检：同一张表的实测列 36,360 B body、195 帧，36,360 ＋ 195 × 15 ＝ 39,285 B，等于 TASK-03 实测。

body 合计的加法（69 行归并为 10 项）：
120,763（0x000D）＋ 66,706（0x00AC）＋ 65,541（0x01C7）＋ 53,596（0x0061）＋ 36,363（0x000E）＋ 30,004（0x0164）＋ 19,012（0x0002）＋ 13,770（0x0015）＋ 13,175（0x0166）＋ 31,750（其余 60 行）＝ 450,680。

帧数 237 ＝ 实测 195 ＋ 2（0x021E 分块）＋ 33（0x00AC 按每块 255 条分块）＋ 7（可选帧：0x0245、0x023F、0x006B/0x0067、SAO、清理通告 2 帧）。

## 主要增长项（路径相对 `Server/src/Dfo.Server.Protocol/Game/`）

| 帧 | 公式 | 容量上界与出处 | 最大 body | 确定/保守 |
|---|---|---|---:|---|
| 0x000D ×6 | Main 5＋101(14＋N)；Avatar 5＋131N；个人仓库 5＋101N；Pet 3＋101N；账号仓库 9＋101N；GuildMedal 3 | Main 349 实格＋3 货币＋11 共享材料；Avatar 210；个人仓库 200；Pet 240；账号仓库 120（`Abstractions/Inventory/InventorySlotMappingRuleService.cs`、`Application/Inventory/InventoryCapacityDomainService.cs`）；编码 `GameInventorySnapshotCodec.cs:8`、`GameInventoryDelivery.cs:60/65` | 120,763 | 确定 |
| 0x00AC 1→34 | 批次帧 1 ＋ Σ块(1＋8m)，每块 ≤255 | PVF 带 [cool time] 的 stackable 8,334 个（`StateSynchronization/ItemUsageProjection.cs:80`） | 66,706 | 保守 |
| 0x01C7 | 1＋4＋文本 | 文本 ≤65,536（`Application/.../ClientSettingsDomainService.cs:55`） | 65,541 | 代码上界 |
| 0x0061 | 6＋Σ摘要(≤166)＋Σ正文(≤563) | 摘要数 u8 ≤255，每页 20 封（`MailboxQueryDomainService.cs:16`） | 53,596 | 保守 |
| 0x000E | 3＋101n | 每日补给只进普通背包 349＋11＝360 | 36,363 | 保守 |
| 0x0164 | 4＋30,000，固定 | — | 30,004 | 确定 |
| 0x0002 ×4 | 子类型0 161＋名字＋23×外观；子类型1 321＋装备＋3×技能；子类型6 25；名册 18＋Σ(58＋名字＋23×外观) | 名字 ≤18 B、外观 ≤15 件、32 装备槽合计 3,428、每页技能 ≤204、账号 ≤32 角色（`CharacterCreationRuleService.cs:22/40`、`DrawnAppearance.cs:14`、`CharacterSkillRuleService.cs:139`） | 19,012 | 技能页保守 |
| 0x0015 ×2 | 每帧 3＋2q | quest.lst 3,441 个任务 | 13,770 | 保守 |
| 0x0166 ×5 | 5×35＋26N | 称号簿 80＋170＋50＋100＋100＝500（PVF `etc/titlebook.etc`） | 13,175 | 确定 |

其余 60 行（含 30 种固定帧、0x0227 的 105 帧、0x0465 710 对、0x0005 1,047 个地下城、0x0069 宠物 141 格、0x0167 253 个称号成就等）合计 31,750，逐行见 tsv。

## 按保守值估计的项

CMD 0x0004 premium 数（取 u8 上限 255）、0x0015 可接任务数（3,441）、0x0013 与 0x0002 子类型1 每页技能数（204）、0x00AC 冷却条目（8,334）、0x021E 次数限制条目（362）、0x03C1 租赁件数（272）、0x0061 摘要数（255）、0x000E 补给条目（360）、0x01C7 文本长度（65,536）。第二帧 0x0015（25 B）未在代码中定位到产出点，按第一帧同上界计。

## 未计入

- **无生产写入、取实测值的列表**：0x015F、0x00AE、批次 0x00AC、0x00FB×2、0x0158、0x00CD、0x0286、job 9 的暗骑连招 0x01C0。只有遗留导入或直接写库才会放大；最大的是 0x01C0（多 1 帧，2 页 × 64 KiB ＝ 131,072 B）与 0x0286（u32 计数，只受 1 MiB 帧长约束）。
- **人群项**：城镇在场投影（约 582 B／人）、PvP 频道大厅快照、选角后放闸的成就／称号投影。实测与 `PlanTownPublicationAsync` 的前置条件表明选角时通常不触发。

这些项若成立，可能突破 2 × 上界的余量（1,048,576 − 908,470 ＝ 140,106 B）；属于本推算口径之外，由 T-006 接线后的实测核对。
