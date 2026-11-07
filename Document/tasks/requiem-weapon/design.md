# 镇魂武器释魂开光方案（#282）

基线：`main` 35049db76（0.2.12）。分支 `feature/requiem-weapon-282`，工作区 `DNF-JP-86-Server-group8`。
证据：[PVF 盘点](pvf-inventory.md)、[参照调查](reference-survey.md)、[客户端设计图路径](client-re.md)、
[客户端开光路径](client-re-emancipate.md)。所用 PVF 为 `DNFClient-A21-debug/Script.pvf`（DC250D42…），
用户测试前须核对实际运行服务的 PVF。

## 闭环与现状

| 环节 | 原生路径 | 本仓现状 |
|---|---|---|
| 材料 | 每日任务 4533/4534/4615–4620 发镇魂之石，远古每日 1994–2001 发低级镇魂之石 | 通用任务系统，待实测确认 |
| 镇魂武器 | 艾丽丝(NPC 132)商店 63：`[need material] 10000204 100` 兑换；礼盒 10099772（140 低级镇魂之石，3 石 + 自选一把） | 商店 `[need material]` 与自选礼包 0x00A0/0x0207 已实现，待实测 |
| 开光 | 使用释魂之真灵转换书 10150749（`[emancipate ticket] 19`）→ 点镇魂武器 → 窗口 0x173 → CMD **0x010F** | **未实现**，本任务主体 |
| 设计图 | 31 张 `[recipe] 1` 释魂设计图经 CMD 0x0019 | 现有配方源拒绝 `[recipe] 1` 与 `[success rate]`，作为第二阶段 |

## 开光（CMD 0x010F）

- 请求 19B：`u16 券槽, u32 券ID, u16 目标槽, u32 目标ID, u8 opaque, u16 材料槽, u32 材料ID`。
  券 19 的材料槽/ID 恒为 `0xFFFF/0x0000FFFF`，材料由服务端按目标 `[emancipate] [input]` 扣除。ClientStatic/Confirmed。
- 回包：成功 `u8 1, u8 N, N×{u8 list, u16 slot, u32 count}`（只做减量/移除）；失败 `u8 0, u8 code`。
  客户端不从回包放入产物，产物以 NOTI 0x000E 同格先清后填。成功提示 0xEC3B。
- 校验（服务端权威）：在线选角、城镇、券在背包且 ticket=19、目标在装备背包（不在穿戴/快捷栏，否则 0x13）、
  目标模板 `[emancipate type]` 与券一致（否则 0x11）、未锁（0xD6）、耐久满（0x07）、无异界气息（0xAF）、
  材料齐（0x16）、券/目标 ID 与槽中实际物品一致（0x04）。opaque 字节仅记录，不参与判定（Unknown）。
- 结果：同一事务内扣券 1、扣 `[input]` 材料（含账户共享材料），目标**原位换模板**为 `[output]`，保留 UID。
  保留强化、增幅、锻造、附魔（客户端确认框 0xBA66 文案 ClientStatic；与 70 `copyItemOption` 交集一致）；
  其余属性：耐久保持当前值（转换前已校验满耐久），交易/封装状态按产物模板，`core[74..76]` 原样保留交给按券类型的策略钩子。
  PVF 中开光没有成功率字段，按必成处理；失败分支只有上面的业务拒绝（不扣任何资源）。
- 重放：沿用执行器提交后重放当前状态批，同帧不重复扣费。

## 分层与共用（与 #281 龙跃约定）

- #281 提供 `IEquipmentEmancipateDefinitionSource`（装备 `[emancipate]` 块）与装备原位换模板能力
  （`IEquipmentInstance.TransformAsync`，存储层放开非叠加装备换模板）。本分支建在其提交之上。
- 本分支提供：券侧 `[emancipate ticket]` 读取、`IEquipmentEmancipateDomainService`、
  按券类型注册的 `IEquipmentEmancipatePolicy`（默认实现做上面的保留规则；券 2/3 由 #281 实现）、
  `Dfo.Server.Protocol/Game/UpgradeChronicle/` 切片、0x010F 协议页与 catalog。

## 第二阶段：释魂设计图（0x0019）

用户 2026-11-07 确认转换书实测通过后，要求同 PR 交付设计图路线，`[success rate]` 按万分比（1000 = 10%）。

- 配方源只接受 `[recipe] 1` 的镇魂形态：同时带 `[success rate]`、`[fail output]` 与 `[maintain ability] normal`；其他 `[recipe] 1` 仍拒绝。
- 规则服务把计划读成"装备升级"：恰好一个装备输入、一个装备产物，失败产出含同一装备 ×1。
- 结算：数量只能为 1（A21 产物为装备时客户端固定 1）；装备栏里该模板必须恰好一件（多件拒绝，客户端本地亦拒绝）；
  锁定/过期/异界（0x13）/未修理（0xD9）拒绝且不扣。设计图与其余材料无论成败都扣；掷 0..9999，小于成功率为成功。
- 成功：原位换模板，UID、强化、增幅、锻造、附魔保留，`[sealing]` 产物置封装，耐久取新模板上限；产物行只供结果窗显示，
  写零强化/增幅字段（上游实机见过非零导致卡死），真实物品由 NOTI 0x000E 先清后填。
- 失败：武器原样留在原格，发放 `[fail output]` 中除武器外的物品（浓缩的灵魂 ×1）。
- 0x0019 失败码改按客户端表：满包 0x04（原共用映射 0x7E 会显示防沉迷文案）、异界 0x13、未修理 0xD9。
- `[broadcast type] 1` 的全服广播未实现。

## 未定与风险

- 开光产物模板为 `[trade]`，转换书说明却写"无法交易"：先按产物模板，实测时观察。
- 若同格 NOTI 000E 先清后填在实机表现异常，改为扣减行包含目标槽后再放入。
