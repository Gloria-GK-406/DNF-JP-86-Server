---
kind: rks-verification
decision: pass
---

# spec-004 自动交付验证

日期：2026-09-30；基线 `4dbd73b48952c3158004167de6f02be14767df39`。此 `pass` 限于 TaskContract 的自动交付条件；A21 实帧、GUI、重登仍在 `../Human-Test-List.md` 待集中验收，不属于 `ClientVerified`。独立结果审查见 `../reviews/change-review.md`。

| 条件 | 新鲜证据与结论 |
|---|---|
| SC-1 来源、状态、材料 | `../evidence-and-design.md` 分离 70 ELF、上游、A21 构包/响应和真实 PVF；`protocol/game/` 人工记录说明固定段、action 2 动态尾与证据等级。真实 PVF 基础设施全量 407 通过/2 跳过，包含 `PvfEquipmentOutworldContentSourceTests`；净化材料 `1183`、清除 `10000408`、赋予/扭转材料及 Lv55 准入由 `etc/amplifyitem.etc` 读取。 |
| SC-2 事务、回滚、重放 | `EquipmentOutworldDomainTests`、`EquipmentOutworldHandlerTests` 与普通强化准入测试在定向集合中 40/40 通过，覆盖状态转换、UID 与无关字段、材料/所有权/状态拒绝、SQLite 提交失败及重读、同序号成功/失败重放。无 PVF Application 全量 2086 通过/171 跳过。 |
| SC-3 协议与投递 | `EquipmentOutworldWireTests`、`OutworldFrameIntegrationTests` 所在协议与网络全量分别为 1120 通过/60 跳过、70/70 通过；固定 `0x00CC` 与 `0x00CD` action0/1 接线，失败按错误码回应，成功后权威刷新装备与材料。action2 拒绝且不扣物；普通强化拒绝非零 `AmplifyType`。 |
| SC-4 人测清单 | `../Human-Test-List.md` 给固定 PVF 样本、净化/清除/赋予/扭转/拒绝/重登与抓帧位置，并注明自动与未实测边界。 |

## 构建与回归

- `dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet`：0 警告、0 错误。
- 定向 Application：40/40 通过（不设 PVF 及真实 PVF 各一次）。
- Networking 全量：70/70 通过；Protocol 全量：1120 通过、60 跳过。
- `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf` 的 Infrastructure 全量：407 通过、2 跳过。
- 不设 PVF 的 Application 全量：2086 通过、171 跳过、0 失败。
- 真实 PVF Application 全量：2266 通过、2 失败。失败为 `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature` 与 `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`，两者在上一接受基线的同条件测试中已记录；不将该全量套件记为通过，也不宣称其根因已修复。
- `git diff HEAD --check` 无空白错误。未启动真实客户端、部署或修改用户数据库。

## 未完成的人机边界

污染装备自然产出与 `0x00CD` action2 见技术债 D-004/D-005。用户集中 A21 验收须补请求/响应实帧、GUI 结果和重登一致性；在此之前仅称 `CurrentImplementation + ClientStatic + ContentStatic`。
