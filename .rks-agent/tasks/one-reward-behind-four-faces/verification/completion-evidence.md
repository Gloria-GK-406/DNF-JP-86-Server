---
kind: rks-verification
decision: pass
---

# 验证 —— 翻牌奖励按线上真实结构重做

## 被评价的断言

> 契约 `one-reward-behind-four-faces` revision 1 的七条成功条件全部满足：翻牌的协议编码层按线上
> 真实结构重写，规则层逐值不变，判据钉的是解码结果，且真实客户端上翻开的牌显示出这一局的
> 装备与金币。

## 条件到证据

| SC | 条件 | 证据 | 结果 |
|---|---|---|---|
| SC-1 | 卡尾按三组八 slot 写出，本局牌面在组 0 的某一列，条目 0 金币、条目 1 装备 | `The_reward_is_declared_on_the_free_row_as_gold_then_the_piece`、`The_gold_entrys_identifier_is_not_the_pieces`、`A_card_with_nothing_behind_it_declares_only_its_gold`、`Only_the_position_the_flip_claims_reaches_the_wire` —— 全部经 `CardPanelFrameReader` 解码后断言 | pass |
| SC-2 | 组 1 / 组 2 写空，帧长符合 `210 + 8N + 8E` | `The_gold_and_black_diamond_rows_are_declared_empty`、`The_frame_grows_by_one_entry_when_a_piece_sits_behind_the_card`（274 / 282）、`The_body_keeps_its_length_whatever_a_paid_flip_costs`、`Every_face_produces_a_frame_that_can_be_walked_to_its_end` | pass |
| SC-3 | 应答八条状态快照，`a`/`b` 座位号或保留值，免费翻牌 `c = 0` | `The_answer_declares_every_column_including_the_untouched_ones`、`Only_the_column_that_was_turned_over_carries_a_seat`、`A_flip_names_its_own_row_and_leaves_the_other_alone`、`The_seat_this_server_answers_as_is_zero`、`The_answer_hands_over_nothing` | pass |
| SC-4 | 记录 1–3 的填充是合法形状（`c = 0`） | `The_whole_answer_can_be_walked_to_its_end`；读取器的 `Require(at == body.Length)` 使 `c = 0xFF` 的填充无法走到帧尾 | pass |
| SC-5 | 规则层行为与基线逐值相同 | `git diff --stat a5d164af -- Server/src/Dfo.Server.Application Server/src/Dfo.Server.Abstractions Server/src/Dfo.Server.Infrastructure Server/src/Dfo.Server.Data` **输出为空**；Application 906 与 Infrastructure 215 两个程序集的通过数与基线逐个相同 | pass |
| SC-6 | 全量判据通过，跳过数停在 2 | `PVF_ARCHIVE_PATH=… dotnet test Server/86JP.Server.slnx --nologo` → **1976 通过 / 0 失败 / 2 跳过**，退出码 0（基线 1964 / 0 / 2） | pass |
| SC-7 | 真实客户端上翻开一张牌，牌面显示这一局的装备与金币 | 实机一局：牌面显示「破旧的棉布护腕 + 1 金币」，且只开点中的那一张。服务端日志 `Select card … Free position 0 taken, 4 positions revealed, bag told, charged 0, paid 1`（`live-rq009.log:418`），使用者当场确认屏幕 | pass |

## 判据钉的是什么 —— 单独取证

契约把「判据必须能在不跑客户端的前提下失败，且其断言对象是解码结果」列为硬约束。

**变异检验**：对交付态施加八个变异，逐个编译通过后跑判据、再按 sha256 还原：

| 变异 | 结果 |
|---|---|
| 装备写进金币条目 | RED |
| 奖励写进黄金行 | RED |
| 牌面铺到四列 | RED |
| 数量写到金币那一格 | RED |
| 应答填充记录写 `c = 0xFF` | RED |
| 座位号写 1 而不是 0 | RED |
| 每一列都声明被翻开 | RED |
| 免费翻牌顺手写黄金行的座位 | RED |

**独立评审者另行复核了其中四个**（含一个「同步改长度函数、使编码器内部自洽」的变异，模拟上一轮那族自洽的错实现），全部报红且均非编译失败，跑完还原并 sha256 校验。评审结论 `approved`，见 `../reviews/change-review.md`。

## 评审之后的改动

评审通过后只改了注释：`DungeonClearSlice.cs`、`CardRewardPayloadCodec.cs`、`CardRewardHandlers.cs`、
`DungeonClearCodecTests.cs` 四处仍在陈述已被推翻的旧读法（「274 bytes 定长」「free-card pair at
240 and 245」「a single position byte」「answers with every position of the face」），按评审的
非阻断观察第 1 条改写。**唯一的非注释改动是一个循环变量改名**（`flag` → `column`），无行为影响。
改后全量判据重跑，仍为 1976 / 0 / 2。

## 未纳入本次验证的

- 座位号在组队时的语义未验（单人实测为 0），Spec 的 Assumptions 已具名。
- 每张牌 1 金币是占位值；「金币应当一定给且随等级难度浮动」已确认为后继 Spec 的范围。
- 付费（黄金）牌与黑砖牌的送达路径不在本次范围，帧上写空是有意的。
- 规则服务摇四个位置而只有第 0 位能送达，属已知的规则层清理项，另开任务。
