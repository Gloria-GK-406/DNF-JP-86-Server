---
kind: rks-verification
decision: pass
---

# 验证 —— 翻牌一定给金币，且金额随副本与难度浮动

## 被评价的断言

> 契约 `every-flip-pays-gold` revision 2 的九条成功条件全部满足：免费牌一定付出一笔由档案决定的
> 金币，金额随副本基准等级与本局难度浮动，同一局稳定不变，其余规则层取值逐值不变。

## 结论：`pass`

九条成功条件全部有当前证据。SC-9 由使用者的一次实机运行取得。

## 条件到证据

| SC | 条件 | 证据 | 结果 |
|---|---|---|---|
| SC-1 | 免费牌的金币恒大于零，与牌里有没有装备无关 | `A_settled_face_carries_its_gold_whether_or_not_it_drew_anything`（空牌面与非空两种情形）、`A_flip_pays_the_cards_gold_whether_or_not_it_held_anything`、`The_card_carries_the_gold_the_settlement_handed_it` | pass |
| SC-2 | 金额 = 该副本基准等级的档案基数 × 该难度的档案倍率，再按该等级的档案百分比浮动 | `A_cards_gold_is_the_levels_base_scaled_by_the_difficulty`、`The_faces_gold_is_the_levels_base_scaled_by_the_difficulty`、`Each_difficulty_scales_the_gold_by_its_own_entry`、`The_golds_base_is_taken_at_the_dungeons_level_not_the_characters`、`The_archive_declares_the_numbers_a_cards_gold_is_built_from` | pass |
| SC-3 | 浮动双向且不超出档案声明的百分比 | `The_variance_moves_the_amount_both_ways_and_stays_inside_the_band`（200 个种子，上下两侧都出现） | pass |
| SC-4 | 同一局重复询问、断线重来，金额不变 | `The_same_clear_settles_the_same_gold_every_time`、`What_the_gold_spends_of_the_generator_never_reaches_the_positions` | pass |
| SC-5 | 结算通知的条目 0 携带这个金额 | `TheGoldTheSettlementFixedReachesTheCard` —— 把 handler 发出的帧编码出来、按客户端读法解回来再断言 | pass |
| SC-6 | 翻牌一律入账，牌里没有装备时也入账 | `A_flip_pays_the_cards_gold_whether_or_not_it_held_anything`（两个牌位）、`A_card_the_bag_could_not_take_is_still_paid_for` | pass |
| SC-7 | 其余规则层取值与基线 `927e2d0e` 逐值相同 | 65 条删除行经独立评审逐条审过，全部是结构性签名改写或金币相关；出货概率、品级带、副本定价、房间数、掉落池、稀有度阶梯、经验估值一个未动 | pass |
| SC-8 | 全量判据通过，跳过数停在 2 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` → **1993 通过 / 0 失败 / 2 跳过**，退出码 0（本任务基线 1976 / 0 / 2） | pass |
| SC-9 | 真实客户端上翻开一张牌，显示的金币是一个随局变化的正数，不是 1 | 实机一局：副本 144，屏幕显示 **48 金币**，使用者当场确认。服务端日志 `Select card … Free position 0 taken, 4 positions revealed, bag unchanged, charged 0, paid 48`（`live-sc9-gold.log:550`） | pass |

## 判据钉的是什么 —— 四轮独立评审的结果

契约把「判据的断言对象是规则层产出的数值与解码后的帧」列为硬约束。四轮评审，前三轮各裁
`changes_required`，**每一轮都查出真实的空过判据**：

| 轮次 | 查出的空过判据 | 根因 |
|---|---|---|
| 一 | 结算金币不进帧、基数按角色等级取、第三列写死 13 | 前者整段无判据；后两者是 fixture 把两个值写成了同一个 |
| 二 | 难度倍率永远取第 0 档 | fixture 的两档难度同为 `1.5` |
| 三 | 帧写错牌种、付费牌拿免费底薪 | 全仓六张手写牌面一律 `FaceGold = 37` |

**四条都是同一个形状**：一个取错下标或取错维度的查表，产出的仍是「随局变化的正数」——
屏幕上、日志里、判据里三处都看不出来。

第四轮据此做了系统性排查，把变更集触及的查表维度列成 21 项清册、跑了 18 条变异，**15 条必红**；
三条绿的逐条判定不阻塞（两条是本次 diff 一字未改的既有代码，一条是代码自证不可达的分支）。
裁决 `approved`。见 `../reviews/change-review.md`。

## 评审通过后的改动

一处，非实质：`DropRulesTests.cs:192` 与 `MonsterDropServiceTests.cs:79` 给怪物金币表填的波动
百分比由 `13` 改为 `9`。档案在 86 级声明的是 `9`，而那两处紧挨着 `1357` 写在「The archive's own
numbers」的注释下面；该值走的是怪物金币路径（`GoldAmount` 读 `VolumeBand` 而非
`VariancePercents`），**完全不参与运算**。改后全量重跑仍 1993 / 0 / 2。

## SC-9 的实机取证

副本 144 的基准等级是 5，档案基数 44，最低难度倍率 1.02，该等级的波动段 ±13% ——
预测区间 **39～51**。实机得到 **48**，落在区间内。

**这一局的牌里没有装备**（日志 `bag unchanged`），而金币照付。这正是本课题存在的理由：
在这次改动之前，摇空的那些局玩家翻开一张牌会一点反馈都没有。
