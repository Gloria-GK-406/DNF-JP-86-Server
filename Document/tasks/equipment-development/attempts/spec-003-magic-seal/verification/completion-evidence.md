---
kind: rks-verification
decision: pass
---

# spec-003 魔法封印自动交付验证

日期：2026-09-30。目标：`8ab85ebdcd5c872d6d7c762e8766a21ad015a7f9` 后的当前完整增量。此 `pass` 只断言 TaskContract 规定的**自动交付**成功条件；A21 实帧、GUI、重登仍在 Human Test List 中待用户集中验收，不能标 `ClientVerified`。独立 mixed review 见 `../reviews/change-review.md` 的 Round 3 `approved`。

| 条件 | 新鲜证据与结论 |
|---|---|
| SC-1 来源/费用/未知 | `../evidence-and-design.md` 对照 70 ELF 地址、上游 `RandomOptionResolver`、A21 `0x0191/0x01B6/0x0197/0x01C8` 构包/响应 RVA 与真实 PVF；`protocol/game/client-to-server/` 人工区及 `protocol/game/server-to-client/CMD-0x01B6.md` 分离证据等级。第二字段非哨兵不扣材料，两个相邻入口隔离。真实 PVF 内容测试 `PvfEquipmentMagicSealContentSourceTests` 在 `PVF_ARCHIVE_PATH=E:/Project/DNF/DNFClient-A21-debug/Script.pvf` 下通过 1/1，样本 `101010339` 的费用 1720/3800、候选类型 15 值 13/98、卷轴 `2600460` Lv30 只作静态读取。 |
| SC-2 持久/拒绝/回滚 | `EquipmentMagicSealDomainTests` 与 `MagicSealHandlerTests` 共 16/16 通过，覆盖 UID/无关属性保留、解封/指定词条变更、非哨兵材料拒绝、无权/锁定/过期/错槽/不足、SQLite 强制提交失败回滚、重读、同序号重放及当前金币刷新。规则服务使用显式种子，收据与日志可重现一次决定。 |
| SC-3 协议/投递 | `MagicSealWireTests` 覆盖 A21 4B/5B 解码与最小 ACK；Networking 真实 TCP `MagicSealFrameIntegrationTests` 以 A21 14B 入站头验证两条接线，`0x0191` 的 handler 专属日志断言、`0x01B6` 的拒绝帧断言。协议全量 1118 通过/60 跳过，Networking 全量 69/69 通过。领域提交后发送权威装备/当前金币，未结算不发成功。A21 `0x01B6` 回调静态读法支持最小成功/失败体，但无原服实帧或 GUI 效果证明。 |
| SC-4 人测可执行 | `../Human-Test-List.md` 给出固定 PVF 样本、解封/变更/拒绝/重登步骤、抓帧与服务端定位，并明确卷轴隔离及未实测。 |

## 构建与回归

- `dotnet build Server/86JP.Server.slnx --no-restore --verbosity quiet`：0 警告、0 错误。
- `dotnet test Server/tests/Dfo.Server.Application.Tests --filter 'FullyQualifiedName~EquipmentMagicSealDomainTests|FullyQualifiedName~MagicSealHandlerTests' --no-restore --verbosity quiet`：16/16 通过。
- `dotnet test Server/tests/Dfo.Server.Networking.Tests --no-restore --verbosity quiet`：69/69 通过。
- `dotnet test Server/tests/Dfo.Server.Protocol.Tests --no-restore --verbosity quiet`：1118 通过、60 跳过。
- 真实 PVF `dotnet test Server/tests/Dfo.Server.Infrastructure.Tests --no-restore --verbosity quiet`：406 通过、2 跳过。
- 不设 `PVF_ARCHIVE_PATH` 的 Application 全量：2063 通过、171 跳过、0 失败。
- 真实 PVF Application 全量第二次运行：2243 通过、2 失败；失败均为已在接受基线记录的 `CreatureGoesBackOnTests.A_creature_taken_off_can_be_put_back_on_and_is_the_same_creature` 和 `A_creature_aimed_at_a_slot_that_is_not_its_own_is_refused`。首次运行还出现一项 `QuestLogTests.AQuestGivenUpIsInNeitherList`，隔离复跑通过，第二次全量未复现。无 PVF 全量此前偶发一项 `CharacterSkillLearningTests.Invalid_changes_leave_all_skills_unchanged`，隔离与再次全量通过。上述偶发不据此宣称根因已修复；两项宠物失败不算本子项回归通过。
- `git diff --check` 无空白错误。真实客户端未启动、未部署、未修改用户数据库。

## 后续人工边界

用户集中 A21 验收需要保留 `0x0191` 第二字段、`0x01B6` 实际响应/错误提示及两入口 GUI/重登的抓帧；在此之前只能称 `CurrentImplementation + ClientStatic + ContentStatic`。卷轴结算、`0x0197` 和 `0x01C8` 均不属于本轮已闭合路径。
