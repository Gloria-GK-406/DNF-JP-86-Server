---
kind: rks-verification
decision: pass
---

# 完成验证：角色身上的金币

## 被评估的主张

spec-002 的完整变更集满足 TaskContract revision 1 的 CSC-1..CSC-7：玩家赚到的钱就是他背包上看到的数字、重登后仍是它、花钱从它扣、增减只有一个入口且携带上限在那里生效、新建角色有确定起始值、两笔钱不能互相误用、范围外行为不变。

基线 `e4f29e1d`，本次验证时未提交。

## 条件到证据的映射

| 条件 | 证据 | 结果 |
|---|---|---|
| CSC-1 玩家赚到的金币显示在背包上，重登后仍是那个数 | 实机（授权方本人操作客户端）：地面拾取 17 与 18，服务端日志 `character now carries 17` → `35`，累加正确；跨多次登录后库中仍为 35。后续拾取 46：日志 `drop 4 is 46 gold, character now carries 199513`，库中 `carried_gold = 199513`，**客户端显示 199513**，三方吻合 | pass |
| CSC-2 花钱从他看得见的那笔钱里扣，差额等于价格 | 实机：起始 200000，付费翻牌日志 `charged 580`、免费翻牌 `paid 47`，库中 `carried_gold = 199467`，恰为 `200000 − 580 + 47`。授权方报告翻牌表现正常 | pass |
| CSC-3 增减只有一个入口，携带上限在那里生效，超限处理一致 | 结构证据：`UPDATE character_stats` 全仓仅 `InventoryStateTables.cs:190/224` 两处，均在 `SqliteCharacterCarriedGoldTable` 内；`AddGoldAsync`/`TrySpendGoldAsync` 调用点仅四处（翻牌收益、退款、扣费、地面拾取）。截断落在唯一入口上，故在每条入账路径上必然一致——是结构保证而非判据保证。判据证据：`CarriedGoldCapTests` 7 条，其中三条用记录型替身断言**交给存储的 ceiling 本身**（而非观察数字变化，因为传 `int.MaxValue` 也会让数字同样地动），并成对覆盖档案下限与已买升级两个分支，另断言所询问的等级取自角色行而非会话（两者取值刻意不同） | pass |
| CSC-4 新建角色的随身金币可读出且与「从未写过」可区分 | `character_stats` 的行在建角色时必定创建，列为 `NOT NULL DEFAULT 0`，故「无行」不是一种状态。判据 `A_new_character_carries_zero_and_has_a_row_to_carry_it_in`：读出为 0 且行存在；`A_character_that_does_not_exist_is_neither_paid_nor_charged`：`FindAsync` 返回 null，与持有零可区分 | pass |
| CSC-5 两笔钱不能互相误用 | 随身一侧 `character.AddGoldAsync(amount, ct)` 不带也不能带角色标识；金库一侧 `cargoGold.AddAsync(accountId, amount, ct)` 强制传账号标识。两者签名不同形，误用在编译期被拒。金库那笔的两个切片消费方已全部移除，`IAccountCargoGoldService` 现仅余接口、注册与实现三处 | pass |
| CSC-6 变更时客户端被告知，且所告知的与所存的一致 | 翻牌发 NOTI 0x000E（主背包槽 0，绝对值）；地面拾取发 GET_ITEM 增量帧后紧跟一帧绝对值。**客户端对 0x000E 是赋值而非累加**，两个独立实机取证：翻牌那次（若累加会显示约 399467，实际正常）与拾取那次（若累加会显示 199559，实际 199513）。判据 `AssertCarriedGold` 断言帧内的金额而非仅断言帧存在 | pass |
| CSC-7 范围外的既有行为不变 | 本次新鲜运行全量判据：`488+52+60+255+6+221+933 = 2015 通过 / 2 跳过 / 0 失败`，exit 0。基线 1993，增量 22 全部来自新判据（Persistence +7、Host +2、Infrastructure +6、Application +7），Protocol / Networking / Pvf 一个不少，无程序集下降。两个跳过仍是缺档案守卫，即档案已加载的证据 | pass |

## RQ 覆盖

RQ-001→CSC-1；RQ-002→CSC-1；RQ-003→CSC-2；RQ-004→CSC-3；RQ-005→CSC-3；RQ-006→并发判据（`Task.Run` + `Barrier`，8 条线程同时释放，走各自连接争文件写锁）；RQ-007→CSC-4；RQ-008→CSC-6；RQ-009→CSC-5；RQ-010→CSC-7。AC-001..AC-010 全部通过。

## 范围核验

未触碰：存取款协议、账号金库存量、拍卖行与 `auction_gold_limit`、邮件金币与卖装备收入、复活币与胜点两个相邻槽位（虚拟槽过滤改为 `> 0 and <= 2`，槽 1/2 仍走物品行）。`protocol/` 仅在 `NOTI-0x000E.md` 人工区补记本次新增的发送方与语义，符合 AGENTS.md 对该目录的定位。

## 与评审的关系

评审的 `approved` 只证明其自身范围。上表每一行的证据均为本阶段新鲜取得或可当场复现。评审提出的放行条件（拾取路径帧集在整改中改变，需一次拾取的实机核销）已由授权方执行并闭合，结果记于 CSC-1 与 CSC-6 两行。
