---
kind: rks-verification
decision: pass
---

# spec-005 自动交付验证

日期：2026-09-30；接受前基线 `c15e6add5590d5f957210763a0b1c118a93982c3`。此 `pass` 仅指 TaskContract 自动交付条件；A21 实帧、GUI、重登在 `../Human-Test-List.md` 待集中验收，不能称 `ClientVerified`。独立结果审查见 `../reviews/change-review.md`。

| 条件 | 新鲜证据与结论 |
|---|---|
| SC-1 入口、规则、证据 | `../evidence-and-design.md` 区分 70 ELF、上游、A21 静态和真实 PVF，`protocol/game/` 人工记录普通 `0x01B7` 请求及响应；真实 PVF Infrastructure 全量 408 通过/2 跳过，含锻造内容源。动态名字节、卷券变体及错误码未知未冒称实测。 |
| SC-2 结果与守恒 | `EquipmentForgingDomainTests` 与规则测试覆盖成功、概率失败、材料数量、拒绝资产不变、独立 `GenuineUpgrade`、原 UID、SQLite 提交失败与重登重读；handler 测试覆盖同序号重放。锻造定向 Application 21/21、无 PVF Application 全量 2107 通过/171 跳过。 |
| SC-3 协议及投递 | 字节和真实 TCP 测试所在 Protocol/Networking 全量分别 1123 通过/60 跳过、71/71 通过。variant0 有界解码、ACK/错误、成功后权威目标/材料刷新；variant1/2 与畸形尾在扣料前隔离。无证据金币费用不扣，普通强化字段不改。 |
| SC-4 人测清单 | `../Human-Test-List.md` 给真实 PVF 固定样本、成功/概率失败/拒绝/重登及抓帧步骤，自动与未实测边界分开。 |

## 构建与回归

- 独立执行 `dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet`：0 警告、0 错误。
- 修正后锻造定向 Application：21/21；无 PVF Application 全量：2107 通过、171 跳过、0 失败。
- Protocol 全量：1123 通过、60 跳过；Networking 全量：71/71。
- `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf` 的 Infrastructure 全量：408 通过、2 跳过。
- 同一真实 PVF 下 Application 全量：2287 通过、2 失败。失败仍是前一接受基线已记录的 `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature` 与 `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`；不把该套件记为通过，也不宣称根因已修复。
- 首次尝试并行构建与测试时，测试进程锁住基础设施测试输出 DLL，导致构建拷贝失败；单独重跑构建成功。属于执行争用，不计为产品通过或失败证据。
- `git diff --cached --check` 清洁。未启动客户端、部署或改动用户数据库。

## 未完成的人机边界

卷券 variant1/2、动态名语义与真实错误码需后续实帧/GUI 校准。当前仅称 `CurrentImplementation + ClientStatic + ContentStatic`；用户集中 A21 验收须补请求/响应实帧、GUI 结果和重登一致性。
