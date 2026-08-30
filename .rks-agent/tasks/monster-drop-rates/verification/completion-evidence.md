---
kind: rks-verification
decision: pass
---

# 终验：怪物死了会按档案掉东西

**被评估的主张**：本任务的九条成功条件全部满足。

日期 2026-08-30。工作树 `.worktrees/monster-drop-rates`，分支 `monster-drop-rates`，
基线 `main = 289a98ec`。全部证据在本次跑取，不复用实现期间的任何一次运行。

所有测试运行均设 `PVF_ARCHIVE_PATH=/d/project/DNF/DNFClient-A21-protocol/Script.pvf`。

## 逐条

| SC | 判据 | 结果 |
|---|---|---|
| SC-1 | `PvfDropRateTableSourceTests`：`The_top_band_pays_the_five_rates_the_archive_declares`（81–200 段 = 750 / 10 / 135 / 8 / 5）、`The_declared_band_count_is_what_the_file_holds`、`Each_bonus_table_holds_one_group_for_each_rate`（三张加成表各 5 组）、`The_equipment_slot_carries_the_steep_rank_multipliers`（0.2 / 2 / 7 / 16.8） | **通过** |
| SC-2 | `RarityLadderTests.The_two_rarest_bands_are_reachable`：基数 1000003、最高摇点 1000002、六档权重之和 = 最高摇点；`The_first_group_pays_the_shares_its_thresholds_describe` | **通过** |
| SC-3 | `PvfDropItemPoolSourceTests`：设计图 10087807（品级 57 / 稀有度 2 / 权重 1000）在桶中；材料 10088621、10088620 不在任何桶中；装备池 6062、堆叠物池 774；`The_starting_shirt_is_not_creatable` | **通过** |
| SC-4 | `DropRulesTests`：`A_draw_takes_a_piece_from_inside_the_band`、`A_draw_is_weighted_by_what_each_piece_declares`、`An_empty_band_draws_nothing`（桶为空、稀有度过高两种边界） | **通过** |
| SC-5 | `DropRulesTests.No_slot_hits_when_every_rate_is_zero`、`Every_certain_slot_hits_in_the_same_occasion`；`MonsterDropServiceTests.A_kill_that_hits_no_rate_drops_nothing`、`A_kill_that_hits_gold_and_equipment_drops_both_under_different_numbers` | **通过** |
| SC-6 | 编解码与拾取判据全部通过（见下）+ **一局实机**（见「实机」） | **通过** |
| SC-7 | `MonsterDropServiceTests.One_occasion_rolls_one_answer`；`DropRandomTests.One_seed_gives_one_sequence`、`Neighbouring_occasions_do_not_march_in_step` | **通过** |
| SC-8 | `DropRateAdjusterTests.The_first_phase_adjustment_changes_nothing`（逐字段）、`A_raised_rate_is_what_the_gate_rolls_against` | **通过** |
| SC-9 | 本次运行：构建 **0 警告 0 错误**；全套 **1679 通过 / 2 跳过 / 0 失败** | **通过** |

命令与输出：

```text
dotnet build Server/86JP.Server.slnx        → 0 个警告  0 个错误
dotnet test  Server/86JP.Server.slnx --no-build
  Dfo.Pvf.Tests                    60 / 0 失败
  Dfo.Server.Networking.Tests      51 / 0 失败
  Dfo.Server.Protocol.Tests       372 / 0 失败
  Dfo.Server.Application.Tests    768 / 0 失败
  Dfo.Server.Infrastructure.Tests 184 / 0 失败（2 跳过）
  Dfo.Server.Host.Tests             4 / 0 失败
  Dfo.Server.Persistence.Tests    240 / 0 失败
  合计 1679 通过 / 2 跳过 / 0 失败（基线 1614 / 2 / 0）
```

那 2 个跳过是既有的、与本任务无关的档案相关判据，基线同样跳过 2 个。

## SC-6 的自动化部分

线级与拾取都过了，且线级那半是对**真实帧**的逐字节比对而非自证：

* `MonsterDeathCodecTests.A_kill_that_paid_gold_is_the_captured_frame` ——
  复现 `packet_log.upstream-killexp-20260827.txt` 里 actor 38088 那 55 字节整帧
* `A_kill_that_paid_twice_is_the_captured_frame` —— 复现 actor 32543 那 103 字节整帧（两件掉落）
* `A_kill_with_no_drops_is_the_actor_number_a_zero_count_and_four_closing_bytes` ——
  无掉落时仍是原来的 7 字节，已验证的线级字节未变
* `GroundPickupTests` 9 条，含按编号取走、重复请求不再给、
  声明掉落与怪掉落不共号、包里的判别值等于击杀announce的那个

## 实机（2026-08-30，新手副本 10000，种子 1222085113）

日志 `live-run-20260830.log`，428 行，**零告警、零异常**。用户操作，我只读日志与数据库。

14 次击杀、27 件掉落、4 个房间。

**一、每一件都经得起回档案核。** 1 级怪 → `[item drop ref table]` 第 1 行 `0 3` → 品级带 [1,4]。
掉出的 7 件装备与 3 件堆叠物，**全部**品级在带内、稀有度 0、`[creation rate] > 0`：

```text
31002 grade3 cr375 [weapon]    37003 grade3 cr375 [weapon]   27601 grade3 cr375 [weapon]
12401 grade3 cr625 [pants]     14400 grade1 cr625 [shoulder]  29901 grade3 cr375 [weapon]
14401 grade3 cr625 [shoulder]  6003 grade1 cr1000 [throw]     6004 grade3 cr800 [throw]
80006 grade1 cr1000 [throw]
```

堆叠物全是 `[throw]` 而没有一件设计图——**这是池子结构预测出来的**：设计图品级 5–77，
1 级怪的带够不着。不是凑出来的。

**二、金币数额逐个吻合。** 全部是 17 或 18。
`[gold drop ref table]` 1 级基数 17 × 普通怪 `[gold quantity][0]` = 1 × `[gold volume]` 100–110%
= 17…18.7 → 取整 17 或 18。

**三、按房间编号、并让开房间自己声明的那几件——实机确认。**

```text
room 61000  pieces 1..2    起始 1   （声明 0 件）
room 61001  pieces 2..8    起始 2   （声明 1 件 = 新手图那个箱子）
room 61002  pieces 1..10   起始 1
room 61005  pieces 1..8    起始 1   （boss 房）
```

61001 从 2 开始，正因为它声明了一个可破坏物。这是设计里最容易错又最难发现的一条。

**四、拾取，连同两个刚修的缺陷。** 日志：`drop 2 is item 31002, into slot 9`。
数据库 `items` 表 slot 9 的 `item_core`：

```text
ItemKind=1  ItemId=31002  Value=0x4D7EE4A9  Durability=45
```

* **耐久 45** = 31002 自己 `.equ` 里 `[durability]` 声明的值。
  实现期间这里是硬编码 0，也就是捡起来是把坏武器；线上看不出来，掉落帧根本没有耐久字段。
* **判别值 0x4D7EE4A9** = `DropInstance(room 61000, seed 1222085113, actor 56807)`，
  逐位等于击杀那一帧announce的那个。评审 B2 的修复由此实机确认。

金币被拒了 3 次（`refused as NothingThere`）——本期有意如此，本仓没有写入金币的路径。

**五、收尾。** boss 房清空 → 开出口 → `SetPlayResult` 结算（rank 99，clear 奖励 14，升到 2 级）
→ 回城 `Set user area 1:0 -> 1:2`。**结算出得来、城回得去、无重连。**

## 这一局没有覆盖到的

* **上一个任务那三帧一帧都没来**：`CMD 0x0075 BOSS_DIE_CHECK`、`0x0045 SCORE_SCROLL`、
  `0x0048 EPLP_COMMAND`。新手副本走的是 `GIVEUP_GAME` 退出。
  结算与回城确实成了，但**走的是另一条路**——那三片（`6ce69953`）仍然没有实机确认。
* **地面材料的数量**：61001 那个箱子没被打开取走，那一条仍未验。
* **档次倍率**：14 次击杀全是 rank 0，新手副本的 boss 地图也声明 `[normal]`。
  0.2 / 2 / 7 / 16.8 那一组只有判据，没有实机。
* **非白装**：本局全是稀有度 0。1 级的品级带里本来也没有更高稀有度的东西。

四条都不属于本 Spec 的成功条件，记录在此以免被误当作已验。

## 评审

`reviews/change-review.md`，`decision: approved`。
第一轮 `changes_required`（3 阻塞 / 10 非阻塞），全部修完；重审确认三个阻塞的修法
并各自回插变异验证变红。重审另记 3 条残留（O1/O2/O3），均已在重审后处理：

* **O1** 判别值的不变式只从一端有判据 —— 已收敛到一个共享入口
  `DungeonEntryPayloadCodec.MonsterDropInstance`，两侧同一处
* **O2** `The_gold_multiples_come_from_the_file_being_read` 的两条断言都不可证伪 ——
  已改为断言可破坏物表回落到 `[1]` / `(100,100)` 而怪物表是 `1 2 4 6` / `(100,110)`；
  回插「从固定文件读」的变异现在变红
* **O3** 时装那条判据只查了一个物品号却写成全称 —— 已扩到三个，并写明真正定死全称的是
  装备池 6062 那个计数

## 判据强度：十一次变异，十一次变红

按「能分辨读对了与读了个自洽的错」这条约束验的。前三条是上游确实犯过的错。

| 变异 | 结果 |
|---|---|
| 稀有度分母写死 1000000（上游原样） | 红 |
| 档次倍率恒取第 0 组（上游原样） | 红 |
| 装备池改读 `ItemDictionary.etc` 那类：入池闸忽略 `[creation rate]` | 红 |
| 率分母在规则里写死 10000 | 红 |
| 掉落记录写成 44 字节 | 红 |
| 金币写成物品号 1 | 红 |
| 金币金额取第 2 列而非第 1 列 | 红 |
| 拾取的判别值从掉落编号推（评审 B2 的原缺陷） | 红 |
| 时装槽抽装备池（评审 B3 的原缺陷） | 红 |
| `[gold quantity]` 从固定文件读（评审 O2） | 红 |
| 怪掉的装备按耐久 0 入包（准备实机时自查发现的缺陷） | 红 |
| 拾取的判别值不按击杀推（封存后实机暴露） | 红 |
| 堆叠限额 0 读作「不堆」而非「不限」（封存后实机暴露） | 红 |
