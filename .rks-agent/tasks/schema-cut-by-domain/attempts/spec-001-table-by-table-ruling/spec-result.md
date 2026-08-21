---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 全表归属裁定与目标形状

## Delivered

- 一份覆盖改动前全部 53 张表、420 列的归属裁定，合成为一份 43 张表的目标形状：
  `Docs/Refactor/TableOwnershipRuling.md`。
- 每一处合并附三条判据的逐条判断，每一处「概念相近而不合并」附所依托的依据；一处红线违反被裁为拆表。
- 两处具名裁定：邮件附件不并入物品（键判据未观测，且它带着四列物品概念里没有的来源出处）；金币上限归派生数值，并由此定下实施先后。
- 目标形状带足以写出建表语句的声明层：13 张改动表的键与必须携带的约束、四个无源新增列的声明、类型与可空的继承规则及其两条例外、六条索引各自的结局、六张每角色一行的表由谁建行、八个不能写错的非零默认值。
- 十六条与真实来源两端比对的机械一致性检查，使「同一个数字改一处漏一处」这类失效成为红灯而非人眼工作。

## Success Evidence

| Condition | Evidence |
|---|---|
| SC-1 | `verification/completion-evidence.md` — 53 行裁定，编号连续无重复，与 `CharacterDatabaseSchema.cs` 的表集合完全相等 |
| SC-2 | 同上 — M-1..M-7、X-1 与 S-1..S-15 逐条；拒绝可依托的三种依据在表前声明 |
| SC-3 | 同上 — 附录 229 行零重复零双落点，23 张源表逐列与声明相等；191 + 229 = 420；45 退役 / 184 落点 |
| SC-4 | 同上 — 「Mail attachments」一节，三条判据逐条判断 + 观测到的第二理由 |
| SC-5 | 同上 — 「The gold limit, and the ordering it settles」，含第二处同型耦合 |
| SC-6 | 同上 — 红线 43 行一表一行，无一判违反；标题合计 165 = 161 + 4 |
| SC-7 | 同上 — 25 条路径全部存在，五条事实抽查到代码全部为真 |

## Repository Result

- Changed paths: `Docs/Refactor/TableOwnershipRuling.md`（新增）与本 attempt 的过程工件。生产代码、测试与库形状声明一行未动。
- Baseline or accepted result: `fc6467af30eb42bfca8319da8fc238082e00116f`（本 Spec 未提交，交由父控制器接受）
- Parent rollback required: no

## Successor Guidance

后续实施必须知道的六件事：

1. **实施是三块，不是四块。** 账号侧五张表在本裁定下一处不改；根契约设想的账号块没有内容。两张仓库物品表归物品块。
2. **次序有两处约束，方向一致：角色本体先行。** 金币上限概念上属派生数值却由物品侧读；`PvpSkillProfileService` 一个方法同时触及角色本体与技能两块。物品侧与技能各自跟随角色本体，彼此之间任意次序。
3. **五处与根契约字面冲突（C-1..C-5），须由根控制器先行裁决。** 分别触及 SC-1（收集箱不承载物品实例）、SC-4（`character_expert_job` 保留）、SC-5（职业专属技能页合并会撞红线）、SC-6（账号设置保留）与 Scope/In 的三处到期。记录已写明：实施不应在某条被抵触的成功条件被重述之前就据其开工。
4. **两处超出根契约 Scope/In（非抵触，是未被点名），须单独确认**：每日挑战两表的合并、`character_invisible_falgs` 的拼写更正。若被驳回，该表原样不动，其余不受影响。
5. **目标是 43 张表，不是契约草案设想的约 31 张。** 判据推翻了草案的几处：账号侧零合并、物品侧收敛到六张而非三张、邮件附件留在邮件域、职业专属技能页不合并。
6. **十六条 Deferred 各有承接人**，其中 D-15（时装明细主键的损失）与 D-16（仓库唯一性的弱化）由物品侧在实施中裁定，D-4（十三列语义未定、归宿暂定）由角色本体侧承接。

一条方法上的教训值得带给后续实施：**每一次新加的规则或小节，第一次被独立评审时都不完整**。本 Spec 十轮评审中，有六轮的发现全部落在上一轮新加的内容上。后续实施若新增约定，应假设它第一遍写不全。

## Artifacts

- `Docs/Refactor/TableOwnershipRuling.md` — 交付物
- `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-001-table-by-table-ruling/spec.md` — 规格
- `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-001-table-by-table-ruling/reviews/change-review.md` — 十轮评审与整改
- `.rks-agent/tasks/schema-cut-by-domain/attempts/spec-001-table-by-table-ruling/verification/completion-evidence.md` — 新鲜验证
