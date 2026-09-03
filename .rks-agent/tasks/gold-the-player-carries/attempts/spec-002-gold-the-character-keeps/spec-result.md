---
kind: rks-spec-result
outcome: completed
terminal_stage: verification
---

# Spec Result: 角色身上的金币

## Delivered

- 随身金币成为本服务器真实持有的一笔钱：存为 `character_stats.carried_gold`，与治理它的携带上限同处一行，跨会话保留。
- 增减只有一个入口——挂在既有角色对象上的 `ICharacter.AddGoldAsync` / `TrySpendGoldAsync`，不带角色标识，与账号金库一侧必须显式给出账号标识的调用形态不同形。
- 携带上限真实生效：取档案 `etc/(r)goldlimitbylevel.etc` 按等级的下限与角色行上已买升级的较大者，在唯一入口上截断。
- 新建角色带确定起始值：`character_stats` 的行随角色创建而生，列为 `NOT NULL DEFAULT 0`，「无行」不再是一种可与持有零混淆的状态。
- 三条既有收支路径改向随身金币：通关翻牌收益、付费翻牌扣费、地面拾取。账号金库不再承接其中任何一笔。
- 变更时客户端被告知：翻牌发 NOTI 0x000E（主背包槽 0，绝对值）；地面拾取在既有 GET_ITEM 增量帧后紧跟一帧绝对值，使携带上限截断时二者不再错开。

## Success Evidence

| Condition | Evidence |
|---|---|
| CSC-1 | `./verification/completion-evidence.md`：实机三方吻合（日志 `character now carries 199513` / 库 `carried_gold = 199513` / 客户端 199513） |
| CSC-2 | 同上：`200000 − 580 + 47 = 199467`，与库中一致 |
| CSC-3 | 同上：`UPDATE character_stats` 全仓两处且同在唯一存储类内；`CarriedGoldCapTests` 断言交给存储的 ceiling 本身 |
| CSC-4 | 同上：`A_new_character_carries_zero_and_has_a_row_to_carry_it_in` 与不存在角色的对照 |
| CSC-5 | 同上：两侧签名不同形；金库那笔的切片消费方已全部移除 |
| CSC-6 | 同上：赋值语义的两个独立实机取证 |
| CSC-7 | 同上：2015 通过 / 2 跳过 / 0 失败，无程序集下降 |

## Repository Result

- Changed paths: 产品与判据共约 30 个文件（新增 `ICharacterCarriedGoldTable` 与其 SQLite 实现、`IGoldCarryLimitSource` 与 `PvfGoldCarryLimitSource`、`ICarriedGoldRuleService` 与 `CarriedGoldRuleService`、`ICharacter` 的三个成员与其实现、`GameInventoryDelivery.CarriedGoldNotification`；改向 `SelectCardCommand` 与 `GroundPickupSlice`；`character_stats.carried_gold` 列与补列声明；四个新判据文件共 22 条），加 `protocol/game/server-to-client/NOTI-0x000E.md` 与 `Docs/新功能接入规范.md` 的记述更新，以及本任务的流程产物。
- Baseline or accepted result: 基线 `e4f29e1dab...`（spec-001 的已接受结果），分支 `gold-the-player-carries`。接受提交由父控制器的工作区控制器创建。
- Parent rollback required: no

## Successor Guidance

- **客户端对 NOTI 0x000E 是赋值，对 GET_ITEM 的金币形态是累加。**这条此前无取证的语义现在有两个独立实机证据（翻牌：若累加会显示约 399467；拾取：若累加会显示 199559）。已记入 `protocol/game/server-to-client/NOTI-0x000E.md`。任何日后要推送金币的路径都依赖它。
- **`character_stats.gold_carry_limit` 至今没有任何写入点。**上限因此完全来自档案的按等级下限。若日后实现「花钱买上限」，写入点要与 `CarriedGoldRuleService.CarryLimitFor` 的 `Math.Max` 语义对齐——它是下限与购买额的较大者，不是二选一。
- **槽 1 与槽 2（复活币、胜点）仍走物品行且无人写。**它们与金币是同类概念。实现它们时应跟随金币搬出物品行，而不是相反；`InventoryRuntimeValueState` 的记述已写明这一点。
- **不要照抄参照实现的落库机制。**它的脏集合加定时落库是因为其内存背包与数据库是两份数据。本服务器的写随命令事务提交。
- **按角色的执行闸 `IActiveCharacterAccessDomainService` 声明并注册了但零消费方。**本次的单语句写入正是为此而设。若日后接上该闸，单语句仍然正确，但届时可重新评估是否还需要它。
- **一个未解释的观察，与本特性无关但值得单独查。**实机过程中授权方报告捡起过一堆地面金币，而服务端**没有任何一条日志**——既非成功、非 refused、非 no published session，且全场 `fail:|Exception|at Dfo.Server` 命中为 0，排除了命令级异常这第四种形态。故请求根本没到 handler。前后多次金币拾取走同一条路且完全正常，因此这不是随身金币的问题，而是输入路径上的另一件事。
- **授权方另报告：升级后的状态推送只做了任务列表**，其余状态未推。这会让任何依赖登录后状态刷新的东西看起来像坏了。属独立后续项。
- 遗留的非阻断项：并发判据在高负载机器上可能变慢（8 个写者争文件锁）；`IGoldCarryLimitSource` 住在 `Abstractions/Content/ItemPlacementContent.cs`，文件名与主题不符；`ICharacterCarriedGoldTable` 是 public 且已注册 DI，将来任何人自带一个 ceiling 即可绕过上限（今天无旁路，与账号金库同形）。

## Artifacts

- `./TaskContract.md`
- `./routing-decision.md`
- `./spec.md`
- `./plan.md`
- `./sdd-execution.yaml`
- `./reviews/change-review.md`
- `./verification/completion-evidence.md`
